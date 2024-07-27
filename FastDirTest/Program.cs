using FastFile;
using FastFileV2;
using FastFileV3;
using FastFileV4;
using FastFileV5;
using FastFileInfo;
using System.Diagnostics;

namespace FastDirTest
{
    public static class StopwatchExtensions
    {
        public static TimeSpan End(this Stopwatch stopwatch)
        {
            if (!stopwatch.IsRunning) return TimeSpan.Zero;
            TimeSpan time = stopwatch.Elapsed;
            stopwatch.Reset();
            return time;
        }
    }
    internal class Program
    {
        internal const int nameWidth = 60, timeWidth = 30, countWidth = 30;
        internal static Stopwatch stopwatch = new();
        internal static int Count { get; set; }
        internal static string SearchPath { get; } = @"C:\";
        internal static TimeSpan Time { get; set; }

        private static void Main(string[] args)
        {
            Console.WriteLine($"Hello, World! {SearchPath}\n");
            string header = $"| {"Enumerator name",-nameWidth} | {"Enumerating Time",-timeWidth} | {"Enumerated Count",-countWidth} |";
            Console.WriteLine(header);
            var line = "|" + new string('-', header.Length - 2) + "|";
            Console.WriteLine(line);

            stopwatch.Start();
            TestEnumeratingFiles(FastFileInfo.FastFileInfo.EnumerateFiles(SearchPath, "*", SearchOption.AllDirectories, null), "FastFileInfo enumerator");

            stopwatch.Start();
            TestEnumeratingFiles(WinAPI.GetFiles(SearchPath), "1. Gen WinAPI use");

            stopwatch.Start();
            TestEnumeratingFiles(WinAPIv2.EnumerateFiles(SearchPath), "2. Gen WinAPI use");

            stopwatch.Start();
            TestEnumeratingFiles(WinAPIv3.GetFilesRecursive(SearchPath), "3. Gen WinAPI use, recursive search");

            stopwatch.Start();
            TestEnumeratingFiles(WinAPIv3.GetFilesRecursiveParallel(SearchPath), "3. Gen WinAPI use, parallel recursive search");

            stopwatch.Start();
            TestEnumeratingFiles(WinAPIv3.GetFilesRecursiveNew(SearchPath), "3. Gen WinAPI use, new recursive search");

            stopwatch.Start();
            TestEnumeratingFiles(WinAPIv3.GetFilesRecursiveNewParallel(SearchPath), "3. Gen WinAPI use, new parallel recursive search");

            stopwatch.Start();
            TestEnumeratingFiles(WinAPIv3.GetFilesQueue(SearchPath), "3. Gen WinAPI use, queued search");

            stopwatch.Start();
            TestEnumeratingFiles(WinAPIv3.GetFilesQueueParallel(SearchPath), "3. Gen WinAPI use, parallel queued search");

            stopwatch.Start();
            TestEnumeratingFiles(WinAPIv3.GetFilesStack(SearchPath), "3. Gen WinAPI use, stacked search");

            stopwatch.Start();
            TestEnumeratingFiles(WinAPIv3.GetFilesStackParallel(SearchPath), "3. Gen WinAPI use, parallel stacked search");

            stopwatch.Start();
            TestEnumeratingFiles(WinAPIv3.GetFilesRecursive(SearchPath), "3. Gen WinAPI use, recursive foreach search");

            stopwatch.Start();
            TestEnumeratingFiles(WinAPIv4.EnumerateFiles(SearchPath, "*", SearchOption.AllDirectories, FastFileV4.SearchFor.Files), "4. Gen WinAPI use, paralleled Enumerate");

            stopwatch.Start();
            TestEnumeratingFiles(WinAPIv5.EnumerateFileSystem(SearchPath), "5. Gen WinAPI use, improved paralleled enumerate");

            stopwatch.Start();
            TestEnumeratingFiles(OtherGetFiles.V1GetFiles(SearchPath), "Built-in 'Get Files/Dirs' with queue");

            stopwatch.Start();
            TestEnumeratingFiles(OtherGetFiles.V2GetFiles(SearchPath), "Recursive built-in 'Enumerate Files/Dirs'");

            stopwatch.Start();
            TestEnumeratingFiles(OtherGetFiles.GetAllFilesWithCMD(SearchPath), "Exploring with built-in 'Dir' command with cmd");

            stopwatch.Start();
            TestEnumeratingFiles(OtherGetFiles.GetAllFilesWithPowerShell(SearchPath), "Powershell folder exploring");

            Console.WriteLine(line);
            Console.WriteLine("\n\nPress any key to exit...");
            Console.ReadKey();
        }

        private static IEnumerable<T> TestEnumeratingFiles<T>(IEnumerable<T> listOfFiles, string name)
        {
            Count = listOfFiles.Count();
            Time = stopwatch.End();
            Console.WriteLine($"| {name,-nameWidth} | {Time,-timeWidth} | {Count,-countWidth} |");
            return listOfFiles;
        }
    }
}
