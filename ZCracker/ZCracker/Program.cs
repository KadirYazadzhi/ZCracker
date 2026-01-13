using System;
using System.IO;
using System.Threading.Tasks;
using Ionic.Zip;
using System.Diagnostics;
using System.Threading;

class ArchiveBruteForcer {
    private static int maxThreads = Environment.ProcessorCount;
    private static string archiveFile;

    static async Task Main(string[] args) {
        PrintBanner();
        await ReadingDataAsync();
    }

    private static void PrintBanner() {
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
    }

    private static async Task ReadingDataAsync() {
        Console.Write("Enter the path to the archive file: ");
        archiveFile = Console.ReadLine();
        
        Console.Write("Enter the path to the password list: ");
        string dictionaryPath = Console.ReadLine();
        
        await BruteForceArchiveAsync(dictionaryPath);
    }

    static async Task BruteForceArchiveAsync(string passwordList) {
        if (!File.Exists(archiveFile)) {
            Console.WriteLine("Error: Archive file not found.");
            return;
        }

        if (!File.Exists(passwordList)) {
            Console.WriteLine("Error: Password list not found.");
            return;
        }

        var passwords = File.ReadLines(passwordList);

        if (passwords.Count() > 100000) {
            Console.WriteLine("Error: Password list is too long.");
            return;
        }
        
        Console.WriteLine($"Starting brute-force attack on the archive password...");

        Stopwatch stopwatch = Stopwatch.StartNew();

        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        var tasks = new List<Task>();
        bool passwordFound = false;
        var semaphore = new SemaphoreSlim(maxThreads); 
        
        var passwordChunks = Chunkify(passwords, maxThreads);

        foreach (var chunk in passwordChunks) {
            await semaphore.WaitAsync(); 

            tasks.Add(Task.Run(async () => {
                try {
                    if (cancellationTokenSource.Token.IsCancellationRequested) return;

                    foreach (var password in chunk) {
                        if (await TryOpenArchiveAsync(archiveFile, password)) {
                            cancellationTokenSource.Cancel();
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[+] Correct password found: {password}");
                            passwordFound = true;
                        }
                    }
                }
                finally {
                    semaphore.Release(); 
                }
            }));
        }

        await Task.WhenAll(tasks); 

        stopwatch.Stop();

        if (!passwordFound) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[!] No valid password found.");
        }

        Console.WriteLine($"Time taken: {stopwatch.Elapsed.TotalSeconds:F2} seconds");
    }

    static async Task<bool> TryOpenArchiveAsync(string archiveFile, string password) {
        try {
            string extractPath = @"C:\temp\";

            if (Directory.Exists(extractPath)) {
                Directory.Delete(extractPath, true);
            }

            using (ZipFile zip = ZipFile.Read(archiveFile)) {
                zip.Password = password;

                foreach (ZipEntry entry in zip) {
                    string filePath = Path.Combine(extractPath, entry.FileName);
                    entry.Extract(extractPath, ExtractExistingFileAction.OverwriteSilently);

                    if (File.Exists(filePath)) {
                        return true; 
                    }
                }
            }
        }
        catch (Exception) {
            return false;
        }
        return false; 
    }

    static IEnumerable<IEnumerable<string>> Chunkify(IEnumerable<string> source, int chunkSize) {
        var chunk = new List<string>(chunkSize);
        foreach (var item in source) {
            chunk.Add(item);
            if (chunk.Count == chunkSize) {
                yield return chunk;
                chunk = new List<string>(chunkSize);
            }
        }
        if (chunk.Any()) {
            yield return chunk;
        }
    }
}
