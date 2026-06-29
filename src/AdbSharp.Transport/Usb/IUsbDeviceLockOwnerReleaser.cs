namespace AdbSharp.Transport.Usb;

/// <summary>
/// Releases host processes that are holding USB device interfaces.
/// </summary>
public interface IUsbDeviceLockOwnerReleaser
{
    /// <summary>
    /// Attempts to release a detected lock owner.
    /// </summary>
    /// <param name="owner">The detected owner.</param>
    /// <param name="options">Release options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The release result.</returns>
    ValueTask<UsbDeviceLockReleaseResult> ReleaseAsync(
        UsbDeviceLockOwner owner,
        UsbDeviceLockReleaseOptions? options = null,
        CancellationToken cancellationToken = default);
}
