using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AdbSharp.Adb;
using AdbSharp.Authentication.Adb;
using AdbSharp.Common;

namespace AdbSharp.Tests.Adb;

public sealed class AdbPairingCompatibilityValidatorTests
{
    [Fact]
    public async Task ValidateAsync_reports_pairing_success_without_adb_smoke_check()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = RunPairingServerAsync(listener.AcceptTcpClientAsync(cancellation.Token).AsTask(), cancellation.Token);

        using var keyPair = AdbKeyPair.Create();
        var result = await AdbPairingCompatibilityValidator.ValidateAsync(
            new AdbPairingCompatibilityEndpoint("Google", "Pixel", "127.0.0.1", port, "123456"),
            keyPair,
            new AdbPairingCompatibilityOptions
            {
                PairingOptions = new AdbPairingOptions { Backend = new PassthroughPairingBackend() },
                VerifyAdbConnection = false
            },
            cancellation.Token);

        Assert.True(result.PairingSucceeded);
        Assert.False(result.AdbConnectionTested);
        Assert.True(result.IsCompatible);
        Assert.Equal(AdbPairingPeerInfoKind.DeviceGuid, result.PeerInfoKind);
        Assert.Equal("device-guid-compat", result.DeviceGuid);
        await server.WaitAsync(cancellation.Token);
    }

    [Fact]
    public async Task ValidateAsync_reports_adb_smoke_failure_after_pairing_success()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pairingListener = new TcpListener(IPAddress.Loopback, 0);
        pairingListener.Start();
        var pairingPort = ((IPEndPoint)pairingListener.LocalEndpoint).Port;
        var server = RunPairingServerAsync(pairingListener.AcceptTcpClientAsync(cancellation.Token).AsTask(), cancellation.Token);
        var closedAdbPort = ReserveClosedPort();

        using var keyPair = AdbKeyPair.Create();
        var result = await AdbPairingCompatibilityValidator.ValidateAsync(
            new AdbPairingCompatibilityEndpoint("Samsung", "Galaxy", "127.0.0.1", pairingPort, "123456")
            {
                AdbPort = closedAdbPort
            },
            keyPair,
            new AdbPairingCompatibilityOptions
            {
                PairingOptions = new AdbPairingOptions { Backend = new PassthroughPairingBackend() },
                VerifyAdbConnection = true
            },
            cancellation.Token);

        Assert.True(result.PairingSucceeded);
        Assert.True(result.AdbConnectionTested);
        Assert.False(result.AdbConnectionSucceeded);
        Assert.False(result.IsCompatible);
        Assert.NotNull(result.ErrorType);
        await server.WaitAsync(cancellation.Token);
    }

    [Fact]
    public async Task ValidateAsync_reports_pairing_endpoint_failure()
    {
        using var keyPair = AdbKeyPair.Create();
        var result = await AdbPairingCompatibilityValidator.ValidateAsync(
            new AdbPairingCompatibilityEndpoint("OnePlus", "Test", "127.0.0.1", ReserveClosedPort(), "123456"),
            keyPair,
            new AdbPairingCompatibilityOptions { VerifyAdbConnection = false });

        Assert.False(result.PairingSucceeded);
        Assert.False(result.IsCompatible);
        Assert.Equal(nameof(DeviceConnectionException), result.ErrorType);
    }

    private static async Task RunPairingServerAsync(Task<TcpClient> acceptTask, CancellationToken cancellationToken)
    {
        using var socket = await acceptTask.WaitAsync(cancellationToken);
        await using var stream = socket.GetStream();

        var clientSpake = await ReadPacketAsync(stream, cancellationToken);
        Assert.Equal(0, clientSpake.Type);
        Assert.Equal("client-spake", Encoding.UTF8.GetString(clientSpake.Payload));

        await WritePacketAsync(stream, 0, "server-spake"u8.ToArray(), cancellationToken);

        var clientPeerInfo = await ReadPacketAsync(stream, cancellationToken);
        Assert.Equal(1, clientPeerInfo.Type);
        Assert.Equal((byte)AdbPairingPeerInfoKind.AdbRsaPublicKey, clientPeerInfo.Payload[0]);

        await WritePacketAsync(
            stream,
            1,
            CreatePeerInfoWireBytes(AdbPairingPeerInfoKind.DeviceGuid, "device-guid-compat"u8),
            cancellationToken);
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

    private static int ReserveClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
