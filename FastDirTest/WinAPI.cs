using Microsoft.Win32.SafeHandles;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Permissions;

namespace FastFile
{

    public class WinAPI
    {
        #region Import from kernel32

        [Serializable, StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto), BestFitMapping(false)]
        internal struct WIN32_FIND_DATA
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
        #endregion Import from kernel32

        #region Private Fields

        private const int MAX_PATH = 260;

        #endregion Private Fields

        #region Public Methods

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
                    WIN32_FIND_DATA fd = new();
                    // Discover all files/folders by ending a directory with "*", e.g. "X:\*".
                    DeepSearch(path, directories, folders, fd);
                }
            }
            catch (Exception) { }
            return directories;
        }

        #endregion Public Methods

        #region Private Methods

        private static void DeepSearch(string path, List<string> directories, Stack<string> folders, WIN32_FIND_DATA fd)
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
            ref WIN32_FIND_DATA lpFindFileData
            );

        /// <summary>
        /// https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findnextfilew
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool FindNextFile(
            SafeFindHandle hFindFile,
            ref WIN32_FIND_DATA lpFindFileData
            );

        #endregion Private Methods

        #region Private Classes

        private sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
        {


            [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
            internal SafeFindHandle() : base(true) { }



            protected override bool ReleaseHandle()
            {
                return FindClose(handle);
            }

            /// <summary>
            /// https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findclose
            /// </summary>
            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            private static extern bool FindClose(IntPtr handle);
        }

        #endregion Private Classes
    }

}
