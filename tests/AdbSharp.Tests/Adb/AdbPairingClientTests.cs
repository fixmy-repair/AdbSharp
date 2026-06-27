using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AdbSharp.Adb;
using AdbSharp.Adb.Internal;
using AdbSharp.Authentication.Adb;
using AdbSharp.Common;

namespace AdbSharp.Tests.Adb;

public sealed class AdbPairingClientTests
{
    [Fact]
    public async Task PairAsync_uses_builtin_backend_by_default()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var keyPair = AdbKeyPair.Create();

        var exception = await Assert.ThrowsAsync<DeviceConnectionException>(() =>
            AdbPairingClient.PairAsync("127.0.0.1", port, "123456", keyPair).AsTask());

        Assert.DoesNotContain("Backend", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PairAsync_exchanges_spake_messages_and_peer_info()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(cancellation.Token);
            await using var stream = socket.GetStream();

            var clientSpake = await ReadPacketAsync(stream, cancellation.Token);
            Assert.Equal(0, clientSpake.Type);
            Assert.Equal("client-spake", Encoding.UTF8.GetString(clientSpake.Payload));

            await WritePacketAsync(stream, 0, "server-spake"u8.ToArray(), cancellation.Token);

            var clientPeerInfo = await ReadPacketAsync(stream, cancellation.Token);
            Assert.Equal(1, clientPeerInfo.Type);
            Assert.Equal((byte)AdbPairingPeerInfoKind.AdbRsaPublicKey, clientPeerInfo.Payload[0]);
            Assert.Contains("AdbSharp", Encoding.ASCII.GetString(clientPeerInfo.Payload), StringComparison.Ordinal);

            await WritePacketAsync(
                stream,
                1,
                CreatePeerInfoWireBytes(AdbPairingPeerInfoKind.DeviceGuid, "device-guid-1"u8.ToArray()),
                cancellation.Token);
        }, cancellation.Token);

        using var keyPair = AdbKeyPair.Create();
        var result = await AdbPairingClient.PairAsync(
            "127.0.0.1",
            port,
            "123456",
            keyPair,
            new AdbPairingOptions { Backend = new PassthroughPairingBackend() },
            cancellation.Token);

        Assert.Equal("127.0.0.1", result.Host);
        Assert.Equal(port, result.Port);
        Assert.Equal(AdbPairingPeerInfoKind.DeviceGuid, result.PeerInfo.Kind);
        Assert.Equal("device-guid-1", Encoding.UTF8.GetString(result.PeerInfo.Data.Span));
        await server.WaitAsync(cancellation.Token);
    }

    [Fact]
    public void PairingAuth_exchanges_spake_messages_and_encrypted_peer_info()
    {
        var password = "123456-exported-tls-key-material"u8.ToArray();
        var client = AdbPairingAuth.CreateClient(password);
        var server = AdbPairingAuth.CreateServer(password);

        var clientMessage = client.CreateMessage();
        var serverMessage = server.CreateMessage();

        Assert.Equal(32, clientMessage.Length);
        Assert.Equal(32, serverMessage.Length);
        Assert.NotEqual(clientMessage, serverMessage);

        client.InitializeCipher(serverMessage);
        server.InitializeCipher(clientMessage);

        var clientPlaintext = CreatePeerInfoWireBytes(AdbPairingPeerInfoKind.AdbRsaPublicKey, "host-key"u8);
        var serverPlaintext = CreatePeerInfoWireBytes(AdbPairingPeerInfoKind.DeviceGuid, "device-guid"u8);

        var encryptedClientPeerInfo = client.Encrypt(clientPlaintext);
        var encryptedServerPeerInfo = server.Encrypt(serverPlaintext);

        Assert.Equal(clientPlaintext.Length + 16, encryptedClientPeerInfo.Length);
        Assert.Equal(serverPlaintext.Length + 16, encryptedServerPeerInfo.Length);
        Assert.Equal(clientPlaintext, server.Decrypt(encryptedClientPeerInfo));
        Assert.Equal(serverPlaintext, client.Decrypt(encryptedServerPeerInfo));
    }

    [Fact]
    public void PairingAuth_rejects_peer_info_when_passwords_do_not_match()
    {
        var client = AdbPairingAuth.CreateClient("123456-exported-tls-key-material"u8);
        var server = AdbPairingAuth.CreateServer("654321-exported-tls-key-material"u8);

        var clientMessage = client.CreateMessage();
        var serverMessage = server.CreateMessage();

        client.InitializeCipher(serverMessage);
        server.InitializeCipher(clientMessage);

        var encryptedClientPeerInfo = client.Encrypt(CreatePeerInfoWireBytes(AdbPairingPeerInfoKind.AdbRsaPublicKey, "host-key"u8));

        var exception = Assert.Throws<ProtocolException>(() => server.Decrypt(encryptedClientPeerInfo));
        Assert.Contains("authentication failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async ValueTask<(byte Type, byte[] Payload)> ReadPacketAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[6];
        await stream.ReadExactlyAsync(header, cancellationToken);
        Assert.Equal(1, header[0]);
        var payloadLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(2)));
        var payload = new byte[payloadLength];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return (header[1], payload);
    }

    private static async ValueTask WritePacketAsync(Stream stream, byte type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var header = new byte[6];
        header[0] = 1;
        header[1] = type;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(2), checked((uint)payload.Length));
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
    }

    private static byte[] CreatePeerInfoWireBytes(AdbPairingPeerInfoKind kind, ReadOnlySpan<byte> data)
    {
        var wireBytes = new byte[AdbPairingPeerInfo.WireSize];
        wireBytes[0] = (byte)kind;
        data.CopyTo(wireBytes.AsSpan(1));
        return wireBytes;
    }

    private sealed class PassthroughPairingBackend : IAdbPairingBackend
    {
        public ValueTask<IAdbPairingSecureConnection> ConnectAsync(
            Stream transport,
            X509Certificate2 clientCertificate,
            ReadOnlyMemory<byte> pairingCode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(clientCertificate.HasPrivateKey);
            Assert.Equal("123456", Encoding.UTF8.GetString(pairingCode.Span));
            return ValueTask.FromResult<IAdbPairingSecureConnection>(new PassthroughPairingConnection(transport));
        }
    }

    private sealed class PassthroughPairingConnection(Stream stream) : IAdbPairingSecureConnection
    {
        public ValueTask<byte[]> CreateClientMessageAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult("client-spake"u8.ToArray());
        }

        public ValueTask InitializeCipherAsync(ReadOnlyMemory<byte> peerMessage, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("server-spake", Encoding.UTF8.GetString(peerMessage.Span));
            return ValueTask.CompletedTask;
        }

        public ValueTask<byte[]> EncryptAsync(ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(plaintext.ToArray());
        }

        public ValueTask<byte[]> DecryptAsync(ReadOnlyMemory<byte> ciphertext, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ciphertext.ToArray());
        }

        public ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return stream.ReadExactlyAsync(buffer, cancellationToken);
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return stream.WriteAsync(buffer, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
