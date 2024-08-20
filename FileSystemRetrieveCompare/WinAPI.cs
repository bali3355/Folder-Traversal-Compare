using System.Runtime.InteropServices;

namespace FileSystemRetrieveCompare
{

    public class WinAPI
    {
        public static IEnumerable<string> GetFiles(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path), "The provided path is NULL or empty.");
            var directories = new List<string>();

            if (path.Last() != '\\') path += '\\';
            var folders = new Stack<string>([path]);

            try
            {
                while (folders.TryPop(out path))
                {
                    if (path.Last() != '\\') path += '\\';
                    WIN32_FIND_DATA_STRUCT fd = new();
                    // Discover all files/folders by ending a directory with "*", e.g. "X:\*".
                    DeepSearch(path, directories, folders, fd);
                }
            }
            catch (Exception) { }
            return directories;
        }

        private static void DeepSearch(string path, List<string> directories, Stack<string> folders, WIN32_FIND_DATA_STRUCT fd)
        {
            using SafeFindHandle hFile = FindFirstFile(path + "*", ref fd);

            // If we encounter an error, or there are no files/directories, we return no entries.
            if (hFile.IsInvalid) return;
            do
            {
                // If a directory (and not a Reparse Point), and the name is not "." or ".."
                if (fd.cFileName == "." || fd.cFileName == "..") continue;
                string fullPath = Path.Combine(path, fd.cFileName);
                if (fd.dwFileAttributes.HasFlag(FileAttributes.Directory))
                {
                    folders.Push(fullPath);
                    continue;
                }
                // Otherwise, if this is a file
                directories.Add(fullPath);
            }
            while (FindNextFile(hFile, ref fd));
        }

        /// <summary>
        /// https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findfirstfilew
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFindHandle FindFirstFile(
            string lpFileName,
            ref WIN32_FIND_DATA_STRUCT lpFindFileData
            );

        /// <summary>
        /// https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findnextfilew
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool FindNextFile(
            SafeFindHandle hFindFile,
            ref WIN32_FIND_DATA_STRUCT lpFindFileData
            );
    }
}
