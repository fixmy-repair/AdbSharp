using System.Globalization;
using System.Text;

namespace AdbSharp.Protocol.Adb;

/// <summary>
/// Encodes ADB service strings and host request prefixes.
/// </summary>
public static class AdbServiceEncoding
{
    /// <summary>
    /// Encodes a stream service string as a null-terminated UTF-8 payload.
    /// </summary>
    /// <param name="service">The service string.</param>
    /// <returns>The encoded payload.</returns>
    public static byte[] EncodeOpenPayload(string service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var byteCount = Encoding.UTF8.GetByteCount(service);
        var bytes = new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(service, bytes);
        return bytes;
    }

    /// <summary>
    /// Encodes an ADB server-style request using a four-hex-digit length prefix.
    /// </summary>
    /// <param name="request">The request string.</param>
    /// <returns>The encoded request bytes.</returns>
    public static byte[] EncodeLengthPrefixedRequest(string request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestBytes = Encoding.UTF8.GetBytes(request);
        if (requestBytes.Length > 0xffff)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "ADB host request is too large.");
        }

        var prefix = requestBytes.Length.ToString("x4", CultureInfo.InvariantCulture);
        var prefixBytes = Encoding.ASCII.GetBytes(prefix);
        var result = new byte[prefixBytes.Length + requestBytes.Length];
        prefixBytes.CopyTo(result, 0);
        requestBytes.CopyTo(result, prefixBytes.Length);
        return result;
    }
}
