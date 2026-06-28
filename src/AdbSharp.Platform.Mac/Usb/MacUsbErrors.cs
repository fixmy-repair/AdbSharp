using AdbSharp.Platform.Mac.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Mac.Usb;

internal static class MacUsbErrors
{
    public static UsbTransportException Create(uint result, string operation)
    {
        return new UsbTransportException(Map(result), $"Failed to {operation}; IOReturn 0x{result:x8}.");
    }

    public static UsbTransportError Map(uint result)
    {
        return result switch
        {
            MacNative.IoReturnNotPrivileged or MacNative.IoReturnNotPermitted => UsbTransportError.PermissionDenied,
            MacNative.IoReturnNoDevice or MacNative.IoReturnNotAttached => UsbTransportError.DeviceDisconnected,
            MacNative.IoReturnNotFound => UsbTransportError.DeviceNotFound,
            MacNative.UsbReturnPipeStalled => UsbTransportError.EndpointStalled,
            MacNative.UsbReturnUnknownPipe => UsbTransportError.InvalidEndpoint,
            MacNative.IoReturnTimeout or MacNative.IoReturnNotReady or MacNative.IoReturnNotResponding or MacNative.UsbReturnTransactionTimeout => UsbTransportError.Timeout,
            MacNative.IoReturnBusy => UsbTransportError.Busy,
            MacNative.IoReturnExclusiveAccess => UsbTransportError.ExclusiveAccess,
            MacNative.IoReturnAborted or MacNative.UsbReturnTransactionReturned => UsbTransportError.OperationAborted,
            MacNative.IoReturnIoError => UsbTransportError.Io,
            _ => UsbTransportError.Unknown
        };
    }
}
