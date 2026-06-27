namespace AdbSharp.Transport.Usb;

/// <summary>
/// USB endpoint direction.
/// </summary>
public enum UsbEndpointDirection
{
    /// <summary>
    /// Data flows from host to device.
    /// </summary>
    Out,

    /// <summary>
    /// Data flows from device to host.
    /// </summary>
    In
}

/// <summary>
/// USB endpoint transfer kind.
/// </summary>
public enum UsbTransferKind
{
    /// <summary>
    /// Bulk endpoint transfer.
    /// </summary>
    Bulk
}

/// <summary>
/// Describes a USB endpoint selected for Android protocol traffic.
/// </summary>
/// <param name="Address">The endpoint address.</param>
/// <param name="Direction">The endpoint direction.</param>
/// <param name="TransferKind">The endpoint transfer kind.</param>
/// <param name="MaxPacketSize">The endpoint max packet size.</param>
public sealed record UsbEndpoint(
    byte Address,
    UsbEndpointDirection Direction,
    UsbTransferKind TransferKind,
    ushort MaxPacketSize);
