using System.Security.Cryptography.X509Certificates;
using AdbSharp.Authentication.Adb;
using AdbSharp.Protocol.Adb;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Adb;

/// <summary>
/// Options used when connecting to an ADB device.
/// </summary>
public sealed class AdbClientOptions
{
    /// <summary>
    /// Gets or sets the USB transport factory used by <see cref="AdbClient.ConnectAsync" />. When omitted, the global transport registry is used.
    /// </summary>
    public IUsbTransportFactory? TransportFactory { get; set; }

    /// <summary>
    /// Gets or sets optional USB lock conflict handling for USB open failures.
    /// </summary>
    public UsbDeviceLockConflictOptions? LockConflictHandling { get; set; }

    /// <summary>
    /// Gets or sets the authenticator used for device authorization.
    /// </summary>
    public IAdbAuthenticator? Authenticator { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether ADB <c>STLS</c> transport upgrades are allowed.
    /// </summary>
    public bool EnableTls { get; set; } = true;

    /// <summary>
    /// Gets or sets the TLS target host name used for ADB TLS handshakes.
    /// </summary>
    public string TlsTargetHost { get; set; } = "adb";

    /// <summary>
    /// Gets or sets an optional client certificate provider for ADB TLS handshakes.
    /// </summary>
    public Func<CancellationToken, ValueTask<X509Certificate2?>>? TlsCertificateProvider { get; set; }

    /// <summary>
    /// Gets or sets the host identity payload advertised during connection.
    /// </summary>
    public string SystemIdentity { get; set; } = "host::features=shell_v2,cmd,stat_v2,ls_v2,fixed_push_mkdir";

    /// <summary>
    /// Gets or sets the host maximum payload size.
    /// </summary>
    public int MaxPayload { get; set; } = AdbConstants.MaxPayload;
}
