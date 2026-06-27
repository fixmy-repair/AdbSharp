namespace AdbSharp.Common.Devices;

/// <summary>
/// Feature flags discovered from ADB or Fastboot capability probes.
/// </summary>
/// <param name="SupportsAdb">Indicates whether ADB services are available.</param>
/// <param name="SupportsFastboot">Indicates whether Fastboot commands are available.</param>
/// <param name="SupportsFastbootd">Indicates whether userspace Fastboot commands are available.</param>
/// <param name="SupportsFileSync">Indicates whether the ADB file sync service is available.</param>
/// <param name="SupportsShellV2">Indicates whether shell v2 features are advertised.</param>
/// <param name="SupportsTls">Indicates whether ADB stream TLS is advertised.</param>
/// <param name="SupportsDynamicPartitions">Indicates whether logical partition operations are supported.</param>
/// <param name="SupportsLogicalPartitions">Indicates whether logical partition commands are supported.</param>
/// <param name="SupportsSnapshotUpdates">Indicates whether snapshot update commands are supported.</param>
/// <param name="SupportsVirtualAb">Indicates whether Virtual A/B update behavior is detected.</param>
public sealed record DeviceCapabilities(
    bool SupportsAdb = false,
    bool SupportsFastboot = false,
    bool SupportsFastbootd = false,
    bool SupportsFileSync = false,
    bool SupportsShellV2 = false,
    bool SupportsTls = false,
    bool SupportsDynamicPartitions = false,
    bool SupportsLogicalPartitions = false,
    bool SupportsSnapshotUpdates = false,
    bool SupportsVirtualAb = false)
{
    /// <summary>
    /// Gets an empty capability set.
    /// </summary>
    public static DeviceCapabilities Empty { get; } = new();
}
