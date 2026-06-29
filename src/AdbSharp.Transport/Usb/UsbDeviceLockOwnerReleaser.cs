using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace AdbSharp.Transport.Usb;

/// <summary>
/// Default USB lock owner releaser.
/// </summary>
public sealed class UsbDeviceLockOwnerReleaser : IUsbDeviceLockOwnerReleaser
{
    private const string AdbKillServerRequest = "host:kill";

    /// <summary>
    /// Gets the default lock owner releaser.
    /// </summary>
    public static IUsbDeviceLockOwnerReleaser Default { get; } = new UsbDeviceLockOwnerReleaser();

    /// <inheritdoc />
    public async ValueTask<UsbDeviceLockReleaseResult> ReleaseAsync(
        UsbDeviceLockOwner owner,
        UsbDeviceLockReleaseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        options ??= new UsbDeviceLockReleaseOptions();

        if (owner.SupportsGracefulAdbRelease && options.AllowGracefulAdbServerKill)
        {
            ValidateAdbServerOptions(options);
            var graceful = await TryKillAdbServerAsync(owner, options, cancellationToken).ConfigureAwait(false);
            if (graceful.Succeeded || !options.AllowProcessTermination)
            {
                return graceful;
            }
        }

        return options.AllowProcessTermination
            ? await TryTerminateProcessAsync(owner, cancellationToken).ConfigureAwait(false)
            : new UsbDeviceLockReleaseResult(
                owner,
                UsbDeviceLockReleaseKind.ProcessTerminate,
                Succeeded: false,
                "Process termination was not enabled by the caller.");
    }

    private static void ValidateAdbServerOptions(UsbDeviceLockReleaseOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AdbServerHost);
        if (options.AdbServerPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ADB server port must be in the range 1 through 65535.");
        }

        if (options.AdbServerTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ADB server timeout must be positive.");
        }
    }

    private static async ValueTask<UsbDeviceLockReleaseResult> TryKillAdbServerAsync(
        UsbDeviceLockOwner owner,
        UsbDeviceLockReleaseOptions options,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(options.AdbServerTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(options.AdbServerHost, options.AdbServerPort, linked.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            var request = EncodeAdbServerRequest(AdbKillServerRequest);
            await stream.WriteAsync(request, linked.Token).ConfigureAwait(false);
            await stream.FlushAsync(linked.Token).ConfigureAwait(false);

            var status = new byte[4];
            var read = await ReadAtMostAsync(stream, status, linked.Token).ConfigureAwait(false);
            if (read == 0 || Encoding.ASCII.GetString(status) == "OKAY")
            {
                return new UsbDeviceLockReleaseResult(
                    owner,
                    UsbDeviceLockReleaseKind.GracefulAdbServerKill,
                    Succeeded: true,
                    "ADB server accepted a graceful kill request.");
            }

            return new UsbDeviceLockReleaseResult(
                owner,
                UsbDeviceLockReleaseKind.GracefulAdbServerKill,
                Succeeded: false,
                $"ADB server returned '{Encoding.ASCII.GetString(status.AsSpan(0, read))}'.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            var message = ex is OperationCanceledException
                ? "Timed out while contacting the local ADB server."
                : ex.Message;
            return new UsbDeviceLockReleaseResult(
                owner,
                UsbDeviceLockReleaseKind.GracefulAdbServerKill,
                Succeeded: false,
                message,
                ex);
        }
    }

    private static async ValueTask<int> ReadAtMostAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static byte[] EncodeAdbServerRequest(string request)
    {
        var requestBytes = Encoding.UTF8.GetBytes(request);
        var prefix = requestBytes.Length.ToString("x4", CultureInfo.InvariantCulture);
        var buffer = new byte[4 + requestBytes.Length];
        Encoding.ASCII.GetBytes(prefix, buffer);
        requestBytes.CopyTo(buffer.AsSpan(4));
        return buffer;
    }

    private static async ValueTask<UsbDeviceLockReleaseResult> TryTerminateProcessAsync(
        UsbDeviceLockOwner owner,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(owner.ProcessId);
            process.Kill(entireProcessTree: false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new UsbDeviceLockReleaseResult(
                owner,
                UsbDeviceLockReleaseKind.ProcessTerminate,
                Succeeded: true,
                "The owner process was terminated.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new UsbDeviceLockReleaseResult(
                owner,
                UsbDeviceLockReleaseKind.ProcessTerminate,
                Succeeded: false,
                ex.Message,
                ex);
        }
    }
}
