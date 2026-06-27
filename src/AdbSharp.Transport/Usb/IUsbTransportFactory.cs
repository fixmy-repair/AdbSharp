using AdbSharp.Common.Devices;

namespace AdbSharp.Transport.Usb;

/// <summary>
/// Opens USB transports for descriptors produced by a matching enumerator.
/// </summary>
public interface IUsbTransportFactory
{
    /// <summary>
    /// Gets the platform name handled by this factory.
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// Determines whether this factory can open the specified descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor to inspect.</param>
    /// <returns><see langword="true"/> when the descriptor can be opened.</returns>
    bool CanOpen(UsbDeviceDescriptor descriptor);

    /// <summary>
    /// Opens the selected USB transport.
    /// </summary>
    /// <param name="descriptor">The descriptor to open.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An opened USB transport.</returns>
    ValueTask<IUsbTransport> OpenAsync(UsbDeviceDescriptor descriptor, CancellationToken cancellationToken = default);
}
