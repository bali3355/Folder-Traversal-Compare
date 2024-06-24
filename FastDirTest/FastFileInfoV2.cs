﻿using Microsoft.Win32.SafeHandles;
using System.Collections;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Permissions;

namespace FastFileV2
{
    /// <summary>
    /// Based on Opulus FastFile <see cref="FastFile.FastFileInfo"/>
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
        public static IEnumerable<FastFileInfoV2> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption) => EnumerateDirectories(path, searchPattern, searchOption, null);
        public static IEnumerable<FastFileInfoV2> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption, IFolderFilter folderFilter)
        {
            ExceptionHandle(path, searchPattern, searchOption);
            return new FileEnumerable(Path.GetFullPath(path), searchPattern, searchOption, folderFilter, false);
        }

        public static IEnumerable<FastFileInfoV2> EnumerateFiles(string path) => EnumerateFiles(path, "*");
        public static IEnumerable<FastFileInfoV2> EnumerateFiles(string path, string searchPattern) => EnumerateFiles(path, searchPattern, SearchOption.TopDirectoryOnly, null);
        public static IEnumerable<FastFileInfoV2> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) => EnumerateFiles(path, searchPattern, searchOption, null);
        public static IEnumerable<FastFileInfoV2> EnumerateFiles(string path, string searchPattern, SearchOption searchOption, IFolderFilter folderFilter)
        {
            ExceptionHandle(path, searchPattern, searchOption);
            return new FileEnumerable(Path.GetFullPath(path), searchPattern, searchOption, folderFilter, true);
        }

        private static void ExceptionHandle(string path, string searchPattern, SearchOption searchOption)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(searchPattern);
            if ((searchOption != SearchOption.TopDirectoryOnly) && (searchOption != SearchOption.AllDirectories))
                throw new ArgumentOutOfRangeException(nameof(searchOption));
        }

        private class FileEnumerable(string path, string filter, SearchOption searchOption, IFolderFilter folderFilter, bool searchForFiles) : IEnumerable<FastFileInfoV2>
        {
            public IEnumerator<FastFileInfoV2> GetEnumerator() => new FileEnumerator(path, filter, searchOption, folderFilter, searchForFiles, true, false, false);
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
        public class FileEnumerator : IEnumerator<FastFileInfoV2>
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern SafeFindHandle FindFirstFile(string fileName, [In, Out] WIN32_FIND_DATA data);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern bool FindNextFile(SafeFindHandle hndFindFile, [In, Out, MarshalAs(UnmanagedType.LPStruct)] WIN32_FIND_DATA lpFindFileData);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern SafeFindHandle FindFirstFileEx(string fileName, int infoLevel, [In, Out] WIN32_FIND_DATA data, int searchScope, string notUsedNull, int additionalFlags);

            private string initialFolder;
            private string searchFilter;
            private IFolderFilter folderFilter;
            //---
            private string currentFolder;
            private SafeFindHandle hndFile;
            private WIN32_FIND_DATA findData;
            private Queue<string> queue;
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
                this.folderFilter = folderFilter;
                Reset();
            }

            public FastFileInfoV2 Current => new(currentFolder, findData);

            public void Dispose()
            {
                hndFile?.Dispose();
                hndFile = null;
                GC.SuppressFinalize(this);
                GC.Collect();
            }

            object IEnumerator.Current => new FastFileInfoV2(currentFolder, findData);

            public bool MoveNext()
            {
                while (true)
                {
                    isCurrent = FindNextFile(hndFile, findData);

                    while (findData.dwFileAttributes.HasFlag(FileAttributes.Directory) && isCurrent)
                    {
                        var cFilename = findData.cFileName;
                        if (cFilename is not "." and not "..")
                        {
                            queue.Enqueue(Path.Combine(currentFolder, cFilename));
                            if (searchScope == 1) return true;
                        }
                        isCurrent = FindNextFile(hndFile, findData);
                    }

                    if (isCurrent)
                    {
                        if (searchScope == 0) return true;
                        continue;
                    }

                    if (queue.Count == 0) return false;

                    // Initialize the next folder for processing
                    InitFolder(queue.Dequeue());
                }
            }

            private void InitFolder(string folder)
            {
                hndFile?.Dispose();
                string searchPath = Path.Combine(folder, searchFilter);
                if (useEx)
                    hndFile = FindFirstFileEx(searchPath, infoLevel, findData, 1, null, additionalFlags);
                else
                    hndFile = FindFirstFile(searchPath, findData);
                currentFolder = folder;
                isCurrent = !hndFile.IsInvalid; // e.g. unaccessible C:\System Volume Information or filter like *.txt in a directory with no text files
            }

            public void Reset()
            {
                isCurrent = false;
                currentFolder = initialFolder;
                findData = new WIN32_FIND_DATA();
                queue = new Queue<string>();
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
        bool SearchFolder(FastFileInfoV2 folder);
    }
}
