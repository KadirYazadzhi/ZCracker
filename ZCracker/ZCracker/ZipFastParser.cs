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
        long FileLength
    );

    public static class ZipFastParser
    {
        private const uint LocalFileHeaderSignature = 0x04034b50;
        private const uint CentralDirectoryHeaderSignature = 0x02014b50;
        private const uint EndOfCentralDirSignature = 0x06054b50;

        public static TargetFileMetadata Parse(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                // 1. Locate End of Central Directory (EOCD) Record
                // Scan backwards from end of file.
                long fileLength = fs.Length;
                if (fileLength < 22) throw new InvalidDataException("File too short to be a ZIP.");

                long scanStart = Math.Max(0, fileLength - 65536); // Scan last 64KB
                fs.Seek(scanStart, SeekOrigin.Begin);
                
                byte[] buffer = new byte[fileLength - scanStart];
                fs.ReadExactly(buffer, 0, buffer.Length);

                int eocdOffset = -1;
                for (int i = buffer.Length - 4; i >= 0; i--)
                {
                    if (buffer[i] == 0x50 && buffer[i+1] == 0x4b && buffer[i+2] == 0x05 && buffer[i+3] == 0x06)
                    {
                        eocdOffset = i;
                        break;
                    }
                }

                if (eocdOffset == -1) throw new InvalidDataException("Could not find End of Central Directory record.");

                // Read EOCD
                fs.Seek(scanStart + eocdOffset + 16, SeekOrigin.Begin); // Skip Signature(4)+Disks(4)+Entries(8)
                uint cdOffset = br.ReadUInt32(); // Offset of start of central directory

                // 2. Parse Central Directory to find the encrypted file
                fs.Seek(cdOffset, SeekOrigin.Begin);

                while (fs.Position < scanStart + eocdOffset)
                {
                    uint sig = br.ReadUInt32();
                    if (sig != CentralDirectoryHeaderSignature) break;

                    fs.Seek(4, SeekOrigin.Current); // Version made by / needed
                    ushort bitFlag = br.ReadUInt16();
                    ushort compressionMethod = br.ReadUInt16();
                    ushort lastModTime = br.ReadUInt16();
                    fs.Seek(2, SeekOrigin.Current); // Date

                    uint crc32 = br.ReadUInt32();
                    uint compressedSize = br.ReadUInt32();
                    uint uncompressedSize = br.ReadUInt32();

                    ushort fileNameLen = br.ReadUInt16();
                    ushort extraLen = br.ReadUInt16();
                    ushort commentLen = br.ReadUInt16();
                    fs.Seek(8, SeekOrigin.Current); // Disk start, internal attr, ext attr
                    
                    uint localHeaderOffset = br.ReadUInt32();

                    byte[] nameBytes = br.ReadBytes(fileNameLen);
                    string fileName = Encoding.UTF8.GetString(nameBytes);

                    // Skip Extra & Comment
                    if (extraLen > 0) fs.Seek(extraLen, SeekOrigin.Current);
                    if (commentLen > 0) fs.Seek(commentLen, SeekOrigin.Current);

                    bool isEncrypted = (bitFlag & 1) == 1;

                    // We found an encrypted file!
                    if (isEncrypted)
                    {
                        // Now we have the RELIABLE metadata from Central Directory.
                        // We must go to the Local Header to find the Data Offset (skipping variable fields).
                        
                        long savedPos = fs.Position;
                        fs.Seek(localHeaderOffset, SeekOrigin.Begin);
                        
                        if (br.ReadUInt32() != LocalFileHeaderSignature) 
                            throw new InvalidDataException("Local Header mismatch.");

                        fs.Seek(22, SeekOrigin.Current); // Skip to Name Length
                        ushort localNameLen = br.ReadUInt16();
                        ushort localExtraLen = br.ReadUInt16();
                        
                        // Data starts after Name + Extra
                        long dataOffset = localHeaderOffset + 30 + localNameLen + localExtraLen;
                        
                        // Read the Encryption Header (12 bytes) from the data stream
                        fs.Seek(dataOffset, SeekOrigin.Begin);
                        byte[] encryptionHeader = br.ReadBytes(12);

                        // Header Verifier Logic
                        uint headerVerifier = crc32;
                        bool hasDataDescriptor = (bitFlag & 8) != 0;
                        if (hasDataDescriptor)
                        {
                            headerVerifier = (uint)(lastModTime << 16);
                        }
                        
                        // Adjust CompressedSize:
                        // Central Directory "Compressed Size" usually includes the 12-byte header? 
                        // Actually, CD size is size of compressed stream. 
                        // Encrypted stream = 12 header + Compressed Data.
                        // So usually CD size = 12 + DeflatedSize.
                        // If we read 'CompressedSize' bytes starting at 'DataOffset', we get header + data.
                        // Which is exactly what ZipVerifier needs.
                        
                        return new TargetFileMetadata(
                            EncryptionHeader: encryptionHeader,
                            HeaderVerifier: headerVerifier,
                            RealCrc32: crc32, // TRUST THE CENTRAL DIRECTORY CRC!
                            IsEncrypted: isEncrypted,
                            IsSupported: compressionMethod == 0 || compressionMethod == 8,
                            FileName: fileName,
                            DataOffset: dataOffset, // Points to the 12-byte header
                            CompressedSize: compressedSize,
                            FileLength: fileLength
                        );
                    }
                }
            }

            throw new FileNotFoundException("No encrypted files found in the archive.");
        }
    }
}
