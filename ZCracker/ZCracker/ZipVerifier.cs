using System;
using System.IO;
using System.IO.Compression;

namespace ZCracker
{
    public static class ZipVerifier
    {
        public static bool Verify(string password, string archivePath, TargetFileMetadata metadata)
        {
            try
            {
                using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(metadata.DataOffset, SeekOrigin.Begin);

                // Use a modest buffer size (e.g., 4KB or 16KB)
                byte[] buffer = new byte[4096];
                var engine = new ZipCryptoEngine();
                engine.InitKeys(password);

                // 1. Verify Header (first 12 bytes)
                byte[] header = new byte[12];
                if (fs.Read(header, 0, 12) != 12) return false;
                
                engine.DecryptBuffer(header, 12);
                
                byte checkByte = (byte)((metadata.HeaderVerifier >> 24) & 0xFF);
                if (header[11] != checkByte) return false;

                // 2. Setup Stream for Payload
                // We create a wrapper stream that decrypts on the fly to avoid loading everything
                long payloadLength = metadata.CompressedSize - 12; // Actual compressed data size
                var decryptStream = new DecryptStream(fs, payloadLength, engine);

                // 3. Verify Content
                // If CRC is 0, we can't trust it. Try to decompress instead.
                if (metadata.RealCrc32 == 0)
                {
                    // Compression Method 8 is Deflate. 0 is Store.
                    // If stored, we can't verify much other than header check (which passed).
                    // If we are here, we assume it's valid if it decompresses or is stored.
                    // For Deflate (8), we try to copy through a DeflateStream.
                    
                    // Note: Most encrypted zip files use Deflate (8).
                    try
                    {
                        using var deflateStream = new DeflateStream(decryptStream, CompressionMode.Decompress);
                        // Count decompressed bytes
                        byte[] sink = new byte[4096];
                        long totalDecompressed = 0;
                        int read;
                        while ((read = deflateStream.Read(sink, 0, sink.Length)) > 0)
                        {
                            totalDecompressed += read;
                            // Optimization: Fail early if we exceed expected size
                            if (totalDecompressed > metadata.UncompressedSize) return false;
                        }
                        
                        // Strict check: Decompressed size must match exactly
                        return totalDecompressed == metadata.UncompressedSize;
                    }
                    catch
                    {
                        return false; // Decompression failed
                    }
                }
                else
                {
                    // Calculate CRC on the fly
                    uint crc = 0xFFFFFFFF;
                    int read;
                    while ((read = decryptStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        for (int i = 0; i < read; i++)
                        {
                            byte index = (byte)(((crc) & 0xff) ^ buffer[i]);
                            crc = (crc >> 8) ^ Crc32Calculator.Table[index];
                        }
                    }
                    return ~crc == metadata.RealCrc32;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    // Helper stream to decrypt on read without buffering everything
    public class DecryptStream : Stream
    {
        private readonly Stream _baseStream;
        private long _remaining;
        private readonly ZipCryptoEngine _engine;

        public DecryptStream(Stream baseStream, long length, ZipCryptoEngine engine)
        {
            _baseStream = baseStream;
            _remaining = length;
            _engine = engine;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            
            int toRead = (int)Math.Min(count, _remaining);
            int read = _baseStream.Read(buffer, offset, toRead);
            
            if (read == 0) return 0;

            // Decrypt the chunk in place
            // We need a temporary buffer or handle offset carefully because DecryptBuffer takes array
            // ZipCryptoEngine.DecryptBuffer processes array from 0 to length usually?
            // Let's look at ZipCryptoEngine signature: DecryptBuffer(byte[] buffer, int length) -> starts at 0
            // So we need to be careful if offset > 0.
            
            if (offset == 0)
            {
                _engine.DecryptBuffer(buffer, read);
            }
            else
            {
                // Copy to temp, decrypt, copy back (slow) or modify Engine to accept offset
                // Ideally modify Engine, but for now we can process byte by byte or unsafe
                // Since we are inside ZCracker, let's just do it manually here to avoid modifying Engine heavily
                // Actually Engine.DecryptBuffer assumes 0. Let's make a local span copy or simple loop.
                
                // Fast path:
                for (int i = 0; i < read; i++)
                {
                    buffer[offset + i] = _engine.DecryptByteAndUpdate(buffer[offset + i]);
                }
            }
            
            _remaining -= read;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _remaining;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public static class Crc32Calculator
    {
        public static readonly uint[] Table;

        static Crc32Calculator()
        {
            Table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320;
                    else
                        crc >>= 1;
                }
                Table[i] = crc;
            }
        }
    }
}