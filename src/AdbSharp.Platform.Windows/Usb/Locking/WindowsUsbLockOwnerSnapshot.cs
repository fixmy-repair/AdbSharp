namespace AdbSharp.Platform.Windows.Usb.Locking;

internal sealed record WindowsUsbLockOwnerSnapshot(
    IReadOnlyList<WindowsUsbLockOwnerCandidate> Owners,
    bool IsPartial,
    string? Message);
