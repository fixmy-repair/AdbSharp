namespace AdbSharp.Transport.Usb;

/// <summary>
/// Result of attempting to release a USB lock owner.
/// </summary>
/// <param name="Owner">The owner that was targeted.</param>
/// <param name="Kind">The release mechanism that was attempted.</param>
/// <param name="Succeeded">Whether the release succeeded.</param>
/// <param name="Message">A diagnostic message.</param>
/// <param name="Exception">The underlying failure, when available.</param>
public sealed record UsbDeviceLockReleaseResult(
    UsbDeviceLockOwner Owner,
    UsbDeviceLockReleaseKind Kind,
    bool Succeeded,
    string? Message,
    Exception? Exception = null);
