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
                if (metadata.CompressedSize > int.MaxValue) return false;

                byte[] fileData;
                using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Seek(metadata.DataOffset, SeekOrigin.Begin);
                    fileData = new byte[metadata.CompressedSize];
                    fs.ReadExactly(fileData, 0, (int)metadata.CompressedSize);
                }

                var engine = new ZipCryptoEngine();
                engine.InitKeys(password);

                // Decrypt
                engine.DecryptBuffer(fileData, fileData.Length);

                // Verify Header byte (sanity check)
                byte checkByte = (byte)((metadata.HeaderVerifier >> 24) & 0xFF);
                if (fileData[11] != checkByte) return false; 

                // Verify Content CRC
                // Calculate CRC32 of the *Payload* (bytes 12 onwards)
                uint calculatedCrc = Crc32Calculator.Compute(fileData, 12, fileData.Length - 12);

                return calculatedCrc == metadata.RealCrc32;
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