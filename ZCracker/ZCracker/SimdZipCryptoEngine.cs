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
        
        // Helper for Bitwise Blend (Select) to avoid casting to Byte/Double for BlendVariable
        // Returns: (left & ~mask) | (right & mask)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<uint> Blend(Vector256<uint> left, Vector256<uint> right, Vector256<uint> mask)
        {
             return Avx2.Or(Avx2.And(left, Avx2.Not(mask)), Avx2.And(right, mask));
        }

        /// <summary>
        /// Checks a batch of 8 passwords using AVX2.
        /// </summary>
        public int CheckBatch(byte[] encryptedHeader, uint crcCheck, char** passwords, int* lengths)
        {
            // Initial State
            Vector256<uint> vKey0 = Vector256.Create(305419896u);
            Vector256<uint> vKey1 = Vector256.Create(591751049u);
            Vector256<uint> vKey2 = Vector256.Create(878082192u);

            // Determine max length
            int maxLen = 0;
            for(int i=0; i<8; i++) maxLen = Math.Max(maxLen, lengths[i]);

            uint* pChars = stackalloc uint[8];
            uint* pActive = stackalloc uint[8];

            // 1. Process Passwords (SIMD)
            for (int i = 0; i < maxLen; i++)
            {
                // Prepare chars and mask
                for(int lane=0; lane<8; lane++)
                {
                    if (i < lengths[lane])
                    {
                        pChars[lane] = (byte)passwords[lane][i];
                        pActive[lane] = 0xFFFFFFFF; 
                    }
                    else
                    {
                        pChars[lane] = 0;
                        pActive[lane] = 0;
                    }
                }
                
                Vector256<uint> vChar = Avx.LoadVector256(pChars);
                Vector256<uint> vMask = Avx.LoadVector256(pActive);
                
                // Update Key0
                // index = (key0 ^ c) & 0xff
                Vector256<uint> vIndex = Avx2.And(Avx2.Xor(vKey0, vChar), VecCrcMaskFF);
                
                // key0 = CrcTable[index] ^ (key0 >> 8)
                // Gather requires int* and Vector256<int>. 4 is scale (sizeof uint).
                Vector256<int> vCrcLookupInt = Avx2.GatherVector256((int*)CrcTable, vIndex.AsInt32(), 4);
                Vector256<uint> vCrcLookup = vCrcLookupInt.AsUInt32();
                
                Vector256<uint> vKey0New = Avx2.Xor(vCrcLookup, Avx2.ShiftRightLogical(vKey0, 8));
                
                // Blend: Only update if active
                vKey0 = Blend(vKey0, vKey0New, vMask);

                // Update Key1
                // key1 = key1 + (key0 & 0xff)
                // key1 = key1 * 134775813 + 1
                Vector256<uint> vKey0Low = Avx2.And(vKey0, VecCrcMaskFF);
                Vector256<uint> vKey1New = Avx2.Add(vKey1, vKey0Low);
                vKey1New = Avx2.Add(Avx2.MultiplyLow(vKey1New, VecKey1Mult), VecOne);
                
                vKey1 = Blend(vKey1, vKey1New, vMask);

                // Update Key2
                // key2 = CrcTable[(key2 ^ (key1 >> 24)) & 0xff] ^ (key2 >> 8)
                Vector256<uint> vKey1Shift = Avx2.ShiftRightLogical(vKey1, 24);
                vIndex = Avx2.And(Avx2.Xor(vKey2, vKey1Shift), VecCrcMaskFF);
                
                vCrcLookupInt = Avx2.GatherVector256((int*)CrcTable, vIndex.AsInt32(), 4);
                vCrcLookup = vCrcLookupInt.AsUInt32();

                Vector256<uint> vKey2New = Avx2.Xor(vCrcLookup, Avx2.ShiftRightLogical(vKey2, 8));
                
                vKey2 = Blend(vKey2, vKey2New, vMask);
            }

            // 2. Process Encrypted Header
            fixed (byte* headerPtr = encryptedHeader)
            {
                for (int k = 0; k < 12; k++)
                {
                    byte c = headerPtr[k]; 
                    Vector256<uint> vC = Vector256.Create((uint)c);

                    // Decrypt Byte Logic
                    // temp = key2 | 2
                    Vector256<uint> vTemp = Avx2.Or(vKey2, Vector256.Create(2u));
                    // val = (temp * (temp ^ 1)) >> 8
                    Vector256<uint> vDecrypt = Avx2.ShiftRightLogical(Avx2.MultiplyLow(vTemp, Avx2.Xor(vTemp, VecOne)), 8);
                    
                    Vector256<uint> vDecryptedByte = Avx2.And(vDecrypt, VecCrcMaskFF);
                    Vector256<uint> vP = Avx2.Xor(vC, vDecryptedByte);
                    
                    if (k == 11)
                    {
                        // Check crc
                        Vector256<uint> vCheck = Vector256.Create(crcCheck >> 24);
                        Vector256<uint> vMatch = Avx2.CompareEqual(vP, vCheck); 
                        
                        if (!Avx.TestZ(vMatch, vMatch))
                        {
                            uint mask = (uint)Avx2.MoveMask(vMatch.AsByte());
                            for(int lane=0; lane<8; lane++)
                            {
                                // MoveMask creates 32 bits from 32 bytes.
                                // Each 32-bit integer is 4 bytes.
                                // If any byte in the integer is FF, the corresponding bit is 1.
                                // Since CompareEqual sets all bytes to FF for true, we look at any bit for that lane.
                                // Lane 0 is bits 0,1,2,3.
                                if ((mask & (1 << (lane * 4))) != 0) return lane;
                            }
                        }
                        return -1;
                    }

                    // Key0
                    Vector256<uint> vIndex = Avx2.And(Avx2.Xor(vKey0, vP), VecCrcMaskFF);
                    Vector256<int> vCrcLookupInt = Avx2.GatherVector256((int*)CrcTable, vIndex.AsInt32(), 4);
                    vKey0 = Avx2.Xor(vCrcLookupInt.AsUInt32(), Avx2.ShiftRightLogical(vKey0, 8));

                    // Key1
                    Vector256<uint> vKey0Low = Avx2.And(vKey0, VecCrcMaskFF);
                    vKey1 = Avx2.Add(vKey1, vKey0Low);
                    vKey1 = Avx2.Add(Avx2.MultiplyLow(vKey1, VecKey1Mult), VecOne);

                    // Key2
                    Vector256<uint> vKey1Shift = Avx2.ShiftRightLogical(vKey1, 24);
                    vIndex = Avx2.And(Avx2.Xor(vKey2, vKey1Shift), VecCrcMaskFF);
                    vCrcLookupInt = Avx2.GatherVector256((int*)CrcTable, vIndex.AsInt32(), 4);
                    vKey2 = Avx2.Xor(vCrcLookupInt.AsUInt32(), Avx2.ShiftRightLogical(vKey2, 8));
                }
            }

            return -1;
        }
    }
}