using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace FileSystemRetrieveCompare
{
    /// <summary>
    ///  Based on this code:
    ///  <see href="https://web.archive.org/web/20130426032447/http://codepaste.net/msm8b1"/>
    /// </summary>
    internal class WinAPIv2
    {
        #region Find Files
        #region Import from kernel32

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFindHandle FindFirstFileW(string lpFileName, out WIN32_FIND_DATA_STRUCT lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool FindNextFileW(SafeFindHandle hFindFile, out WIN32_FIND_DATA_STRUCT lpFindFileData);

        #endregion Import from kernel32

        private static IEnumerable<string> FileSearch(string path, string searchPattern)
        {
            using var findHandle = FindFirstFileW(Path.Combine(path, searchPattern), out WIN32_FIND_DATA_STRUCT findData);
            if (findHandle.IsInvalid) yield break;
            do
            {
                if ((findData.dwFileAttributes & FileAttributes.Directory) != 0 || findData.cFileName == "thumbs.db") continue;
                yield return Path.Combine(path, findData.cFileName);
            } while (FindNextFileW(findHandle, out findData));
        }

        private static IEnumerable<string> DirectorySearch(string path, string searchPattern)
        {
            using var findHandle = FindFirstFileW(Path.Combine(path, searchPattern), out WIN32_FIND_DATA_STRUCT findData);
            if (findHandle.IsInvalid) yield break;
            do
            {
                if ((findData.dwFileAttributes & FileAttributes.Directory) == 0 || findData.cFileName is "." or "..") continue;
                yield return Path.Combine(path, findData.cFileName);
            } while (FindNextFileW(findHandle, out findData));
        }

        private static IEnumerable<string> GetDirectories(string path, string searchPattern) => InternalSearch(path, searchPattern, true);
        private static IEnumerable<string> GetFiles(string path, string searchPattern) => InternalSearch(path, searchPattern, false);
        private static IEnumerable<string> InternalSearch(string path, string searchPattern, bool isGetDirs)
        {
            using var findHandle = FindFirstFileW(Path.Combine(path, searchPattern), out WIN32_FIND_DATA_STRUCT findData);
            if (findHandle.IsInvalid) yield break;
            do
            {
                if (findData.cFileName is "." or ".." or "thumbs.db") continue;
                if (isGetDirs == ((findData.dwFileAttributes & FileAttributes.Directory) != 0)) yield return Path.Combine(path, findData.cFileName);
            } while (FindNextFileW(findHandle, out findData));
        }

        #endregion

        public static ConcurrentBag<string> GetFilesRecursive(string path)
        {
            var allFiles = new ConcurrentBag<string>();
            var subFiles = GetFiles(path, "*");
            foreach (var subFile in subFiles)
            {
                allFiles.Add(Path.Combine(path, subFile));
            }
            foreach (var subDir in GetDirectories(path, "*"))
            {
                foreach (var subFile in GetFilesRecursive(Path.Combine(path, subDir)))
                { allFiles.Add(subFile); }
            }
            return allFiles;
        }

        public static ConcurrentBag<string> GetFilesRecursiveParallel(string path)
        {
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
                Parallel.ForEach(GetFilesRecursiveParallel(relativePath), (file) => allFiles.Add(file));
            });
            return allFiles;
        }

        public static ConcurrentBag<string> GetFilesRecursiveNew(string path)
        {
            var allFiles = new ConcurrentBag<string>();
            var subFiles = FileSearch(path, "*");
            foreach (var subFile in subFiles)
            {
                allFiles.Add(Path.Combine(path, subFile));
            }
            foreach (var subDir in DirectorySearch(path, "*"))
            {
                foreach (var subFile in GetFilesRecursiveNew(Path.Combine(path, subDir)))
                { allFiles.Add(subFile); }
            }
            return allFiles;
        }

        public static ConcurrentBag<string> GetFilesRecursiveNewParallel(string path)
        {
            var allFiles = new ConcurrentBag<string>();
            var subFiles = FileSearch(path, "*");
            _ = Parallel.ForEach(subFiles, (subFile) =>
            {
                allFiles.Add(Path.Combine(path, subFile));
            });

            var subDirs = DirectorySearch(path, "*");
            _ = Parallel.ForEach(subDirs, (subDir) =>
            {
                var relativePath = Path.Combine(path, subDir);
                Parallel.ForEach(GetFilesRecursiveNewParallel(relativePath), (file) => allFiles.Add(file));
            });
            return allFiles;
        }

        public static ConcurrentBag<string> GetFilesQueue(string path)
        {
            var allFiles = new ConcurrentBag<string>();
            var queue = new ConcurrentQueue<string>([path]);

            while (queue.TryDequeue(out var currentPath))
            {
                foreach (var subDir in DirectorySearch(currentPath, "*")) queue.Enqueue(subDir);
                foreach (var subFile in FileSearch(currentPath, "*")) allFiles.Add(subFile);
            }
            return allFiles;
        }

        public static ConcurrentBag<string> GetFilesQueueParallel(string path)
        {
            try
            {
                var searchResults = new ConcurrentBag<string>();
                var folderQueue = new ConcurrentQueue<string>([path]);
                while (!folderQueue.IsEmpty) folderQueue = GetQueueSearch(searchResults, folderQueue);
                return searchResults;
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        private static ConcurrentQueue<string> GetQueueSearch(ConcurrentBag<string> searchResults, ConcurrentQueue<string> folderQueue)
        {
            var tmpQueue = folderQueue;
            folderQueue = [];
            Parallel.ForEach(tmpQueue, (currentPath) =>
            {
                foreach (var subDir in DirectorySearch(currentPath, "*")) folderQueue.Enqueue(subDir);
                foreach (var subFile in FileSearch(currentPath, "*")) searchResults.Add(subFile);
            });
            return folderQueue;
        }

        public static ConcurrentBag<string> GetFilesStack(string path)
        {
            var allFiles = new ConcurrentBag<string>();
            var queue = new ConcurrentStack<string>([path]);

            while (queue.TryPop(out var currentPath))
            {
                foreach (var subDir in DirectorySearch(currentPath, "*")) queue.Push(subDir);
                foreach (var subFile in FileSearch(currentPath, "*")) allFiles.Add(subFile);
            }
            return allFiles;
        }

        public static ConcurrentBag<string> GetFilesStackParallel(string path)
        {
            var searchResults = new ConcurrentBag<string>();
            var folderStack = new ConcurrentStack<string>([path]);

            while (!folderStack.IsEmpty)
            {
                var tmpStack = folderStack;
                folderStack = [];
                Parallel.ForEach(tmpStack, (currentPath) =>
                {
                    foreach (var subDir in DirectorySearch(currentPath, "*")) folderStack.Push(subDir);
                    foreach (var subFile in FileSearch(currentPath, "*")) searchResults.Add(subFile);
                });
            }
            return searchResults;
        }

        
    }
}