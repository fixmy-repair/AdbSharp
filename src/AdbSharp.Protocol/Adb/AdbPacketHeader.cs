using System.Buffers.Binary;
using AdbSharp.Common;

namespace AdbSharp.Protocol.Adb;

/// <summary>
/// ADB packet header.
/// </summary>
/// <param name="Command">The packet command.</param>
/// <param name="Arg0">The first command argument.</param>
/// <param name="Arg1">The second command argument.</param>
/// <param name="PayloadLength">The payload length in bytes.</param>
/// <param name="PayloadChecksum">The unsigned byte-sum payload checksum.</param>
/// <param name="Magic">The command magic value.</param>
public readonly record struct AdbPacketHeader(
    AdbCommand Command,
    uint Arg0,
    uint Arg1,
    uint PayloadLength,
    uint PayloadChecksum,
    uint Magic)
{
    /// <summary>
    /// Creates a validated header for a command and payload.
    /// </summary>
    /// <param name="command">The packet command.</param>
    /// <param name="arg0">The first command argument.</param>
    /// <param name="arg1">The second command argument.</param>
    /// <param name="payload">The packet payload.</param>
    /// <returns>A packet header.</returns>
    public static AdbPacketHeader Create(AdbCommand command, uint arg0, uint arg1, ReadOnlySpan<byte> payload)
    {
        return Create(command, arg0, arg1, payload, skipChecksum: false);
    }

    /// <summary>
    /// Creates a validated header for a command and payload.
    /// </summary>
    /// <param name="command">The packet command.</param>
    /// <param name="arg0">The first command argument.</param>
    /// <param name="arg1">The second command argument.</param>
    /// <param name="payload">The packet payload.</param>
    /// <param name="skipChecksum">True to encode a zero payload checksum for peers that negotiated checksum skipping.</param>
    /// <returns>A packet header.</returns>
    public static AdbPacketHeader Create(AdbCommand command, uint arg0, uint arg1, ReadOnlySpan<byte> payload, bool skipChecksum)
    {
        return new AdbPacketHeader(
            command,
            arg0,
            arg1,
            checked((uint)payload.Length),
            skipChecksum ? 0 : AdbPacketCodec.ComputeChecksum(payload),
            AdbPacketCodec.ComputeMagic(command));
    }

    /// <summary>
    /// Writes the header using ADB little-endian wire format.
    /// </summary>
    /// <param name="destination">The destination buffer.</param>
    public void Write(Span<byte> destination)
    {
        if (destination.Length < AdbConstants.HeaderLength)
        {
            throw new ArgumentException("Destination is too small for an ADB header.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination[..4], (uint)Command);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), Arg0);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), Arg1);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), PayloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), PayloadChecksum);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), Magic);
    }

    /// <summary>
    /// Reads and validates the fixed header fields.
    /// </summary>
    /// <param name="source">The source buffer.</param>
    /// <returns>The parsed header.</returns>
    public static AdbPacketHeader Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < AdbConstants.HeaderLength)
        {
            throw new ProtocolException("ADB header is truncated.");
        }

        var command = (AdbCommand)BinaryPrimitives.ReadUInt32LittleEndian(source[..4]);
        var header = new AdbPacketHeader(
            command,
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(12, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(16, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(20, 4)));

        if (header.Magic != AdbPacketCodec.ComputeMagic(command))
        {
            throw new ProtocolException($"Invalid ADB magic for command {command}.");
        }

        if (header.PayloadLength > AdbConstants.MaxPayload)
        {
            throw new ProtocolException($"ADB payload length {header.PayloadLength} exceeds {AdbConstants.MaxPayload}.");
        }

        return header;
    }
}
