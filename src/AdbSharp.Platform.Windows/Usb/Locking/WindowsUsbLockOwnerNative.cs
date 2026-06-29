using System.Runtime.InteropServices;

namespace AdbSharp.Platform.Windows.Usb.Locking;

internal static partial class WindowsUsbLockOwnerNative
{
    public const int ErrorAccessDenied = 5;
    public const int ErrorInvalidParameter = 87;
    public const int StatusSuccess = 0;
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    public const int StatusBufferOverflow = unchecked((int)0x80000005);
    public const int StatusBufferTooSmall = unchecked((int)0xC0000023);
    public const int SystemExtendedHandleInformation = 64;
    public const int ObjectNameInformation = 1;
    public const uint ProcessDuplicateHandle = 0x0040;
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint DuplicateSameAccess = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SystemHandleTableEntryInfoEx
    {
        public readonly IntPtr Object;
        public readonly IntPtr UniqueProcessId;
        public readonly IntPtr HandleValue;
        public readonly uint GrantedAccess;
        public readonly ushort CreatorBackTraceIndex;
        public readonly ushort ObjectTypeIndex;
        public readonly uint HandleAttributes;
        public readonly uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryObject(
        IntPtr handle,
        int objectInformationClass,
        IntPtr objectInformation,
        int objectInformationLength,
        out int returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DuplicateHandle(
        IntPtr sourceProcessHandle,
        IntPtr sourceHandle,
        IntPtr targetProcessHandle,
        out IntPtr targetHandle,
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint options);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(IntPtr process, uint flags, IntPtr fileName, ref int size);
}
