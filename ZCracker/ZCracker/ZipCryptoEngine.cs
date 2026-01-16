using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ZCracker
{
    public unsafe class ZipCryptoEngine
    {
        private static readonly uint* CrcTable;

        static ZipCryptoEngine()
        {
            // Allocate unmanaged memory for the CRC table to ensure it's pinned and cache-friendly
            // This is a one-time static allocation.
            CrcTable = (uint*)NativeMemory.Alloc(256 * sizeof(uint));
            GenerateCrcTable();
        }

        private static void GenerateCrcTable()
        {
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
                CrcTable[i] = crc;
            }
        }

        // The ZipCrypto state
        private uint _key0;
        private uint _key1;
        private uint _key2;

        public ZipCryptoEngine()
        {
            Reset();
        }

        /// <summary>
        /// Decrypts a buffer in place.
        /// </summary>
        public void DecryptBuffer(byte[] buffer, int length)
        {
            // Decrypt byte by byte
            for (int i = 0; i < length; i++)
            {
                byte c = buffer[i];
                byte p = (byte)(c ^ DecryptByte());
                UpdateKeys(p);
                buffer[i] = p;
            }
        }

        public void InitKeys(string password)
        {
            Reset();
            foreach (char c in password)
            {
                UpdateKeys((byte)c);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Reset()
        {
            _key0 = 305419896;
            _key1 = 591751049;
            _key2 = 878082192;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateKeys(byte c)
        {
            _key0 = CrcTable[(_key0 ^ c) & 0xFF] ^ (_key0 >> 8);
            _key1 = _key1 + (_key0 & 0xFF);
            _key1 = (_key1 * 134775813) + 1;
            _key2 = CrcTable[(_key2 ^ (byte)(_key1 >> 24)) & 0xFF] ^ (_key2 >> 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte DecryptByte()
        {
            uint temp = _key2 | 2;
            return (byte)((temp * (temp ^ 1)) >> 8);
        }
        
        /// <summary>
        /// Decrypts a single byte and updates the keys. Useful for streams.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte DecryptByteAndUpdate()
        {
            byte val = DecryptByte(); // Decrypt using current keys
            // But wait, the standard loop is:
            // c = cipher
            // p = c ^ k
            // UpdateKeys(p)
            // We don't have 'c' here. This helper design in stream was:
            // buffer[i] = _engine.DecryptByteAndUpdate(); <-- This is wrong.
            // We need to pass the cipher byte.
            throw new NotImplementedException("Use DecryptByteAndUpdate(byte c) instead");
        }

        /// <summary>
        /// Decrypts a single cipher byte, updates keys, and returns the plain byte.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte DecryptByteAndUpdate(byte cipherByte)
        {
            byte plain = (byte)(cipherByte ^ DecryptByte());
            UpdateKeys(plain);
            return plain;
        }

        /// <summary>
        /// Highly optimized, zero-allocation password check.
        /// </summary>
        /// <param name="encryptedHeader">The 12 bytes of encryption header from the zip file.</param>
        /// <param name="crcCheck">The high byte (or high word) of the file's CRC32.</param>
        /// <param name="password">The password to test.</param>
        /// <returns>True if the password decrypts the header correctly.</returns>
        public bool CheckPassword(byte[] encryptedHeader, uint crcCheck, string password)
        {
            Reset();

            // Process password bytes directly from the string memory (fixed) or stack buffer.
            // Assuming most passwords are ASCII/ANSI. 
            // We iterate the string characters directly.
            
            fixed (char* ptr = password)
            {
                char* current = ptr;
                // Optimization: String length lookup is fast, avoid overhead if possible.
                // We use a simplified loop.
                int len = password.Length;
                for (int i = 0; i < len; i++)
                {
                    // Cast char to byte. This assumes the password is compatible with the zip encoding 
                    // (usually CodePage 437 or UTF-8, but for brute force standard ASCII is 99% of cases).
                    // This avoids Encoding.UTF8.GetBytes allocation.
                    UpdateKeys((byte)*current);
                    current++;
                }
            }

            // Decrypt the 12-byte header
            // We use a fixed block for the encrypted header to avoid bounds checks
            fixed (byte* headerPtr = encryptedHeader)
            {
                // We only need to verify the last byte (byte 11) against the CRC high byte.
                // However, we must run the state machine for the first 11 bytes.
                
                // Decrypt bytes 0-10
                byte c = headerPtr[0]; UpdateKeys((byte)(c ^ DecryptByte()));
                c = headerPtr[1]; UpdateKeys((byte)(c ^ DecryptByte()));
                c = headerPtr[2]; UpdateKeys((byte)(c ^ DecryptByte()));
                c = headerPtr[3]; UpdateKeys((byte)(c ^ DecryptByte()));
                c = headerPtr[4]; UpdateKeys((byte)(c ^ DecryptByte()));
                c = headerPtr[5]; UpdateKeys((byte)(c ^ DecryptByte()));
                c = headerPtr[6]; UpdateKeys((byte)(c ^ DecryptByte()));
                c = headerPtr[7]; UpdateKeys((byte)(c ^ DecryptByte()));
                c = headerPtr[8]; UpdateKeys((byte)(c ^ DecryptByte()));
                c = headerPtr[9]; UpdateKeys((byte)(c ^ DecryptByte()));
                c = headerPtr[10]; UpdateKeys((byte)(c ^ DecryptByte()));

                // Decrypt byte 11 and compare
                byte result = (byte)(headerPtr[11] ^ DecryptByte());
                
                // The verification byte is the high byte of the 16-bit CRC of the file 
                // (or high byte of the last modification time if bit 3 is set, but typically it's CRC).
                // We expect crcCheck to be that byte.
                return result == (byte)((crcCheck >> 24) & 0xFF);
            }
        }
    }
}
