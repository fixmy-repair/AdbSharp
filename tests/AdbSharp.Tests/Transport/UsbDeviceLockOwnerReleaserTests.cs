using System.Net;
using System.Net.Sockets;
using System.Text;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Transport;

public sealed class UsbDeviceLockOwnerReleaserTests
{
    [Fact]
    public async Task ReleaseAsync_sends_adb_host_kill_request_to_local_server()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var owner = new UsbDeviceLockOwner(
                123,
                "adb",
                "/usr/bin/adb",
                UsbDeviceLockOwnerKind.AdbServer,
                UsbDeviceLockOwnerConfidence.Exact,
                "test");
            var releaser = new UsbDeviceLockOwnerReleaser();

            var releaseTask = releaser.ReleaseAsync(
                owner,
                new UsbDeviceLockReleaseOptions
                {
                    AdbServerHost = IPAddress.Loopback.ToString(),
                    AdbServerPort = port,
                    AdbServerTimeout = TimeSpan.FromSeconds(2)
                },
                cancellation.Token).AsTask();
            using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
            await using var stream = client.GetStream();
            var request = await ReadKillRequestAsync(stream, cancellation.Token);
            await stream.WriteAsync("OKAY"u8.ToArray(), cancellation.Token);
            var result = await releaseTask;

            Assert.True(result.Succeeded);
            Assert.Equal(UsbDeviceLockReleaseKind.GracefulAdbServerKill, result.Kind);
            Assert.Equal("0009host:kill", request);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ReleaseAsync_does_not_terminate_process_without_explicit_opt_in()
    {
        var owner = new UsbDeviceLockOwner(
            int.MaxValue,
            "unknown",
            null,
            UsbDeviceLockOwnerKind.Unknown,
            UsbDeviceLockOwnerConfidence.Low,
            "test");
        var releaser = new UsbDeviceLockOwnerReleaser();

        var result = await releaser.ReleaseAsync(owner, new UsbDeviceLockReleaseOptions());

        Assert.False(result.Succeeded);
        Assert.Equal(UsbDeviceLockReleaseKind.ProcessTerminate, result.Kind);
        Assert.Contains("not enabled", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadKillRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[13];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return Encoding.ASCII.GetString(buffer.AsSpan(0, offset));
    }
}
