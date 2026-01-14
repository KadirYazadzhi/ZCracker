using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading;

namespace ZCracker
{
    /// <summary>
    /// Represents a pointer and length to a password in memory.
    /// </summary>
    public readonly struct PasswordEntry
    {
        public readonly unsafe byte* Pointer;
        public readonly int Length;

        public unsafe PasswordEntry(byte* pointer, int length)
        {
            Pointer = pointer;
            Length = length;
        }
        
        public override string ToString()
        {
            unsafe
            {
                return System.Text.Encoding.UTF8.GetString(Pointer, Length);
            }
        }
    }

    /// <summary>
    /// A fixed-size batch of passwords to reduce channel overhead.
    /// </summary>
    public unsafe struct PasswordBatch
    {
        public const int Capacity = 1024;
        public int Count;
        public fixed long Pointers[Capacity]; // Storing pointer as long
        public fixed int Lengths[Capacity];

        public void Add(byte* ptr, int len)
        {
            if (Count < Capacity)
            {
                Pointers[Count] = (long)ptr;
                Lengths[Count] = len;
                Count++;
            }
        }
        
        public bool IsFull => Count >= Capacity;
        
        public void Clear()
        {
            Count = 0;
        }

        public PasswordEntry Get(int index)
        {
            return new PasswordEntry((byte*)Pointers[index], Lengths[index]);
        }
    }

    public unsafe class ZeroAllocFileReader : IDisposable
    {
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly byte* _basePointer;
        private readonly long _fileLength;

        public ZeroAllocFileReader(string filePath)
        {
            _fileLength = new FileInfo(filePath).Length;
            _mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref _basePointer);
        }

        /// <summary>
        /// Fast scan using AVX2 to find newlines and yield batches.
        /// </summary>
        public void ProduceBatches(System.Threading.Channels.ChannelWriter<PasswordBatch> writer, CancellationToken token)
        {
            byte* current = _basePointer;
            byte* end = _basePointer + _fileLength;
            byte* lineStart = current;
            
            var batch = new PasswordBatch();

            // Vector setup for finding newlines
            Vector256<byte> vNewLine = Vector256.Create((byte)'\n');
            Vector256<byte> vReturn = Vector256.Create((byte)'\r');

            while (current < end)
            {
                if (token.IsCancellationRequested) break;

                // Simple scalar loop fallback for simplicity and edge cases, 
                // or hybrid approach. For simplicity + speed, manual unroll or SIMD scan.
                // Let's do a smart scalar scan first to verify logic, it's often IO bound anyway, 
                // but since it's memory mapped, CPU is the limit.
                
                // Optimized SIMD Search for next \n
                // We search from 'current'
                
                byte* searchPtr = current;
                bool found = false;
                
                // SIMD Scan Loop
                while (searchPtr + 32 <= end)
                {
                    Vector256<byte> vBlock = Avx.LoadVector256(searchPtr);
                    Vector256<byte> vEq = Avx2.CompareEqual(vBlock, vNewLine);
                    int mask = Avx2.MoveMask(vEq);

                    if (mask != 0)
                    {
                        // Found a newline
                        int offset = System.Numerics.BitOperations.TrailingZeroCount(mask);
                        byte* lineEnd = searchPtr + offset;
                        
                        // Handle \r if present (Windows style)
                        int len = (int)(lineEnd - lineStart);
                        if (len > 0 && *(lineEnd - 1) == '\r')
                        {
                            len--;
                        }

                        // Add to batch
                        batch.Add(lineStart, len);
                        if (batch.IsFull)
                        {
                            writer.TryWrite(batch); // Blocking or TryWrite? Ideally WriteAsync but we are in tight loop.
                            // Since we are producer, we might fill up. 
                            // Channel handles backpressure.
                            // We use a SpinWait or simple loop if full.
                            while(!writer.TryWrite(batch)) 
                            { 
                                if (token.IsCancellationRequested) return; 
                                Thread.SpinWait(1); 
                            }
                            batch.Clear();
                        }

                        // Move pointers
                        current = lineEnd + 1;
                        lineStart = current;
                        found = true;
                        break; 
                    }
                    searchPtr += 32;
                }

                if (!found)
                {
                    // Fallback to scalar for remaining bytes or if SIMD didn't find in this block
                    if (searchPtr >= end) 
                    {
                        // Process trailing line
                        if (lineStart < end)
                        {
                            int len = (int)(end - lineStart);
                             // Handle trailing \r
                            if (len > 0 && *(end - 1) == '\r') len--;
                            
                            batch.Add(lineStart, len);
                        }
                        break; // EOF
                    }
                    
                    // Scalar scan until newline or end
                    while (current < end && *current != '\n')
                    {
                        current++;
                    }
                    
                    int remainingLen = (int)(current - lineStart);
                    if (remainingLen > 0 && current > _basePointer && *(current - 1) == '\r') remainingLen--;

                    batch.Add(lineStart, remainingLen);
                     if (batch.IsFull)
                    {
                        while(!writer.TryWrite(batch)) 
                        {
                             if (token.IsCancellationRequested) return;
                             Thread.SpinWait(1);
                        }
                        batch.Clear();
                    }
                    
                    current++; // Skip \n
                    lineStart = current;
                }
            }

            // Flush remaining batch
            if (batch.Count > 0)
            {
                writer.TryWrite(batch);
            }
            
            writer.Complete();
        }

        public void Dispose()
        {
            if (_basePointer != null)
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
            _accessor?.Dispose();
            _mmf?.Dispose();
        }
    }
}
