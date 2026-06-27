using System.Threading.Channels;
using System.Buffers;
using AdbSharp.Adb.Internal;

namespace AdbSharp.Adb;

/// <summary>
/// Represents a multiplexed ADB logical stream.
/// </summary>
public sealed class AdbStream : IAsyncDisposable
{
    private readonly AdbConnection connection;
    private readonly TaskCompletionSource<uint> remoteIdSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly Channel<ReadOnlyMemory<byte>> incoming = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private TaskCompletionSource writeReadySource = CreateWriteReadySource();
    private ReadOnlyMemory<byte> current;
    private int currentOffset;
    private Exception? terminalWriteException;
    private bool disposed;

    internal AdbStream(AdbConnection connection, uint localId)
    {
        this.connection = connection;
        LocalId = localId;
    }

    /// <summary>
    /// Gets the local stream identifier.
    /// </summary>
    public uint LocalId { get; }

    /// <summary>
    /// Gets the remote stream identifier after the stream is opened.
    /// </summary>
    public uint RemoteId => remoteIdSource.Task.IsCompletedSuccessfully ? remoteIdSource.Task.Result : 0;

    /// <summary>
    /// Reads stream data.
    /// </summary>
    /// <param name="buffer">The destination buffer.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of bytes read, or zero when the stream is closed.</returns>
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (buffer.Length == 0)
        {
            return 0;
        }

        while (currentOffset >= current.Length)
        {
            if (!await incoming.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return 0;
            }

            if (incoming.Reader.TryRead(out current))
            {
                currentOffset = 0;
            }
        }

        var count = Math.Min(buffer.Length, current.Length - currentOffset);
        current.Slice(currentOffset, count).CopyTo(buffer);
        currentOffset += count;
        return count;
    }

    /// <summary>
    /// Writes stream data.
    /// </summary>
    /// <param name="buffer">The source buffer.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfNotWritable();

        var remoteId = await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await writeReadySource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            ThrowIfNotWritable();
            writeReadySource = CreateWriteReadySource();
            await connection.SendWriteAsync(LocalId, remoteId, buffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <summary>
    /// Reads exactly the requested number of bytes unless the stream closes.
    /// </summary>
    /// <param name="buffer">The destination buffer.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="EndOfStreamException">The stream closed before enough bytes were read.</exception>
    public async ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("ADB stream closed before the requested bytes were read.");
            }

            offset += read;
        }
    }

    /// <summary>
    /// Reads the stream until it closes.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The collected bytes.</returns>
    public async ValueTask<byte[]> ReadToEndAsync(CancellationToken cancellationToken = default)
    {
        await using var buffer = new PooledMemoryStream();
        var rented = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                var read = await ReadAsync(rented, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return buffer.ToArray();
                }

                buffer.Write(rented.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        incoming.Writer.TryComplete();
        terminalWriteException = new ObjectDisposedException(nameof(AdbStream));
        writeReadySource.TrySetResult();
        await writeGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (RemoteId != 0)
            {
                await connection.SendCloseAsync(LocalId, RemoteId, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            writeGate.Release();
            writeGate.Dispose();
        }
    }

    internal async ValueTask<uint> WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(static state => ((TaskCompletionSource<uint>)state!).TrySetCanceled(), remoteIdSource);
        return await remoteIdSource.Task.ConfigureAwait(false);
    }

    internal void SetRemoteId(uint remoteId)
    {
        remoteIdSource.TrySetResult(remoteId);
    }

    internal void NotifyOkay()
    {
        writeReadySource.TrySetResult();
    }

    internal void Enqueue(ReadOnlyMemory<byte> payload)
    {
        incoming.Writer.TryWrite(payload);
    }

    internal void Complete()
    {
        incoming.Writer.TryComplete();
        terminalWriteException = new EndOfStreamException("ADB stream closed before it was ready for another write.");
        writeReadySource.TrySetResult();
    }

    internal void Abort(Exception exception)
    {
        terminalWriteException = exception;
        remoteIdSource.TrySetException(exception);
        incoming.Writer.TryComplete(exception);
        writeReadySource.TrySetResult();
    }

    private static TaskCompletionSource CreateWriteReadySource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void ThrowIfNotWritable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (terminalWriteException is not null)
        {
            throw terminalWriteException;
        }
    }
}
