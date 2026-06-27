using AdbSharp.Transport.Usb;

namespace AdbSharp.Fastboot;

/// <summary>
/// Options used when connecting to a Fastboot device.
/// </summary>
public sealed class FastbootClientOptions
{
    /// <summary>
    /// Gets or sets the USB transport factory. When omitted, the global transport registry is used.
    /// </summary>
    public IUsbTransportFactory? TransportFactory { get; set; }

    /// <summary>
    /// Gets or sets the transfer chunk size used for data phases.
    /// </summary>
    public int ChunkSize { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets or sets a progress sink for bootloader informational text.
    /// </summary>
    public IProgress<string>? InfoProgress { get; set; }
}
