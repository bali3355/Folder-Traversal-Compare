using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace FastFileV3
{
    /// <summary>
    ///  Based on this code:
    ///  <see href="https://web.archive.org/web/20130426032447/http://codepaste.net/msm8b1"/>
    /// </summary>
    internal class WinAPIv3
    {
        #region Import from kernel32

        [Serializable, StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode), BestFitMapping(false)]
        private struct WIN32_FIND_DATA
        {
            public FileAttributes dwFileAttributes;
            public FILETIME ftCreationTime;
            public FILETIME ftLastAccessTime;
            public FILETIME ftLastWriteTime;
            public int nFileSizeHigh;
            public int nFileSizeLow;
            public int dwReserved0;
            public int dwReserved1;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string cFileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternate;
        }

        #endregion Import from kernel32

        private const int MAX_PATH = 260;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

        public static IEnumerable<string> GetDirectories(string path, string searchPattern) => GetInternal(path, searchPattern, true);
        public static IEnumerable<string> GetFiles(string path, string searchPattern) => GetInternal(path, searchPattern, false);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool FindClose(IntPtr hFindFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindFirstFileW(string lpFileName,
                                                   out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool FindNextFileW(IntPtr hFindFile,
                                                out WIN32_FIND_DATA lpFindFileData);

        private static IEnumerable<string> GetInternal(string path, string searchPattern, bool isGetDirs)
        {
            var findHandle = FindFirstFileW(Path.Combine(path, searchPattern), out WIN32_FIND_DATA findData);

            if (findHandle == INVALID_HANDLE_VALUE) yield break;
            try
            {
                do
                {
                    if (findData.cFileName == "." || findData.cFileName == ".." || findData.cFileName == "thumbs.db") continue;
                    if (isGetDirs
                            ? (findData.dwFileAttributes & FileAttributes.Directory) != 0
                            : (findData.dwFileAttributes & FileAttributes.Directory) == 0)
                        yield return Path.Combine(path, findData.cFileName);
                } while (FindNextFileW(findHandle, out findData));
            }
            finally
            {
                FindClose(findHandle);
            }
        }

        public static ConcurrentBag<string> GetAllDirectories(string path, int deepness, int rootLength)
        {
            if (!(deepness < 1 || path.Split('\\').Length < (deepness + rootLength))) return [];

            var allDirs = new ConcurrentBag<string>();
            var subDirs = GetDirectories(path, "*");

            _ = Parallel.ForEach(subDirs, (subDir) =>
            {
                var relativePath = Path.Combine(path, subDir);
                allDirs.Add(relativePath);
                Parallel.ForEach(GetAllDirectories(relativePath, deepness, rootLength), (dir) => allDirs.Add(dir));
            });
            return allDirs;
        }

        public static ConcurrentBag<string> GetAllFiles(string path, int deepness, int rootLength)
        {
            if (!(deepness < 1 || path.Split('\\').Length < (deepness + rootLength))) return [];

            var allFiles = new ConcurrentBag<string>();
            var subFiles = GetFiles(path, "*");
            _ = Parallel.ForEach(subFiles, (subFile) =>
            {
                allFiles.Add(Path.Combine(path, subFile));
            });

            var subDirs = GetDirectories(path, "*");
            _ = Parallel.ForEach(subDirs, (subDir) =>
            {
                var relativePath = Path.Combine(path, subDir);
                Parallel.ForEach(GetAllFiles(relativePath, deepness, rootLength), (file) => allFiles.Add(file));
            });
            return allFiles;
        }

        public static ConcurrentBag<string> GetAllFilesV2(string path)
        {
            var allFiles = new ConcurrentBag<string>();
            var queue = new ConcurrentQueue<string>([path]);

            while (queue.TryDequeue(out var currentPath))
            {
                foreach (var subDir in GetDirectories(currentPath, "*")) queue.Enqueue(subDir);
                foreach (var subFile in GetFiles(currentPath, "*")) allFiles.Add(subFile);
            }
            return allFiles;
        }
        public static ConcurrentBag<string> GetAllFilesV3(string path)
        {
            var allFiles = new ConcurrentBag<string>();
            var queue = new ConcurrentStack<string>([path]);

            while (queue.TryPop(out var currentPath))
            {
                foreach (var subDir in GetDirectories(currentPath, "*")) queue.Push(subDir);
                foreach (var subFile in GetFiles(currentPath, "*")) allFiles.Add(subFile);
            }
            return allFiles;
        }

        public static ConcurrentBag<string> GetAllFilesV4(string path)
        {
            var allFiles = new ConcurrentBag<string>();
            var subFiles = GetFiles(path, "*");
            foreach (var subFile in subFiles)
            {
                allFiles.Add(Path.Combine(path, subFile));
            }
            foreach (var subDir in GetDirectories(path, "*"))
            {
                foreach (var subFile in GetAllFilesV4(Path.Combine(path, subDir)))
                { allFiles.Add(subFile); }
            }
            return allFiles;
        }
    }
}