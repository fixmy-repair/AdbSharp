using System.Globalization;
using System.Text;
using AdbSharp.Common;

namespace AdbSharp.Protocol.Fastboot;

/// <summary>
/// Fastboot command and response codec.
/// </summary>
public static class FastbootProtocol
{
    /// <summary>
    /// Maximum command packet length in bytes.
    /// </summary>
    public const int MaxCommandLength = 4096;

    /// <summary>
    /// Maximum response packet length in bytes.
    /// </summary>
    public const int MaxResponseLength = 256;

    /// <summary>
    /// Encodes a Fastboot command.
    /// </summary>
    /// <param name="command">The command text.</param>
    /// <returns>The ASCII command bytes.</returns>
    public static byte[] EncodeCommand(string command)
    {
        var length = GetEncodedCommandLength(command);
        var bytes = new byte[length];
        _ = EncodeCommand(command, bytes);
        return bytes;
    }

    /// <summary>
    /// Gets the encoded byte length of a Fastboot command.
    /// </summary>
    /// <param name="command">The command text.</param>
    /// <returns>The ASCII byte length.</returns>
    public static int GetEncodedCommandLength(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        foreach (var character in command)
        {
            if (character > 0x7f)
            {
                throw new ArgumentException("Fastboot commands must contain only ASCII characters.", nameof(command));
            }
        }

        if (command.Length > MaxCommandLength)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Fastboot commands must fit in one 4096-byte packet.");
        }

        return command.Length;
    }

    /// <summary>
    /// Encodes a Fastboot command into a caller-provided buffer.
    /// </summary>
    /// <param name="command">The command text.</param>
    /// <param name="destination">The destination buffer.</param>
    /// <returns>The number of bytes written.</returns>
    public static int EncodeCommand(string command, Span<byte> destination)
    {
        var length = GetEncodedCommandLength(command);
        if (destination.Length < length)
        {
            throw new ArgumentException("Destination is too small for the encoded Fastboot command.", nameof(destination));
        }

        for (var index = 0; index < command.Length; index++)
        {
            destination[index] = (byte)command[index];
        }

        return length;
    }

    /// <summary>
    /// Parses a Fastboot response packet.
    /// </summary>
    /// <param name="packet">The raw response packet.</param>
    /// <returns>The parsed response.</returns>
    public static FastbootResponse ParseResponse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 4)
        {
            throw new ProtocolException("Fastboot response is shorter than the four-byte tag.");
        }

        if (packet.Length > MaxResponseLength)
        {
            throw new ProtocolException("Fastboot response exceeds the maximum packet length.");
        }

        var payloadBytes = packet[4..];
        if (packet[..4].SequenceEqual("OKAY"u8))
        {
            return new FastbootResponse(FastbootResponseKind.Okay, GetPayloadString(payloadBytes));
        }

        if (packet[..4].SequenceEqual("FAIL"u8))
        {
            return new FastbootResponse(FastbootResponseKind.Fail, GetPayloadString(payloadBytes));
        }

        if (packet[..4].SequenceEqual("INFO"u8))
        {
            return new FastbootResponse(FastbootResponseKind.Info, GetPayloadString(payloadBytes));
        }

        if (packet[..4].SequenceEqual("TEXT"u8))
        {
            return new FastbootResponse(FastbootResponseKind.Text, GetPayloadString(payloadBytes));
        }

        if (packet[..4].SequenceEqual("DATA"u8))
        {
            return ParseData(payloadBytes);
        }

        throw new ProtocolException($"Unknown Fastboot response tag '{Encoding.ASCII.GetString(packet[..4])}'.");
    }

    /// <summary>
    /// Formats the Fastboot download command for a byte length.
    /// </summary>
    /// <param name="length">The data length.</param>
    /// <returns>The command text.</returns>
    public static string FormatDownloadCommand(long length)
    {
        if (length < 0 || length > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Fastboot download length must fit in an unsigned 32-bit value.");
        }

        return string.Create(CultureInfo.InvariantCulture, $"download:{length:x8}");
    }

    private static FastbootResponse ParseData(ReadOnlySpan<byte> payloadBytes)
    {
        if (payloadBytes.Length < 8)
        {
            throw new ProtocolException("Fastboot DATA response is missing the eight-digit length.");
        }

        var payload = GetPayloadString(payloadBytes);
        if (!uint.TryParse(payload.AsSpan(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var length))
        {
            throw new ProtocolException("Fastboot DATA response length is not hexadecimal.");
        }

        return new FastbootResponse(FastbootResponseKind.Data, payload, length);
    }

    private static string GetPayloadString(ReadOnlySpan<byte> payloadBytes)
    {
        return Encoding.ASCII.GetString(payloadBytes.TrimEnd((byte)0));
    }
}
