using FastFile;
using FastFileV2;
using FastFileV3;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace FastDirTest
{
    public static class StopwatchExtensions
    {
        public static (Stopwatch, TimeSpan) End(this Stopwatch stopwatch)
        {
            if (!stopwatch.IsRunning) return (stopwatch, TimeSpan.Zero);
            var time = stopwatch.Elapsed;
            stopwatch.Reset();
            return (stopwatch, time);

        }
    }
    internal class Program
    {
        internal static Stopwatch stopwatch = new();
        internal static int Count { get; set; }
        internal static string SearchPath { get; } = @"C:\";
        internal static string[] Separator { get; } = ["\r\n"];
        internal static TimeSpan Time { get; set; }
        private static IEnumerable<string> GetAllDirectoriesWithCMD(string searchPath)
        {
            using var process = new Process
            {
                StartInfo =
                {
                    FileName = "cmd.exe",
                    Arguments = $"chcp 850 | /C dir /AD /B /S \"{searchPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var cmdOutput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return [.. cmdOutput.Split(Separator, StringSplitOptions.RemoveEmptyEntries)];
        }

        private static IEnumerable<string> GetAllFilesWithCMD(string searchPath)
        {
            using var process = new Process
            {
                StartInfo =
                {
                    FileName = "cmd.exe",
                    Arguments = $"chcp 850 | /C dir /A-D /B /S \"{searchPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var cmdOutput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return [.. cmdOutput.Split(Separator, StringSplitOptions.RemoveEmptyEntries)];
        }

        private static IEnumerable<string> GetAllFilesWithPowerShell(string searchPath)
        {
            var powershellArgs = @"
            $path = '" + searchPath + @"'
            $stack = New-Object System.Collections.Stack
            #Load first level
            $stack.Push((Get-Item -Force -Path $path))
            #Recurse
            while($stack.Count -gt 0 -and ($item = $stack.Pop())) {
                if ($item.PSIsContainer)
                {
                    #If folders also needed
                    #Write-Host ""$($item.FullName)""
                    Get-ChildItem -Force -Path $item.FullName | ForEach-Object { $stack.Push($_) }
                } else {
                    Write-Host ""$($item.FullName)""
                }
            }";
            using var process = new Process
            {
                StartInfo =
                {
                    FileName = "powershell.exe",
                    Arguments = powershellArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var cmdOutput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return [.. cmdOutput.Split("\n", StringSplitOptions.RemoveEmptyEntries)];
        }

        private static IEnumerable<string> GetAllFoldersWithPowerShell(string searchPath)
        {
            var powershellArgs = @"
            $path = '" + searchPath + @"'
            $stack = New-Object System.Collections.Stack
            #Load first level
            $stack.Push((Get-Item -Force -Path $path))
            #Recurse
            while($stack.Count -gt 0 -and ($item = $stack.Pop())) {
                if ($item.PSIsContainer)
                {
                    Write-Host ""$($item.FullName)""
                    Get-ChildItem -Force -Path $item.FullName | ForEach-Object { $stack.Push($_) }
                }
            }";
            using var process = new Process
            {
                StartInfo =
                {
                    FileName = "powershell.exe",
                    Arguments = powershellArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var cmdOutput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return [.. cmdOutput.Split("\n", StringSplitOptions.RemoveEmptyEntries)];
        }

        private static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            stopwatch.Start();
            TestEnumeratingFiles(FastFileInfo.EnumerateFiles(SearchPath, "*", SearchOption.AllDirectories, null), "FastFileInfo");

            stopwatch.Start();
            TestEnumeratingFiles(FastFileInfo.GetFiles(SearchPath, "*", SearchOption.AllDirectories), "FastFileInfoGetFiles");

            stopwatch.Start();
            TestEnumeratingFiles(FastFileInfoV2.EnumerateFiles(SearchPath, "*", SearchOption.AllDirectories, null), "FastFileInfoV2");

            stopwatch.Start();
            TestEnumeratingFiles(FastFileInfoV3.GetAllFiles(SearchPath, -1, SearchPath.Split('\\').Length), "FastFileInfoV3.GetAllFiles");

            stopwatch.Start();
            TestEnumeratingFiles(FastFileInfoV3.GetAllFilesV2(SearchPath), "FastFileInfoV3.GetAllFilesV2");

            stopwatch.Start();
            TestEnumeratingFiles(FastFileInfoV3.GetAllFilesV3(SearchPath), "FastFileInfoV3.GetAllFilesV3");

            stopwatch.Start();
            TestEnumeratingFiles(V1GetFiles(SearchPath), "V1GetFiles");

            stopwatch.Start();
            TestEnumeratingFiles(V2GetFiles(SearchPath), "V2GetFiles");

            stopwatch.Start();
            TestEnumeratingFiles(GetAllFilesWithCMD(SearchPath), "GetAllFilesWithCMD");

            stopwatch.Start();
            TestEnumeratingFiles(GetAllFilesWithPowerShell(SearchPath), "GetAllFilesWithPowerShell");

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static void TestEnumeratingFiles(IEnumerable<string> listOfFiles, string name)
        {
            Count = listOfFiles.Count();
            Time = stopwatch.End().Item2;
            Console.WriteLine($"Result Time({name}): {Time} count: {Count}");
        }
        private static void TestEnumeratingFiles(IEnumerable<FastFileInfo> listOfFiles, string name)
        {
            Count = listOfFiles.Count();
            Time = stopwatch.End().Item2;
            Console.WriteLine($"Result Time({name}): {Time} count: {Count}");
        }
        private static void TestEnumeratingFiles(IEnumerable<FastFileInfoV2> listOfFiles, string name)
        {
            Count = listOfFiles.Count();
            Time = stopwatch.End().Item2;
            Console.WriteLine($"Result Time({name}): {Time} count: {Count}");
        }
        private static void TestEnumeratingFiles(IEnumerable<FastFileInfoV3> listOfFiles, string name)
        {
            Count = listOfFiles.Count();
            Time = stopwatch.End().Item2;
            Console.WriteLine($"Result Time({name}): {Time} count: {Count}");
        }
        private static IEnumerable<string> V1GetFiles(string path)
        {
            //https://stackoverflow.com/questions/2106877/is-there-a-faster-way-than-this-to-find-all-the-files-in-a-directory-and-all-sub
            ConcurrentQueue<string> pendingQueue = [];
            ConcurrentBag<string> filesNames = [];
            pendingQueue.Enqueue(path);

            while (!pendingQueue.IsEmpty)
            {
                try
                {
                    pendingQueue.TryDequeue(out path);
                    Parallel.ForEach(Directory.GetFiles(path), filesNames.Add);
                    Parallel.ForEach(Directory.GetDirectories(path), pendingQueue.Enqueue);
                }
                catch (Exception) { continue; }
            }
            return filesNames;
        }
        private static IEnumerable<string> V2GetFiles(string path)
        {
            var filesBag = new ConcurrentBag<string>();

            try
            {
                var dirs = Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).AsParallel();
                var files = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).AsParallel();
                Parallel.ForEach(files, filesBag.Add);
                Parallel.ForEach(dirs, dir =>
                {
                    try { Parallel.ForEach(V2GetFiles(dir), filesBag.Add); }
                    catch { return; }
                });
            }
            catch
            { return []; }

            return filesBag;
        }

    }
}
