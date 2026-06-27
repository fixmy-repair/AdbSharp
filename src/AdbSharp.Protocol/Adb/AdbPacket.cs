using AdbSharp.Common;

namespace AdbSharp.Protocol.Adb;

/// <summary>
/// Complete ADB packet with header and payload.
/// </summary>
public sealed class AdbPacket
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdbPacket"/> class.
    /// </summary>
    /// <param name="header">The packet header.</param>
    /// <param name="payload">The packet payload.</param>
    public AdbPacket(AdbPacketHeader header, ReadOnlyMemory<byte> payload)
    {
        if (header.PayloadLength != payload.Length)
        {
            throw new ProtocolException("ADB header payload length does not match payload size.");
        }

        if (header.PayloadChecksum != AdbPacketCodec.ComputeChecksum(payload.Span))
        {
            throw new ProtocolException("ADB payload checksum is invalid.");
        }

        Header = header;
        Payload = payload;
    }

    /// <summary>
    /// Gets the packet header.
    /// </summary>
    public AdbPacketHeader Header { get; }

    /// <summary>
    /// Gets the packet payload.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>
    /// Creates a packet with a generated header.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="arg0">The first command argument.</param>
    /// <param name="arg1">The second command argument.</param>
    /// <param name="payload">The payload.</param>
    /// <returns>The packet.</returns>
    public static AdbPacket Create(AdbCommand command, uint arg0, uint arg1, ReadOnlyMemory<byte> payload)
    {
        return new AdbPacket(AdbPacketHeader.Create(command, arg0, arg1, payload.Span), payload);
    }
}
