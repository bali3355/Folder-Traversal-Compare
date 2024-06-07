using FastFile;
using LightingFile;
using CustomFile;
using System.Diagnostics;

namespace FastDirTest
{
    public static class StopwatchExtensions
    {
        public static Stopwatch SwitchWatch(this Stopwatch stopwatch)
        {
            if (stopwatch.IsRunning) stopwatch.Stop();
            else stopwatch.Start();
            return stopwatch;
        }
    }

    internal class Program
    {
        internal static Stopwatch stopwatch = new();
        internal static readonly string[] separator = ["\r\n"];
        internal static readonly string searchPath = @"C:\Users\";

        private static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");

            stopwatch.SwitchWatch();
            var lightingList = LightingFileInfo.EnumerateFiles(searchPath, "*", SearchOption.AllDirectories);
            var count = lightingList.Count();
            stopwatch.SwitchWatch();
            Console.WriteLine($"Result time({nameof(lightingList)}): {stopwatch.Elapsed} count: {lightingList.Count()}");

            stopwatch.SwitchWatch();
            var fastList = FastFileInfo.EnumerateFiles(searchPath, "*", SearchOption.AllDirectories, null);
            var fastCount = fastList.Count();
            stopwatch.SwitchWatch();
            Console.WriteLine($"Result time({nameof(fastList)}): {stopwatch.Elapsed} count: {fastList.Count()}");

            stopwatch.SwitchWatch();
            var customList = CustomFileInfo.GetAllFiles(searchPath, -1, searchPath.Split('\\').Length);
            stopwatch.SwitchWatch();
            Console.WriteLine($"Result time({nameof(customList)}): {stopwatch.Elapsed} count: {customList.Count()}");

            //TestingWithCMD(searchPath, lightingList);
            Console.ReadKey();
        }

        private static void TestingWithCMD(string searchPath, IEnumerable<LightingFileInfo> lightingList)
        {
            using var process = new Process
            {
                StartInfo =
        {
            FileName = "cmd.exe",
            Arguments = $"/C chcp 850 | dir /AD /B /S \"{searchPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        }
            };

            process.Start();
            var cmdOutput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var cmdList = cmdOutput.Split(separator, StringSplitOptions.RemoveEmptyEntries).ToList();
            var missingFromCMDList = lightingList
                .Where(x => !cmdList.Contains(x.FullName))
                .AsParallel()
                .Select(x => $"{x.FullName} - {x.Attributes}");

            var MissingFromLightingList = cmdList.Except(lightingList.Select(x => x.FullName)).AsParallel();
            var summList = new HashSet<string>(missingFromCMDList.Concat(MissingFromLightingList).Order());

            Parallel.ForEach(summList, item => { Console.WriteLine(item); });

            Console.WriteLine($"Result({searchPath}):\n" +
                              $"Lighting: {lightingList.Count()}\n" +
                              $"CMD: {cmdList.Count}");
        }

    }
}