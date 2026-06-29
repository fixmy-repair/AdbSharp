namespace AdbSharp.Platform.Mac.Usb.Locking;

internal sealed record MacProcessSnapshot(int ProcessId, string? ProcessName, string? ExecutablePath);
