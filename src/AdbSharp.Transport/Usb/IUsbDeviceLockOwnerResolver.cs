using AdbSharp.Common.Devices;

namespace AdbSharp.Transport.Usb;

/// <summary>
/// Resolves host processes that may be holding a USB device interface open.
/// </summary>
public interface IUsbDeviceLockOwnerResolver
{
    /// <summary>
    /// Gets the host platform name handled by the resolver.
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// Determines whether this resolver can inspect the specified USB descriptor.
    /// </summary>
    /// <param name="descriptor">The USB descriptor to inspect.</param>
    /// <returns><see langword="true" /> when this resolver can inspect the descriptor.</returns>
    bool CanResolve(UsbDeviceDescriptor descriptor);

    /// <summary>
    /// Resolves processes that may be holding the specified USB interface.
    /// </summary>
    /// <param name="descriptor">The USB descriptor to inspect.</param>
    /// <param name="openFailure">The open failure that triggered resolution, when available.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The owner resolution result.</returns>
    ValueTask<UsbDeviceLockOwnerResolution> ResolveAsync(
        UsbDeviceDescriptor descriptor,
        UsbTransportException? openFailure = null,
        CancellationToken cancellationToken = default);
}
