namespace AdbSharp.Common.Devices;

/// <summary>
/// Stable descriptive identity for an Android device.
/// </summary>
/// <param name="SerialNumber">The device serial number when reported by the transport.</param>
/// <param name="Manufacturer">The USB manufacturer string when available.</param>
/// <param name="Product">The USB product string when available.</param>
/// <param name="Model">The Android model name when known.</param>
/// <param name="TransportId">A host-local stable transport identifier.</param>
public sealed record DeviceIdentity(
    string? SerialNumber,
    string? Manufacturer,
    string? Product,
    string? Model,
    string TransportId);
