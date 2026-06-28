namespace AdbSharp.Platform.Windows.Usb.Native;

internal readonly record struct WindowsUsbOverlappedResult(bool Success, int BytesTransferred, int ErrorCode);
