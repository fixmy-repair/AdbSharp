using System.Globalization;

namespace AdbSharp.Platform.Mac.Usb;

internal sealed record MacUsbTransportId(uint LocationId, byte InterfaceNumber, ushort VendorId, ushort ProductId)
{
    public string Encode()
    {
        return string.Create(CultureInfo.InvariantCulture, $"mac:{LocationId:x8}:{InterfaceNumber}:{VendorId:x4}:{ProductId:x4}");
    }

    public static bool TryParse(string transportId, out MacUsbTransportId result)
    {
        result = default!;
        var parts = transportId.Split(':');
        if (parts.Length != 5 || !string.Equals(parts[0], "mac", StringComparison.Ordinal))
        {
            return false;
        }

        if (!uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var locationId)
            || !byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var interfaceNumber)
            || !ushort.TryParse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vendorId)
            || !ushort.TryParse(parts[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var productId))
        {
            return false;
        }

        result = new MacUsbTransportId(locationId, interfaceNumber, vendorId, productId);
        return true;
    }
}
