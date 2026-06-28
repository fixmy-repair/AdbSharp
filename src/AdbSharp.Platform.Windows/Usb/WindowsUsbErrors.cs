using System.ComponentModel;
using System.Runtime.InteropServices;
using AdbSharp.Platform.Windows.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Windows.Usb;

internal static class WindowsUsbErrors
{
    public static UsbTransportException Create(string message)
    {
        var errorCode = Marshal.GetLastPInvokeError();
        return Create(errorCode, message);
    }

    public static UsbTransportException Create(int errorCode, string message)
    {
        return new UsbTransportException(Map(errorCode), $"{message} Win32 error {errorCode}.", new Win32Exception(errorCode));
    }

    public static UsbTransportError Map(int errorCode)
    {
        return errorCode switch
        {
            WindowsUsbNative.ErrorAccessDenied => UsbTransportError.PermissionDenied,
            WindowsUsbNative.ErrorFileNotFound => UsbTransportError.DeviceNotFound,
            WindowsUsbNative.ErrorDeviceNotConnected or WindowsUsbNative.ErrorInvalidHandle => UsbTransportError.DeviceDisconnected,
            WindowsUsbNative.ErrorSemTimeout => UsbTransportError.Timeout,
            WindowsUsbNative.ErrorBusy => UsbTransportError.Busy,
            WindowsUsbNative.ErrorSharingViolation => UsbTransportError.ExclusiveAccess,
            WindowsUsbNative.ErrorOperationAborted => UsbTransportError.OperationAborted,
            WindowsUsbNative.ErrorGenFailure => UsbTransportError.Io,
            _ => UsbTransportError.Unknown
        };
    }
}
