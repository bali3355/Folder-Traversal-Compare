using Microsoft.Win32.SafeHandles;
using System.Collections;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Permissions;

namespace LightingFile
{
    [Serializable]
    public class LightingFileInfo
    {

        public readonly FileAttributes Attributes;
        public readonly long Length;
        public readonly string Name;
        public readonly string AlternateName;
        public readonly string FullName;
        private static readonly char[] separator = ['|'];

        public string? DirectoryName => Path.GetDirectoryName(FullName);
        public bool Exists => File.Exists(FullName);
        public override string ToString() => Name;
        public LightingFileInfo(string filename) : this(new FileInfo(filename)) { }

        public LightingFileInfo(FileInfo file)
        {
            Name = file.Name;
            FullName = file.FullName;
            if (file.Exists)
            {
                Length = file.Length;
                Attributes = file.Attributes;
            }
        }
        internal LightingFileInfo(string dir, WIN32_FIND_DATA findData)
        {
            Attributes = findData.dwFileAttributes;
            Length = CombineHighLowInts(findData.nFileSizeHigh, findData.nFileSizeLow);
            Name = findData.cFileName;
            AlternateName = findData.cAlternateFileName;
            FullName = Path.Combine(dir, findData.cFileName);
        }
        public static IEnumerable<LightingFileInfo> EnumerateDirectories(string path) => EnumerateDirectories(path, "*");
        public static IEnumerable<LightingFileInfo> EnumerateDirectories(string path, string searchPattern) => EnumerateDirectories(path, searchPattern, SearchOption.TopDirectoryOnly);
        public static IEnumerable<LightingFileInfo> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption) => EnumerateDirectories(path, searchPattern, searchOption, null);
        public static IEnumerable<LightingFileInfo> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption, IFolderFilter folderFilter)
        {
            ExceptionHandle(path, searchPattern, searchOption);
            return new FileEnumerable(Path.GetFullPath(path), searchPattern, searchOption, null, false);
        }
        public static IEnumerable<LightingFileInfo> EnumerateFiles(string path) => EnumerateFiles(path, "*");
        public static IEnumerable<LightingFileInfo> EnumerateFiles(string path, string searchPattern) => EnumerateFiles(path, searchPattern, SearchOption.TopDirectoryOnly, null);
        public static IEnumerable<LightingFileInfo> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) => EnumerateFiles(path, searchPattern, searchOption, null);
        public static IEnumerable<LightingFileInfo> EnumerateFiles(string path, string searchPattern, SearchOption searchOption, IFolderFilter folderFilter)
        {
            ExceptionHandle(path, searchPattern, searchOption);
            return new FileEnumerable(Path.GetFullPath(path), searchPattern, searchOption, folderFilter, true);
        }

        public static IList<LightingFileInfo> GetFiles2(string path, string searchPattern = "*", bool searchSubfolders = false)
        {
            var searchOption = searchSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return GetFiles(path, searchPattern, searchOption);
        }
        public static IList<LightingFileInfo> GetFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly, IFolderFilter folderFilter = null)
        {
            var list = new List<LightingFileInfo>();
            string[] arr = searchPattern.Split(separator, StringSplitOptions.RemoveEmptyEntries);
            Hashtable ht = (arr.Length > 1 ? new Hashtable() : null); // don't need to worry about case since it should be consistent
            foreach (string sp in arr)
            {
                string sp2 = sp.Trim();
                if (sp2.Length == 0)
                    continue;

                IEnumerable<LightingFileInfo> e = EnumerateFiles(path, sp2, searchOption, folderFilter);
                if (ht == null)
                    list.AddRange(e);
                else
                {
                    var e2 = e.GetEnumerator();
                    if (ht.Count == 0)
                    {
                        while (e2.MoveNext())
                        {
                            LightingFileInfo f = e2.Current;
                            list.Add(f);
                            ht[f.FullName] = f;
                        }
                    }
                    else
                    {
                        while (e2.MoveNext())
                        {
                            LightingFileInfo f = e2.Current;
                            if (!ht.Contains(f.FullName))
                            {
                                list.Add(f);
                                ht[f.FullName] = f;
                            }
                        }
                    }
                }
            }

            return list;
        }

        private static void ExceptionHandle(string path, string searchPattern, SearchOption searchOption)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(searchPattern);
            if ((searchOption != SearchOption.TopDirectoryOnly) && (searchOption != SearchOption.AllDirectories))
                throw new ArgumentOutOfRangeException(nameof(searchOption));
        }

        private class FileEnumerable(string path, string filter, SearchOption searchOption, IFolderFilter folderFilter, bool searchForFiles) : IEnumerable<LightingFileInfo>
        {
            public IEnumerator<LightingFileInfo> GetEnumerator() => new FileEnumerator(path, filter, searchOption, folderFilter, searchForFiles, true, false, false);
            IEnumerator IEnumerable.GetEnumerator() => new FileEnumerator(path, filter, searchOption, folderFilter, searchForFiles, true, false, false);
        }

        // Wraps a FindFirstFile handle.
        private sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
            [DllImport("kernel32.dll")]
            private static extern bool FindClose(IntPtr handle);

            [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
            internal SafeFindHandle() : base(true) { }

            protected override bool ReleaseHandle()
            {
                return FindClose(handle);
            }
        }

        [System.Security.SuppressUnmanagedCodeSecurity]
        public class FileEnumerator : IEnumerator<LightingFileInfo>
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern SafeFindHandle FindFirstFile(string fileName, [In, Out] WIN32_FIND_DATA data);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern bool FindNextFile(SafeFindHandle hndFindFile, [In, Out, MarshalAs(UnmanagedType.LPStruct)] WIN32_FIND_DATA lpFindFileData);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern SafeFindHandle FindFirstFileEx(string fileName, int infoLevel, [In, Out] WIN32_FIND_DATA data, int searchScope, string notUsedNull, int additionalFlags);

            private string initialFolder;
            private SearchOption searchOption;
            private string searchFilter;
            private IFolderFilter folderFilter;
            //---
            private string currentFolder;
            private SafeFindHandle hndFile;
            private WIN32_FIND_DATA findData;
            private int currentPathIndex;
            private IList<string> currentPaths;
            private List<string> pendingFolders;
            private Queue<IList<string>> queue;
            private bool stepToNext;
            private bool usePendingFolders = false;
            private bool useGetDirectories = false;
            private bool isCurrent = false;
            //---
            private readonly bool useEx = false;
            private readonly int infoLevel = 0;
            private readonly int searchScope = 0;
            private readonly int additionalFlags = 0;

            public FileEnumerator(string initialFolder, string searchFilter, SearchOption searchOption, IFolderFilter folderFilter) => Init(initialFolder, searchFilter, searchOption, folderFilter);

            public FileEnumerator(string initialFolder, string searchFilter, SearchOption searchOption, IFolderFilter folderFilter, bool searchForFiles, bool basicInfoOnly, bool caseSensitive, bool largeBuffer)
            {
                Init(initialFolder, searchFilter, searchOption, folderFilter);
                searchScope = searchForFiles ? 0 : 1; // 0 = files, 1 = limit to directories
                useEx = true;
                infoLevel = basicInfoOnly ? 1 : 0; // 0 is standard (includes the cAlternateName, which is the short name with the tidle character)
                additionalFlags |= caseSensitive ? 1 : 0;
                additionalFlags |= largeBuffer ? 2 : 0;
            }

            private void Init(string initialFolder, string searchFilter, SearchOption searchOption, IFolderFilter folderFilter)
            {
                this.initialFolder = initialFolder;
                this.searchFilter = searchFilter;
                this.searchOption = searchOption;
                this.folderFilter = folderFilter;
                usePendingFolders = (searchFilter == "*" || searchFilter == "*.*") && searchOption == SearchOption.AllDirectories;
                useGetDirectories = !usePendingFolders && searchOption == SearchOption.AllDirectories;
                Reset();
            }

            public LightingFileInfo Current => new(currentFolder, findData);

            public void Dispose()
            {
                hndFile?.Dispose();
                hndFile = null;
                GC.SuppressFinalize(this);
            }

            object IEnumerator.Current => new LightingFileInfo(currentFolder, findData);

            public bool MoveNext()
            {
                while (true)
                {
                    if (stepToNext) isCurrent = FindNextFile(hndFile, findData);

                    if (isCurrent || !stepToNext)
                    {
                        while (findData.dwFileAttributes.HasFlag(FileAttributes.Directory))
                        {
                            var cFilename = findData.cFileName;
                            if (cFilename != "." && cFilename != "..")
                            {
                                if (folderFilter == null || folderFilter.SearchFolder(new LightingFileInfo(currentFolder, findData)))
                                    pendingFolders.Add(Path.Combine(currentFolder, cFilename));
                                if (searchScope == 1)
                                {
                                    stepToNext = true;
                                    return true;
                                }

                            }
                            isCurrent = FindNextFile(hndFile, findData);
                            if (!isCurrent) break;
                        }
                    }
                    stepToNext = true;
                    if (isCurrent)
                    {
                        if (searchScope == 0) return true;
                        continue;
                    }

                    if (pendingFolders.Count > 0)
                    {
                        queue.Enqueue(pendingFolders);
                        pendingFolders = [];
                    }

                    currentPathIndex++;
                    if (currentPathIndex == currentPaths.Count)
                    {
                        if (queue.Count == 0)
                        {
                            currentPathIndex--; // so that calling MoveNext() after very last has no impact
                            return false; // no more paths to process
                        }
                        currentPaths = queue.Dequeue();
                        currentPathIndex = 0;
                    }

                    // Initialize the next folder for processing
                    string f = currentPaths[currentPathIndex];
                    InitFolder(f);
                }
            }

            private void InitFolder(string folder)
            {
                hndFile?.Dispose();

                string searchPath = Path.Combine(folder, searchFilter);
                if (useEx)
                    hndFile = FindFirstFileEx(searchPath, infoLevel, findData, searchScope, null, additionalFlags);
                else
                    hndFile = FindFirstFile(searchPath, findData);
                currentFolder = folder;
                stepToNext = false;
                isCurrent = !hndFile.IsInvalid; // e.g. unaccessible C:\System Volume Information or filter like *.txt in a directory with no text files
            }

            public void Reset()
            {
                currentPathIndex = 0;
                stepToNext = false;
                isCurrent = false;
                currentPaths = [initialFolder];
                findData = new WIN32_FIND_DATA();
                pendingFolders = [];
                queue = new Queue<IList<string>>();
                InitFolder(initialFolder);
            }
        }

        public static long CombineHighLowInts(uint high, uint low) => (((long)high) << 0x20) | low;
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

        public override string ToString() => "File name=" + cFileName;
    }

    public interface IFolderFilter
    {
        bool SearchFolder(LightingFileInfo folder);
    }
}
