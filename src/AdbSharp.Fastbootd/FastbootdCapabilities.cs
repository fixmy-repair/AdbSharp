namespace AdbSharp.Fastbootd;

/// <summary>
/// Capability flags discovered from a userspace Fastboot implementation.
/// </summary>
/// <param name="IsUserspace">Indicates whether <c>getvar:is-userspace</c> reports userspace Fastboot.</param>
/// <param name="SupportsDynamicPartitions">Indicates whether dynamic partition metadata is advertised.</param>
/// <param name="SupportsLogicalPartitions">Indicates whether logical partition operations are available.</param>
/// <param name="SupportsSnapshotUpdates">Indicates whether snapshot update status is advertised.</param>
/// <param name="SupportsVirtualAb">Indicates whether Virtual A/B support is inferred from snapshot update support.</param>
/// <param name="SuperPartitionName">The reported super partition name, when available.</param>
/// <param name="SnapshotUpdateStatus">The reported snapshot update status, when available.</param>
public sealed record FastbootdCapabilities(
    bool IsUserspace,
    bool SupportsDynamicPartitions,
    bool SupportsLogicalPartitions,
    bool SupportsSnapshotUpdates,
    bool SupportsVirtualAb,
    string? SuperPartitionName = null,
    string? SnapshotUpdateStatus = null);
