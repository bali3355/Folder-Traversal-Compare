using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.Reflection.Metadata;
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
        public static ConcurrentBag<string> GetDirectories(string path, int deepness, int rootLength)
        {
            if (!(deepness < 1 || path.Split('\\').Length < (deepness + rootLength))) return [];

            var allDirs = new ConcurrentBag<string>();
            var subDirs = GetDirectories(path, "*");

            _ = Parallel.ForEach(subDirs, (subDir) =>
            {
                var relativePath = Path.Combine(path, subDir);
                allDirs.Add(relativePath);
                Parallel.ForEach(GetDirectories(relativePath, deepness, rootLength), (dir) => allDirs.Add(dir));
            });
            return allDirs;
        }

        public static ConcurrentBag<string> GetFiles(string path, int deepness, int rootLength)
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
                Parallel.ForEach(GetFiles(relativePath, deepness, rootLength), (file) => allFiles.Add(file));
            });
            return allFiles;
        }

        public static ConcurrentBag<string> GetFilesv2(string path)
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

        public static ConcurrentBag<string> GetFilesv3(string path)
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

        public static ConcurrentBag<string> GetFilesv4(string path)
        {
            var allFiles = new ConcurrentBag<string>();
            var subFiles = GetFiles(path, "*");
            foreach (var subFile in subFiles)
            {
                allFiles.Add(Path.Combine(path, subFile));
            }
            foreach (var subDir in GetDirectories(path, "*"))
            {
                foreach (var subFile in GetFilesv4(Path.Combine(path, subDir)))
                { allFiles.Add(subFile); }
            }
            return allFiles;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool FindClose(IntPtr hFindFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFindHandle FindFirstFileW(string lpFileName,
                                                   out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool FindNextFileW(SafeFindHandle hFindFile,
                                                out WIN32_FIND_DATA lpFindFileData);

        private static IEnumerable<string> GetDirectories(string path, string searchPattern) => InternalSearch(path, searchPattern, true);
        private static IEnumerable<string> GetFiles(string path, string searchPattern) => InternalSearch(path, searchPattern, false);
        private static IEnumerable<string> InternalSearch(string path, string searchPattern, bool isGetDirs)
        {
            using var findHandle = FindFirstFileW(Path.Combine(path, searchPattern), out WIN32_FIND_DATA findData);
            if (findHandle.IsInvalid) yield break;
            do
            {
                if (findData.cFileName is "." or ".." or "thumbs.db") continue;
                if (isGetDirs == ((findData.dwFileAttributes & FileAttributes.Directory) != 0)) yield return Path.Combine(path, findData.cFileName);
            } while (FindNextFileW(findHandle, out findData));
        }

        private sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeFindHandle() : base(true) { }
            protected override bool ReleaseHandle() => FindClose(handle);
        }
    }
}