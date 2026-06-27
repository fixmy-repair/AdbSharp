using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using AdbSharp.Common;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using ZstdCompressionStream = ZstdSharp.CompressionStream;
using ZstdDecompressionStream = ZstdSharp.DecompressionStream;

namespace AdbSharp.Adb.Sync;

internal static class AdbSyncProtocol
{
    public const int DefaultMode = 0x81a4;
    private const int MaxChunk = 64 * 1024;
    private const int MaxPathLength = 1024;
    private const int MaxNameLength = 255;
    private const int StatV1Length = 16;
    private const int StatV2Length = 72;
    private const int DentV1Length = 20;
    private const int DentV2Length = 76;
    private const int SendSetupV2Length = 12;
    private const int ReceiveSetupV2Length = 8;
    private const int NoError = 0;
    private const int NoSuchFileOrDirectory = 2;

    public static async ValueTask<AdbFileStat?> StatAsync(AdbStream stream, string remotePath, bool followSymlinks, bool useStatV2, CancellationToken cancellationToken)
    {
        if (useStatV2)
        {
            await SendRequestAsync(stream, followSymlinks ? "STA2" : "LST2", remotePath, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            return await ReadStatV2Async(stream, remotePath, cancellationToken).ConfigureAwait(false);
        }

        await SendRequestAsync(stream, "STAT", remotePath, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        return await ReadStatV1Async(stream, remotePath, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<IReadOnlyList<AdbDirectoryEntry>> ListDirectoryAsync(AdbStream stream, string remotePath, bool useListV2, CancellationToken cancellationToken)
    {
        await SendRequestAsync(stream, useListV2 ? "LIS2" : "LIST", remotePath, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        return useListV2
            ? await ReadDirectoryV2Async(stream, remotePath, cancellationToken).ConfigureAwait(false)
            : await ReadDirectoryV1Async(stream, remotePath, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask PushAsync(AdbStream stream, Stream source, string remotePath, int mode, DateTimeOffset? modifiedTime, bool useSendV2, AdbSyncCompression compression, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        compression = useSendV2 ? compression : AdbSyncCompression.None;
        if (useSendV2)
        {
            Span<byte> setup = stackalloc byte[SendSetupV2Length];
            WriteAsciiId("SND2", setup);
            BinaryPrimitives.WriteUInt32LittleEndian(setup.Slice(4, 4), checked((uint)mode));
            BinaryPrimitives.WriteUInt32LittleEndian(setup.Slice(8, 4), checked((uint)compression));
            await SendRequestAsync(stream, "SND2", remotePath, setup.ToArray(), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SendRequestAsync(stream, "SEND", FormattableString.Invariant($"{remotePath},{mode}"), ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        }

        await using (var writer = new SyncDataWriteStream(stream, cancellationToken))
        await using (var compressed = CreateCompressionWriter(writer, compression))
        {
            await CopyToSyncWriterAsync(source, compressed, progress, cancellationToken).ConfigureAwait(false);
        }

        var done = new byte[8];
        WriteAsciiId("DONE", done);
        var timestamp = modifiedTime?.ToUnixTimeSeconds() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        BinaryPrimitives.WriteUInt32LittleEndian(done.AsSpan(4), checked((uint)timestamp));
        await stream.WriteAsync(done, cancellationToken).ConfigureAwait(false);
        await ReadStatusAsync(stream, remotePath, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask PullAsync(AdbStream stream, string remotePath, Stream destination, bool useRecvV2, AdbSyncCompression compression, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        compression = useRecvV2 ? compression : AdbSyncCompression.None;
        if (useRecvV2)
        {
            Span<byte> setup = stackalloc byte[ReceiveSetupV2Length];
            WriteAsciiId("RCV2", setup);
            BinaryPrimitives.WriteUInt32LittleEndian(setup.Slice(4, 4), checked((uint)compression));
            await SendRequestAsync(stream, "RCV2", remotePath, setup.ToArray(), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SendRequestAsync(stream, "RECV", remotePath, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        }

        await using var reader = new SyncDataReadStream(stream, remotePath, cancellationToken);
        if (compression == AdbSyncCompression.None)
        {
            await CopyFromSyncReaderAsync(reader, destination, progress, cancellationToken).ConfigureAwait(false);
            await reader.EnsureDoneAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using (var decompressed = CreateCompressionReader(reader, compression))
        {
            await CopyFromSyncReaderAsync(decompressed, destination, progress, cancellationToken).ConfigureAwait(false);
        }

        await reader.EnsureDoneAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask SendRequestAsync(AdbStream stream, string id, string path, ReadOnlyMemory<byte> setup, CancellationToken cancellationToken)
    {
        var pathBytes = Encoding.UTF8.GetBytes(path);
        if (pathBytes.Length > MaxPathLength)
        {
            throw new ArgumentOutOfRangeException(nameof(path), $"ADB sync paths must be at most {MaxPathLength} UTF-8 bytes.");
        }

        var length = 8 + pathBytes.Length + setup.Length;
        var header = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            WriteAsciiId(id, header);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), checked((uint)pathBytes.Length));
            pathBytes.CopyTo(header.AsSpan(8));
            setup.CopyTo(header.AsMemory(8 + pathBytes.Length));
            await stream.WriteAsync(header.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header);
        }
    }

    private static async ValueTask<AdbFileStat?> ReadStatV1Async(AdbStream stream, string remotePath, CancellationToken cancellationToken)
    {
        var buffer = new byte[StatV1Length];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        var id = ReadAsciiId(buffer);
        if (id != "STAT")
        {
            throw new ProtocolException($"Unexpected ADB sync stat response '{id}'.");
        }

        var mode = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4, 4));
        if (mode == 0)
        {
            return null;
        }

        var size = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(8, 4));
        var modified = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(12, 4));
        return new AdbFileStat(remotePath, mode, size, ToUnixTime(modified));
    }

    private static async ValueTask<AdbFileStat?> ReadStatV2Async(AdbStream stream, string remotePath, CancellationToken cancellationToken)
    {
        var buffer = new byte[StatV2Length];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        var id = ReadAsciiId(buffer);
        if (id is not ("STA2" or "LST2"))
        {
            throw new ProtocolException($"Unexpected ADB sync stat v2 response '{id}'.");
        }

        var error = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4, 4));
        if (error == NoSuchFileOrDirectory)
        {
            return null;
        }

        if (error != NoError)
        {
            throw new AdbSyncException($"ADB sync stat failed for '{remotePath}' with device error {error}.", remotePath, checked((int)error));
        }

        return ReadStatV2Body(remotePath, buffer, NoError);
    }

    private static async ValueTask<IReadOnlyList<AdbDirectoryEntry>> ReadDirectoryV1Async(AdbStream stream, string remotePath, CancellationToken cancellationToken)
    {
        var entries = new List<AdbDirectoryEntry>();
        var buffer = new byte[DentV1Length];
        while (true)
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            var id = ReadAsciiId(buffer);
            if (id == "DONE")
            {
                return entries;
            }

            if (id != "DENT")
            {
                throw new ProtocolException($"Unexpected ADB sync directory response '{id}'.");
            }

            var mode = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4, 4));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(8, 4));
            var modified = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(12, 4));
            var nameLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(16, 4));
            var name = await ReadNameAsync(stream, nameLength, cancellationToken).ConfigureAwait(false);
            entries.Add(new AdbDirectoryEntry(name, new AdbFileStat(CombineRemotePath(remotePath, name), mode, size, ToUnixTime(modified))));
        }
    }

