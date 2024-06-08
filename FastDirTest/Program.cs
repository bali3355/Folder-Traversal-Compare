using FastFile;
using LightingFile;
using CustomFile;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace FastDirTest
{
    internal class Program
    {
        internal static Stopwatch stopwatch = new();
        internal static string SearchPath { get; } = @"C:\";
        internal static string[] Separator { get; } = ["\r\n"];

        private static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            stopwatch.Start();
            var lightingList = LightingFileInfo.EnumerateFiles(SearchPath, "*", SearchOption.AllDirectories);
            var count = lightingList.Count();
            stopwatch.Stop();
            Console.WriteLine($"Result time({nameof(lightingList)}): {stopwatch.Elapsed} count: {count}");

            stopwatch.Start();
            var fastList = FastFileInfo.EnumerateFiles(SearchPath, "*", SearchOption.AllDirectories, null);
            var fastCount = fastList.Count();
            stopwatch.Stop();
            Console.WriteLine($"Result time({nameof(fastList)}): {stopwatch.Elapsed} count: {fastCount}");

            stopwatch.Start();
            var getList = FastFileInfo.GetFiles(SearchPath, "*", SearchOption.AllDirectories);
            var getCount = getList.Count;
            stopwatch.Stop();
            Console.WriteLine($"Result time({nameof(getList)}): {stopwatch.Elapsed} count: {getCount}");

            stopwatch.Start();
            var customList = CustomFileInfo.GetAllFiles(SearchPath, -1, SearchPath.Split('\\').Length);
            var customCount = customList.Count;
            stopwatch.Stop();
            Console.WriteLine($"Result time({nameof(customList)}): {stopwatch.Elapsed} count: {customCount}");

            stopwatch.Start();
            var v1List = V1GetFiles(SearchPath);
            var v1Count = v1List.Count();
            stopwatch.Stop();
            Console.WriteLine($"Result time({nameof(v1List)}): {stopwatch.Elapsed} count: {v1Count}");

            stopwatch.Start();
            var v2List = V2GetFiles(SearchPath);
            var v2Count = v2List.Count();
            stopwatch.Stop();
            Console.WriteLine($"Result time({nameof(v2List)}): {stopwatch.Elapsed} count: {v2Count}");

            stopwatch.Start();
            var cmdList = GetAllFilesWithCMD(SearchPath);
            var cmdCount = cmdList.Count();
            stopwatch.Stop();
            Console.WriteLine($"Result time({nameof(cmdList)}): {stopwatch.Elapsed} count: {cmdCount}");

            stopwatch.Start();
            var psList = GetAllFilesWithPowerShell(SearchPath);
            var psCount = psList.Count();
            stopwatch.Stop();
            Console.WriteLine($"Result time({nameof(psList)}): {stopwatch.Elapsed} count: {psCount}");

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
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
