namespace AdbSharp.Platform.Mac.Usb;

internal readonly record struct MacUsbAsyncTransferResult(uint Status, nuint BytesTransferred);
