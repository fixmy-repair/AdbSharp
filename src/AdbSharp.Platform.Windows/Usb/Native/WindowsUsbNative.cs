using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AdbSharp.Platform.Windows.Usb.Native;

internal static partial class WindowsUsbNative
{
    public const uint DigcfPresent = 0x00000002;
    public const uint DigcfDeviceInterface = 0x00000010;
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x00000001;
    public const uint FileShareWrite = 0x00000002;
    public const uint OpenExisting = 3;
    public const uint FileAttributeNormal = 0x00000080;
    public const uint FileFlagOverlapped = 0x40000000;
    public const byte UsbEndpointDirectionMask = 0x80;
    public const int UsbdPipeTypeBulk = 2;
    public const uint PipeTransferTimeoutPolicy = 3;
    public const uint TransferTimeoutMilliseconds = 1000;
    public const int ErrorFileNotFound = 2;
    public const int ErrorAccessDenied = 5;
    public const int ErrorInvalidHandle = 6;
    public const int ErrorGenFailure = 31;
    public const int ErrorSharingViolation = 32;
    public const int ErrorSemTimeout = 121;
    public const int ErrorBusy = 170;
    public const int ErrorOperationAborted = 995;
    public const int ErrorDeviceNotConnected = 1167;
    public static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct UsbInterfaceDescriptor
    {
        public byte Length;
        public byte DescriptorType;
        public byte InterfaceNumber;
        public byte AlternateSetting;
        public byte NumEndpoints;
        public byte InterfaceClass;
        public byte InterfaceSubClass;
        public byte InterfaceProtocol;
        public byte InterfaceIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PipeInformation
    {
        public int PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial IntPtr SetupDiGetClassDevs(in Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, in Guid interfaceClassGuid, uint memberIndex, ref DeviceInterfaceData deviceInterfaceData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref DeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        out int requiredSize,
        IntPtr deviceInfoData);

    [LibraryImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [LibraryImport("winusb.dll", EntryPoint = "WinUsb_Initialize", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsbInitialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);

    [LibraryImport("winusb.dll", EntryPoint = "WinUsb_Free")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsbFree(IntPtr interfaceHandle);

    [LibraryImport("winusb.dll", EntryPoint = "WinUsb_QueryInterfaceSettings", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsbQueryInterfaceSettings(IntPtr interfaceHandle, byte alternateInterfaceNumber, out UsbInterfaceDescriptor descriptor);

    [LibraryImport("winusb.dll", EntryPoint = "WinUsb_QueryPipe", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsbQueryPipe(IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex, out PipeInformation pipeInformation);

    [LibraryImport("winusb.dll", EntryPoint = "WinUsb_SetPipePolicy", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsbSetPipePolicy(IntPtr interfaceHandle, byte pipeId, uint policyType, uint valueLength, ref uint value);

    [LibraryImport("winusb.dll", EntryPoint = "WinUsb_ReadPipe", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsbReadPipe(IntPtr interfaceHandle, byte pipeId, [Out] byte[] buffer, int bufferLength, out int lengthTransferred, IntPtr overlapped);

    [LibraryImport("winusb.dll", EntryPoint = "WinUsb_WritePipe", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsbWritePipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, int bufferLength, out int lengthTransferred, IntPtr overlapped);
}
