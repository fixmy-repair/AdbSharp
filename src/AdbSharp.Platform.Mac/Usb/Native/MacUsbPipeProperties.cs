namespace AdbSharp.Platform.Mac.Usb.Native;

internal readonly record struct MacUsbPipeProperties(
    byte PipeReference,
    byte Direction,
    byte Number,
    byte TransferType,
    ushort MaxPacketSize,
    byte Interval);
