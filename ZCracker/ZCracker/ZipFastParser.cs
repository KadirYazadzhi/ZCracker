using System;
using System.IO;
using System.Text;

namespace ZCracker
{
    public readonly record struct TargetFileMetadata(
        byte[] EncryptionHeader,
        uint HeaderVerifier, 
        uint RealCrc32,      
        bool IsEncrypted,
        bool IsSupported,
        string FileName,
        long DataOffset,
        long CompressedSize,
        long FileLength // Debug info
    );

    public static class ZipFastParser
    {
        private const uint LocalFileHeaderSignature = 0x04034b50;
        private const uint DataDescriptorSignature = 0x08074b50; // 50 4b 07 08

        public static TargetFileMetadata Parse(string filePath)
        {
            long fileLength = new FileInfo(filePath).Length;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                while (fs.Position < fs.Length)
                {
                    uint signature = br.ReadUInt32();
                    if (signature != LocalFileHeaderSignature)
                    {
                        throw new InvalidDataException("Could not find Local File Header signature.");
                    }

                    fs.Seek(2, SeekOrigin.Current); // Version
                    ushort bitFlag = br.ReadUInt16();
                    bool isEncrypted = (bitFlag & 1) == 1;
                    bool hasDataDescriptor = (bitFlag & 8) != 0;

                    ushort compressionMethod = br.ReadUInt16();
                    ushort lastModTime = br.ReadUInt16();
                    fs.Seek(2, SeekOrigin.Current); // Last Mod Date

                    uint crc32 = br.ReadUInt32();
                    uint compressedSize = br.ReadUInt32();
                    uint uncompressedSize = br.ReadUInt32();

                    ushort fileNameLen = br.ReadUInt16();
                    ushort extraFieldLen = br.ReadUInt16();

                    byte[] nameBytes = br.ReadBytes(fileNameLen);
                    string fileName = Encoding.UTF8.GetString(nameBytes);

                    if (extraFieldLen > 0)
                    {
                        fs.Seek(extraFieldLen, SeekOrigin.Current);
                    }

                    long currentPos = fs.Position;

                    if (isEncrypted)
                    {
                        byte[] encryptionHeader = br.ReadBytes(12);

                        uint headerVerifier = crc32;
                        if (hasDataDescriptor)
                        {
                            headerVerifier = (uint)(lastModTime << 16);
                        }

                        uint realCrc32 = crc32;
                        long actualCompressedSize = compressedSize;

                        // If bit 3 is set, CRC is usually 0 in header. We need to find Data Descriptor.
                        if (hasDataDescriptor)
                        {
                            // Heuristic Scan for Data Descriptor Signature (0x08074b50)
                            // We scan starting from where we think data ends (or just after header to be safe)
                            // Scan window: From currentPos + 12 (min compressed data 0?) to End of File.
                            
                            // Safe seek bypassing BinaryReader
                            long scanStart = currentPos;
                            if (actualCompressedSize > 0) 
                            {
                                scanStart += actualCompressedSize; // Optimization: Jump to expected end
                                if (scanStart > fs.Length) scanStart = currentPos; // Safety
                            }
                            
                            long savedPos = fs.Position;
                            fs.Seek(scanStart, SeekOrigin.Begin);
                            
                            // Scan forward 4KB or until EOF looking for signature
                            // We assume it's near the expected end.
                            
                            byte[] buffer = new byte[4096];
                            int bytesRead;
                            bool found = false;
                            
                            // To be robust, let's scan a bit BEFORE expected end too, in case size excluded header
                            if (actualCompressedSize > 0 && scanStart > 20)
                            {
                                fs.Seek(Math.Max(currentPos, scanStart - 20), SeekOrigin.Begin);
                            }

                            while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0 && !found)
                            {
                                for (int i = 0; i < bytesRead - 4; i++)
                                {
                                    // Check for 0x08074b50 (Little Endian: 50 4b 07 08)
                                    if (buffer[i] == 0x50 && buffer[i+1] == 0x4b && buffer[i+2] == 0x07 && buffer[i+3] == 0x08)
                                    {
                                        // Found signature!
                                        // Next 4 bytes are CRC32
                                        int crcOffset = i + 4;
                                        if (crcOffset + 4 <= bytesRead)
                                        {
                                            realCrc32 = BitConverter.ToUInt32(buffer, crcOffset);
                                            found = true;
                                            
                                            // Also update Compressed Size if it was 0
                                            // Note: calculating exact compressed size here is tricky without uncomp size check
                                            // but for verification we trust the Header size if present, 
                                            // or we need to calculate it: (FoundPos - currentPos).
                                            
                                            long foundPos = fs.Position - bytesRead + i;
                                            long calculatedSize = foundPos - currentPos;
                                            if (actualCompressedSize == 0 || Math.Abs(calculatedSize - actualCompressedSize) < 20)
                                            {
                                                actualCompressedSize = calculatedSize;
                                            }
                                        }
                                        break;
                                    }
                                }
                                if (fs.Position > currentPos + actualCompressedSize + 4096 + 100000) break; // Don't scan forever if file is huge
                            }
                            
                            fs.Seek(savedPos, SeekOrigin.Begin);
                        }

                        return new TargetFileMetadata(
                            EncryptionHeader: encryptionHeader,
                            HeaderVerifier: headerVerifier,
                            RealCrc32: realCrc32,
                            IsEncrypted: isEncrypted,
                            IsSupported: compressionMethod == 0 || compressionMethod == 8,
                            FileName: fileName,
                            DataOffset: currentPos,
                            CompressedSize: actualCompressedSize,
                            FileLength: fileLength
                        );
                    }
                }
            }

            throw new FileNotFoundException("No encrypted files found in the archive.");
        }
    }
}