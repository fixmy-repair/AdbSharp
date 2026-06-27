using AdbSharp.Common;
using AdbSharp.Common.Diagnostics;
using AdbSharp.Protocol.Fastboot;
using AdbSharp.Transport.Usb;
using System.Buffers;

namespace AdbSharp.Fastboot.Internal;

internal sealed class FastbootConnection(IUsbTransport transport, FastbootClientOptions options) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public FastbootClientOptions Options { get; } = options;

    public async ValueTask<FastbootResponse> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteCommandAsync(command, cancellationToken).ConfigureAwait(false);
            return await ReadTerminalResponseAsync(command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DownloadAsync(Stream source, long length, IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (Options.ChunkSize <= 0)
        {
            throw new InvalidOperationException("Fastboot chunk size must be positive.");
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = FastbootProtocol.FormatDownloadCommand(length);
            await WriteCommandAsync(command, cancellationToken).ConfigureAwait(false);
            var response = await ReadDataResponseAsync(command, cancellationToken).ConfigureAwait(false);
            if (response.DataLength != length)
            {
                throw new ProtocolException($"Fastboot device accepted {response.DataLength} bytes, expected {length}.");
            }

            var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(Options.ChunkSize, 1024 * 1024));
            try
            {
                long transferred = 0;
                while (transferred < length)
                {
                    var toRead = (int)Math.Min(buffer.Length, length - transferred);
                    var read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("Source ended before the declared Fastboot download length.");
                    }

                    await transport.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    transferred += read;
                    progress?.Report(new TransferProgress(TransferDirection.Upload, transferred, length, "download"));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await ReadTerminalResponseAsync(command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<byte[]> UploadAsync(string command, IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        await using var destination = new MemoryStream();
        await UploadToAsync(command, destination, progress, cancellationToken).ConfigureAwait(false);
        return destination.ToArray();
    }

    public async ValueTask UploadToAsync(string command, Stream destination, IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(destination);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteCommandAsync(command, cancellationToken).ConfigureAwait(false);
            var response = await ReadDataResponseAsync(command, cancellationToken).ConfigureAwait(false);
            var length = response.DataLength ?? 0;
            var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(Options.ChunkSize, 1024 * 1024));
            try
            {
                long transferred = 0;
                while (transferred < length)
                {
                    var read = await transport.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - transferred)), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw new DeviceConnectionException("USB transport closed during Fastboot upload.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    transferred += read;
                    progress?.Report(new TransferProgress(TransferDirection.Download, transferred, length, command));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await ReadTerminalResponseAsync(command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        gate.Dispose();
        await transport.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask WriteCommandAsync(string command, CancellationToken cancellationToken)
    {
        var length = FastbootProtocol.GetEncodedCommandLength(command);
        var rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var written = FastbootProtocol.EncodeCommand(command, rented);
            await transport.WriteAsync(rented.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private async ValueTask<FastbootResponse> ReadDataResponseAsync(string command, CancellationToken cancellationToken)
    {
        while (true)
        {
            var response = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            switch (response.Kind)
            {
                case FastbootResponseKind.Info:
                case FastbootResponseKind.Text:
                    Options.InfoProgress?.Report(response.Payload);
                    break;
                case FastbootResponseKind.Fail:
                    throw new FastbootCommandException(command, response.Payload);
                case FastbootResponseKind.Data:
                    return response;
                default:
                    throw new ProtocolException($"Expected Fastboot DATA response for '{command}', got {response.Kind}.");
            }
        }
    }

    private async ValueTask<FastbootResponse> ReadTerminalResponseAsync(string command, CancellationToken cancellationToken)
    {
        while (true)
        {
            var response = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
            switch (response.Kind)
            {
                case FastbootResponseKind.Info:
                case FastbootResponseKind.Text:
                    Options.InfoProgress?.Report(response.Payload);
                    break;
                case FastbootResponseKind.Okay:
                    return response;
                case FastbootResponseKind.Fail:
                    throw new FastbootCommandException(command, response.Payload);
                default:
                    throw new ProtocolException($"Unexpected Fastboot response {response.Kind} for '{command}'.");
            }
        }
    }

    private async ValueTask<FastbootResponse> ReadResponseAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(FastbootProtocol.MaxResponseLength);
        try
        {
            var read = await transport.ReadAsync(buffer.AsMemory(0, FastbootProtocol.MaxResponseLength), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new DeviceConnectionException("USB transport closed while reading Fastboot response.");
            }

            return FastbootProtocol.ParseResponse(buffer.AsSpan(0, read));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
