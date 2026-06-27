using System.Text;

namespace AdbSharp.Platform.Windows.Usb;

internal sealed record WindowsUsbTransportId(string DevicePath)
{
    public string Encode()
    {
        return "windows:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(DevicePath)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryParse(string transportId, out WindowsUsbTransportId result)
    {
        result = default!;
        const string prefix = "windows:";
        if (!transportId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var encoded = transportId[prefix.Length..].Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
        try
        {
            result = new WindowsUsbTransportId(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
