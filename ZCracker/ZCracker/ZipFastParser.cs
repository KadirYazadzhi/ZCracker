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
        string FileName
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
                        // If we don't find a local header immediately, this might be a central directory or we need to scan.
                        // For this high-performance parser, we assume the first valid entry starts at 0 or we scan forward.
                        // In a robust tool, we'd scan, but here we'll throw or skip.
                        // Let's try to scan forward byte by byte if not found (slow, but robust) 
                        // or just assume standard zip structure where the first file is at 0.
                        // We will return failure for this simple implementation if not found at 0.
                        throw new InvalidDataException("Could not find Local File Header signature at expected location.");
                    }

                    // Extract version (2)
                    fs.Seek(2, SeekOrigin.Current);

                    // General Purpose Bit Flag (2)
                    ushort bitFlag = br.ReadUInt16();
                    bool isEncrypted = (bitFlag & 1) == 1;
                    bool hasDataDescriptor = (bitFlag & 8) != 0;

                    // Compression Method (2)
                    ushort compressionMethod = br.ReadUInt16();

                    // Last Mod Time (2)
                    ushort lastModTime = br.ReadUInt16();

                    // Last Mod Date (2)
                    fs.Seek(2, SeekOrigin.Current);

                    // CRC-32 (4)
                    uint crc32 = br.ReadUInt32();

                    // Compressed Size (4)
                    // Uncompressed Size (4)
                    fs.Seek(8, SeekOrigin.Current);

                    // File Name Length (2)
                    ushort fileNameLen = br.ReadUInt16();

                    // Extra Field Length (2)
                    ushort extraFieldLen = br.ReadUInt16();

                    // File Name
                    byte[] nameBytes = br.ReadBytes(fileNameLen);
                    string fileName = Encoding.UTF8.GetString(nameBytes);

                    // Skip Extra Field
                    if (extraFieldLen > 0)
                    {
                        fs.Seek(extraFieldLen, SeekOrigin.Current);
                    }

                    // If encrypted, the next 12 bytes are the encryption header
                    byte[] encryptionHeader = Array.Empty<byte>();

                    if (isEncrypted)
                    {
                        encryptionHeader = br.ReadBytes(12);

                        // Handling the verification byte logic
                        // If bit 3 (hasDataDescriptor) is set, the CRC in the header is 0.
                        // The verification byte becomes the high byte of the Last Mod Time.
                        // We need to adjust the "Expected CRC" we return to the engine so the engine can check efficiently.
                        
                        uint checkValue = crc32;
                        if (hasDataDescriptor)
                        {
                            // If data descriptor is present, the CRC is in the descriptor after data, 
                            // and the header verification byte is the high byte of LastModTime.
                            // We mock the CRC uint so the high byte matches LastModTime for the check.
                            checkValue = (uint)(lastModTime << 16); 
                            // Note: We shift it to the high byte position (bits 24-31) because our Engine checks (crc >> 24).
                            // lastModTime is 16 bit. High byte is bits 8-15.
                            // So we want bits 8-15 of lastModTime to be at bits 24-31 of checkValue.
                            // checkValue = (uint)lastModTime << 16; -> 
                            // e.g. Time = 0xAABB. High byte AA. 
                            // checkValue = 0xAABB0000. High byte (>>24) is AA. Correct.
                        }
                        
                        return new TargetFileMetadata(
                            EncryptionHeader: encryptionHeader,
                            Crc32: checkValue,
                            IsEncrypted: isEncrypted,
                            IsSupported: compressionMethod == 0 || compressionMethod == 8, // Store or Deflate
                            FileName: fileName
                        );
                    }
                    
                    // If not encrypted or we want to look for the next file, we would loop here.
                    // For this specific brute-force tool, we stop at the first encrypted file found.
                }
            }

            throw new FileNotFoundException("No encrypted files found in the archive.");
        }
    }
}
