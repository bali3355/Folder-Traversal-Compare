using Microsoft.Win32.SafeHandles;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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

    [StructLayout(LayoutKind.Auto)]
    public readonly struct FileSystemEntry
    {
        public readonly string Name;
        public readonly string FullName;
        public readonly FileAttributes Attributes;
        public readonly long Length;
        private readonly Lazy<bool> _exists;
        private readonly Lazy<string?> _directoryName;

        public FileSystemEntry(string cFileName, StringBuilder fullName, FileAttributes attributes, long length)
        {
            var tempFullName = fullName.ToString();

            Name = string.Intern(cFileName);
            FullName = tempFullName;
            Attributes = attributes;
            Length = length;
            _exists = new(() => File.Exists(tempFullName));
            _directoryName = new(() => Path.GetDirectoryName(tempFullName));
        }
        public bool Exists => _exists.Value;
        public string? DirectoryName => _directoryName.Value;
    }

    [Serializable]
    public class WinAPIv5
    {

        public static IEnumerable<FileSystemEntry> EnumerateFileSystem(string path,
                                                                string searchPattern = "*",
                                                                SearchFor searchFor = SearchFor.Files,
                                                                int deepnessLevel = -1,
                                                                CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(searchPattern);

            return new FileEnumerable(Path.GetFullPath(path), searchPattern, searchFor, deepnessLevel, cancellationToken);
        }

        private class FileEnumerable(string path, string filter, SearchFor searchFor, int deepnessLevel, CancellationToken cancellationToken) : IEnumerable<FileSystemEntry>
        {
            private readonly int _deepnessLevel = deepnessLevel <= 0 ? -1 : deepnessLevel;
            private readonly int _maxDegreeOfParallelism = (int)(Environment.ProcessorCount * 1.5);
            public IEnumerator<FileSystemEntry> GetEnumerator() => new ParallelFileEnumerator(path, filter, searchFor, _maxDegreeOfParallelism, _deepnessLevel, cancellationToken);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private class ParallelFileEnumerator : IEnumerator<FileSystemEntry>
        {
            private readonly CancellationToken _cToken;
            private readonly int _deepnessLevel;
            private readonly ConcurrentStack<(StringBuilder path, int depth)> _directoryStack;
            private readonly string _initialPath;
            private readonly int _maxDegreeOfParallelism;
            private readonly BlockingCollection<FileSystemEntry> _resultQueue;
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
                _directoryStack = new ConcurrentStack<(StringBuilder path, int depth)>([(new StringBuilder(_initialPath), 0)]);
                _resultQueue = new BlockingCollection<FileSystemEntry>(new ConcurrentQueue<FileSystemEntry>());
                StartProducerTasks();
            }

            public FileSystemEntry Current { get; private set; }

            object IEnumerator.Current => Current;

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern SafeFindHandle FindFirstFile(StringBuilder fileName, [In, Out] WIN32_FIND_DATA data);

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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsToLeftOut(string fileName) => fileName switch
            {
                "." or ".." or "Thumbs.db" => true,
                _ => false
            };

            private void ProcessDirectory(StringBuilder path, int depth)
            {
                if (_deepnessLevel != -1 && depth >= _deepnessLevel) return;
                var findData = new WIN32_FIND_DATA();
                try
                {
                    StringBuilder tempPath = new(260);
                    tempPath.Append(path).Append(Path.DirectorySeparatorChar).Append(_searchPattern);
                    using var hFind = FindFirstFile(tempPath, findData);
                    if (hFind.IsInvalid) return;

                    do
                    {
                        _cToken.ThrowIfCancellationRequested();

                        if (IsToLeftOut(findData.cFileName)) continue;

                        tempPath = new(260);
                        tempPath.Append(path).Append(Path.DirectorySeparatorChar).Append(findData.cFileName);

                        if (findData.dwFileAttributes.HasFlag(FileAttributes.Directory))
                        {
                            _directoryStack.Push((tempPath, depth + 1));

                            if (_searchFor != SearchFor.Files) _resultQueue.Add(new FileSystemEntry(findData.cFileName, tempPath, findData.dwFileAttributes, 0));
                        }
                        else if (_searchFor != SearchFor.Directories)
                        {
                            long fileSize = ((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow;
                            _resultQueue.Add(new FileSystemEntry(findData.cFileName, tempPath, findData.dwFileAttributes, fileSize));
                        }
                    }
                    while (FindNextFile(hFind, findData));
                }
                catch (OperationCanceledException) { /* Expected when cancellation is requested */ }
                catch (Exception ex) { Debug.WriteLine($"Error processing directory {path}: {ex.Message}"); }
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
                    _ = Task.Factory.StartNew(ProducerWork, _cToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
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
}
