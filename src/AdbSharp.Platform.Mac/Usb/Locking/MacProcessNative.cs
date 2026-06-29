using System.Runtime.InteropServices;

namespace AdbSharp.Platform.Mac.Usb.Locking;

internal static partial class MacProcessNative
{
    public const int ProcAllPids = 1;
    public const int ProcPidPathInfoMaxSize = 4096;

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libproc.dylib", EntryPoint = "proc_listpids", SetLastError = true)]
    public static partial int ProcListPids(uint type, uint typeInfo, IntPtr buffer, int bufferSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libproc.dylib", EntryPoint = "proc_name", SetLastError = true)]
    public static partial int ProcName(int processId, IntPtr buffer, uint bufferSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libproc.dylib", EntryPoint = "proc_pidpath", SetLastError = true)]
    public static partial int ProcPidPath(int processId, IntPtr buffer, uint bufferSize);
}
