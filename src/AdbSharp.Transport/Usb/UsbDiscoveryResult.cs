using AdbSharp.Common.Devices;

namespace AdbSharp.Transport.Usb;

/// <summary>
/// Contains USB descriptors and non-fatal discovery issues from all registered enumerators.
/// </summary>
/// <param name="Devices">The descriptors returned by successful enumerators.</param>
/// <param name="Issues">The discovery issues returned by failed enumerators.</param>
public sealed record UsbDiscoveryResult(
    IReadOnlyList<UsbDeviceDescriptor> Devices,
    IReadOnlyList<UsbDiscoveryIssue> Issues);
