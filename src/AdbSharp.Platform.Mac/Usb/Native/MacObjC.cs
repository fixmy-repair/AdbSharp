using System.Runtime.InteropServices;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Mac.Usb.Native;

internal static partial class MacObjC
{
    private const int RtldNow = 0x2;
    private const int BlockHasSignature = 1 << 30;
    private const nuint UsbHostObjectInitOptionsNone = 0;
    private const nuint UsbHostAbortOptionSynchronous = 1;
    private const string CompletionBlockSignature = "v24@?0I8Q16";

    private static readonly Lock LoadGate = new();
    private static readonly IntPtr LibSystem = NativeLibrary.Load("/usr/lib/libSystem.B.dylib");
    private static readonly IntPtr ConcreteStackBlock = NativeLibrary.GetExport(LibSystem, "_NSConcreteStackBlock");
    private static readonly IntPtr CompletionBlockDescriptor = CreateCompletionBlockDescriptor();
    private static readonly IntPtr UsbHostQueue = DispatchQueueCreate("com.adbsharp.usbhost", IntPtr.Zero);
    private static bool usbHostLoaded;

    private static readonly IntPtr AllocSelector = RegisterSelector("alloc");
    private static readonly IntPtr ReleaseSelector = RegisterSelector("release");
    private static readonly IntPtr DestroySelector = RegisterSelector("destroy");
    private static readonly IntPtr InitWithLengthSelector = RegisterSelector("initWithLength:");
    private static readonly IntPtr InitWithBytesLengthSelector = RegisterSelector("initWithBytes:length:");
    private static readonly IntPtr MutableBytesSelector = RegisterSelector("mutableBytes");
    private static readonly IntPtr Utf8StringSelector = RegisterSelector("UTF8String");
    private static readonly IntPtr ErrorDomainSelector = RegisterSelector("domain");
    private static readonly IntPtr ErrorCodeSelector = RegisterSelector("code");
    private static readonly IntPtr LocalizedDescriptionSelector = RegisterSelector("localizedDescription");
    private static readonly IntPtr InitWithIoServiceSelector = RegisterSelector("initWithIOService:options:queue:error:interestHandler:");
    private static readonly IntPtr ConfigurationDescriptorSelector = RegisterSelector("configurationDescriptor");
    private static readonly IntPtr InterfaceDescriptorSelector = RegisterSelector("interfaceDescriptor");
    private static readonly IntPtr SelectAlternateSettingSelector = RegisterSelector("selectAlternateSetting:error:");
    private static readonly IntPtr CopyPipeWithAddressSelector = RegisterSelector("copyPipeWithAddress:error:");
    private static readonly IntPtr SendIoRequestSelector = RegisterSelector("sendIORequestWithData:bytesTransferred:completionTimeout:error:");
    private static readonly IntPtr EnqueueIoRequestSelector = RegisterSelector("enqueueIORequestWithData:completionTimeout:error:completionHandler:");
    private static readonly IntPtr AbortWithOptionSelector = RegisterSelector("abortWithOption:error:");
    private static readonly IntPtr ClearStallSelector = RegisterSelector("clearStallWithError:");

    public static void EnsureUsbHostLoaded()
    {
        if (usbHostLoaded)
        {
            return;
        }

        lock (LoadGate)
        {
            if (usbHostLoaded)
            {
                return;
            }

            var handle = Dlopen("/System/Library/Frameworks/IOUSBHost.framework/IOUSBHost", RtldNow);
            if (handle == IntPtr.Zero)
            {
                throw new UsbTransportException(UsbTransportError.PlatformDependencyMissing, "Failed to load IOUSBHost.framework.");
            }

            usbHostLoaded = true;
        }
    }

    public static IntPtr CreateUsbHostInterface(uint service, out IntPtr error)
    {
        EnsureUsbHostLoaded();
        var @class = GetRequiredClass("IOUSBHostInterface");
        var allocated = SendIntPtr(@class, AllocSelector);
        if (allocated == IntPtr.Zero)
        {
            error = IntPtr.Zero;
            return IntPtr.Zero;
        }

        return SendInitWithIoService(
            allocated,
            InitWithIoServiceSelector,
            service,
            UsbHostObjectInitOptionsNone,
            UsbHostQueue,
            out error,
            IntPtr.Zero);
    }

