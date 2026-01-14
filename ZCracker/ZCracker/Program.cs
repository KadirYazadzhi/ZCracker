using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ZCracker
{
    class Program
    {
        private static long _attempts = 0;
        private static bool _found = false;
        private static string _foundPassword = string.Empty;

        static async Task Main(string[] args)
        {
            PrintBanner();
            await Run();
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

        private static async Task Run()
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
                Console.WriteLine($"Data Offset: {metadata.DataOffset}");
                Console.WriteLine($"Compressed Size: {metadata.CompressedSize}");
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine("Detecting Hardware Acceleration...");

                // Detect GPU
                bool useGpu = false;
                GpuZipCryptoEngine? gpuEngine = null;
                try
                {
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
                Console.WriteLine($"Mode: {(useGpu ? "GPU Acceleration" : "CPU SIMD (AVX2) + Memory Mapped I/O")}");
                Console.WriteLine($"Threads: {Environment.ProcessorCount}");
                Console.WriteLine("--------------------------------------------------");

                var stopwatch = Stopwatch.StartNew();

                using (var cts = new CancellationTokenSource())
                using (var reader = new ZeroAllocFileReader(wordlistPath))
                {
                    // Monitor Task
                    var monitorTask = Task.Run(async () =>
                    {
                        while (!cts.Token.IsCancellationRequested && !_found)
                        {
                            await Task.Delay(1000);
                            long current = Interlocked.Read(ref _attempts);
                            double seconds = stopwatch.Elapsed.TotalSeconds;
                            double rate = current / seconds;
                            string rateStr = rate > 1000000 ? "{rate/1000000:F2} M/s" : "{rate:N0} p/s";
                            
                            Console.Write($"\rSpeed: {rateStr} | Total: {current:N0} | Time: {stopwatch.Elapsed:mm\\:ss}");
                        }
                    }, cts.Token);

                    // Channel for Producer-Consumer
                    var channel = Channel.CreateBounded<PasswordBatch>(new BoundedChannelOptions(100) 
                    {
                        SingleWriter = true,
                        SingleReader = false,
                        FullMode = BoundedChannelFullMode.Wait
                    });

                    // Producer Task
                    var producer = Task.Run(() => 
                    {
                        try
                        {
                            reader.ProduceBatches(channel.Writer, cts.Token);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Producer Error: {ex.Message}");
                            channel.Writer.Complete(ex);
                        }
                    });

                    // Consumer Tasks
                    int consumersCount = Environment.ProcessorCount;
                    var consumers = new Task[consumersCount];

                    // Helper for Deep Verification
                    // We create one scalar engine per thread to reuse logic if needed, 
                    // or just use ZipVerifier which uses new engine.
                    
                    for (int i = 0; i < consumersCount; i++)
                    {
                        consumers[i] = Task.Run(async () => 
                        {
                            var simdEngine = new SimdZipCryptoEngine();
                            var scalarEngine = new ZipCryptoEngine(); // For double-checking matches in batch
                            
                            // Allocate unmanaged memory once per thread
                            IntPtr ptrsBuffer = Marshal.AllocHGlobal(8 * IntPtr.Size);
                            IntPtr lensBuffer = Marshal.AllocHGlobal(8 * sizeof(int));
                            
                            try
                            {
                                while (await channel.Reader.WaitToReadAsync(cts.Token))
                                {
                                    while (channel.Reader.TryRead(out PasswordBatch batch))
                                    {
                                        if (_found) break;

                                        unsafe 
                                        {
                                            byte** ptrs = (byte**)ptrsBuffer;
                                            int* lens = (int*)lensBuffer;

                                            for(int j=0; j < batch.Count; j += 8)
                                            {
                                                int remaining = Math.Min(8, batch.Count - j);
                                                
                                                // Fill SIMD buffers
                                                for(int k=0; k < remaining; k++)
                                                {
                                                    ptrs[k] = (byte*)batch.Pointers[j+k];
                                                    lens[k] = batch.Lengths[j+k];
                                                }
                                                // Fill rest with 0 length
                                                for(int k=remaining; k<8; k++) lens[k] = 0;

                                                // 1. Fast SIMD Check (Returns index of FIRST match or -1)
                                                int result = simdEngine.CheckBatch(metadata.EncryptionHeader, metadata.Crc32, ptrs, lens);
                                                
                                                if (result != -1)
                                                {
                                                    // Potential match found in this group of 8!
                                                    // But wait, there might be MULTIPLE matches (false positives).
                                                    // SIMD only told us the first one.
                                                    // We must Scalar Check ALL 8 to be sure we don't miss the real one
                                                    // if it's "shadowed" by a false positive earlier in the batch.
                                                    
                                                    for(int k=0; k < remaining; k++)
                                                    {
                                                        int idx = j + k;
                                                        var entry = batch.Get(idx);
                                                        string candidate = entry.ToString();
                                                        
                                                        // Fast Scalar Check (Header Only)
                                                        if (scalarEngine.CheckPassword(metadata.EncryptionHeader, metadata.Crc32, candidate))
                                                        {
                                                            // Header Matches! Now Deep Verify.
                                                            if (ZipVerifier.Verify(candidate, archivePath, metadata))
                                                            {
                                                                _foundPassword = candidate;
                                                                _found = true;
                                                                cts.Cancel();
                                                                return;
                                                            }
                                                            // Else: False Positive, ignore and continue
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        Interlocked.Add(ref _attempts, batch.Count);
                                    }
                                }
                            }
                            catch (OperationCanceledException) { }
                            finally
                            {
                                Marshal.FreeHGlobal(ptrsBuffer);
                                Marshal.FreeHGlobal(lensBuffer);
                            }
                        });
                    }

                    try
                    {
                        await Task.WhenAny(Task.WhenAll(consumers), Task.Delay(Timeout.Infinite, cts.Token));
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
    }
}