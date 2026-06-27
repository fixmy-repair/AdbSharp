namespace AdbSharp.Adb;

/// <summary>
/// Reports the result of validating an Android wireless debugging pairing endpoint against real hardware.
/// </summary>
/// <param name="Vendor">The vendor or OEM name supplied for the endpoint.</param>
/// <param name="Model">The model name supplied for the endpoint.</param>
/// <param name="Host">The pairing endpoint host name or IP address.</param>
/// <param name="PairingPort">The pairing endpoint port.</param>
/// <param name="PairingSucceeded">A value indicating whether the pairing exchange completed.</param>
/// <param name="AdbConnectionTested">A value indicating whether the ADB TCP smoke check was attempted.</param>
/// <param name="AdbConnectionSucceeded">A value indicating whether the ADB TCP smoke check completed.</param>
/// <param name="Elapsed">The elapsed validation time.</param>
public sealed record AdbPairingCompatibilityResult(
    string Vendor,
    string Model,
    string Host,
    int PairingPort,
    bool PairingSucceeded,
    bool AdbConnectionTested,
    bool AdbConnectionSucceeded,
    TimeSpan Elapsed)
{
    /// <summary>
    /// Gets the ADB TCP port used for the optional post-pair smoke check.
    /// </summary>
    public int? AdbPort { get; init; }

    /// <summary>
    /// Gets the peer-info kind returned by the device after pairing succeeds.
    /// </summary>
    public AdbPairingPeerInfoKind? PeerInfoKind { get; init; }

    /// <summary>
    /// Gets the device GUID returned by the peer-info exchange when available.
    /// </summary>
    public string? DeviceGuid { get; init; }

    /// <summary>
    /// Gets the manufacturer reported by <c>ro.product.manufacturer</c> during the post-pair ADB smoke check.
    /// </summary>
    public string? ProductManufacturer { get; init; }

    /// <summary>
    /// Gets the model reported by <c>ro.product.model</c> during the post-pair ADB smoke check.
    /// </summary>
    public string? ProductModel { get; init; }

    /// <summary>
    /// Gets a value indicating whether the reported manufacturer matched the expected manufacturer, when one was supplied.
    /// </summary>
    public bool? ExpectedManufacturerMatched { get; init; }

    /// <summary>
    /// Gets a value indicating whether the reported model matched the expected model, when one was supplied.
    /// </summary>
    public bool? ExpectedModelMatched { get; init; }

    /// <summary>
    /// Gets the exception type when validation failed.
    /// </summary>
    public string? ErrorType { get; init; }

    /// <summary>
    /// Gets the sanitized validation failure message.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets a value indicating whether this endpoint passed the requested compatibility checks.
    /// </summary>
    public bool IsCompatible =>
        PairingSucceeded
        && (!AdbConnectionTested || AdbConnectionSucceeded)
        && ExpectedManufacturerMatched is not false
        && ExpectedModelMatched is not false;
}
