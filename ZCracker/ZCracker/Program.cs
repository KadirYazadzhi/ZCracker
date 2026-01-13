using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
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
            string? archivePath = Console.ReadLine()?.Trim('"', ' '); // Remove quotes if user dragged file

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

                if (!metadata.IsSupported)
                {
                    Console.WriteLine("Warning: Compression method might not be standard. Proceeding with ZipCrypto check anyway.");
                }

                Console.WriteLine($"Target File: {metadata.FileName}");
                Console.WriteLine("Starting High-Performance Brute-Force Engine...");
                Console.WriteLine("Algorithm: ZipCrypto (Native/Unsafe)");
                Console.WriteLine($"Threads: {Environment.ProcessorCount}");
                Console.WriteLine("--------------------------------------------------");

                var stopwatch = Stopwatch.StartNew();
                
                // Launch status monitor task
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
                            Console.Title = $"ZCracker | Speed: {rate:N0} p/s | Attempts: {current:N0}";
                            Console.Write($"\rSpeed: {rate/1000000:F2} M/s | Total: {current:N0} | Time: {stopwatch.Elapsed:mm\\:ss}");
                        }
                    }, cts.Token);

                    try
                    {
                        // Use File.ReadLines for memory efficiency with large wordlists
                        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
                        
                        Parallel.ForEach(
                            File.ReadLines(wordlistPath), 
                            options, 
                            () => new ZipCryptoEngine(), // Init thread-local engine
                            (password, loopState, engine) =>
                            {
                                if (_found)
                                {
                                    loopState.Stop();
                                    return engine;
                                }

                                Interlocked.Increment(ref _attempts);

                                if (engine.CheckPassword(metadata.EncryptionHeader, metadata.Crc32, password))
                                {
                                    _found = true;
                                    _foundPassword = password;
                                    loopState.Stop();
                                }

                                return engine;
                            },
                            (engine) => { } // Finalizer for thread-local
                        );
                    }
                    catch (OperationCanceledException) { }
                    finally
                    {
                        cts.Cancel();
                        stopwatch.Stop();
                        // Wait for monitor to clear line
                        Thread.Sleep(500); 
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
            }
        }
    }
}