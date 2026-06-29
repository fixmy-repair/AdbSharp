namespace AdbSharp.Transport.Usb;

/// <summary>
/// Opt-in behavior for USB open failures caused by device locks.
/// </summary>
public sealed class UsbDeviceLockConflictOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether owner resolution should run when opening fails with a lock-like error.
    /// </summary>
    public bool ResolveOwners { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether detected ADB server owners should be released gracefully.
    /// </summary>
    public bool ReleaseAdbServer { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the open should be retried after a successful release.
    /// </summary>
    public bool RetryAfterRelease { get; set; } = true;

    /// <summary>
    /// Gets or sets the delay before retrying after a successful release.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets or sets an explicit owner resolver. When omitted, the resolver registry is used.
    /// </summary>
    public IUsbDeviceLockOwnerResolver? OwnerResolver { get; set; }

    /// <summary>
    /// Gets or sets an explicit owner releaser. When omitted, <see cref="UsbDeviceLockOwnerReleaser.Default" /> is used.
    /// </summary>
    public IUsbDeviceLockOwnerReleaser? OwnerReleaser { get; set; }

    /// <summary>
    /// Gets or sets release options used for automatic graceful ADB release.
    /// </summary>
    public UsbDeviceLockReleaseOptions ReleaseOptions { get; set; } = new();
}
