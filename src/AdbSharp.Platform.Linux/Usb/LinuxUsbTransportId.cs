using System.Globalization;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Linux.Usb;

internal sealed record LinuxUsbTransportId(
    byte BusNumber,
    byte DeviceAddress,
    byte ConfigurationValue,
    byte InterfaceNumber,
    byte AlternateSetting,
    UsbEndpoint BulkIn,
    UsbEndpoint BulkOut)
{
    public string Encode()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"linux:{BusNumber}:{DeviceAddress}:{ConfigurationValue}:{InterfaceNumber}:{AlternateSetting}:{BulkIn.Address:x2}:{BulkOut.Address:x2}:{BulkIn.MaxPacketSize}:{BulkOut.MaxPacketSize}");
    }

    public static bool TryParse(string transportId, out LinuxUsbTransportId result)
    {
        result = default!;
        var parts = transportId.Split(':');
        if (parts.Length != 10 || !string.Equals(parts[0], "linux", StringComparison.Ordinal))
        {
            return false;
        }

        if (!byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bus)
            || !byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var address)
            || !byte.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var configuration)
            || !byte.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var interfaceNumber)
            || !byte.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var alternateSetting)
            || !byte.TryParse(parts[6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bulkInAddress)
            || !byte.TryParse(parts[7], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bulkOutAddress)
            || !ushort.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bulkInMaxPacket)
            || !ushort.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bulkOutMaxPacket))
        {
            return false;
        }

        result = new LinuxUsbTransportId(
            bus,
            address,
            configuration,
            interfaceNumber,
            alternateSetting,
            new UsbEndpoint(bulkInAddress, UsbEndpointDirection.In, UsbTransferKind.Bulk, bulkInMaxPacket),
            new UsbEndpoint(bulkOutAddress, UsbEndpointDirection.Out, UsbTransferKind.Bulk, bulkOutMaxPacket));
        return true;
    }
}
