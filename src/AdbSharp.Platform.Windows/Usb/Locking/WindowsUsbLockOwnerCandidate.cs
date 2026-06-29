using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Windows.Usb.Locking;

internal sealed record WindowsUsbLockOwnerCandidate(
    int ProcessId,
    string? ProcessName,
    string? ExecutablePath,
    string? ObjectName,
    UsbDeviceLockOwnerConfidence Confidence);
