using Microsoft.Win32.SafeHandles;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Permissions;

namespace FastFileV4
{
    public enum SearchFor
    {
        Files = 0,
        Directories = 1,
        FilesAndDirectories = 2,
    }

    [Serializable]
    public class WinAPIv4
    {
        public readonly string AlternateName;
        public readonly FileAttributes Attributes;
        public readonly string FullName;
        public readonly long Length;
        public readonly string Name;
        public WinAPIv4(string filename) : this(new FileInfo(filename)) { }

        public WinAPIv4(FileInfo file)
        {
            Name = file.Name;
            FullName = file.FullName;
            if (file.Exists)
            {
                Length = file.Length;
                Attributes = file.Attributes;
            }
        }

        internal WinAPIv4(string dir, WIN32_FIND_DATA findData)
        {
            Attributes = findData.dwFileAttributes;
            Length = CombineHighLowInts(findData.nFileSizeHigh, findData.nFileSizeLow);
            Name = findData.cFileName;
            AlternateName = findData.cAlternateFileName;
            FullName = Path.Combine(dir, findData.cFileName);
        }

        public string? DirectoryName => Path.GetDirectoryName(FullName);
        public bool Exists => File.Exists(FullName);
        public static long CombineHighLowInts(uint high, uint low) => (((long)high) << 0x20) | low;

        public static IEnumerable<WinAPIv4> EnumerateDirectories(string path) => EnumerateDirectories(path, "*");

        public static IEnumerable<WinAPIv4> EnumerateDirectories(string path, string searchPattern) => EnumerateDirectories(path, searchPattern, SearchOption.TopDirectoryOnly);

        public static IEnumerable<WinAPIv4> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption) => EnumerateDirectories(path, searchPattern, SearchOption.TopDirectoryOnly, SearchFor.Directories);

