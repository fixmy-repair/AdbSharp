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
        : this(header, payload, AllowsPreNegotiationZeroChecksum(header))
    {
    }

    private AdbPacket(AdbPacketHeader header, ReadOnlyMemory<byte> payload, bool allowZeroChecksum)
    {
        if (header.PayloadLength != payload.Length)
        {
            throw new ProtocolException("ADB header payload length does not match payload size.");
        }

        var checksum = AdbPacketCodec.ComputeChecksum(payload.Span);
        if (header.PayloadChecksum != checksum && !AllowsZeroChecksum(header, allowZeroChecksum))
        {
            throw new ProtocolException($"ADB payload checksum is invalid for {header.Command}: length={payload.Length}, expected=0x{header.PayloadChecksum:x8}, actual=0x{checksum:x8}.");
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
        return Create(command, arg0, arg1, payload, skipChecksum: false);
    }

    /// <summary>
    /// Creates a packet with a generated header.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="arg0">The first command argument.</param>
    /// <param name="arg1">The second command argument.</param>
    /// <param name="payload">The payload.</param>
    /// <param name="skipChecksum">True to encode a zero payload checksum for peers that negotiated checksum skipping.</param>
    /// <returns>The packet.</returns>
    public static AdbPacket Create(AdbCommand command, uint arg0, uint arg1, ReadOnlyMemory<byte> payload, bool skipChecksum)
    {
        return new AdbPacket(AdbPacketHeader.Create(command, arg0, arg1, payload.Span, skipChecksum), payload, skipChecksum);
    }

    /// <summary>
    /// Creates a packet from wire data while applying the negotiated checksum mode.
    /// </summary>
    /// <param name="header">The parsed wire header.</param>
    /// <param name="payload">The parsed wire payload.</param>
    /// <param name="allowZeroChecksum">True when the peer negotiated checksum skipping.</param>
    /// <returns>The packet.</returns>
    public static AdbPacket FromWire(AdbPacketHeader header, ReadOnlyMemory<byte> payload, bool allowZeroChecksum)
    {
        return new AdbPacket(header, payload, allowZeroChecksum || AllowsPreNegotiationZeroChecksum(header));
    }

    private static bool AllowsZeroChecksum(AdbPacketHeader header, bool allowZeroChecksum)
    {
        return header.PayloadChecksum == 0
            && (allowZeroChecksum || AllowsPreNegotiationZeroChecksum(header));
    }

    private static bool AllowsPreNegotiationZeroChecksum(AdbPacketHeader header)
    {
        return header.PayloadChecksum == 0
            && (header.Command == AdbCommand.Auth
                || (header.Command == AdbCommand.Connect && header.Arg0 >= AdbConstants.VersionSkipChecksum));
    }
}
