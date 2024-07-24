using System.Collections.Concurrent;
using System.Diagnostics;

namespace FastDirTest
{
    internal static class OtherGetFiles
    {
        #region Internal Properties

        internal static string[] Separator { get; } = ["\r\n"];

        #endregion Internal Properties

        #region Public Methods

        public static IEnumerable<string> GetAllDirectoriesWithCMD(string searchPath)
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
        public static IEnumerable<string> GetAllFilesWithCMD(string searchPath)
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
        public static IEnumerable<string> GetAllFilesWithPowerShell(string searchPath)
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
        public static IEnumerable<string> GetAllFoldersWithPowerShell(string searchPath)
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
        /// <summary>
        /// Based on https://stackoverflow.com/a/2107294
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static IEnumerable<string> V1GetFiles(string path)
        {

            ConcurrentQueue<string> pendingQueue = [];
            ConcurrentBag<string> filesNames = [];
            pendingQueue.Enqueue(path);

            while (!pendingQueue.IsEmpty)
            {
                try
                {
                    pendingQueue.TryDequeue(out path);
                    Parallel.ForEach(Directory.GetFiles(path), filesNames.Add);
                    var dirs = Directory.GetDirectories(path);
                    //If you need folders too
                    //Parallel.ForEach(dirs, filesNames.Add);
                    Parallel.ForEach(dirs, pendingQueue.Enqueue);
                }
                catch (Exception) { continue; }
            }
            return filesNames;
        }

        /// <summary>
        /// Other solution with bags using Enumerate Files/Directories
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static IEnumerable<string> V2GetFiles(string path)
        {
            var filesBag = new ConcurrentBag<string>();

            try
            {
                var dirs = Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).AsParallel();
                var files = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).AsParallel();
                Parallel.ForEach(files, filesBag.Add);
                //If you need folders too
                //Parallel.ForEach(dirs, filesBag.Add);
                Parallel.ForEach(dirs, dir =>
                {
                    Parallel.ForEach(V2GetFiles(dir), filesBag.Add);
                });
            }
            catch
            { return []; }

            return filesBag;
        }

        #endregion Public Methods
    }
}