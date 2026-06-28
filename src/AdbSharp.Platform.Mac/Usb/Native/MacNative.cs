using System.Runtime.InteropServices;

namespace AdbSharp.Platform.Mac.Usb.Native;

internal static partial class MacNative
{
    public const uint Success = 0;
    public const uint Utf8Encoding = 0x08000100;
    public const int SInt32NumberType = 3;
    public const byte UsbDirectionOut = 0;
    public const byte UsbDirectionIn = 1;
    public const byte UsbTransferTypeBulk = 2;
    public const byte UsbEndpointInMask = 0x80;
    public const uint TransferNoDataTimeoutMilliseconds = 1000;
    public const uint TransferCompletionTimeoutMilliseconds = 15000;
    public const double TransferTimeoutSeconds = 15;
    public const uint IoReturnNoDevice = 0xe00002c0;
    public const uint IoReturnNotPrivileged = 0xe00002c1;
    public const uint IoReturnExclusiveAccess = 0xe00002c5;
    public const uint IoReturnIoError = 0xe00002ca;
    public const uint IoReturnNotOpen = 0xe00002cd;
    public const uint IoReturnBusy = 0xe00002d5;
    public const uint IoReturnTimeout = 0xe00002d6;
    public const uint IoReturnNotReady = 0xe00002d8;
    public const uint IoReturnNotAttached = 0xe00002d9;
    public const uint IoReturnNotPermitted = 0xe00002e2;
    public const uint IoReturnAborted = 0xe00002eb;
    public const uint IoReturnNotResponding = 0xe00002ed;
    public const uint IoReturnNotFound = 0xe00002f0;
    public const uint UsbReturnPipeStalled = 0xe000404f;
    public const uint UsbReturnTransactionReturned = 0xe0004050;
    public const uint UsbReturnTransactionTimeout = 0xe0004051;
    public const uint UsbReturnUnknownPipe = 0xe0004061;
    public const uint MachSendInvalidDestination = 0x10000003;

