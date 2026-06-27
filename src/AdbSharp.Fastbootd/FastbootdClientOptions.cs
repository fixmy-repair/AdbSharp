using AdbSharp.Common.Devices;
using AdbSharp.Fastboot;

namespace AdbSharp.Fastbootd;

/// <summary>
/// Options used when connecting to a userspace Fastboot device.
/// </summary>
public sealed class FastbootdClientOptions
{
    /// <summary>
    /// Gets or sets the underlying Fastboot options.
    /// </summary>
    public FastbootClientOptions FastbootOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets whether bootloader Fastboot devices may be rebooted to userspace Fastboot automatically.
    /// </summary>
    public bool AllowAutomaticTransition { get; set; } = true;

    /// <summary>
    /// Gets or sets the timeout used while waiting for a device to reappear in userspace Fastboot.
    /// </summary>
    public TimeSpan TransitionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the delay between rediscovery attempts after <c>reboot-fastboot</c>.
    /// </summary>
    public TimeSpan RediscoveryPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets an optional rediscovery callback used after <c>reboot-fastboot</c>.
    /// </summary>
    public Func<AndroidDevice, CancellationToken, ValueTask<AndroidDevice?>>? RediscoverFastbootdAsync { get; set; }
}
