using System;
using System.IO;
using System.Text;

namespace ZCracker
{
    public readonly record struct TargetFileMetadata(
        byte[] EncryptionHeader,
        uint HeaderVerifier, // For Fast Check (12th byte)
        uint RealCrc32,      // For Deep Verify
        bool IsEncrypted,
        bool IsSupported,
        string FileName,
        long DataOffset,
        long CompressedSize
    );

    public static class ZipFastParser
    {
        private const uint LocalFileHeaderSignature = 0x04034b50;
        private const uint DataDescriptorSignature = 0x08074b50;

        public static TargetFileMetadata Parse(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                while (fs.Position < fs.Length)
                {
                    uint signature = br.ReadUInt32();
                    if (signature != LocalFileHeaderSignature)
                    {
                        throw new InvalidDataException("Could not find Local File Header signature at expected location.");
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

                        // 1. Determine Header Verifier (High byte of CRC or LastModTime)
                        // If bit 3 is set, CRC in header is invalid (0), so we use LastModTime for verification.
                        uint headerVerifier = crc32;
                        if (hasDataDescriptor)
                        {
                            headerVerifier = (uint)(lastModTime << 16);
                        }

                        // 2. Determine Real CRC32
                        uint realCrc32 = crc32;
                        long actualCompressedSize = compressedSize;

                        if (hasDataDescriptor)
                        {
                            // If bit 3 is set, the CRC32 in the header is NOT valid (it's 0).
                            // We MUST read it from the Data Descriptor (after the compressed data).
                            // Note: actualCompressedSize from header might be correct (if updated) or 0.
                            // The user log shows CompressedSize: 33, so it IS present.

                            if (actualCompressedSize > 0)
                            {
                                // IMPORTANT: BinaryReader buffers data. Mixing fs.Seek and br.Read breaks things.
                                // We must seek on fs, then use a NEW reader or read raw bytes.
                                
                                long descriptorPos = currentPos + actualCompressedSize;
                                
                                // Save position just in case
                                long savedPos = fs.Position;
                                fs.Seek(descriptorPos, SeekOrigin.Begin);

                                byte[] buffer = new byte[8];
                                int bytesRead = fs.Read(buffer, 0, 8);
                                
                                if (bytesRead >= 4)
                                {
                                    // Data Descriptor format: 
                                    // [Signature 4 bytes (optional)] [CRC32 4 bytes] [CompSize 4] [UncompSize 4]
                                    
                                    uint val1 = BitConverter.ToUInt32(buffer, 0);
                                    
                                    if (val1 == DataDescriptorSignature)
                                    {
                                        // Signature present, next 4 bytes are CRC
                                        if (bytesRead >= 8)
                                        {
                                            realCrc32 = BitConverter.ToUInt32(buffer, 4);
                                        }
                                    }
                                    else
                                    {
                                        // No signature, val1 IS the CRC
                                        realCrc32 = val1;
                                    }
                                }
                                
                                // Restore if we were iterating (not needed here since we return)
                                // fs.Seek(savedPos, SeekOrigin.Begin);
                            }
                        }

                        // Sanity Check: If realCrc32 is still 0, warn the user?
                        // But 0 is technically a valid CRC (rare).
                        
                        return new TargetFileMetadata(
                            EncryptionHeader: encryptionHeader,
                            HeaderVerifier: headerVerifier,
                            RealCrc32: realCrc32,
                            IsEncrypted: isEncrypted,
                            IsSupported: compressionMethod == 0 || compressionMethod == 8,
                            FileName: fileName,
                            DataOffset: currentPos,
                            CompressedSize: actualCompressedSize
                        );
                    }
                }
            }

            throw new FileNotFoundException("No encrypted files found in the archive.");
        }
    }
}
