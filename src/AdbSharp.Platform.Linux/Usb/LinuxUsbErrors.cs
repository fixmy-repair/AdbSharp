using AdbSharp.Platform.Linux.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Linux.Usb;

internal static class LinuxUsbErrors
{
    public static UsbTransportException Create(int result, string operation)
    {
        return new UsbTransportException(Map(result), $"Failed to {operation}; libusb error {result}.");
    }

    public static UsbTransportError Map(int result)
    {
        return result switch
        {
            LibUsbNative.ErrorAccess => UsbTransportError.PermissionDenied,
            LibUsbNative.ErrorNoDevice => UsbTransportError.DeviceDisconnected,
            LibUsbNative.ErrorNotFound => UsbTransportError.DeviceNotFound,
            LibUsbNative.ErrorBusy => UsbTransportError.Busy,
            LibUsbNative.ErrorTimeout => UsbTransportError.Timeout,
            LibUsbNative.ErrorPipe => UsbTransportError.EndpointStalled,
            LibUsbNative.ErrorInterrupted => UsbTransportError.OperationAborted,
            LibUsbNative.ErrorIo or LibUsbNative.ErrorOverflow => UsbTransportError.Io,
            _ => UsbTransportError.Unknown
        };
    }
}
