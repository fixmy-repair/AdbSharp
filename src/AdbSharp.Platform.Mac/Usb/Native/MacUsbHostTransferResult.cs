namespace AdbSharp.Platform.Mac.Usb.Native;

internal readonly record struct MacUsbHostTransferResult(uint Status, nuint BytesTransferred);
