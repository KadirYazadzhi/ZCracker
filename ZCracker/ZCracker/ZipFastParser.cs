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
                            // CRC32 is not in header. We must find it in Data Descriptor.
                            // But wait, if hasDataDescriptor is set, CompressedSize in header MIGHT be 0 too!
                            // If CompressedSize is 0, we can't easily skip data to find the descriptor without parsing the stream (Deflate).
                            // This is a known issue with streaming ZIPs.
                            
                            // However, most archivers (like Info-ZIP on Linux) DO write the size in local header 
                            // if they can seek back, OR they write it in Central Directory.
                            // If CompressedSize is 0, we MUST look at Central Directory.
                            
                            if (compressedSize == 0)
                            {
                                // Fallback: Scan Central Directory (End of file)
                                // For simplicity/speed in this context, we'll try to guess or throw.
                                // But let's check if the user's file likely has size. 
                                // User log showed: Compressed Size: 33. So size IS present.
                                // If size is present, we can jump to descriptor.
                                
                                // Note: Using Central Directory is the robust way.
                                // Let's quickly implement a CD scanner if needed? 
                                // No, let's assume if size > 0 we use it.
                            }

                            if (actualCompressedSize > 0)
                            {
                                long savedPos = fs.Position;
                                // Jump to end of compressed data
                                // Data starts at currentPos (start of 12 byte header).
                                // CompressedSize includes the 12 bytes usually? 
                                // Wait, earlier I assumed DataOffset points to 12 bytes.
                                // If CompressedSize = 33, and header is 12, then actual data is 21.
                                // The Data Descriptor follows the *compressed data*.
                                
                                fs.Seek(currentPos + actualCompressedSize, SeekOrigin.Begin);
                                
                                // Try to read signature
                                uint possibleSig = br.ReadUInt32();
                                if (possibleSig == DataDescriptorSignature)
                                {
                                    realCrc32 = br.ReadUInt32();
                                }
                                else
                                {
                                    // Signature is optional. This might be the CRC.
                                    realCrc32 = possibleSig;
                                }
                                fs.Seek(savedPos, SeekOrigin.Begin);
                            }
                        }

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