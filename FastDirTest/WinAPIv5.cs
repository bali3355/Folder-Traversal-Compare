using Microsoft.Win32.SafeHandles;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FastFileV5
{
    public enum SearchFor
    {
        Files = 0,
        Directories = 1,
        FilesAndDirectories = 2,
    }

    public class ObjectPool<T>(Func<T> objectGenerator, int maxSize = int.MaxValue) where T : class
    {
        private readonly Func<T> _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
        private readonly ConcurrentBag<T> _objects = [];

        public T Rent()
        {
            if (_objects.TryTake(out T item))
                return item;

            return _objectGenerator();
        }

        public void Return(T item)
        {
            if (_objects.Count < maxSize)
                _objects.Add(item);
        }
    }

    [Serializable]
    public class WinAPIv5
    {
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

        public string AlternateName { get; }
        public FileAttributes Attributes { get; }
        public string? DirectoryName => Path.GetDirectoryName(FullName);
        public bool Exists => File.Exists(FullName);
        public string FullName { get; }
        public long Length { get; }
        public string Name { get; }
        public static long CombineHighLowInts(uint high, uint low) => (((long)high) << 32) | low;

        public static IEnumerable<WinAPIv5> EnumerateFileSystem(
            string path,
            string searchPattern = "*",
            SearchFor searchFor = SearchFor.Files,
            int deepnessLevel = -1,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(searchPattern);

            return new FileEnumerable(Path.GetFullPath(path), searchPattern, searchFor, deepnessLevel, cancellationToken);
        }

        public override string ToString() => FullName;
        private class FileEnumerable(string path, string filter, SearchFor searchFor, int deepnessLevel, CancellationToken cancellationToken) : IEnumerable<WinAPIv5>
        {
            private readonly int _deepnessLevel = deepnessLevel <= 0 ? -1 : deepnessLevel;
            private readonly int _maxDegreeOfParallelism = (int)(Environment.ProcessorCount * 1.5);
            public IEnumerator<WinAPIv5> GetEnumerator() => new ParallelFileEnumerator(path, filter, searchFor, _maxDegreeOfParallelism, _deepnessLevel, cancellationToken);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private class ParallelFileEnumerator : IEnumerator<WinAPIv5>
        {
            private readonly CancellationToken _cToken;
            private readonly int _deepnessLevel;
            private readonly ConcurrentStack<(string path, int depth)> _directoryStack;
            private readonly ObjectPool<WIN32_FIND_DATA> _findDataPool;
            private readonly string _initialPath;
            private readonly int _maxDegreeOfParallelism;
            private readonly ConcurrentDictionary<string, byte> _processedDirectories = new();
            private readonly BlockingCollection<WinAPIv5> _resultQueue;
            private readonly SearchFor _searchFor;
            private readonly string _searchPattern;
            private int _activeProducers;
            private bool _isCompleted;

            public ParallelFileEnumerator(string path, string searchPattern, SearchFor searchFor, int maxDegreeOfParallelism, int deepnessLevel, CancellationToken cancellationToken)
            {
                _initialPath = path;
                _searchPattern = searchPattern;
                _searchFor = searchFor;
                _maxDegreeOfParallelism = maxDegreeOfParallelism;
                _deepnessLevel = deepnessLevel;
                _cToken = cancellationToken;
                _activeProducers = _maxDegreeOfParallelism;
                _directoryStack = new ConcurrentStack<(string path, int depth)>([(_initialPath, 0)]);
                _resultQueue = new BlockingCollection<WinAPIv5>(new ConcurrentQueue<WinAPIv5>());
                _findDataPool = new ObjectPool<WIN32_FIND_DATA>(() => new WIN32_FIND_DATA(), maxSize: _maxDegreeOfParallelism * 2);
                StartProducerTasks();
            }

            public WinAPIv5 Current { get; private set; }

            object IEnumerator.Current => Current;

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern SafeFindHandle FindFirstFile(string fileName, [In, Out] WIN32_FIND_DATA data);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern bool FindNextFile(SafeFindHandle hndFindFile, [In, Out, MarshalAs(UnmanagedType.LPStruct)] WIN32_FIND_DATA lpFindFileData);

            public void Dispose()
            {
                _resultQueue.CompleteAdding();
                _resultQueue.Dispose();
                _directoryStack.Clear();
            }

            public bool MoveNext()
            {
                if (_isCompleted) return false;

                try
                {
                    while (_resultQueue.TryTake(out var item, Timeout.Infinite, _cToken))
                    {
                        Current = item;
                        return true;
                    }
                }
                catch (OperationCanceledException) { /* Expected when cancellation is requested */ }
                catch (InvalidOperationException) { /* Queue is completed*/}

                _isCompleted = true;
                return false;
            }

            public void Reset() => throw new NotSupportedException();

            private void ProcessDirectory(string path, int depth)
            {
                if (!_processedDirectories.TryAdd(path, 1)) return;
                if (_deepnessLevel != -1 && depth >= _deepnessLevel) return;
                var findData = _findDataPool.Rent();
                try
                {
                    using var hFind = FindFirstFile(Path.Combine(path, _searchPattern), findData);
                    if (hFind.IsInvalid) return;
                    

                    do
                    {
                        _cToken.ThrowIfCancellationRequested();

                        if (findData.cFileName is "." or ".." or "Thumbs.db") continue;

                        StringBuilder stringBuilder = new();
                        stringBuilder.Append(path).Append(Path.DirectorySeparatorChar).Append(findData.cFileName);
                        var fullPath = stringBuilder.ToString();

                        if (findData.dwFileAttributes.HasFlag(FileAttributes.Directory))
                        {
                            _directoryStack.Push((fullPath, depth + 1));

                            if (_searchFor != SearchFor.Files) _resultQueue.Add(new WinAPIv5(path, findData));
                        }
                        else if (_searchFor != SearchFor.Directories) _resultQueue.Add(new WinAPIv5(path, findData));
                    }
                    while (FindNextFile(hFind, findData));
                }
                catch (OperationCanceledException) { /* Expected when cancellation is requested */ }
                catch (Exception ex) { Debug.WriteLine($"Error processing directory {path}: {ex.Message}"); }
                finally { _findDataPool.Return(findData); }
            }

            private void ProducerWork()
            {
                try
                {
                    while (_directoryStack.TryPop(out var dirInfo) && !_cToken.IsCancellationRequested)
                    {
                        ProcessDirectory(dirInfo.path, dirInfo.depth);
                        if (_directoryStack.IsEmpty && _activeProducers == 1) break;
                    }
                }
                catch (OperationCanceledException) {  /*Expected when cancellation is requested*/ }
                catch (Exception ex) { Debug.WriteLine($"Error in producer work: {ex}"); }
                finally
                {
                    if (Interlocked.Decrement(ref _activeProducers) == 0)
                    {
                        _resultQueue.CompleteAdding();
                        _directoryStack.Clear();
                    }
                }
            }

            private void StartProducerTasks()
            {
                for (int i = 0; i < _maxDegreeOfParallelism; i++)
                {
                    Task.Factory.StartNew(ProducerWork, _cToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }
            }
        }

        private sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal SafeFindHandle() : base(true) { }

            protected override bool ReleaseHandle() => FindClose(handle);

            [DllImport("kernel32.dll")]
            private static extern bool FindClose(IntPtr handle);
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
}
