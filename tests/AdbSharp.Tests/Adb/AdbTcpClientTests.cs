using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AdbSharp.Adb;
using AdbSharp.Authentication.Adb;
using AdbSharp.Common.Devices;
using AdbSharp.Protocol.Adb;

namespace AdbSharp.Tests.Adb;

public sealed class AdbTcpClientTests
{
    [Fact]
    public async Task ConnectTcpAsync_completes_adb_handshake_over_loopback()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(cancellation.Token);
            await using var stream = socket.GetStream();
            var packet = await ReadPacketAsync(stream, cancellation.Token);
            Assert.Equal(AdbCommand.Connect, packet.Header.Command);
            Assert.Contains("host::", Encoding.UTF8.GetString(packet.Payload.Span), StringComparison.Ordinal);

            await WritePacketAsync(stream, AdbPacket.Create(
                AdbCommand.Connect,
                AdbConstants.Version,
                AdbConstants.MaxPayload,
                "device::features=shell_v2"u8.ToArray()), cancellation.Token);
        }, cancellation.Token);

        await using var client = await AdbClient.ConnectTcpAsync("127.0.0.1", port, cancellationToken: cancellation.Token);

        Assert.Equal(DeviceMode.Adb, client.Device.Mode);
        Assert.Equal($"127.0.0.1:{port}", client.Device.Identity.SerialNumber);
        Assert.Equal($"tcp:127.0.0.1:{port}", client.Device.Identity.TransportId);
        await server.WaitAsync(cancellation.Token);
    }

    [Fact]
    public async Task ConnectWirelessAsync_routes_shell_streams_over_tcp()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(cancellation.Token);
            await using var stream = socket.GetStream();
            var connect = await ReadPacketAsync(stream, cancellation.Token);
            Assert.Equal(AdbCommand.Connect, connect.Header.Command);
            await WritePacketAsync(stream, AdbPacket.Create(
                AdbCommand.Connect,
                AdbConstants.Version,
                AdbConstants.MaxPayload,
                "device::features=shell_v2"u8.ToArray()), cancellation.Token);

            var open = await ReadPacketAsync(stream, cancellation.Token);
            Assert.Equal(AdbCommand.Open, open.Header.Command);
            Assert.Equal("shell:echo hi", Encoding.UTF8.GetString(open.Payload.Span).TrimEnd('\0'));

            await WritePacketAsync(stream, AdbPacket.Create(AdbCommand.Okay, 91, open.Header.Arg0, ReadOnlyMemory<byte>.Empty), cancellation.Token);
            await WritePacketAsync(stream, AdbPacket.Create(AdbCommand.Write, 91, open.Header.Arg0, "hi\n"u8.ToArray()), cancellation.Token);

            var okay = await ReadPacketAsync(stream, cancellation.Token);
            Assert.Equal(AdbCommand.Okay, okay.Header.Command);
            Assert.Equal(open.Header.Arg0, okay.Header.Arg0);
            Assert.Equal(91u, okay.Header.Arg1);

            await WritePacketAsync(stream, AdbPacket.Create(AdbCommand.Close, 91, open.Header.Arg0, ReadOnlyMemory<byte>.Empty), cancellation.Token);
        }, cancellation.Token);

        await using var client = await AdbClient.ConnectWirelessAsync("127.0.0.1", port, cancellationToken: cancellation.Token);
        var output = await client.ShellAsync("echo hi", cancellation.Token);

        Assert.Equal("hi\n", output);
        await server.WaitAsync(cancellation.Token);
    }

    [Fact]
    public async Task ConnectTcpAsync_upgrades_to_tls_when_peer_requests_stls()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(cancellation.Token);
            await using var stream = socket.GetStream();
            var connect = await ReadPacketAsync(stream, cancellation.Token);
            Assert.Equal(AdbCommand.Connect, connect.Header.Command);

            await WritePacketAsync(
                stream,
                AdbPacket.Create(AdbCommand.StartTls, AdbConstants.StartTlsVersion, 0, ReadOnlyMemory<byte>.Empty),
                cancellation.Token);

            var startTls = await ReadPacketAsync(stream, cancellation.Token);
            Assert.Equal(AdbCommand.StartTls, startTls.Header.Command);
            Assert.Equal(AdbConstants.StartTlsVersion, startTls.Header.Arg0);

            using var serverCertificate = CreateLoopbackServerCertificate();
            using var ssl = new SslStream(stream, false);
            await ssl.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = serverCertificate,
                    ClientCertificateRequired = true,
                    EnabledSslProtocols = SslProtocols.None,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    RemoteCertificateValidationCallback = static (_, certificate, _, _) => certificate is not null,
                },
                cancellation.Token);

            Assert.True(ssl.IsAuthenticated);
            Assert.True(ssl.RemoteCertificate is not null);
            await WritePacketAsync(
                ssl,
                AdbPacket.Create(
                    AdbCommand.Connect,
                    AdbConstants.Version,
                    AdbConstants.MaxPayload,
                    "device::features=shell_v2"u8.ToArray()),
                cancellation.Token);
        }, cancellation.Token);

        using var authenticator = new AdbRsaAuthenticator(AdbKeyPair.Create());
        await using var client = await ConnectAndRevealServerFailuresAsync(
            server,
            "127.0.0.1",
            port,
            new AdbClientOptions { Authenticator = authenticator },
            cancellation.Token);

        Assert.True(client.Device.Capabilities.SupportsTls);
        await server.WaitAsync(cancellation.Token);
    }

    [Fact]
    public async Task ConnectTcpAsync_rejects_invalid_port()
    {
        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            AdbClient.ConnectTcpAsync("127.0.0.1", 0).AsTask());

        Assert.Equal("port", exception.ParamName);
    }

    private static async ValueTask<AdbPacket> ReadPacketAsync(Stream stream, CancellationToken cancellationToken)
    {
        var headerBytes = new byte[AdbConstants.HeaderLength];
        await stream.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        var header = AdbPacketHeader.Read(headerBytes);
        var payload = header.PayloadLength == 0 ? [] : new byte[checked((int)header.PayloadLength)];
        if (payload.Length != 0)
        {
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        return new AdbPacket(header, payload);
    }

    private static ValueTask WritePacketAsync(Stream stream, AdbPacket packet, CancellationToken cancellationToken)
    {
        var bytes = new byte[AdbPacketCodec.GetEncodedLength(packet)];
        AdbPacketCodec.Write(packet, bytes);
        return stream.WriteAsync(bytes, cancellationToken);
    }

    private static async ValueTask<AdbClient> ConnectAndRevealServerFailuresAsync(
        Task server,
        string host,
        int port,
        AdbClientOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            return await AdbClient.ConnectTcpAsync(host, port, options, cancellationToken);
        }
        catch
        {
            if (server.IsFaulted)
            {
                await server.ConfigureAwait(false);
            }

            throw;
        }
    }

    private static X509Certificate2 CreateLoopbackServerCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            new X500DistinguishedName("CN=localhost"),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));
    }
}
