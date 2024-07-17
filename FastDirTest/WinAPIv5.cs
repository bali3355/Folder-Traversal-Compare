using Microsoft.Win32.SafeHandles;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FastFileV5
{
    [Serializable]
    public class WinAPIv5
    {
        public long Length { get; }
        public string Name { get; }
        public string AlternateName { get; }
        public string FullName { get; }
        public FileAttributes Attributes { get; }
        public string? DirectoryName => Path.GetDirectoryName(FullName);
        public bool Exists => File.Exists(FullName);
        public override string ToString() => FullName;

        public WinAPIv5(string filename) : this(new FileInfo(filename)) { }

        public WinAPIv5(FileInfo file)
        {
            Name = file.Name;
            FullName = file.FullName;
            if (file.Exists)
            {
                Length = file.Length;
                Attributes = file.Attributes;
            }
        }

        internal WinAPIv5(string dir, WIN32_FIND_DATA findData)
        {
            Attributes = findData.dwFileAttributes;
            Length = CombineHighLowInts(findData.nFileSizeHigh, findData.nFileSizeLow);
            Name = findData.cFileName;
            AlternateName = findData.cAlternateFileName;
            FullName = Path.Combine(dir, findData.cFileName);
        }

        public static long CombineHighLowInts(uint high, uint low) => (((long)high) << 32) | low;

        public static IEnumerable<WinAPIv5> EnumerateFileSystem(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, SearchFor searchFor = SearchFor.Files, int maxDegreeOfParallelism = -1)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(searchPattern);

            return new FileEnumerable(Path.GetFullPath(path), searchPattern, searchOption, searchFor, maxDegreeOfParallelism);
        }

        private class FileEnumerable : IEnumerable<WinAPIv5>
        {
            private readonly string _path;
            private readonly string _filter;
            private readonly SearchOption _searchOption;
            private readonly SearchFor _searchFor;
            private readonly int _maxDegreeOfParallelism;

            public FileEnumerable(string path, string filter, SearchOption searchOption, SearchFor searchFor, int maxDegreeOfParallelism)
            {
                _path = path;
                _filter = filter;
                _searchOption = searchOption;
                _searchFor = searchFor;
                _maxDegreeOfParallelism = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : Environment.ProcessorCount;
            }

            public IEnumerator<WinAPIv5> GetEnumerator()
            {
                return new ParallelFileEnumerator(_path, _filter, _searchOption, _searchFor, _maxDegreeOfParallelism);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            [DllImport("kernel32.dll")]
            private static extern bool FindClose(IntPtr handle);

            internal SafeFindHandle() : base(true) { }

            protected override bool ReleaseHandle()
            {
                return FindClose(handle);
            }
        }

        private class ParallelFileEnumerator : IEnumerator<WinAPIv5>
        {
            private readonly string _initialPath;
            private readonly string _searchPattern;
            private readonly SearchOption _searchOption;
            private readonly SearchFor _searchFor;
            private readonly int _maxDegreeOfParallelism;
            private readonly BlockingCollection<string> _directoryQueue;
            private readonly BlockingCollection<WinAPIv5> _resultQueue;
            private readonly CancellationTokenSource _cts;
            private readonly ConcurrentDictionary<string, byte> _processedDirectories = new();
            private readonly ObjectPool<WIN32_FIND_DATA> _findDataPool;
            private Task[] _producerTasks;

            public ParallelFileEnumerator(string path, string searchPattern, SearchOption searchOption, SearchFor searchFor, int maxDegreeOfParallelism)
            {
                _initialPath = path;
                _searchPattern = searchPattern;
                _searchOption = searchOption;
                _searchFor = searchFor;
                _maxDegreeOfParallelism = maxDegreeOfParallelism;
                _directoryQueue = new BlockingCollection<string>();
                _resultQueue = new BlockingCollection<WinAPIv5>();
                _cts = new CancellationTokenSource();
                _findDataPool = new ObjectPool<WIN32_FIND_DATA>(() => new WIN32_FIND_DATA(), maxSize: _maxDegreeOfParallelism * 2);

                _directoryQueue.Add(_initialPath);
                StartProducerTasks();
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern SafeFindHandle FindFirstFile(string fileName, [In, Out] WIN32_FIND_DATA data);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern bool FindNextFile(SafeFindHandle hndFindFile, [In, Out, MarshalAs(UnmanagedType.LPStruct)] WIN32_FIND_DATA lpFindFileData);

            private void StartProducerTasks()
            {
                _producerTasks = new Task[_maxDegreeOfParallelism];
                for (int i = 0; i < _maxDegreeOfParallelism; i++)
                {
                    _producerTasks[i] = Task.Run(ProducerWork, _cts.Token);
                }
            }

            private void ProducerWork()
            {
                try
                {
                    foreach (var currentPath in _directoryQueue.GetConsumingEnumerable(_cts.Token))
                    {
                        ProcessDirectory(currentPath);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                }
            }

            private void ProcessDirectory(string path)
            {
                if (!_processedDirectories.TryAdd(path, 1)) return;

                var findData = _findDataPool.Rent();
                try
                {
                    using var hFind = FindFirstFile(Path.Combine(path, _searchPattern), findData);

                    if (hFind.IsInvalid)
                        return;

                    do
                    {
                        if (_cts.Token.IsCancellationRequested) return;

                        if (findData.cFileName is "." or "..") continue;

                        var fullPath = Path.Combine(path, findData.cFileName);

                        if (findData.dwFileAttributes.HasFlag(FileAttributes.Directory))
                        {
                            if (_searchOption == SearchOption.AllDirectories)
                            {
                                _directoryQueue.Add(fullPath);
                            }

                            if (_searchFor is SearchFor.Directories or SearchFor.FilesAndDirectories)
                                _resultQueue.Add(new WinAPIv5(path, findData));
                        }
                        else if (_searchFor is SearchFor.Files or SearchFor.FilesAndDirectories)
                        {
                            _resultQueue.Add(new WinAPIv5(path, findData));
                        }
                    }
                    while (FindNextFile(hFind, findData));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing directory {path}: {ex.Message}");
                }
                finally
                {
                    _findDataPool.Return(findData);
                }
            }

            public bool MoveNext()
            {
                if (_resultQueue.TryTake(out var item, 100, _cts.Token))
                {
                    Current = item;
                    return true;
                }
                return false;
            }

            public void Dispose()
            {
                _cts.Cancel();
                _directoryQueue.CompleteAdding();
                Task.WaitAll(_producerTasks, TimeSpan.FromSeconds(30));
                _resultQueue.Dispose();
                _directoryQueue.Dispose();
                _cts.Dispose();
            }

            public WinAPIv5 Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Reset() => throw new NotSupportedException();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
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

    public enum SearchFor
    {
        Files = 0,
        Directories = 1,
        FilesAndDirectories = 2,
    }
    public class ObjectPool<T> where T : class
    {
        private readonly Func<T> _objectGenerator;
        private readonly ConcurrentBag<T> _objects;
        private readonly int _maxSize;

        public ObjectPool(Func<T> objectGenerator, int maxSize = int.MaxValue)
        {
            _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
            _objects = new ConcurrentBag<T>();
            _maxSize = maxSize;
        }

        public T Rent()
        {
            if (_objects.TryTake(out T item))
                return item;

            return _objectGenerator();
        }

        public void Return(T item)
        {
            if (_objects.Count < _maxSize)
                _objects.Add(item);
        }
    }
}
