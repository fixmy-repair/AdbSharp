using AdbSharp.Common.Devices;

namespace AdbSharp.Transport.Usb;

/// <summary>
/// Enumerates Android-compatible USB interfaces on the current host.
/// </summary>
public interface IUsbDeviceEnumerator
{
    /// <summary>
    /// Finds USB descriptors that may expose ADB, Fastboot, or Fastbootd.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The discovered descriptors.</returns>
    ValueTask<IReadOnlyList<UsbDeviceDescriptor>> FindAsync(CancellationToken cancellationToken = default);
}
