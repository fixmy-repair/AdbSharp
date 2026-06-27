namespace AdbSharp.Adb;

/// <summary>
/// Options for Android wireless debugging pairing hardware compatibility validation.
/// </summary>
public sealed class AdbPairingCompatibilityOptions
{
    /// <summary>
    /// Gets or sets pairing options passed to the ADB wireless pairing client.
    /// </summary>
    public AdbPairingOptions? PairingOptions { get; set; }

    /// <summary>
    /// Gets or sets ADB connection options used for the optional post-pair smoke check.
    /// </summary>
    public AdbClientOptions? AdbOptions { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the validator should connect to the ADB TCP endpoint after pairing.
    /// </summary>
    public bool VerifyAdbConnection { get; set; } = true;

    /// <summary>
    /// Gets or sets the default ADB TCP port used when <see cref="AdbPairingCompatibilityEndpoint.AdbPort" /> is omitted.
    /// </summary>
    public int DefaultAdbPort { get; set; } = 5555;
}
