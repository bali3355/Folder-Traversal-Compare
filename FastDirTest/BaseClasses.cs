using Microsoft.Win32.SafeHandles;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;

namespace FileSystemRetrieveCompare
{
    /// <summary>
    /// Contains a class for return file information from FindFirstFile or FindNextFile
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct FileSystemEntry
    {
        public readonly string Name;
        public readonly string FullName;
        public readonly FileAttributes Attributes;
        public readonly long Length;
        private readonly Lazy<bool> _exists;
        private readonly Lazy<string?> _directoryName;

        public FileSystemEntry(string cFileName, StringBuilder fullName, FileAttributes attributes, long length)
        {
            var tempFullName = fullName.ToString();

            Name = string.Intern(cFileName);
            FullName = tempFullName;
            Attributes = attributes;
            Length = length;
            _exists = new(() => File.Exists(tempFullName));
            _directoryName = new(() => Path.GetDirectoryName(tempFullName));
        }

        public FileSystemEntry(string cFileName, string fullName, FileAttributes attributes, long length)
        {
            Name = string.Intern(cFileName);
            FullName = fullName;
            Attributes = attributes;
            Length = length;
            _exists = new(() => File.Exists(fullName));
            _directoryName = new(() => Path.GetDirectoryName(fullName));
        }

        public bool Exists => _exists.Value;
        public string? DirectoryName => _directoryName.Value;
        public static long GetFileLength(uint High, uint Low) => (((long)High) << 0x20) | Low;
    }

    /// <summary>
    /// Defines the search criteria for FindFirstFile or FindNextFile
    /// </summary>
    public enum SearchFor
    {
        Files = 0,
        Directories = 1,
        FilesAndDirectories = 2,
    }

    #region Import from kernel32

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
    }

    [Serializable, StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto), BestFitMapping(false)]
    internal struct WIN32_FIND_DATA_STRUCT
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

        public override readonly string ToString() => "FileName = " + cFileName;
    }
    #endregion Import from kernel32

    /// <summary>
    /// Provides a SafeHandle wrapper for the FindFirstFile and FindNextFile functions.
    /// <seealso cref="ReleaseHandle()"/> is called automatically when the handle is disposed.
    /// </summary>
    sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
        internal SafeFindHandle() : base(true) { }

        protected override bool ReleaseHandle()
        {
            return FindClose(handle);
        }

        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        [DllImport("kernel32.dll")]
        private static extern bool FindClose(IntPtr handle);
    }
}
