namespace AdbSharp.Transport.Usb;

/// <summary>
/// Options for releasing a detected USB device lock owner.
/// </summary>
public sealed class UsbDeviceLockReleaseOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether ADB server owners should be released by the local ADB server protocol.
    /// </summary>
    public bool AllowGracefulAdbServerKill { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the releaser may terminate the owner process.
    /// </summary>
    public bool AllowProcessTermination { get; set; }

    /// <summary>
    /// Gets or sets the local ADB server host used for graceful ADB release.
    /// </summary>
    public string AdbServerHost { get; set; } = "127.0.0.1";

    /// <summary>
    /// Gets or sets the local ADB server port used for graceful ADB release.
    /// </summary>
    public int AdbServerPort { get; set; } = 5037;

    /// <summary>
    /// Gets or sets the timeout used when contacting the local ADB server.
    /// </summary>
    public TimeSpan AdbServerTimeout { get; set; } = TimeSpan.FromSeconds(2);
}
