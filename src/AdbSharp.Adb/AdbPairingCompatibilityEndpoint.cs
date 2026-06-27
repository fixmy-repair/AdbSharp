namespace AdbSharp.Adb;

/// <summary>
/// Describes an Android wireless debugging pairing endpoint used for hardware compatibility validation.
/// </summary>
/// <param name="Vendor">The device vendor or OEM name used for compatibility reporting.</param>
/// <param name="Model">The device model name used for compatibility reporting.</param>
/// <param name="Host">The pairing endpoint host name or IP address.</param>
/// <param name="PairingPort">The pairing endpoint port shown by Android wireless debugging.</param>
/// <param name="PairingCode">The fresh pairing code shown by Android wireless debugging.</param>
public sealed record AdbPairingCompatibilityEndpoint(
    string Vendor,
    string Model,
    string Host,
    int PairingPort,
    string PairingCode)
{
    /// <summary>
    /// Gets the optional ADB TCP port to probe after pairing succeeds.
    /// </summary>
    public int? AdbPort { get; init; }

    /// <summary>
    /// Gets the optional expected value of <c>ro.product.manufacturer</c> after the post-pair ADB connection succeeds.
    /// </summary>
    public string? ExpectedManufacturer { get; init; }

    /// <summary>
    /// Gets the optional expected value of <c>ro.product.model</c> after the post-pair ADB connection succeeds.
    /// </summary>
    public string? ExpectedModel { get; init; }

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Vendor);
        ArgumentException.ThrowIfNullOrWhiteSpace(Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(PairingCode);
        ValidatePort(PairingPort, nameof(PairingPort));
        if (AdbPort is { } adbPort)
        {
            ValidatePort(adbPort, nameof(AdbPort));
        }
    }

    private static void ValidatePort(int port, string parameterName)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(parameterName, "TCP ports must be in the range 1 through 65535.");
        }
    }
}
