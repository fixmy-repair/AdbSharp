using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace AdbSharp.Adb;

/// <summary>
/// Represents an active local TCP listener that forwards accepted sockets to a device-side ADB endpoint.
/// </summary>
public sealed class AdbPortForward : IAsyncDisposable
{
    private readonly AdbClient client;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource cancellation = new();
    private readonly ConcurrentBag<Task> connections = [];
    private readonly Task acceptTask;
    private bool disposed;

    internal AdbPortForward(AdbClient client, TcpListener listener, AdbSocketSpec remote)
    {
        this.client = client;
        this.listener = listener;
        Remote = remote;
        LocalEndPoint = (IPEndPoint)listener.LocalEndpoint;
        acceptTask = AcceptLoopAsync(cancellation.Token);
    }

    /// <summary>
    /// Gets the local TCP endpoint used by the listener.
    /// </summary>
    public IPEndPoint LocalEndPoint { get; }

    /// <summary>
    /// Gets the remote device-side socket specification.
    /// </summary>
    public AdbSocketSpec Remote { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await cancellation.CancelAsync().ConfigureAwait(false);
        listener.Stop();

        try
        {
            await acceptTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        foreach (var connection in connections)
        {
            try
            {
                await connection.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        cancellation.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var socket = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            connections.Add(HandleConnectionAsync(socket, cancellationToken));
        }
    }

    private async Task HandleConnectionAsync(TcpClient socket, CancellationToken cancellationToken)
    {
        using var acceptedSocket = socket;
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var stream = await client.OpenStreamAsync(Remote.Value, cancellationToken).ConfigureAwait(false);
        await using var network = acceptedSocket.GetStream();

        // Full-duplex forwarding requires simultaneous pumps; both tasks are awaited before stream disposal.
#pragma warning disable CA2025
        var socketToAdb = PumpSocketToAdbAsync(network, stream, connectionCts.Token);
        var adbToSocket = PumpAdbToSocketAsync(stream, network, connectionCts.Token);
#pragma warning restore CA2025
        await Task.WhenAny(socketToAdb, adbToSocket).ConfigureAwait(false);
        await connectionCts.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(socketToAdb, adbToSocket).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task PumpSocketToAdbAsync(NetworkStream source, AdbStream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PumpAdbToSocketAsync(AdbStream source, NetworkStream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
