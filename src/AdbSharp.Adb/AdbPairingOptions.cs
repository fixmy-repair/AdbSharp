namespace AdbSharp.Adb;

/// <summary>
/// Options used when pairing with an Android wireless debugging endpoint.
/// </summary>
public sealed class AdbPairingOptions
{
    /// <summary>
    /// Gets or sets an optional backend that implements the secure ADB pairing TLS, exporter, SPAKE2, and AES-GCM operations. When omitted, the built-in managed backend is used.
    /// </summary>
    public IAdbPairingBackend? Backend { get; set; }

    /// <summary>
    /// Gets or sets the public key comment placed in the host ADB public key payload.
    /// </summary>
    public string PublicKeyComment { get; set; } = "AdbSharp";
}
