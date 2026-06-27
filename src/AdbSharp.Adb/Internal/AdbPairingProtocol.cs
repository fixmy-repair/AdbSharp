using System.Buffers.Binary;
using AdbSharp.Common;

namespace AdbSharp.Adb.Internal;

internal static class AdbPairingProtocol
{
    private const byte HeaderVersion = 1;
    private const int HeaderLength = 6;
    private const int MaxPayloadSize = AdbPairingPeerInfo.WireSize * 2;

    public static async ValueTask<AdbPairingPeerInfo> ExchangeAsync(
        IAdbPairingSecureConnection connection,
        AdbPairingPeerInfo localPeerInfo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(localPeerInfo);

        var message = await connection.CreateClientMessageAsync(cancellationToken).ConfigureAwait(false);
        if (message.Length == 0)
        {
            throw new ProtocolException("ADB pairing backend produced an empty SPAKE2 message.");
        }

        await WritePacketAsync(connection, AdbPairingPacketType.Spake2Message, message, cancellationToken).ConfigureAwait(false);
        var peerMessage = await ReadPacketAsync(connection, AdbPairingPacketType.Spake2Message, cancellationToken).ConfigureAwait(false);
        await connection.InitializeCipherAsync(peerMessage, cancellationToken).ConfigureAwait(false);

        var encryptedLocalPeerInfo = await connection.EncryptAsync(localPeerInfo.ToWireBytes(), cancellationToken).ConfigureAwait(false);
        if (encryptedLocalPeerInfo.Length == 0)
        {
            throw new ProtocolException("ADB pairing backend produced an empty encrypted peer-info payload.");
        }

        await WritePacketAsync(connection, AdbPairingPacketType.PeerInfo, encryptedLocalPeerInfo, cancellationToken).ConfigureAwait(false);
        var encryptedPeerInfo = await ReadPacketAsync(connection, AdbPairingPacketType.PeerInfo, cancellationToken).ConfigureAwait(false);
        var peerInfo = await connection.DecryptAsync(encryptedPeerInfo, cancellationToken).ConfigureAwait(false);
        if (peerInfo.Length != AdbPairingPeerInfo.WireSize)
        {
            throw new ProtocolException($"ADB pairing peer-info payload length {peerInfo.Length} is invalid.");
        }

        return AdbPairingPeerInfo.FromWireBytes(peerInfo);
    }

    private static async ValueTask WritePacketAsync(
        IAdbPairingSecureConnection connection,
        AdbPairingPacketType type,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (payload.Length is <= 0 or > MaxPayloadSize)
        {
            throw new ProtocolException($"ADB pairing payload length {payload.Length} is invalid.");
        }

        Span<byte> header = stackalloc byte[HeaderLength];
        header[0] = HeaderVersion;
        header[1] = (byte)type;
        BinaryPrimitives.WriteUInt32BigEndian(header[2..], checked((uint)payload.Length));
        await connection.WriteAsync(header.ToArray(), cancellationToken).ConfigureAwait(false);
        await connection.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<byte[]> ReadPacketAsync(
        IAdbPairingSecureConnection connection,
        AdbPairingPacketType expectedType,
        CancellationToken cancellationToken)
    {
        var header = new byte[HeaderLength];
        await connection.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

        if (header[0] != HeaderVersion)
        {
            throw new ProtocolException($"ADB pairing header version {header[0]} is unsupported.");
        }

        if (header[1] is not ((byte)AdbPairingPacketType.Spake2Message) and not ((byte)AdbPairingPacketType.PeerInfo))
        {
            throw new ProtocolException($"ADB pairing packet type {header[1]} is unknown.");
        }

        var actualType = (AdbPairingPacketType)header[1];
        if (actualType != expectedType)
        {
            throw new ProtocolException($"ADB pairing packet type {actualType} was received while waiting for {expectedType}.");
        }

        var payloadLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(2)));
        if (payloadLength is <= 0 or > MaxPayloadSize)
        {
            throw new ProtocolException($"ADB pairing payload length {payloadLength} is invalid.");
        }

        var payload = new byte[payloadLength];
        await connection.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }
}
