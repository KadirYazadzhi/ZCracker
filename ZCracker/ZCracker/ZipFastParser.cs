using System;
using System.IO;
using System.Text;

namespace ZCracker
{
    public readonly record struct TargetFileMetadata(
        byte[] EncryptionHeader,
        uint Crc32,
        bool IsEncrypted,
        bool IsSupported,
        string FileName,
        long DataOffset,
        long CompressedSize
    );

    public static class ZipFastParser
    {
        private const uint LocalFileHeaderSignature = 0x04034b50;

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

                    // Calculate where data starts
                    long currentPos = fs.Position;

                    if (isEncrypted)
                    {
                        byte[] encryptionHeader = br.ReadBytes(12);

                        uint checkValue = crc32;
                        if (hasDataDescriptor)
                        {
                            checkValue = (uint)(lastModTime << 16); 
                        }
                        
                        // We return the offset AFTER the 12-byte header, where the actual compressed data starts.
                        // Wait, normally the 12 bytes are prepended to the data.
                        // For verification, we need to decrypt those 12 bytes AND the data.
                        // So let's return the offset AT the 12 byte header (start of encryption stream).
                        
                        return new TargetFileMetadata(
                            EncryptionHeader: encryptionHeader,
                            Crc32: crc32, // Real CRC needed for verification
                            IsEncrypted: isEncrypted,
                            IsSupported: compressionMethod == 0 || compressionMethod == 8,
                            FileName: fileName,
                            DataOffset: currentPos, // Points to the 12-byte header
                            CompressedSize: compressedSize // Includes the 12-byte header? Usually yes in spec, but typically size is data + 12.
                        );
                    }
                }
            }

            throw new FileNotFoundException("No encrypted files found in the archive.");
        }
    }
}