    [LibraryImport("/System/Library/Frameworks/IOKit.framework/IOKit", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr IOServiceMatching(string name);

    [LibraryImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    public static partial uint IOServiceGetMatchingServices(IntPtr mainPort, IntPtr matching, out uint existing);

    [LibraryImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    public static partial uint IOIteratorNext(uint iterator);

    [LibraryImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    public static partial uint IOObjectRelease(uint value);

    [LibraryImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    public static partial uint IOObjectRetain(uint value);

    [LibraryImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    public static partial uint IOObjectGetClass(uint value, byte* className);

    [LibraryImport("/System/Library/Frameworks/IOKit.framework/IOKit", StringMarshalling = StringMarshalling.Utf8)]
    public static partial uint IORegistryEntryGetParentEntry(uint entry, string plane, out uint parent);

    [LibraryImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    public static partial IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);

    [LibraryImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    public static partial uint IOCreatePlugInInterfaceForService(uint service, IntPtr pluginType, IntPtr interfaceType, out IntPtr pluginInterface, out int score);

    [LibraryImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    public static partial uint IODestroyPlugInInterface(IntPtr pluginInterface);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial IntPtr CFUUIDGetConstantUUIDWithBytes(
        IntPtr allocator,
        byte byte0,
        byte byte1,
        byte byte2,
        byte byte3,
        byte byte4,
        byte byte5,
        byte byte6,
        byte byte7,
        byte byte8,
        byte byte9,
        byte byte10,
        byte byte11,
        byte byte12,
        byte byte13,
        byte byte14,
        byte byte15);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial MacCfUuidBytes CFUUIDGetUUIDBytes(IntPtr uuid);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CFStringGetCString(IntPtr value, byte[] buffer, nint bufferSize, uint encoding);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CFNumberGetValue(IntPtr number, int type, out int value);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial nint CFGetTypeID(IntPtr value);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial nint CFStringGetTypeID();

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial nint CFNumberGetTypeID();

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial void CFRelease(IntPtr value);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial IntPtr CFRunLoopGetCurrent();

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial void CFRunLoopRun();

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial void CFRunLoopStop(IntPtr runLoop);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static partial void CFRunLoopWakeUp(IntPtr runLoop);

    public static IntPtr CreateCfPluginInterfaceId()
    {
        return CFUUIDGetConstantUUIDWithBytes(
            IntPtr.Zero,
            0xc2,
            0x44,
            0xe8,
            0x58,
            0x10,
            0x9c,
            0x11,
            0xd4,
            0x91,
            0xd4,
            0x00,
            0x50,
            0xe4,
            0xc6,
            0x42,
            0x6f);
    }

    public static IntPtr CreateUsbInterfaceUserClientTypeId()
    {
        return CFUUIDGetConstantUUIDWithBytes(
            IntPtr.Zero,
            0x2d,
            0x97,
            0x86,
            0xc6,
            0x9e,
            0xf3,
            0x11,
            0xd4,
            0xad,
            0x51,
            0x00,
            0x0a,
            0x27,
            0x05,
            0x28,
            0x61);
    }

    public static IntPtr CreateUsbInterfaceInterfaceId182()
    {
        return CFUUIDGetConstantUUIDWithBytes(
            IntPtr.Zero,
            0x49,
            0x23,
            0xac,
            0x4c,
            0x48,
            0x96,
            0x11,
            0xd5,
            0x92,
            0x08,
            0x00,
            0x0a,
            0x27,
            0x80,
            0x1e,
            0x86);
    }

    public static IntPtr CreateUsbInterfaceInterfaceId183()
    {
        return CFUUIDGetConstantUUIDWithBytes(
            IntPtr.Zero,
            0x1c,
            0x43,
            0x83,
            0x56,
            0x74,
            0xc4,
            0x11,
            0xd5,
            0x92,
            0xe6,
            0x00,
            0x0a,
            0x27,
            0x80,
            0x1e,
            0x86);
    }

    public static IntPtr CreateUsbInterfaceInterfaceId500()
    {
        return CFUUIDGetConstantUUIDWithBytes(
            IntPtr.Zero,
            0x6c,
            0x0d,
            0x38,
            0xc3,
            0xb0,
            0x93,
            0x4e,
            0xa7,
            0x80,
            0x9b,
            0x09,
            0xfb,
            0x5d,
            0xdd,
            0xac,
            0x16);
    }

    public static IntPtr CreateUsbInterfaceInterfaceId550()
    {
        return CFUUIDGetConstantUUIDWithBytes(
            IntPtr.Zero,
            0x6a,
            0xe4,
            0x4d,
            0x3f,
            0xeb,
            0x45,
            0x48,
            0x7f,
            0x8e,
            0x8e,
            0xb9,
            0x3b,
            0x99,
            0xf8,
            0xea,
            0x9e);
    }

    public static IntPtr CreateUsbInterfaceInterfaceId650()
    {
        return CFUUIDGetConstantUUIDWithBytes(
            IntPtr.Zero,
            0x08,
            0x15,
            0x1a,
            0x89,
            0x80,
            0x81,
            0x40,
            0x87,
            0x8f,
            0x9e,
            0x0a,
            0xfe,
            0xdf,
            0xdb,
            0x5d,
            0x9f);
    }

    public static IntPtr CreateUsbInterfaceInterfaceId700()
    {
        return CFUUIDGetConstantUUIDWithBytes(
            IntPtr.Zero,
            0x17,
            0xf9,
            0xe5,
            0x9c,
            0xb0,
            0xa1,
            0x40,
            0x1d,
            0x9a,
            0xc0,
            0x8d,
            0xe2,
            0x7a,
            0xc6,
            0x04,
            0x7e);
    }

    public static IntPtr CreateUsbInterfaceInterfaceId800()
    {
        return CFUUIDGetConstantUUIDWithBytes(
            IntPtr.Zero,
            0x33,
            0xa8,
            0x5d,
            0xb0,
            0x0c,
            0x3b,
            0x43,
            0x28,
            0x8f,
            0x02,
            0xfd,
            0xa8,
            0x1b,
            0x11,
            0x7f,
            0x4c);
    }

    public static IntPtr CreateUsbInterfaceInterfaceId942()
    {
        return CFUUIDGetConstantUUIDWithBytes(
            IntPtr.Zero,
            0x87,
            0x52,
            0x66,
            0x3b,
            0xc0,
            0x7b,
            0x4b,
            0xae,
            0x95,
            0x84,
            0x22,
            0x03,
            0x2f,
            0xab,
            0x9c,
            0x5a);
    }
}