        public static IEnumerable<WinAPIv4> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption, SearchFor searchFor)
        {
            ExceptionHandle(path, searchPattern, searchOption);
            return new FileEnumerable(Path.GetFullPath(path), searchPattern, searchOption, searchFor);
        }

        public static IEnumerable<WinAPIv4> EnumerateFiles(string path) => EnumerateFiles(path, "*");

        public static IEnumerable<WinAPIv4> EnumerateFiles(string path, string searchPattern) => EnumerateFiles(path, searchPattern, SearchOption.TopDirectoryOnly);

        public static IEnumerable<WinAPIv4> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) => EnumerateFiles(path, searchPattern, SearchOption.TopDirectoryOnly, SearchFor.Files);

        public static IEnumerable<WinAPIv4> EnumerateFiles(string path, string searchPattern, SearchOption searchOption, SearchFor searchFor)
        {
            ExceptionHandle(path, searchPattern, searchOption);
            return new FileEnumerable(Path.GetFullPath(path), searchPattern, searchOption, searchFor);
        }

        public override string ToString() => FullName;
        private static void ExceptionHandle(string path, string searchPattern, SearchOption searchOption)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(searchPattern);
            if ((searchOption != SearchOption.TopDirectoryOnly) && (searchOption != SearchOption.AllDirectories))
                throw new ArgumentOutOfRangeException(nameof(searchOption));
        }



        public class FileEnumerable : IEnumerable<WinAPIv4>
        {
            private readonly string _filter;
            private readonly int _maxDegreeOfParallelism;
            private readonly string _path;
            private readonly SearchFor _searchFor;
            private readonly SearchOption _searchOption;
            public FileEnumerable(string path, string filter, SearchOption searchOption, SearchFor searchFor, int maxDegreeOfParallelism = -1)
            {
                _path = path;
                _filter = filter;
                _searchOption = searchOption;
                _searchFor = searchFor;
                _maxDegreeOfParallelism = maxDegreeOfParallelism;
            }

            public IEnumerator<WinAPIv4> GetEnumerator()
            {
                return new ParallelFileEnumerator(_path, _filter, _searchOption, _searchFor, _maxDegreeOfParallelism);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [System.Security.SuppressUnmanagedCodeSecurity]
        public class ParallelFileEnumerator : IEnumerator<WinAPIv4>
        {
            private readonly ManualResetEventSlim _completionEvent = new(false);
            private readonly CancellationTokenSource _cts;
            private readonly ConcurrentQueue<string> _directoryQueue;
            private readonly string _initialPath;
            private readonly int _maxDegreeOfParallelism;
            private readonly ConcurrentDictionary<string, byte> _processedDirectories = [];
            private readonly BlockingCollection<WinAPIv4> _resultQueue;
            private readonly SearchFor _searchFor;
            private readonly SearchOption _searchOption;
            private readonly string _searchPattern;
            private readonly AutoResetEvent _workAvailable = new(false);
            private int _activeProducers;
            private bool _enumerateRunning = true;

            public ParallelFileEnumerator(string path, string searchPattern, SearchOption searchOption, SearchFor searchFor, int maxDegreeOfParallelism)
            {
                _initialPath = path;
                _searchPattern = searchPattern;
                _searchOption = searchOption;
                _searchFor = searchFor;
                _maxDegreeOfParallelism = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : Environment.ProcessorCount;
                _directoryQueue = [];
                _resultQueue = [];
                _cts = new CancellationTokenSource();
                _activeProducers = _maxDegreeOfParallelism;

                _directoryQueue.Enqueue(_initialPath);
                StartProducerTasks();
            }

            public WinAPIv4 Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
                _cts.Cancel();
                _enumerateRunning = false;
                _workAvailable.Set(); // Wake up any waiting threads
                _completionEvent.Wait(TimeSpan.FromSeconds(30)); // Wait for all producers to complete
                _resultQueue.Dispose();
                _cts.Dispose();
                _completionEvent.Dispose();
                _workAvailable.Dispose();
            }

            public bool MoveNext()
            {
                while (_enumerateRunning || _resultQueue.Count > 0)
                {
                    if (_resultQueue.TryTake(out var item, 1000, _cts.Token))
                    {
                        Current = item;
                        return true;
                    }

                    if (_directoryQueue.IsEmpty) _enumerateRunning = false;
                    
                }
                return false;
            }

            public void Reset() => throw new NotSupportedException();

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern SafeFindHandle FindFirstFile(string fileName, [In, Out] WIN32_FIND_DATA data);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern SafeFindHandle FindFirstFileEx(string fileName, int infoLevel, [In, Out] WIN32_FIND_DATA data, int searchScope, string notUsedNull, int additionalFlags);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern bool FindNextFile(SafeFindHandle hndFindFile, [In, Out, MarshalAs(UnmanagedType.LPStruct)] WIN32_FIND_DATA lpFindFileData);
            private void ProcessDirectory(string path)
            {
                if (!_processedDirectories.TryAdd(path, 1)) return; // Skip if already processed
                var searchPattern = _searchFor == SearchFor.Directories ? "*." : _searchPattern;
                try
                {
                    var findData = new WIN32_FIND_DATA();
                    using var hFind = FindFirstFile(Path.Combine(path, searchPattern), findData);

                    if (hFind.IsInvalid) return;

                    do
                    {
                        if (_cts.IsCancellationRequested) return;

                        if (findData.cFileName is "." or "..") continue;

                        var fullPath = Path.Combine(path, findData.cFileName);

                        if (findData.dwFileAttributes.HasFlag(FileAttributes.Directory))
                        {
                            if (_searchOption == SearchOption.AllDirectories)
                            {
                                _directoryQueue.Enqueue(fullPath);
                                _workAvailable.Set(); // Signal that work is available
                            }

                            if (_searchFor != SearchFor.Files)
                                _resultQueue.Add(new WinAPIv4(path, findData));
                        }
                        else if (_searchFor != SearchFor.Directories)
                        {
                            _resultQueue.Add(new WinAPIv4(path, findData));
                        }
                    }
                    while (FindNextFile(hFind, findData));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing directory {path}: {ex.Message}");
                }
            }

            private void ProducerWork()
            {
                try
                {
                    while (!_cts.IsCancellationRequested && _enumerateRunning)
                    {
                        if (_directoryQueue.TryDequeue(out string currentPath))
                        {
                            ProcessDirectory(currentPath);
                        }
                        else
                        {
                            _workAvailable.WaitOne(100); // Wait for work or timeout
                        }
                    }
                }
                finally
                {
                    if (Interlocked.Decrement(ref _activeProducers) == 0)
                    {
                        _resultQueue.CompleteAdding();
                        _completionEvent.Set();
                    }
                }
            }

            private void StartProducerTasks()
            {
                for (int i = 0; i < _maxDegreeOfParallelism; i++)
                {
                    Task.Factory.StartNew(ProducerWork, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }
            }
        }

        // Wraps a FindFirstFile handle.
        private sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
            internal SafeFindHandle() : base(true) { }

            protected override bool ReleaseHandle()
            {
                return FindClose(handle);
            }

            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
            [DllImport("kernel32.dll")]
            private static extern bool FindClose(IntPtr handle);
        }
    }

    /// <summary>
    /// Contains information about the file that is found by the FindFirstFile or FindNextFile functions.
    /// </summary>
    [Serializable, StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto), BestFitMapping(false)]
    internal class WIN32_FIND_DATA
    {
        public FileAttributes dwFileAttributes;
        public uint ftCreationTime_dwLowDateTime;
        public uint ftCreationTime_dwHighDateTime;
        public uint ftLastAccessTime_dwLowDateTime;
        public uint ftLastAccessTime_dwHighDateTime;
        public uint ftLastWriteTime_dwLowDateTime;
        public uint ftLastWriteTime_dwHighDateTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public int dwReserved0;
        public int dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;

        public override string ToString() => "FileName = " + cFileName;
    }
}
