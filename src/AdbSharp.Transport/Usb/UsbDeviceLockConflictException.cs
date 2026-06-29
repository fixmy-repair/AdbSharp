namespace AdbSharp.Transport.Usb;

/// <summary>
/// Represents an open failure after opt-in USB lock owner handling was attempted.
/// </summary>
public sealed class UsbDeviceLockConflictException(
    UsbTransportError error,
    string message,
    UsbDeviceLockOwnerResolution resolution,
    IReadOnlyList<UsbDeviceLockReleaseResult> releaseResults,
    Exception innerException) : UsbTransportException(error, message, innerException)
{
    /// <summary>
    /// Gets the owner resolution result.
    /// </summary>
    public UsbDeviceLockOwnerResolution Resolution { get; } = resolution;

    /// <summary>
    /// Gets release attempts performed before the failure.
    /// </summary>
    public IReadOnlyList<UsbDeviceLockReleaseResult> ReleaseResults { get; } = releaseResults;
}