    private static async ValueTask<IReadOnlyList<AdbDirectoryEntry>> ReadDirectoryV2Async(AdbStream stream, string remotePath, CancellationToken cancellationToken)
    {
        var entries = new List<AdbDirectoryEntry>();
        var buffer = new byte[DentV2Length];
        while (true)
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            var id = ReadAsciiId(buffer);
            if (id == "DONE")
            {
                return entries;
            }

            if (id != "DNT2")
            {
                throw new ProtocolException($"Unexpected ADB sync directory v2 response '{id}'.");
            }

            var nameLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(72, 4));
            var name = await ReadNameAsync(stream, nameLength, cancellationToken).ConfigureAwait(false);
            var error = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4, 4));
            entries.Add(new AdbDirectoryEntry(name, ReadStatV2Body(CombineRemotePath(remotePath, name), buffer, checked((int)error))));
        }
    }

    private static async ValueTask CopyToSyncWriterAsync(Stream source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaxChunk);
        try
        {
            long transferred = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, MaxChunk), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                transferred += read;
                progress?.Report(transferred);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async ValueTask CopyFromSyncReaderAsync(Stream source, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaxChunk);
        try
        {
            long transferred = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, MaxChunk), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                transferred += read;
                progress?.Report(transferred);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Stream CreateCompressionWriter(Stream destination, AdbSyncCompression compression)
    {
        return compression switch
        {
            AdbSyncCompression.None => destination,
            AdbSyncCompression.Brotli => new BrotliStream(destination, CompressionLevel.Fastest, leaveOpen: true),
            AdbSyncCompression.Lz4 => LZ4Frame.Encode(destination, LZ4Level.L00_FAST, extraMemory: 0, leaveOpen: true).AsStream(false),
            AdbSyncCompression.Zstd => new ZstdCompressionStream(destination, level: 1, bufferSize: MaxChunk, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(compression), compression, "Unsupported ADB sync compression type.")
        };
    }

    private static Stream CreateCompressionReader(Stream source, AdbSyncCompression compression)
    {
        return compression switch
        {
            AdbSyncCompression.None => source,
            AdbSyncCompression.Brotli => new BrotliStream(source, CompressionMode.Decompress, leaveOpen: true),
            AdbSyncCompression.Lz4 => LZ4Frame.Decode(source, extraMemory: 0, leaveOpen: true).AsStream(false, true),
            AdbSyncCompression.Zstd => new ZstdDecompressionStream(source, bufferSize: MaxChunk, checkEndOfStream: true, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(compression), compression, "Unsupported ADB sync compression type.")
        };
    }

    private static async ValueTask SendChunkAsync(AdbStream stream, string id, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var length = 8 + payload.Length;
        var header = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            WriteAsciiId(id, header);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), checked((uint)payload.Length));
            payload.CopyTo(header.AsMemory(8));
            await stream.WriteAsync(header.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header);
        }
    }

    private static async ValueTask ReadStatusAsync(AdbStream stream, string remotePath, CancellationToken cancellationToken)
    {
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var id = ReadAsciiId(header);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
        if (id == "OKAY")
        {
            return;
        }

        if (id == "FAIL")
        {
            throw await ReadFailAsync(stream, remotePath, length, cancellationToken).ConfigureAwait(false);
        }

        throw new ProtocolException($"Unexpected ADB sync status '{id}'.");
    }

    private static AdbFileStat ReadStatV2Body(string remotePath, ReadOnlySpan<byte> buffer, int error)
    {
        return new AdbFileStat(
            remotePath,
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(24, 4)),
            checked((long)BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(40, 8))),
            ToUnixTime(BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(56, 8))),
            ToUnixTime(BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(48, 8))),
            ToUnixTime(BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(64, 8))),
            BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(8, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(16, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(28, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(32, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(36, 4)),
            error);
    }

    private static async ValueTask<string> ReadNameAsync(AdbStream stream, uint length, CancellationToken cancellationToken)
    {
        if (length > MaxNameLength)
        {
            throw new ProtocolException($"ADB sync directory entry name length {length} exceeds {MaxNameLength}.");
        }

        var nameBytes = new byte[checked((int)length)];
        if (length != 0)
        {
            await stream.ReadExactlyAsync(nameBytes, cancellationToken).ConfigureAwait(false);
        }

        var name = Encoding.UTF8.GetString(nameBytes);
        if (name.Contains('/', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal))
        {
            throw new ProtocolException("ADB sync directory entry names cannot contain path separators.");
        }

        return name;
    }

    private static async ValueTask<AdbSyncException> ReadFailAsync(AdbStream stream, string remotePath, uint length, CancellationToken cancellationToken)
    {
        if (length > MaxChunk)
        {
            throw new ProtocolException($"ADB sync FAIL message length {length} exceeds {MaxChunk}.");
        }

        var message = new byte[checked((int)length)];
        if (length != 0)
        {
            await stream.ReadExactlyAsync(message, cancellationToken).ConfigureAwait(false);
        }

        var text = Encoding.UTF8.GetString(message);
        return new AdbSyncException(string.IsNullOrWhiteSpace(text) ? $"ADB sync failed for '{remotePath}'." : text, remotePath);
    }

    private static DateTimeOffset ToUnixTime(long seconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.UnixEpoch;
        }
    }

    private static string CombineRemotePath(string directory, string name)
    {
        return directory.EndsWith('/', StringComparison.Ordinal)
            ? string.Concat(directory, name)
            : string.Create(CultureInfo.InvariantCulture, $"{directory}/{name}");
    }

    private static void WriteAsciiId(string id, Span<byte> destination)
    {
        if (id.Length != 4)
        {
            throw new ArgumentException("ADB sync IDs must be four ASCII characters.", nameof(id));
        }

        Encoding.ASCII.GetBytes(id, destination);
    }

    private static string ReadAsciiId(ReadOnlySpan<byte> source)
    {
        return Encoding.ASCII.GetString(source[..4]);
    }

    private sealed class SyncDataWriteStream(AdbStream stream, CancellationToken operationCancellationToken) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer.AsMemory(offset, count), operationCancellationToken).AsTask().GetAwaiter().GetResult();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            WriteAsync(buffer.ToArray(), operationCancellationToken).AsTask().GetAwaiter().GetResult();
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return WriteAsync(buffer.AsMemory(offset, count), SelectCancellationToken(cancellationToken)).AsTask();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var effectiveCancellationToken = SelectCancellationToken(cancellationToken);
            while (!buffer.IsEmpty)
            {
                var count = Math.Min(buffer.Length, MaxChunk);
                await SendChunkAsync(stream, "DATA", buffer[..count], effectiveCancellationToken).ConfigureAwait(false);
                buffer = buffer[count..];
            }
        }

        private CancellationToken SelectCancellationToken(CancellationToken cancellationToken)
        {
            return cancellationToken.CanBeCanceled ? cancellationToken : operationCancellationToken;
        }
    }

    private sealed class SyncDataReadStream(AdbStream stream, string remotePath, CancellationToken operationCancellationToken) : Stream
    {
        private readonly byte[] header = new byte[8];
        private byte[]? buffer = ArrayPool<byte>.Shared.Rent(MaxChunk);
        private int offset;
        private int length;
        private bool done;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer.AsMemory(offset, count), operationCancellationToken).AsTask().GetAwaiter().GetResult();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), SelectCancellationToken(cancellationToken)).AsTask();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(buffer is null, this);
            if (destination.Length == 0)
            {
                return 0;
            }

            while (offset >= length)
            {
                if (!await ReadNextDataAsync(SelectCancellationToken(cancellationToken)).ConfigureAwait(false))
                {
                    return 0;
                }
            }

            var count = Math.Min(destination.Length, length - offset);
            buffer.AsMemory(offset, count).CopyTo(destination);
            offset += count;
            return count;
        }

        public async ValueTask EnsureDoneAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(buffer is null, this);
            if (done)
            {
                return;
            }

            if (offset < length)
            {
                throw new ProtocolException("ADB sync compressed stream completed before all DATA bytes were consumed.");
            }

            while (!done)
            {
                await ReadHeaderAsync(SelectCancellationToken(cancellationToken)).ConfigureAwait(false);
                var id = ReadAsciiId(header);
                var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
                switch (id)
                {
                    case "DONE":
                        done = true;
                        return;

                    case "DATA" when payloadLength == 0:
                        continue;

                    case "DATA":
                        throw new ProtocolException("ADB sync received DATA after the compressed stream ended.");

                    case "FAIL":
                        throw await ReadFailAsync(stream, remotePath, payloadLength, SelectCancellationToken(cancellationToken)).ConfigureAwait(false);

                    default:
                        throw new ProtocolException($"Unexpected ADB sync response '{id}'.");
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = null;
            }

            base.Dispose(disposing);
        }

        private async ValueTask<bool> ReadNextDataAsync(CancellationToken cancellationToken)
        {
            if (done)
            {
                return false;
            }

            while (true)
            {
                await ReadHeaderAsync(cancellationToken).ConfigureAwait(false);
                var id = ReadAsciiId(header);
                var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
                switch (id)
                {
                    case "DATA":
                        if (payloadLength > MaxChunk)
                        {
                            throw new ProtocolException($"ADB sync DATA length {payloadLength} exceeds {MaxChunk}.");
                        }

                        if (payloadLength == 0)
                        {
                            continue;
                        }

                        length = checked((int)payloadLength);
                        offset = 0;
                        await stream.ReadExactlyAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                        return true;

                    case "DONE":
                        done = true;
                        return false;

                    case "FAIL":
                        throw await ReadFailAsync(stream, remotePath, payloadLength, cancellationToken).ConfigureAwait(false);

                    default:
                        throw new ProtocolException($"Unexpected ADB sync response '{id}'.");
                }
            }
        }

        private ValueTask ReadHeaderAsync(CancellationToken cancellationToken)
        {
            return stream.ReadExactlyAsync(header, cancellationToken);
        }

        private CancellationToken SelectCancellationToken(CancellationToken cancellationToken)
        {
            return cancellationToken.CanBeCanceled ? cancellationToken : operationCancellationToken;
        }
    }
}
