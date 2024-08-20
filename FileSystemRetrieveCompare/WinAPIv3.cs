using System.Collections;
using System.Runtime.InteropServices;

namespace FileSystemRetrieveCompare
{

    /// <summary>
    /// Based on Opulus <see cref="FastFileInfo.FastFileInfo"/>
    /// </summary>
    [Serializable]
    public class WinAPIv3
    {
        public static IEnumerable<FileSystemEntry> EnumerateFileSystem(string path, string filter = "*", SearchFor searchFor = SearchFor.Files, int deepnessLevel = -1)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(filter);
            return new FileEnumerable(Path.GetFullPath(path), filter, searchFor, deepnessLevel);
        }

        private class FileEnumerable(string path, string filter, SearchFor searchFor, int deepnessLevel) : IEnumerable<FileSystemEntry>
        {
            public IEnumerator<FileSystemEntry> GetEnumerator() => new FileEnumerator(path, filter, searchFor, deepnessLevel, true, false, false);
            IEnumerator IEnumerable.GetEnumerator() => new FileEnumerator(path, filter, searchFor, deepnessLevel, true, false, false);
        }

        [System.Security.SuppressUnmanagedCodeSecurity]
        public class FileEnumerator : IEnumerator<FileSystemEntry>
        {
            #region WINAPI32 Imports
            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern SafeFindHandle FindFirstFile(string fileName, [Out] WIN32_FIND_DATA data);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern SafeFindHandle FindFirstFileEx(string fileName, int infoLevel, [Out] WIN32_FIND_DATA data, int searchScope, string notUsedNull, int additionalFlags);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern bool FindNextFile(SafeFindHandle hndFindFile, [In, Out, MarshalAs(UnmanagedType.LPStruct)] WIN32_FIND_DATA lpFindFileData);
            #endregion

            public FileEnumerator(string initialFolder, string searchFilter) => Init(initialFolder, searchFilter);

            public FileEnumerator(string initialFolder, string searchFilter, SearchFor searchFor, int deepnessLevel, bool basicInfoOnly, bool caseSensitive, bool largeBuffer)
            {
                Init(initialFolder, searchFilter);
                SearchFor = (int)searchFor; // 0 = files, 1 = directories, 2 = files and directories
                DeepnessLevel = deepnessLevel < 1 ? -1 : deepnessLevel;
                UseEx = true;
                InfoLevel = basicInfoOnly ? 1 : 0; // 0 is standard (includes the cAlternateName, which is the short name with the tidle character)
                AdditionalFlags |= caseSensitive ? 1 : 0;
                AdditionalFlags |= largeBuffer ? 2 : 0;
            }

            private void Init(string initialFolder, string searchFilter)
            {
                InitialFolder = initialFolder;
                SearchFilter = searchFilter;
                Reset();
            }

            private void InitFolder((string path, int level) folder)
            {
                CurrentFolder = folder.path;
                HndFile?.Dispose();
                string searchPath = Path.Combine(folder.path, SearchFilter);
                if (UseEx)
                    HndFile = FindFirstFileEx(searchPath, InfoLevel, FindData, 1, null, AdditionalFlags); // 0 = files, 1 = limit to directories(files and directories)
                else
                    HndFile = FindFirstFile(searchPath, FindData);
                IsCurrent = !HndFile.IsInvalid; // e.g. unaccessible C:\System Volume Information or filter like *.txt in a directory with no text files
            }
            private static long CombineHighLowInts(uint high, uint low) => (((long)high) << 0x20) | low;
            public FileSystemEntry Current => new(FindData.cFileName, CurrentFolder, FindData.dwFileAttributes, CombineHighLowInts(FindData.nFileSizeHigh, FindData.nFileSizeLow));
            object IEnumerator.Current => new FileSystemEntry(FindData.cFileName, CurrentFolder, FindData.dwFileAttributes, CombineHighLowInts(FindData.nFileSizeHigh, FindData.nFileSizeLow));
            private int AdditionalFlags { get; } = 0;

            //---
            private string CurrentFolder { get; set; }
            private WIN32_FIND_DATA FindData { get; set; }
            private Stack<(string path, int level)> FolderStack { get; set; }
            private SafeFindHandle HndFile { get; set; }
            //---
            private int InfoLevel { get; } = 0;
            private string InitialFolder { get; set; }
            private int DeepnessLevel { get; }
            private bool IsCurrent { get; set; } = false;
            private string SearchFilter { get; set; }
            private int SearchFor { get; } = 0;
            private bool StepToNext { get; set; }
            private bool UseEx { get; } = false;

            public static bool IsToLeftOut(string fileName) => fileName switch
            {
                "." or ".." or "Thumbs.db" => true,
                _ => false
            };

            public bool MoveNext()
            {
                int deepnessLevel = 0;
                while (true)
                {
                    if (StepToNext) IsCurrent = FindNextFile(HndFile, FindData);
                    StepToNext = true;
                    if (!IsCurrent)
                    {
                        if (FolderStack.Count == 0 || (DeepnessLevel != -1 & deepnessLevel >= DeepnessLevel)) return false;
                        InitFolder(FolderStack.Pop());
                        continue;
                    }
                    if (IsToLeftOut(FindData.cFileName)) continue;
                    if (FindData.dwFileAttributes.HasFlag(FileAttributes.Directory))
                    {
                        FolderStack.Push((Path.Combine(CurrentFolder, FindData.cFileName), deepnessLevel + 1));
                        deepnessLevel++;
                        if (SearchFor != 0) return true;
                        continue;
                    }
                    if (SearchFor != 1) return true;
                }
            }
            public void Dispose()
            {
                if (HndFile != null)
                {
                    HndFile.Dispose();
                    HndFile = null;
                }
            }

            public void Reset()
            {
                IsCurrent = false;
                CurrentFolder = InitialFolder;
                FindData = new WIN32_FIND_DATA();
                FolderStack = new();
                InitFolder((InitialFolder, 0));
            }
        }
    }
}
