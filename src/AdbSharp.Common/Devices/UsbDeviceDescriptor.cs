namespace AdbSharp.Common.Devices;

/// <summary>
/// Transport-neutral USB metadata for an Android interface.
/// </summary>
/// <param name="TransportId">A host-local identifier that can be passed back to a transport factory.</param>
/// <param name="VendorId">The USB vendor identifier.</param>
/// <param name="ProductId">The USB product identifier.</param>
/// <param name="InterfaceNumber">The interface number selected for protocol I/O.</param>
/// <param name="InterfaceClass">The USB interface class.</param>
/// <param name="InterfaceSubClass">The USB interface subclass.</param>
/// <param name="InterfaceProtocol">The USB interface protocol.</param>
/// <param name="SerialNumber">The USB serial number when available.</param>
/// <param name="Manufacturer">The manufacturer string when available.</param>
/// <param name="Product">The product string when available.</param>
public sealed record UsbDeviceDescriptor(
    string TransportId,
    ushort VendorId,
    ushort ProductId,
    byte InterfaceNumber,
    byte InterfaceClass,
    byte InterfaceSubClass,
    byte InterfaceProtocol,
    string? SerialNumber,
    string? Manufacturer,
    string? Product);
