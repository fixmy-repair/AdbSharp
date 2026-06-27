namespace AdbSharp.Adb;

/// <summary>
/// Options used when discovering ADB services through mDNS/DNS-SD.
/// </summary>
public sealed class AdbMdnsDiscoveryOptions
{
    /// <summary>
    /// Gets or sets the total discovery window.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Gets or sets the service kinds to query. When omitted, all Android ADB mDNS service types are queried.
    /// </summary>
    public IReadOnlyCollection<AdbMdnsServiceKind>? ServiceKinds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether IPv4 multicast should be queried.
    /// </summary>
    public bool IncludeIPv4 { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether IPv6 multicast should be queried.
    /// </summary>
    public bool IncludeIPv6 { get; set; } = true;
}
