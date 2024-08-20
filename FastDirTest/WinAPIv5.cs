using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace FileSystemRetrieveCompare
{
    [Serializable]
    public class WinAPIv5
    {
        /// <summary>
        /// Enumerates the filesystem entries.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="searchPattern"></param>
        /// <param name="searchFor"></param>
        /// <param name="deepnessLevel"></param>
        /// <param name="cancellationToken"></param>
        /// <returns> An enumerable of <see cref="FileSystemEntry"/> objects. </returns>
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

        /// <summary>
        /// The main parallel enumerator, which is used to enumerate files and folders.
        /// </summary>
        [System.Security.SuppressUnmanagedCodeSecurity]
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

            /// <summary>
            /// Initializes a new instance of the <see cref="ParallelFileEnumerator"/> class.
            /// </summary>
            /// <param name="path"> The main folder to start enumerating. </param>
            /// <param name="searchPattern"> Given filter for search </param>
            /// <param name="searchFor"> Search for files or folders or both </param>
            /// <param name="maxDegreeOfParallelism"> The maximum degree of parallelism, which is the maximum number of tasks that can be active at the same time. </param>
            /// <param name="deepnessLevel"> The deepness level of the search </param>
            /// <param name="cancellationToken"></param>
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

            #region WinAPI 32 methods
            /// <summary>
            /// Searches for the first file or directory that matches the specified search criteria.
            /// </summary>
            /// <param name="fileName"></param>
            /// <param name="data"></param>
            /// <returns></returns>
            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern SafeFindHandle FindFirstFile(StringBuilder fileName, [In, Out] WIN32_FIND_DATA data);

            /// <summary>
            /// Searches for the next file or directory that matches the specified search criteria.
            /// </summary>
            /// <param name="hndFindFile"></param>
            /// <param name="lpFindFileData"></param>
            /// <returns></returns>
            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern bool FindNextFile(SafeFindHandle hndFindFile, [In, Out, MarshalAs(UnmanagedType.LPStruct)] WIN32_FIND_DATA lpFindFileData);

            #endregion

            public void Reset() => throw new NotSupportedException();
            public FileSystemEntry Current { get; private set; }
            object IEnumerator.Current => Current;

            public void Dispose()
            {
                _resultQueue.CompleteAdding();
                _resultQueue.Dispose();
                _directoryStack.Clear();
            }

            /// <summary>
            /// Moves the enumerator to the next element from the <see cref="BlockingCollection{T}"/>.
            /// </summary>
            /// <returns>Enumerated <see cref="FileSystemEntry"/> items</returns>
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


            /// <summary>
            /// Checks if the <paramref name="fileName"/> is <c>reparse point</c> or <c>Thumbs.db</c>
            /// </summary>
            /// <param name="fileName"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsToLeftOut(string fileName) => fileName switch
            {
                "." or ".." or "Thumbs.db" => true,
                _ => false
            };

            /// <summary>
            /// Processes the <see cref="ConcurrentStack{T}"/>
            /// Main thread, which gets the full <paramref name="path"/> and <paramref name="depth"/>
            /// </summary>
            /// <param name="path"></param>
            /// <param name="depth"></param>
            private void ProcessDirectory(StringBuilder path, int depth)
            {
                //If the depth is reached, then return
                if (_deepnessLevel != -1 && depth >= _deepnessLevel) return;
                var findData = new WIN32_FIND_DATA();
                try
                {
                    //Finding first file and then check if it's a directory
                    var tempPath = new StringBuilder(260);
                    tempPath.Append(path).Append(Path.DirectorySeparatorChar).Append(_searchPattern);
                    using var hFind = FindFirstFile(tempPath, findData);
                    //If it's not a directory, then return
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

            /// <summary>
            /// Thread that consumes the <see cref="BlockingCollection{T}"/>.
            /// Each folder is processed by <see cref="ProcessDirectory(StringBuilder, int)"/>
            /// </summary>
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

            /// <summary>
            /// Creates <see cref="Task"/> for each <see cref="ProducerWork()"/>
            /// </summary>
            private void StartProducerTasks()
            {
                for (int i = 0; i < _maxDegreeOfParallelism; i++)
                    _ = Task.Factory.StartNew(ProducerWork, _cToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
        }
    }
}
