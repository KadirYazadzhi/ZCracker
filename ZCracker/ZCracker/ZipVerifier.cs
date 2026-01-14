using System;
using System.IO;

namespace ZCracker
{
    public static class ZipVerifier
    {
        public static bool Verify(string password, string archivePath, TargetFileMetadata metadata)
        {
            try
            {
                // 1. Read the encrypted data (Header + Compressed Data)
                // We need to read 'CompressedSize' bytes starting from 'DataOffset'.
                // Note: CompressedSize in ZIP usually *includes* the 12-byte header for encrypted files?
                // Actually, standard ZIP spec says Compressed Size is the size of the *compressed data stream*.
                // For encrypted files, the 12-byte header is prepended to the data.
                // So we likely need to read 12 + CompressedSize? Or is CompressedSize inclusive?
                // Usually tools treat CompressedSize as the size on disk. So it includes the 12 bytes.
                // We will assume CompressedSize is total bytes to read.
                
                byte[] fileData;
                using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Seek(metadata.DataOffset, SeekOrigin.Begin);
                    // Limit read to avoid memory explosion if file is huge.
                    // If file is > 100MB, maybe we stream?
                    // For now, assume < 2GB and just read.
                    if (metadata.CompressedSize > int.MaxValue) return false; // Too big for this simple verify
                    
                    fileData = new byte[metadata.CompressedSize];
                    fs.ReadExactly(fileData, 0, (int)metadata.CompressedSize);
                }

                // 2. Initialize Engine
                var engine = new ZipCryptoEngine();
                engine.InitKeys(password);

                // 3. Decrypt the whole buffer
                engine.DecryptBuffer(fileData, fileData.Length);

                // 4. Verify Header (Again, just to be sure)
                // The first 12 bytes are the header.
                // The 12th byte (index 11) must match high byte of CRC.
                // But wait, DecryptBuffer modifies in place.
                // So fileData[11] is the decrypted verification byte.
                
                byte checkByte = (byte)((metadata.Crc32 >> 24) & 0xFF);
                if (fileData[11] != checkByte) return false; // Should not happen if FastCheck passed, but good sanity check.

                // 5. Calculate CRC32 of the *Payload* (bytes 12 onwards)
                // We need a CRC32 calculator.
                uint calculatedCrc = Crc32Calculator.Compute(fileData, 12, fileData.Length - 12);

                return calculatedCrc == metadata.Crc32;
            }
            catch
            {
                return false;
            }
        }
    }

    public static class Crc32Calculator
    {
        private static readonly uint[] Table;

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

        public static uint Compute(byte[] buffer, int offset, int count)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < offset + count; i++)
            {
                byte index = (byte)(((crc) & 0xff) ^ buffer[i]);
                crc = (crc >> 8) ^ Table[index];
            }
            return ~crc;
        }
    }
}
