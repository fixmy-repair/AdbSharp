namespace AdbSharp.Transport.Usb;

/// <summary>
/// Describes a host process that may hold a USB device interface lock.
/// </summary>
/// <param name="ProcessId">The host process identifier.</param>
/// <param name="ProcessName">The process name when available.</param>
/// <param name="ExecutablePath">The executable path when available.</param>
/// <param name="Kind">The detected owner kind.</param>
/// <param name="Confidence">The owner confidence level.</param>
/// <param name="Evidence">Short platform-specific evidence for the match.</param>
public sealed record UsbDeviceLockOwner(
    int ProcessId,
    string? ProcessName,
    string? ExecutablePath,
    UsbDeviceLockOwnerKind Kind,
    UsbDeviceLockOwnerConfidence Confidence,
    string? Evidence)
{
    /// <summary>
    /// Gets a value indicating whether this owner can be released through the ADB server protocol.
    /// </summary>
    public bool SupportsGracefulAdbRelease => Kind == UsbDeviceLockOwnerKind.AdbServer;
}
