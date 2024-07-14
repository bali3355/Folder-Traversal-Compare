using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;


namespace FastDirTest
{
    public class WinAPI
    {
        #region Import from kernel32

        [Serializable, StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto), BestFitMapping(false)]
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

        /// <summary>
        /// https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findfirstfilew
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindFirstFile(
            string lpFileName,
            ref WIN32_FIND_DATA lpFindFileData
            );

        /// <summary>
        /// https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findnextfilew
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool FindNextFile(
            IntPtr hFindFile,
            ref WIN32_FIND_DATA lpFindFileData
            );

        /// <summary>
        /// https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findclose
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool FindClose(IntPtr hFindFile);

        public static IEnumerable<string> GetFiles(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path), "The provided path is NULL or empty.");
            var directories = new List<string>();

            if (path.Last() != '\\') path += '\\';
            var folders = new Stack<string>([path]);

            while (folders.TryPop(out path))
            {
                if (path.Last() != '\\') path += '\\';
                WIN32_FIND_DATA fd = new();
                // Discover all files/folders by ending a directory with "*", e.g. "X:\*".
                IntPtr hFile = FindFirstFile(path + "*", ref fd);

                // If we encounter an error, or there are no files/directories, we return no entries.
                if (hFile.ToInt64() == -1) continue;
                do
                {
                    // If a directory (and not a Reparse Point), and the name is not "." or ".." which exist as concepts in the file system,
                    if (fd.cFileName == "." || fd.cFileName == "..") continue;
                    string fullPath = Path.Combine(path, fd.cFileName);
                    // count the directory and add it to a list so we can iterate over it in parallel later on to maximize performance
                    if (fd.dwFileAttributes.HasFlag(FileAttributes.Directory))
                    {
                        folders.Push(fullPath);
                        continue;
                    }
                    // Otherwise, if this is a file
                    directories.Add(fullPath);
                }
                while (FindNextFile(hFile, ref fd));

                FindClose(hFile);
            }
            return directories;
        }

        //public static IEnumerable<string> GetFiles(string path)
        //{
        //    if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path), "The provided path is NULL or empty.");
        //    var directories = new ConcurrentBag<string>();

        //    ProcessSubDirectory(path, directories);

        //    return directories;
        //}

        //private static void ProcessSubDirectory(string path, ConcurrentBag<string> directories)
        //{
        //    if (path.Last() != '\\') path += '\\';
        //    WIN32_FIND_DATA fd = new();
        //    IntPtr hFile = FindFirstFile(path + "*", ref fd);

        //    if (hFile.ToInt64() == -1) return;
        //    do
        //    {
        //        if (fd.cFileName == "." || fd.cFileName == "..") continue;
        //        string fullPath = Path.Combine(path, fd.cFileName);
        //        if (fd.dwFileAttributes.HasFlag(FileAttributes.Directory))
        //        {
        //            ProcessSubDirectory(fullPath, directories);
        //            continue;
        //        }
        //        directories.Add(fullPath);
        //    }
        //    while (FindNextFile(hFile, ref fd));

        //    FindClose(hFile);
        //}

        //public static IEnumerable<string> GetFiles(string path)
        //{
        //    if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path), "The provided path is NULL or empty.");
        //    var directories = new List<string>();

        //    if (path.Last() != '\\') path += '\\';
        //    var folders = new Stack<string>([path]);

        //    while (folders.TryPop(out path))
        //    {
        //        if (path.Last() != '\\') path += '\\';
        //        WIN32_FIND_DATA fd = new();
        //        // Discover all files/folders by ending a directory with "*", e.g. "X:\*".
        //        IntPtr hFile = FindFirstFile(path + "*", ref fd);

        //        // If we encounter an error, or there are no files/directories, we return no entries.
        //        if (hFile.ToInt64() == -1) continue;
        //        do
        //        {
        //            // If a directory (and not a Reparse Point), and the name is not "." or ".." which exist as concepts in the file system,
        //            if (fd.cFileName == "." || fd.cFileName == "..") continue;
        //            string fullPath = Path.Combine(path, fd.cFileName);
        //            // count the directory and add it to a list so we can iterate over it in parallel later on to maximize performance
        //            if (fd.dwFileAttributes.HasFlag(FileAttributes.Directory))
        //            {
        //                folders.Push(fullPath);
        //                continue;
        //            }
        //            // Otherwise, if this is a file
        //            directories.Add(fullPath);
        //        }
        //        while (FindNextFile(hFile, ref fd));

        //        FindClose(hFile);
        //    }
        //    return directories;
        //}


        //public static IEnumerable<string> GetFiles(string path)
        //{
        //    if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path), "The provided path is NULL or empty.");
        //    if (path.Last() != '\\') path += '\\';

        //    var directories = new ConcurrentBag<string>();

        //    WIN32_FIND_DATA fd = new();
        //    IntPtr hFile = FindFirstFile(path + "*", ref fd);

        //    if (hFile.ToInt64() == -1) return[];

        //    try
        //    {
        //        do
        //        {
        //            if (fd.cFileName == "." || fd.cFileName == "..") continue;
        //            string fullPath = Path.Combine(path, fd.cFileName);

        //            if (fd.dwFileAttributes.HasFlag(FileAttributes.Directory))
        //            {
        //                Parallel.Invoke(() => GetFilesParallel(fullPath, directories));
        //                continue;
        //            }

        //            directories.Add(fullPath);
        //        }
        //        while (FindNextFile(hFile, ref fd));
        //    }
        //    finally
        //    {
        //        FindClose(hFile);
        //    }

        //    return directories;
        //}

        //private static void GetFilesParallel(string path, ConcurrentBag<string> directories)
        //{
        //    WIN32_FIND_DATA fd = new();
        //    if (path.Last() != '\\') path += '\\';
        //    IntPtr hFile = FindFirstFile(path + "*", ref fd);

        //    if (hFile.ToInt64() == -1) return;

        //    try
        //    {
        //        do
        //        {
        //            if (fd.cFileName == "." || fd.cFileName == "..") continue;
        //            string fullPath = Path.Combine(path, fd.cFileName);

        //            if (fd.dwFileAttributes.HasFlag(FileAttributes.Directory))
        //            {
        //                Parallel.Invoke(() => GetFilesParallel(fullPath, directories));
        //                continue;
        //            }

        //            directories.Add(fullPath);
        //        }
        //        while (FindNextFile(hFile, ref fd));
        //    }
        //    finally
        //    {
        //        FindClose(hFile);
        //    }
        //}

        //public static IEnumerable<string> GetFiles(string path)
        //{
        //    if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path), "The provided path is NULL or empty.");
        //    var directories = new List<string>();
        //    if (path.Last() != '\\') path += '\\';

        //    WIN32_FIND_DATA fd = new();
        //    // Discover all files/folders by ending a directory with "*", e.g. "X:\*".
        //    IntPtr hFile = FindFirstFile(path + "*", ref fd);

        //    // If we encounter an error, or there are no files/directories, we return no entries.
        //    if (hFile.ToInt64() == -1) return[];

        //    do
        //    {
        //        // If a directory (and not a Reparse Point), and the name is not "." or ".." which exist as concepts in the file system,
        //        if (fd.cFileName == "." || fd.cFileName == "..") continue;
        //        string fullPath = Path.Combine(path, fd.cFileName);
        //        // count the directory and add it to a list so we can iterate over it in parallel later on to maximize performance
        //        if (fd.dwFileAttributes.HasFlag(FileAttributes.Directory))
        //        {
        //            directories.AddRange(GetFiles(fullPath));
        //            continue;
        //        }
        //        // Otherwise, if this is a file ("archive"), increment the file count.
        //        directories.Add(fullPath);
        //    }
        //    while (FindNextFile(hFile, ref fd));

        //    FindClose(hFile);

        //    return directories;
        //}


        //private static IEnumerable<string> SubSearch(string path, ConcurrentQueue<string> folders, ConcurrentBag<string> directories)
        //{
        //    IntPtr hFile = IntPtr.Zero;
        //    WIN32_FIND_DATA fd = new();
        //    // If the provided path doesn't end in a backslash, append one.
        //    if (path.Last() != '\\') path += '\\';

        //    try
        //    {
        //        // Discover all files/folders by ending a directory with "*", e.g. "X:\*".
        //        hFile = FindFirstFile(path + "*", ref fd);

        //        // If we encounter an error, or there are no files/directories, we return no entries.
        //        if (hFile.ToInt64() == -1) return [];

        //        do
        //        {
        //            // If a directory (and not a Reparse Point), and the name is not "." or ".." which exist as concepts in the file system,
        //            if (fd.cFileName == "." || fd.cFileName == "..") continue;
        //            // count the directory and add it to a list so we can iterate over it in parallel later on to maximize performance
        //            if (fd.dwFileAttributes.HasFlag(FileAttributes.Directory))
        //            {
        //                folders.Enqueue(Path.Combine(path, fd.cFileName));
        //                continue;
        //            }
        //            // Otherwise, if this is a file ("archive"), increment the file count.
        //            directories.Add(Path.Combine(path, fd.cFileName));
        //        }
        //        while (FindNextFile(hFile, ref fd));
        //    }
        //    catch (Exception)
        //    {
        //        // Handle as desired.
        //    }
        //    finally
        //    {
        //        if (hFile.ToInt64() != 0)
        //            FindClose(hFile);
        //    }
    }
}
