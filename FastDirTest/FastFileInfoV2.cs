using Microsoft.Win32.SafeHandles;
using System.Collections;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Permissions;



namespace FastFileV2
{
    /// <summary>
    /// Based on Opulus FastFileInfo <see cref="FastFile.FastFileInfo"/>
    /// </summary>
    [Serializable]
    public class FastFileInfoV2
    {
        public readonly long Length;
        public readonly string Name;
        public readonly string AlternateName;
        public readonly string FullName;
        public readonly FileAttributes Attributes;
        public string? DirectoryName => Path.GetDirectoryName(FullName);
        public bool Exists => File.Exists(FullName);
        public override string ToString() => FullName;
        public FastFileInfoV2(string filename) : this(new FileInfo(filename)) { }

        public FastFileInfoV2(FileInfo file)
        {
            Name = file.Name;
            FullName = file.FullName;
            if (file.Exists)
            {
                Length = file.Length;
                Attributes = file.Attributes;
            }
        }
        internal FastFileInfoV2(string dir, WIN32_FIND_DATA findData)
        {
            Attributes = findData.dwFileAttributes;
            Length = CombineHighLowInts(findData.nFileSizeHigh, findData.nFileSizeLow);
            Name = findData.cFileName;
            AlternateName = findData.cAlternateFileName;
            FullName = Path.Combine(dir, findData.cFileName);
        }
        public static IEnumerable<FastFileInfoV2> EnumerateDirectories(string path) => EnumerateDirectories(path, "*");
        public static IEnumerable<FastFileInfoV2> EnumerateDirectories(string path, string searchPattern) => EnumerateDirectories(path, searchPattern, SearchOption.TopDirectoryOnly);
        public static IEnumerable<FastFileInfoV2> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption) => EnumerateDirectories(path, searchPattern, SearchOption.TopDirectoryOnly, SearchFor.Directories);
        public static IEnumerable<FastFileInfoV2> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption, SearchFor searchFor)
        {
            ExceptionHandle(path, searchPattern, searchOption);
            return new FileEnumerable(Path.GetFullPath(path), searchPattern, searchOption, searchFor);
        }

        public static IEnumerable<FastFileInfoV2> EnumerateFiles(string path) => EnumerateFiles(path, "*");
        public static IEnumerable<FastFileInfoV2> EnumerateFiles(string path, string searchPattern) => EnumerateFiles(path, searchPattern, SearchOption.TopDirectoryOnly);
        public static IEnumerable<FastFileInfoV2> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) => EnumerateFiles(path, searchPattern, SearchOption.TopDirectoryOnly, SearchFor.Files);

        public static IEnumerable<FastFileInfoV2> EnumerateFiles(string path, string searchPattern, SearchOption searchOption, SearchFor searchFor)
        {
            ExceptionHandle(path, searchPattern, searchOption);
            return new FileEnumerable(Path.GetFullPath(path), searchPattern, searchOption, searchFor);
        }

        private static void ExceptionHandle(string path, string searchPattern, SearchOption searchOption)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(searchPattern);
            if ((searchOption != SearchOption.TopDirectoryOnly) && (searchOption != SearchOption.AllDirectories))
                throw new ArgumentOutOfRangeException(nameof(searchOption));
        }

        private class FileEnumerable(string path, string filter, SearchOption searchOption, SearchFor searchFor) : IEnumerable<FastFileInfoV2>
        {
            public IEnumerator<FastFileInfoV2> GetEnumerator() => new FileEnumerator(path, filter, searchOption, searchFor, true, false, false);
            IEnumerator IEnumerable.GetEnumerator() => new FileEnumerator(path, filter, searchOption, searchFor, true, false, false);
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
        public class FileEnumerator : IEnumerator<FastFileInfoV2>
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern SafeFindHandle FindFirstFile(string fileName, [In, Out] WIN32_FIND_DATA data);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern bool FindNextFile(SafeFindHandle hndFindFile, [In, Out, MarshalAs(UnmanagedType.LPStruct)] WIN32_FIND_DATA lpFindFileData);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern SafeFindHandle FindFirstFileEx(string fileName, int infoLevel, [In, Out] WIN32_FIND_DATA data, int searchScope, string notUsedNull, int additionalFlags);

            private string InitialFolder { get; set; }
            private string SearchFilter { get; set; }
            //---
            private string CurrentFolder { get; set; }
            private SafeFindHandle HndFile { get; set; }
            private WIN32_FIND_DATA FindData { get; set; }
            private Stack<string> FolderStack { get; set; }
            private bool IsCurrent { get; set; } = false;
            private bool StepToNext { get; set; }
            //---
            private int InfoLevel { get; } = 0;
            private int SearchScope { get; } = 0;
            private bool UseEx { get; } = false;
            private int AdditionalFlags { get; } = 0;

            public FileEnumerator(string initialFolder, string searchFilter, SearchOption searchOption) => Init(initialFolder, searchFilter, searchOption);

            public FileEnumerator(string initialFolder, string searchFilter, SearchOption searchOption, SearchFor searchScope, bool basicInfoOnly, bool caseSensitive, bool largeBuffer)
            {
                Init(initialFolder, searchFilter, searchOption);
                SearchScope = (int)searchScope; // 0 = files, 1 = directories, 2 = files and directories
                UseEx = true;
                InfoLevel = basicInfoOnly ? 1 : 0; // 0 is standard (includes the cAlternateName, which is the short name with the tidle character)
                AdditionalFlags |= caseSensitive ? 1 : 0;
                AdditionalFlags |= largeBuffer ? 2 : 0;
            }

            private void Init(string initialFolder, string searchFilter, SearchOption searchOption)
            {
                InitialFolder = initialFolder;
                SearchFilter = searchFilter;
                Reset();
            }

            public FastFileInfoV2 Current => new(CurrentFolder, FindData);

            public void Dispose()
            {
                HndFile?.Dispose();
                HndFile = null;
                GC.SuppressFinalize(this);
            }

            object IEnumerator.Current => new FastFileInfoV2(CurrentFolder, FindData);

            public bool MoveNext()
            {
                while (true)
                {
                    if (StepToNext) IsCurrent = FindNextFile(HndFile, FindData);
                    StepToNext = true;
                    if (!IsCurrent)
                    {
                        if (FolderStack.Count == 0) return false;
                        InitFolder(FolderStack.Pop());
                        continue;
                    }
                    if (FindData.cFileName is "." or "..") continue;
                    if (FindData.dwFileAttributes.HasFlag(FileAttributes.Directory))
                    {
                        FolderStack.Push(Path.Combine(CurrentFolder, FindData.cFileName));
                        if (SearchScope is 1 or 2) return true;
                        continue;
                    }
                    if (SearchScope is 0 or 2) return true;
                }
            }

            private void InitFolder(string folder)
            {
                CurrentFolder = folder;
                HndFile?.Dispose();
                string searchPath = Path.Combine(folder, SearchFilter);
                if (UseEx)
                    HndFile = FindFirstFileEx(searchPath, InfoLevel, FindData, 1, null, AdditionalFlags); // 0 = files, 1 = limit to directories(files and directories)
                else
                    HndFile = FindFirstFile(searchPath, FindData);
                IsCurrent = !HndFile.IsInvalid; // e.g. unaccessible C:\System Volume Information or filter like *.txt in a directory with no text files
            }

            public void Reset()
            {
                IsCurrent = false;
                CurrentFolder = InitialFolder;
                FindData = new WIN32_FIND_DATA();
                FolderStack = new Stack<string>();
                InitFolder(InitialFolder);
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

        public override string ToString() => "FileName = " + cFileName;
    }
    public enum SearchFor
    {
        Files = 0,
        Directories = 1,
        FilesAndDirectories = 2,
    }
}
