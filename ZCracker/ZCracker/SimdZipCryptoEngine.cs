using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace ZCracker
{
    public unsafe class SimdZipCryptoEngine
    {
        private static readonly uint* CrcTable;
        private static readonly Vector256<uint> VecCrcMaskFF;
        private static readonly Vector256<uint> VecKey1Mult;
        private static readonly Vector256<uint> VecOne;

        static SimdZipCryptoEngine()
        {
            CrcTable = (uint*)NativeMemory.Alloc(256 * sizeof(uint));
            GenerateCrcTable();
            
            VecCrcMaskFF = Vector256.Create((uint)0xFF);
            VecKey1Mult = Vector256.Create(134775813u);
            VecOne = Vector256.Create(1u);
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

        /// <summary>
        /// Checks a batch of 8 passwords using AVX2.
        /// </summary>
        /// <param name="encryptedHeader">The 12 byte header.</param>
        /// <param name="crcCheck">The target verification byte.</param>
        /// <param name="passwords">Array of 8 pointers to password strings (char*).</param>
        /// <param name="lengths">Array of 8 lengths.</param>
        /// <returns>Index of found password or -1.</returns>
        public int CheckBatch(byte[] encryptedHeader, uint crcCheck, char** passwords, int* lengths)
        {
            // Initial State
            Vector256<uint> vKey0 = Vector256.Create(305419896u);
            Vector256<uint> vKey1 = Vector256.Create(591751049u);
            Vector256<uint> vKey2 = Vector256.Create(878082192u);

            // Determine max length to loop
            int maxLen = 0;
            for(int i=0; i<8; i++) maxLen = Math.Max(maxLen, lengths[i]);

            // 1. Process Passwords (SIMD)
            for (int i = 0; i < maxLen; i++)
            {
                // Gather characters for position i
                // We construct a vector of chars (promoted to uint)
                // If i >= length, we effectively treat it as 0 or handle masking? 
                // ZipCrypto state must NOT update if we are past end of password.
                // This implies we need a mask for active lanes.
                
                uint* pChars = stackalloc uint[8];
                uint* pActive = stackalloc uint[8];
                
                for(int lane=0; lane<8; lane++)
                {
                    if (i < lengths[lane])
                    {
                        pChars[lane] = (byte)passwords[lane][i];
                        pActive[lane] = 0xFFFFFFFF; // All 1s
                    }
                    else
                    {
                        pChars[lane] = 0;
                        pActive[lane] = 0;
                    }
                }
                
                Vector256<uint> vChar = Avx.LoadVector256(pChars);
                Vector256<uint> vMask = Avx.LoadVector256(pActive);
                
                // If no lanes are active, break (optimization)
                if (Avx.TestZ(vMask, vMask)) break; // Actually can't break early if we just use maxLen loop but okay.

                // Update Key0
                // index = (key0 ^ c) & 0xff
                Vector256<uint> vIndex = Avx2.And(Avx2.Xor(vKey0, vChar), VecCrcMaskFF);
                
                // key0 = CrcTable[index] ^ (key0 >> 8)
                // GATHER: Base=CrcTable, Index=vIndex, Scale=4
                Vector256<uint> vCrcLookup = Avx2.GatherVectorInt32(CrcTable, vIndex, 4);
                Vector256<uint> vKey0New = Avx2.Xor(vCrcLookup, Avx2.ShiftRightLogical(vKey0, 8));
                
                // Blend: Only update if active
                vKey0 = Avx.BlendVariable(vKey0, vKey0New, vMask);

                // Update Key1
                // key1 = key1 + (key0 & 0xff)
                // key1 = key1 * 134775813 + 1
                Vector256<uint> vKey0Low = Avx2.And(vKey0, VecCrcMaskFF);
                Vector256<uint> vKey1New = Avx2.Add(vKey1, vKey0Low);
                vKey1New = Avx2.Add(Avx2.MultiplyLow(vKey1New, VecKey1Mult), VecOne);
                
                vKey1 = Avx.BlendVariable(vKey1, vKey1New, vMask);

                // Update Key2
                // key2 = CrcTable[(key2 ^ (key1 >> 24)) & 0xff] ^ (key2 >> 8)
                Vector256<uint> vKey1Shift = Avx2.ShiftRightLogical(vKey1, 24);
                vIndex = Avx2.And(Avx2.Xor(vKey2, vKey1Shift), VecCrcMaskFF);
                vCrcLookup = Avx2.GatherVectorInt32(CrcTable, vIndex, 4);
                Vector256<uint> vKey2New = Avx2.Xor(vCrcLookup, Avx2.ShiftRightLogical(vKey2, 8));
                
                vKey2 = Avx.BlendVariable(vKey2, vKey2New, vMask);
            }

            // 2. Process Encrypted Header (12 bytes)
            // The header is the same for all 8 lanes!
            // But the DECRYPT output differs because Keys differ.
            
            fixed (byte* headerPtr = encryptedHeader)
            {
                for (int k = 0; k < 12; k++)
                {
                    byte c = headerPtr[k]; // Same byte for all
                    Vector256<uint> vC = Vector256.Create((uint)c);

                    // Decrypt Byte Logic
                    // temp = key2 | 2
                    Vector256<uint> vTemp = Avx2.Or(vKey2, Vector256.Create(2u));
                    // val = (temp * (temp ^ 1)) >> 8
                    Vector256<uint> vDecrypt = Avx2.ShiftRightLogical(Avx2.MultiplyLow(vTemp, Avx2.Xor(vTemp, VecOne)), 8);
                    // result = (byte)vDecrypt. We need just low byte.
                    Vector256<uint> vDecryptedByte = Avx2.And(vDecrypt, VecCrcMaskFF);
                    
                    // P = C ^ Decrypt
                    // For update keys we need P.
                    Vector256<uint> vP = Avx2.Xor(vC, vDecryptedByte);
                    
                    // Check logic (Only needed at k=11)
                    if (k == 11)
                    {
                        // Compare vP against crcCheck
                        Vector256<uint> vCheck = Vector256.Create(crcCheck >> 24);
                        Vector256<uint> vMatch = Avx2.CompareEqual(vP, vCheck); // -1 (0xFF..) if match, 0 if not
                        
                        if (!Avx.TestZ(vMatch, vMatch))
                        {
                            // Found something!
                            // Extract which lane
                            uint mask = (uint)Avx2.MoveMask(vMatch.AsByte());
                            // MoveMask returns 32 bits (4 per int32 lane).
                            // Lane 0: bits 0-3. Lane 1: bits 4-7.
                            for(int lane=0; lane<8; lane++)
                            {
                                if ((mask & (1 << (lane * 4))) != 0) return lane;
                            }
                        }
                        return -1;
                    }

                    // Update Keys with P (which is vC ^ Decrypt)
                    // Note: In CheckPassword logic: UpdateKeys((byte)(c ^ DecryptByte()))
                    // So we update with the decrypted byte.
                    
                    // Code reused from above (Update Keys logic)
                    // But here mask is all ones (always active)
                    
                    // Key0
                    Vector256<uint> vIndex = Avx2.And(Avx2.Xor(vKey0, vP), VecCrcMaskFF);
                    Vector256<uint> vCrcLookup = Avx2.GatherVectorInt32(CrcTable, vIndex, 4);
                    vKey0 = Avx2.Xor(vCrcLookup, Avx2.ShiftRightLogical(vKey0, 8));

                    // Key1
                    Vector256<uint> vKey0Low = Avx2.And(vKey0, VecCrcMaskFF);
                    vKey1 = Avx2.Add(vKey1, vKey0Low);
                    vKey1 = Avx2.Add(Avx2.MultiplyLow(vKey1, VecKey1Mult), VecOne);

                    // Key2
                    Vector256<uint> vKey1Shift = Avx2.ShiftRightLogical(vKey1, 24);
                    vIndex = Avx2.And(Avx2.Xor(vKey2, vKey1Shift), VecCrcMaskFF);
                    vCrcLookup = Avx2.GatherVectorInt32(CrcTable, vIndex, 4);
                    vKey2 = Avx2.Xor(vCrcLookup, Avx2.ShiftRightLogical(vKey2, 8));
                }
            }

            return -1;
        }
    }
}
