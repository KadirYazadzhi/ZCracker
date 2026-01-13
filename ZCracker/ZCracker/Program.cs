using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ZCracker
{
    class Program
    {
        private static long _attempts = 0;
        private static bool _found = false;
        private static string _foundPassword = string.Empty;

        static void Main(string[] args)
        {
            PrintBanner();
            Run();
        }

        private static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine(@"
▒███████▒ ▄████▄   ██▀███   ▄▄▄       ▄████▄   ██ ▄█▀▓█████  ██▀███  
▒ ▒ ▒ ▄▀░▒██▀ ▀█  ▓██ ▒ ██▒▒████▄    ▒██▀ ▀█   ██▄█▒ ▓█   ▀ ▓██ ▒ ██▒
░ ▒ ▄▀▒░ ▒▓█    ▄ ▓██ ░▄█ ▒▒██  ▀█▄  ▒▓█    ▄ ▓███▄░ ▒███   ▓██ ░▄█ ▒
  ▄▀▒   ░▒▓▓▄ ▄██▒▒██▀▀█▄  ░██▄▄▄▄██ ▒▓▓▄ ▄██▒▓██ █▄ ▒▓█  ▄ ▒██▀▀█▄  
▒███████▒▒ ▓███▀ ░░██▓ ▒██▒ ▓█   ▓██▒▒ ▓███▀ ░▒██▒ █▄░▒████▒░██▓ ▒██▒
░▒▒ ▓░▒░▒░ ░▒ ▒  ░░ ▒▓ ░▒▓░ ▒▒   ▓▒█░░ ░▒ ▒  ░▒ ▒▒ ▓▒░░ ▒░ ░░ ▒▓ ░▒▓░
░░▒ ▒ ░ ▒  ░  ▒     ░▒ ░ ▒░  ▒   ▒▒ ░  ░  ▒   ░ ░▒ ▒░ ░ ░  ░  ░▒ ░ ▒░
░ ░ ░ ░ ░░          ░░   ░   ░   ▒   ░        ░ ░░ ░    ░     ░░   ░ 
  ░ ░    ░ ░         ░           ░  ░░ ░      ░  ░      ░  ░   ░     
░        ░                           ░                               
                                            zcracker - @kadir_   
 ");
            Console.ResetColor();
        }

        private static void Run()
        {
            Console.Write("Enter the path to the archive file: ");
            string? archivePath = Console.ReadLine()?.Trim('"', ' ');

            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
            {
                Console.WriteLine("Error: Archive file not found.");
                return;
            }

            Console.Write("Enter the path to the password list: ");
            string? wordlistPath = Console.ReadLine()?.Trim('"', ' ');

            if (string.IsNullOrEmpty(wordlistPath) || !File.Exists(wordlistPath))
            {
                Console.WriteLine("Error: Password list not found.");
                return;
            }

            try
            {
                Console.WriteLine("Analyzing ZIP structure...");
                var metadata = ZipFastParser.Parse(archivePath);
                
                if (!metadata.IsEncrypted)
                {
                    Console.WriteLine("File is not encrypted!");
                    return;
                }

                Console.WriteLine($"Target File: {metadata.FileName}");
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine("Detecting Hardware Acceleration...");

                // Detect GPU
                bool useGpu = false;
                GpuZipCryptoEngine? gpuEngine = null;
                try
                {
                    // Attempt to init GPU engine
                    gpuEngine = new GpuZipCryptoEngine();
                    if (gpuEngine.IsAvailable)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"[+] GPU Detected: {gpuEngine.DeviceName}");
                        Console.ResetColor();
                        Console.Write("Do you want to use GPU Acceleration? (y/n) [default: n]: ");
                        var response = Console.ReadLine();
                        if (response?.ToLower().StartsWith("y") == true)
                        {
                            useGpu = true;
                        }
                    }
                    else
                    {
                        Console.WriteLine("[-] No compatible GPU detected (CUDA/OpenCL).");
                    }
                }
                catch
                {
                    Console.WriteLine("[-] GPU Initialization failed.");
                }

                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine($"Mode: {(useGpu ? "GPU Acceleration (Experimental)" : "CPU SIMD (AVX2)")}");
                Console.WriteLine($"Threads: {Environment.ProcessorCount}");
                Console.WriteLine("--------------------------------------------------");

                var stopwatch = Stopwatch.StartNew();

                using (var cts = new CancellationTokenSource())
                {
                    var monitorTask = Task.Run(async () =>
                    {
                        while (!cts.Token.IsCancellationRequested && !_found)
                        {
                            await Task.Delay(1000);
                            long current = Interlocked.Read(ref _attempts);
                            double seconds = stopwatch.Elapsed.TotalSeconds;
                            double rate = current / seconds;
                            string rateStr = rate > 1000000 ? $"{rate/1000000:F2} M/s" : $"{rate:N0} p/s";
                            
                            Console.Title = $"ZCracker | Speed: {rateStr} | Attempts: {current:N0}";
                            Console.Write($"\rSpeed: {rateStr} | Total: {current:N0} | Time: {stopwatch.Elapsed:mm\\:ss}");
                        }
                    }, cts.Token);

                    try
                    {
                        if (useGpu && gpuEngine != null)
                        {
                            // GPU Mode - Requires Batching
                            RunGpuMode(wordlistPath, gpuEngine, metadata, cts.Token);
                        }
                        else
                        {
                            // CPU SIMD Mode
                            RunCpuSimdMode(wordlistPath, metadata, cts.Token);
                        }
                    }
                    catch (OperationCanceledException) { }
                    finally
                    {
                        cts.Cancel();
                        stopwatch.Stop();
                        Thread.Sleep(500);
                        gpuEngine?.Dispose();
                    }
                }

                Console.WriteLine();
                Console.WriteLine("--------------------------------------------------");
                if (_found)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[+] SUCCESS! Password Found: {_foundPassword}");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[-] Password not found in the list.");
                }
                Console.ResetColor();
                Console.WriteLine($"Total Time: {stopwatch.Elapsed.TotalSeconds:F2}s");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nCritical Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private static void RunCpuSimdMode(string wordlistPath, TargetFileMetadata metadata, CancellationToken token)
        {
             // We need to read lines and chunk them into batches of 8 for SIMD
             var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token };

             // To use Parallel.ForEach effectively with batching, we should partition the source.
             // But simple ReadLines() with local buffering is easiest.
             
             Parallel.ForEach(
                 File.ReadLines(wordlistPath),
                 options,
                 () => new SimdThreadState(), // Thread-local state
                 (password, loopState, state) =>
                 {
                     if (_found) { loopState.Stop(); return state; }
                     
                     // Add to buffer
                     state.Batch[state.Count] = password;
                     state.Count++;

                     if (state.Count == 8)
                     {
                         ProcessBatch(state, metadata);
                         Interlocked.Add(ref _attempts, 8);
                         if (_found) loopState.Stop();
                         state.Count = 0;
                     }
                     return state;
                 },
                 (state) => 
                 {
                     // Process remaining
                     if (state.Count > 0 && !_found)
                     {
                         // Fill rest with empty or handle count
                         // For simplicity we just process the valid ones (Simd engine handles lengths)
                         for(int i=state.Count; i<8; i++) state.Batch[i] = "";
                         ProcessBatch(state, metadata);
                         Interlocked.Add(ref _attempts, state.Count);
                     }
                 } 
             );
        }

        private static unsafe void ProcessBatch(SimdThreadState state, TargetFileMetadata metadata)
        {
             // Pin strings and create pointers
             // This is inside the hot path, so we must be careful.
             // Pinning 8 strings individually is annoying.
             // But standard GCHandle is okay.
             
             fixed(char* p0 = state.Batch[0])
             fixed(char* p1 = state.Batch[1])
             fixed(char* p2 = state.Batch[2])
             fixed(char* p3 = state.Batch[3])
             fixed(char* p4 = state.Batch[4])
             fixed(char* p5 = state.Batch[5])
             fixed(char* p6 = state.Batch[6])
             fixed(char* p7 = state.Batch[7])
             {
                 state.Ptrs[0] = p0; state.Lens[0] = state.Batch[0].Length;
                 state.Ptrs[1] = p1; state.Lens[1] = state.Batch[1].Length;
                 state.Ptrs[2] = p2; state.Lens[2] = state.Batch[2].Length;
                 state.Ptrs[3] = p3; state.Lens[3] = state.Batch[3].Length;
                 state.Ptrs[4] = p4; state.Lens[4] = state.Batch[4].Length;
                 state.Ptrs[5] = p5; state.Lens[5] = state.Batch[5].Length;
                 state.Ptrs[6] = p6; state.Lens[6] = state.Batch[6].Length;
                 state.Ptrs[7] = p7; state.Lens[7] = state.Batch[7].Length;
                 
                 fixed(char** pPtrs = state.Ptrs)
                 fixed(int* pLens = state.Lens)
                 {
                     int foundIndex = state.Engine.CheckBatch(metadata.EncryptionHeader, metadata.Crc32, pPtrs, pLens);
                     if (foundIndex >= 0)
                     {
                         _found = true;
                         _foundPassword = state.Batch[foundIndex];
                     }
                 }
             }
        }
        
        // Helper class for SIMD state
        class SimdThreadState
        {
            public SimdZipCryptoEngine Engine = new SimdZipCryptoEngine();
            public string[] Batch = new string[8];
            public int Count = 0;
            public unsafe char*[] Ptrs = new char*[8]; // Array on heap, but contents are pointers
            public int[] Lens = new int[8];
        }

        private static void RunGpuMode(string wordlistPath, GpuZipCryptoEngine engine, TargetFileMetadata metadata, CancellationToken token)
        {
            // GPU requires large chunks.
            const int ChunkSize = 100000;
            var buffer = new List<string>(ChunkSize);
            
            // Single-threaded read, batched execute (simplest for GPU offload)
            // Or use a producer-consumer.
            foreach (var line in File.ReadLines(wordlistPath))
            {
                if (token.IsCancellationRequested || _found) break;
                buffer.Add(line);
                if (buffer.Count >= ChunkSize)
                {
                    ProcessGpuBatch(engine, buffer, metadata);
                    buffer.Clear();
                }
            }
            if (buffer.Count > 0 && !_found)
            {
                ProcessGpuBatch(engine, buffer, metadata);
            }
        }

        private static void ProcessGpuBatch(GpuZipCryptoEngine engine, List<string> passwordList, TargetFileMetadata metadata)
        {
            // Convert List<string> to byte[][]
            // This conversion overhead might kill GPU gains if not optimized, but good for demo.
            byte[][] bytes = new byte[passwordList.Count][];
            for(int i=0; i<passwordList.Count; i++)
            {
                bytes[i] = System.Text.Encoding.ASCII.GetBytes(passwordList[i]);
            }
            
            int foundIdx = engine.CheckBatch(bytes, metadata.EncryptionHeader, metadata.Crc32);
            Interlocked.Add(ref _attempts, passwordList.Count);
            
            if (foundIdx >= 0)
            {
                _found = true;
                _foundPassword = passwordList[foundIdx];
            }
        }
    }
}
