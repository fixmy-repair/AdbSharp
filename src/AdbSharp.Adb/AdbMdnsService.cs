using System.Net;

namespace AdbSharp.Adb;

/// <summary>
/// Describes an ADB service discovered through mDNS/DNS-SD.
/// </summary>
/// <param name="instanceName">The DNS-SD service instance name.</param>
/// <param name="serviceType">The ADB DNS-SD service type.</param>
/// <param name="kind">The recognized ADB service kind.</param>
/// <param name="targetHost">The SRV target host name.</param>
/// <param name="port">The TCP port advertised by the service.</param>
/// <param name="addresses">The resolved target host addresses.</param>
/// <param name="txtRecords">The DNS-SD TXT records advertised by the service.</param>
public sealed class AdbMdnsService(
    string instanceName,
    string serviceType,
    AdbMdnsServiceKind kind,
    string targetHost,
    int port,
    IReadOnlyList<IPAddress>? addresses = null,
    IReadOnlyDictionary<string, string>? txtRecords = null)
{
    /// <summary>
    /// Gets the DNS-SD service instance name.
    /// </summary>
    public string InstanceName { get; } = string.IsNullOrWhiteSpace(instanceName)
        ? throw new ArgumentException("mDNS service instance names cannot be empty.", nameof(instanceName))
        : instanceName;

    /// <summary>
    /// Gets the ADB DNS-SD service type.
    /// </summary>
    public string ServiceType { get; } = string.IsNullOrWhiteSpace(serviceType)
        ? throw new ArgumentException("mDNS service types cannot be empty.", nameof(serviceType))
        : serviceType;

    /// <summary>
    /// Gets the recognized ADB service kind.
    /// </summary>
    public AdbMdnsServiceKind Kind { get; } = kind;

    /// <summary>
    /// Gets the SRV target host name.
    /// </summary>
    public string TargetHost { get; } = string.IsNullOrWhiteSpace(targetHost)
        ? throw new ArgumentException("mDNS service target hosts cannot be empty.", nameof(targetHost))
        : targetHost.TrimEnd('.');

    /// <summary>
    /// Gets the TCP port advertised by the service.
    /// </summary>
    public int Port { get; } = port is < 1 or > 65535
        ? throw new ArgumentOutOfRangeException(nameof(port), "TCP ports must be in the range 1 through 65535.")
        : port;

    /// <summary>
    /// Gets the resolved target host addresses.
    /// </summary>
    public IReadOnlyList<IPAddress> Addresses { get; } = addresses is null ? [] : [.. addresses];

    /// <summary>
    /// Gets the DNS-SD TXT records advertised by the service.
    /// </summary>
    public IReadOnlyDictionary<string, string> TxtRecords { get; } = txtRecords is null
        ? []
        : new Dictionary<string, string>(txtRecords, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the preferred host string for direct TCP connections.
    /// </summary>
    public string Host => Addresses.Count == 0 ? TargetHost : Addresses[0].ToString();
}
