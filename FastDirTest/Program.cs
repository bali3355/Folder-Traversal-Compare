// See https://aka.ms/new-console-template for more information
using FastDirTest;
using System.Diagnostics;

Console.WriteLine("Hello, World!");
var searchPath = @"C:\Users\";
var sw = Stopwatch.StartNew();
var list = FastFileInfo.EnumerateDirectories(searchPath, "*", SearchOption.AllDirectories);
sw.Stop();
var firsttime = sw.Elapsed;
Parallel.ForEach(list, (item) => { Console.WriteLine(item.FullName); });
Console.WriteLine($"Result time({searchPath}):\n{firsttime} | {list.Count()}");
Console.ReadKey();
