using System;
using System.Linq;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;
using System.Text;

namespace ZCracker
{
    public class GpuZipCryptoEngine : IDisposable
    {
        private Context? _context;
        private Accelerator? _accelerator;
        private MemoryBuffer1D<uint, Stride1D.Dense>? _crcTableBuffer;
        
        private Action<Index1D, ArrayView<byte>, ArrayView<int>, int, ArrayView<byte>, uint, ArrayView<int>, ArrayView<uint>>? _kernel;

        public bool IsAvailable => _accelerator != null;
        public string DeviceName => _accelerator?.Name ?? "None";

        public GpuZipCryptoEngine()
        {
            try 
            {
                _context = Context.Create(builder => builder.Cuda().OpenCL().CPU());
                
                // Prefer CUDA, then OpenCL.
                var cudaDevices = _context.GetCudaDevices();
                var clDevices = _context.GetCLDevices();
                
                Device? device = null;
                if (cudaDevices.Count > 0)
                {
                    device = cudaDevices[0];
                }
                else if (clDevices.Count > 0)
                {
                    device = clDevices[0];
                }

                if (device != null)
                {
                    _accelerator = device.CreateAccelerator(_context);
                    
                    uint[] crcTableHost = GenerateCrcTable();
                    _crcTableBuffer = _accelerator.Allocate1D(crcTableHost);
                    
                    _kernel = _accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<int>, int, ArrayView<byte>, uint, ArrayView<int>, ArrayView<uint>>(DecryptKernel);
                }
            }
            catch (Exception)
            {
                _context?.Dispose();
                _context = null;
            }
        }

        public void Dispose()
        {
            _crcTableBuffer?.Dispose();
            _accelerator?.Dispose();
            _context?.Dispose();
        }

        private static uint[] GenerateCrcTable()
        {
            uint[] table = new uint[256];
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
                table[i] = crc;
            }
            return table;
        }

        private static void DecryptKernel(
            Index1D index,
            ArrayView<byte> passwordsFlat,
            ArrayView<int> offsets,
            int count,
            ArrayView<byte> header,
            uint crcCheck,
            ArrayView<int> results,
            ArrayView<uint> crcTable)
        {
            int idx = index; // Explicit conversion
            if (idx >= count) return;

            int start = offsets[idx];
            int end = (idx == count - 1) ? (int)passwordsFlat.Length : offsets[idx + 1];
            int len = end - start;

            uint key0 = 305419896;
            uint key1 = 591751049;
            uint key2 = 878082192;

            for (int i = 0; i < len; i++)
            {
                byte c = passwordsFlat[(int)(start + i)];
                key0 = crcTable[(int)((key0 ^ c) & 0xFF)] ^ (key0 >> 8);
                key1 = key1 + (key0 & 0xFF);
                key1 = key1 * 134775813 + 1;
                key2 = crcTable[(int)((key2 ^ (byte)(key1 >> 24)) & 0xFF)] ^ (key2 >> 8);
            }

            for (int k = 0; k < 12; k++)
            {
                byte c = header[k];
                
                uint temp = key2 | 2;
                byte decrypt = (byte)((temp * (temp ^ 1)) >> 8);
                
                if (k == 11)
                {
                    byte result = (byte)(c ^ decrypt);
                    if (result == (byte)(crcCheck >> 24))
                    {
                        results[0] = idx; 
                    }
                    return;
                }

                byte p = (byte)(c ^ decrypt);
                key0 = crcTable[(int)((key0 ^ p) & 0xFF)] ^ (key0 >> 8);
                key1 = key1 + (key0 & 0xFF);
                key1 = key1 * 134775813 + 1;
                key2 = crcTable[(int)((key2 ^ (byte)(key1 >> 24)) & 0xFF)] ^ (key2 >> 8);
            }
        }
        
        public int CheckBatch(byte[][] passwords, byte[] header, uint crcCheck)
        {
            if (_accelerator == null || _kernel == null || _crcTableBuffer == null) return -1;

            int totalLen = 0;
            for (int i = 0; i < passwords.Length; i++) totalLen += passwords[i].Length;

            byte[] flat = new byte[totalLen];
            int[] offsets = new int[passwords.Length];
            int current = 0;
            for (int i = 0; i < passwords.Length; i++)
            {
                offsets[i] = current;
                Array.Copy(passwords[i], 0, flat, current, passwords[i].Length);
                current += passwords[i].Length;
            }

            using var dFlat = _accelerator.Allocate1D(flat);
            using var dOffsets = _accelerator.Allocate1D(offsets);
            using var dHeader = _accelerator.Allocate1D(header);
            using var dResult = _accelerator.Allocate1D(new int[] { -1 });
            
            _kernel(passwords.Length, dFlat.View, dOffsets.View, passwords.Length, dHeader.View, crcCheck, dResult.View, _crcTableBuffer.View);
            
            _accelerator.Synchronize();
            
            int[] res = dResult.GetAsArray1D();
            return res[0];
        }
    }
}