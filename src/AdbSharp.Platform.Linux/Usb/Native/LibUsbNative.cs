using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]

namespace AdbSharp.Platform.Linux.Usb.Native;

internal static partial class LibUsbNative
{
    public const int Success = 0;
    public const int ErrorIo = -1;
    public const int ErrorInvalidParameter = -2;
    public const int ErrorAccess = -3;
    public const int ErrorNoDevice = -4;
    public const int ErrorNotFound = -5;
    public const int ErrorBusy = -6;
    public const int ErrorTimeout = -7;
    public const int ErrorOverflow = -8;
    public const int ErrorPipe = -9;
    public const int ErrorInterrupted = -10;
    public const int ErrorNoMemory = -11;
    public const int ErrorNotSupported = -12;
    public const int ErrorOther = -99;
    public const byte EndpointIn = 0x80;
    public const byte TransferTypeMask = 0x03;
    public const byte TransferTypeBulk = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceDescriptor
    {
        public byte Length;
        public byte DescriptorType;
        public ushort BcdUsb;
        public byte DeviceClass;
        public byte DeviceSubClass;
        public byte DeviceProtocol;
        public byte MaxPacketSize0;
        public ushort VendorId;
        public ushort ProductId;
        public ushort BcdDevice;
        public byte ManufacturerIndex;
        public byte ProductIndex;
        public byte SerialNumberIndex;
        public byte NumConfigurations;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ConfigDescriptor
    {
        public byte Length;
        public byte DescriptorType;
        public ushort TotalLength;
        public byte NumInterfaces;
        public byte ConfigurationValue;
        public byte ConfigurationIndex;
        public byte Attributes;
        public byte MaxPower;
        public IntPtr Interfaces;
        public IntPtr Extra;
        public int ExtraLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Interface
    {
        public IntPtr AltSettings;
        public int NumAltSettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct InterfaceDescriptor
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
        public IntPtr Endpoints;
        public IntPtr Extra;
        public int ExtraLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EndpointDescriptor
    {
        public byte Length;
        public byte DescriptorType;
        public byte EndpointAddress;
        public byte Attributes;
        public ushort MaxPacketSize;
        public byte Interval;
        public byte Refresh;
        public byte SynchAddress;
        public IntPtr Extra;
        public int ExtraLength;
    }

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_init(out IntPtr context);

    [LibraryImport("libusb-1.0")]
    public static partial void libusb_exit(IntPtr context);

    [LibraryImport("libusb-1.0")]
    public static partial nint libusb_get_device_list(IntPtr context, out IntPtr list);

    [LibraryImport("libusb-1.0")]
    public static partial void libusb_free_device_list(IntPtr list, int unrefDevices);

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_get_device_descriptor(IntPtr device, out DeviceDescriptor descriptor);

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_get_config_descriptor(IntPtr device, byte configIndex, out IntPtr configDescriptor);

    [LibraryImport("libusb-1.0")]
    public static partial void libusb_free_config_descriptor(IntPtr configDescriptor);

    [LibraryImport("libusb-1.0")]
    public static partial byte libusb_get_bus_number(IntPtr device);

    [LibraryImport("libusb-1.0")]
    public static partial byte libusb_get_device_address(IntPtr device);

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_open(IntPtr device, out IntPtr handle);

    [LibraryImport("libusb-1.0")]
    public static partial void libusb_close(IntPtr handle);

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_get_string_descriptor_ascii(IntPtr handle, byte descriptorIndex, [Out] byte[] data, int length);

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_set_configuration(IntPtr handle, int configuration);

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_kernel_driver_active(IntPtr handle, int interfaceNumber);

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_detach_kernel_driver(IntPtr handle, int interfaceNumber);

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_attach_kernel_driver(IntPtr handle, int interfaceNumber);

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_claim_interface(IntPtr handle, int interfaceNumber);

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_release_interface(IntPtr handle, int interfaceNumber);

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_set_interface_alt_setting(IntPtr handle, int interfaceNumber, int alternateSetting);

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_reset_device(IntPtr handle);

    [LibraryImport("libusb-1.0")]
    public static partial int libusb_bulk_transfer(IntPtr handle, byte endpoint, byte[] data, int length, out int transferred, uint timeout);
}