    public static IntPtr GetConfigurationDescriptor(IntPtr usbHostInterface)
    {
        return SendIntPtr(usbHostInterface, ConfigurationDescriptorSelector);
    }

    public static IntPtr GetInterfaceDescriptor(IntPtr usbHostInterface)
    {
        return SendIntPtr(usbHostInterface, InterfaceDescriptorSelector);
    }

    public static bool SelectAlternateSetting(IntPtr usbHostInterface, byte alternateSetting, out IntPtr error)
    {
        return SendSelectAlternateSetting(usbHostInterface, SelectAlternateSettingSelector, alternateSetting, out error) != 0;
    }

    public static IntPtr CopyPipeWithAddress(IntPtr usbHostInterface, byte endpointAddress, out IntPtr error)
    {
        return SendCopyPipeWithAddress(usbHostInterface, CopyPipeWithAddressSelector, endpointAddress, out error);
    }

    public static IntPtr CreateMutableData(nuint length)
    {
        var @class = GetRequiredClass("NSMutableData");
        var allocated = SendIntPtr(@class, AllocSelector);
        return allocated == IntPtr.Zero
            ? IntPtr.Zero
            : SendInitWithLength(allocated, InitWithLengthSelector, length);
    }

    public static IntPtr CreateMutableData(ReadOnlySpan<byte> data)
    {
        var @class = GetRequiredClass("NSMutableData");
        var allocated = SendIntPtr(@class, AllocSelector);
        if (allocated == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        fixed (byte* dataPointer = data)
        {
            return SendInitWithBytesLength(allocated, InitWithBytesLengthSelector, (IntPtr)dataPointer, (nuint)data.Length);
        }
    }

    public static IntPtr MutableBytes(IntPtr data)
    {
        return SendIntPtr(data, MutableBytesSelector);
    }

    public static bool SendIoRequest(IntPtr pipe, IntPtr data, double completionTimeout, out nuint bytesTransferred, out IntPtr error)
    {
        nuint localBytesTransferred = 0;
        var result = SendIoRequest(pipe, SendIoRequestSelector, data, &localBytesTransferred, completionTimeout, out error);
        bytesTransferred = localBytesTransferred;
        return result != 0;
    }

    public static bool EnqueueIoRequest(IntPtr pipe, IntPtr data, double completionTimeout, IntPtr completionHandler, out IntPtr error)
    {
        return SendEnqueueIoRequest(pipe, EnqueueIoRequestSelector, data, completionTimeout, out error, completionHandler) != 0;
    }

    public static MacObjCBlock CreateIoCompletionBlock(TaskCompletionSource<MacUsbHostTransferResult> completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var handle = GCHandle.Alloc(completion);
        var stackBlock = Marshal.AllocHGlobal(Marshal.SizeOf<MacBlockLiteral>());
        try
        {
            unsafe
            {
                var literal = new MacBlockLiteral(
                    ConcreteStackBlock,
                    BlockHasSignature,
                    0,
                    (IntPtr)(delegate* unmanaged<IntPtr, uint, nuint, void>)&CompleteIoRequest,
                    CompletionBlockDescriptor,
                    GCHandle.ToIntPtr(handle));
                Marshal.StructureToPtr(literal, stackBlock, false);
            }

            var copiedBlock = BlockCopy(stackBlock);
            if (copiedBlock == IntPtr.Zero)
            {
                throw new UsbTransportException("Failed to allocate macOS IOUSBHost completion block.");
            }

            return new MacObjCBlock(copiedBlock, handle);
        }
        catch
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }

            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(stackBlock);
        }
    }

    public static bool AbortPipe(IntPtr pipe, out IntPtr error)
    {
        return SendAbort(pipe, AbortWithOptionSelector, UsbHostAbortOptionSynchronous, out error) != 0;
    }

    public static bool ClearStall(IntPtr pipe, out IntPtr error)
    {
        return SendClearStall(pipe, ClearStallSelector, out error) != 0;
    }

    public static int GetErrorCode(IntPtr error)
    {
        return error == IntPtr.Zero ? 0 : (int)SendNInt(error, ErrorCodeSelector);
    }

    public static string? GetErrorDomain(IntPtr error)
    {
        return error == IntPtr.Zero ? null : GetString(SendIntPtr(error, ErrorDomainSelector));
    }

    public static string? GetErrorDescription(IntPtr error)
    {
        return error == IntPtr.Zero ? null : GetString(SendIntPtr(error, LocalizedDescriptionSelector));
    }

    public static void Destroy(IntPtr value)
    {
        if (value != IntPtr.Zero)
        {
            SendVoid(value, DestroySelector);
        }
    }

    public static void Release(IntPtr value)
    {
        if (value != IntPtr.Zero)
        {
            SendVoid(value, ReleaseSelector);
        }
    }

    public static UsbTransportException CreateException(IntPtr error, string operation)
    {
        var code = GetErrorCode(error);
        var domain = GetErrorDomain(error);
        var description = GetErrorDescription(error);
        var details = domain is null && description is null
            ? string.Empty
            : $" Domain '{domain ?? "unknown"}'{(description is null ? string.Empty : $": {description}")}.";

        return code == 0
            ? new UsbTransportException($"Failed to {operation}.{details}")
            : new UsbTransportException(MacUsbErrors.Map(unchecked((uint)code)), $"Failed to {operation}; NSError code 0x{code:x8}.{details}");
    }

    private static string? GetString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
        {
            return null;
        }

        var utf8String = SendIntPtr(nsString, Utf8StringSelector);
        return utf8String == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8String);
    }

    private static IntPtr GetRequiredClass(string name)
    {
        var @class = GetClass(name);
        if (@class == IntPtr.Zero)
        {
            throw new UsbTransportException(UsbTransportError.PlatformDependencyMissing, $"Objective-C class '{name}' was not found.");
        }

        return @class;
    }

    private static IntPtr CreateCompletionBlockDescriptor()
    {
        var descriptor = Marshal.AllocHGlobal(Marshal.SizeOf<MacBlockDescriptor>());
        var signature = Marshal.StringToHGlobalAnsi(CompletionBlockSignature);
        var value = new MacBlockDescriptor(0, checked((nuint)Marshal.SizeOf<MacBlockLiteral>()), signature);
        Marshal.StructureToPtr(value, descriptor, false);
        return descriptor;
    }

    [UnmanagedCallersOnly]
    private static void CompleteIoRequest(IntPtr block, uint status, nuint bytesTransferred)
    {
        var context = Marshal.PtrToStructure<MacBlockLiteral>(block).Context;
        var handle = GCHandle.FromIntPtr(context);
        if (handle.Target is TaskCompletionSource<MacUsbHostTransferResult> completion)
        {
            completion.TrySetResult(new MacUsbHostTransferResult(status, bytesTransferred));
        }
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlopen", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr Dlopen(string path, int mode);

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_copy")]
    private static partial IntPtr BlockCopy(IntPtr block);

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_release")]
    internal static partial void BlockRelease(IntPtr block);

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dispatch_queue_create", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr DispatchQueueCreate(string label, IntPtr attr);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetClass(string name);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr RegisterSelector(string name);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint SendNInt(IntPtr receiver, IntPtr selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(IntPtr receiver, IntPtr selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendInitWithLength(IntPtr receiver, IntPtr selector, nuint length);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendInitWithBytesLength(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendInitWithIoService(
        IntPtr receiver,
        IntPtr selector,
        uint service,
        nuint options,
        IntPtr queue,
        out IntPtr error,
        IntPtr interestHandler);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial byte SendSelectAlternateSetting(IntPtr receiver, IntPtr selector, nuint alternateSetting, out IntPtr error);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendCopyPipeWithAddress(IntPtr receiver, IntPtr selector, nuint endpointAddress, out IntPtr error);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial byte SendIoRequest(
        IntPtr receiver,
        IntPtr selector,
        IntPtr data,
        nuint* bytesTransferred,
        double completionTimeout,
        out IntPtr error);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial byte SendEnqueueIoRequest(
        IntPtr receiver,
        IntPtr selector,
        IntPtr data,
        double completionTimeout,
        out IntPtr error,
        IntPtr completionHandler);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial byte SendAbort(IntPtr receiver, IntPtr selector, nuint option, out IntPtr error);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial byte SendClearStall(IntPtr receiver, IntPtr selector, out IntPtr error);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct MacBlockLiteral(
        IntPtr Isa,
        int Flags,
        int Reserved,
        IntPtr Invoke,
        IntPtr Descriptor,
        IntPtr Context);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct MacBlockDescriptor(
        nuint Reserved,
        nuint Size,
        IntPtr Signature);
}
