using AdbSharp.Common.Devices;

namespace AdbSharp.Transport.Usb;

/// <summary>
/// Represents an opened USB bulk transport for Android protocol traffic.
/// </summary>
public interface IUsbTransport : IAsyncDisposable
{
    /// <summary>
    /// Gets the device descriptor associated with this transport.
    /// </summary>
    UsbDeviceDescriptor Descriptor { get; }

    /// <summary>
    /// Gets the selected bulk input endpoint.
    /// </summary>
    UsbEndpoint BulkInEndpoint { get; }

    /// <summary>
    /// Gets the selected bulk output endpoint.
    /// </summary>
    UsbEndpoint BulkOutEndpoint { get; }

    /// <summary>
    /// Reads bytes from the device.
    /// </summary>
    /// <param name="buffer">The destination buffer.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of bytes read.</returns>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes bytes to the device.
    /// </summary>
    /// <param name="buffer">The source buffer.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests a USB-level reset for the opened transport.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Aborts pending I/O and invalidates this transport.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask AbortAsync(CancellationToken cancellationToken = default);
}
