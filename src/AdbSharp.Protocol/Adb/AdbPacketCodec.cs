using System.Buffers.Binary;

namespace AdbSharp.Protocol.Adb;

/// <summary>
/// Encodes and decodes ADB packets.
/// </summary>
public static class AdbPacketCodec
{
    /// <summary>
    /// Computes the ADB command magic value.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>The command magic value.</returns>
    public static uint ComputeMagic(AdbCommand command) => (uint)command ^ uint.MaxValue;

    /// <summary>
    /// Computes the ADB payload checksum.
    /// </summary>
    /// <param name="payload">The payload bytes.</param>
    /// <returns>The unsigned byte-sum checksum.</returns>
    public static uint ComputeChecksum(ReadOnlySpan<byte> payload)
    {
        uint checksum = 0;
        foreach (var value in payload)
        {
            checksum += value;
        }

        return checksum;
    }

    /// <summary>
    /// Gets the encoded packet length.
    /// </summary>
    /// <param name="packet">The packet.</param>
    /// <returns>The encoded byte length.</returns>
    public static int GetEncodedLength(AdbPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return AdbConstants.HeaderLength + packet.Payload.Length;
    }

    /// <summary>
    /// Writes a packet to a caller-provided buffer.
    /// </summary>
    /// <param name="packet">The packet to write.</param>
    /// <param name="destination">The destination buffer.</param>
    /// <returns>The number of bytes written.</returns>
    public static int Write(AdbPacket packet, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var length = GetEncodedLength(packet);
        if (destination.Length < length)
        {
            throw new ArgumentException("Destination is too small for the encoded ADB packet.", nameof(destination));
        }

        packet.Header.Write(destination[..AdbConstants.HeaderLength]);
        packet.Payload.Span.CopyTo(destination[AdbConstants.HeaderLength..]);
        return length;
    }

    /// <summary>
    /// Reads a complete packet from a contiguous buffer.
    /// </summary>
    /// <param name="source">The encoded packet bytes.</param>
    /// <returns>The decoded packet.</returns>
    public static AdbPacket Read(ReadOnlySpan<byte> source)
    {
        var header = AdbPacketHeader.Read(source);
        var totalLength = checked(AdbConstants.HeaderLength + (int)header.PayloadLength);
        if (source.Length < totalLength)
        {
            throw new ArgumentException("Source is too small for the encoded ADB packet.", nameof(source));
        }

        return new AdbPacket(header, source.Slice(AdbConstants.HeaderLength, (int)header.PayloadLength).ToArray());
    }

    /// <summary>
    /// Writes a 32-bit little-endian integer for service-level protocols.
    /// </summary>
    /// <param name="destination">The destination buffer.</param>
    /// <param name="value">The value to write.</param>
    public static void WriteUInt32LittleEndian(Span<byte> destination, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
    }
}
