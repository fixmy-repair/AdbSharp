using System.Buffers.Binary;
using AdbSharp.Common;

namespace AdbSharp.Fastboot.Sparse;

/// <summary>
/// Reads Android sparse image metadata.
/// </summary>
public static class SparseImageReader
{
    private const ushort HeaderLength = 28;
    private const ushort ChunkHeaderLength = 12;

    /// <summary>
    /// Android sparse image magic value.
    /// </summary>
    public const uint Magic = 0xed26ff3a;

    /// <summary>
    /// Reads a sparse image header.
    /// </summary>
    /// <param name="source">The stream positioned at the sparse header.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The parsed sparse image header.</returns>
    public static async ValueTask<SparseImageHeader> ReadHeaderAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var header = new byte[HeaderLength];
        await source.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var details = ParseHeader(header);
        await SkipExactlyAsync(source, (ulong)(details.FileHeaderSize - HeaderLength), cancellationToken).ConfigureAwait(false);
        return details.Header;
    }

    /// <summary>
    /// Reads a sparse chunk header.
    /// </summary>
    /// <param name="source">The stream positioned at a chunk header.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The parsed chunk header.</returns>
    public static ValueTask<SparseChunkHeader> ReadChunkHeaderAsync(Stream source, CancellationToken cancellationToken = default)
    {
        return ReadChunkHeaderAsync(source, ChunkHeaderLength, cancellationToken);
    }

    /// <summary>
    /// Reads a sparse chunk header.
    /// </summary>
    /// <param name="source">The stream positioned at a chunk header.</param>
    /// <param name="chunkHeaderSize">The chunk header size reported by the sparse image header.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The parsed chunk header.</returns>
    public static async ValueTask<SparseChunkHeader> ReadChunkHeaderAsync(Stream source, ushort chunkHeaderSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (chunkHeaderSize < ChunkHeaderLength)
        {
            throw new ProtocolException("Unsupported Android sparse chunk header.");
        }

        var header = new byte[ChunkHeaderLength];
        await source.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        await SkipExactlyAsync(source, (ulong)(chunkHeaderSize - ChunkHeaderLength), cancellationToken).ConfigureAwait(false);
        var chunk = new SparseChunkHeader(
            (SparseChunkKind)BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4)));
        ValidateChunkHeader(chunk, chunkHeaderSize, 0, isStandaloneRead: true);
        return chunk;
    }

    /// <summary>
    /// Reads and validates an Android sparse image from the current stream position.
    /// </summary>
    /// <param name="source">The sparse image stream.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validated sparse image metadata.</returns>
    public static async ValueTask<SparseImageInfo> ReadInfoAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var headerBytes = new byte[HeaderLength];
        await source.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        var details = ParseHeader(headerBytes);
        await SkipExactlyAsync(source, (ulong)(details.FileHeaderSize - HeaderLength), cancellationToken).ConfigureAwait(false);

        var chunks = new List<SparseImageChunk>(checked((int)Math.Min(details.Header.TotalChunks, int.MaxValue)));
        ulong totalBlocks = 0;
        ulong encodedLength = details.FileHeaderSize;
        for (var index = 0u; index < details.Header.TotalChunks; index++)
        {
            var chunk = await ReadChunkHeaderAsync(source, details.ChunkHeaderSize, cancellationToken).ConfigureAwait(false);
            var dataLength = ValidateChunkHeader(chunk, details.ChunkHeaderSize, details.Header.BlockSize, isStandaloneRead: false);
            var outputLength = checked((ulong)chunk.BlockCount * details.Header.BlockSize);
            await SkipExactlyAsync(source, dataLength, cancellationToken).ConfigureAwait(false);

            totalBlocks += chunk.BlockCount;
            encodedLength += chunk.TotalSize;
            chunks.Add(new SparseImageChunk(chunk.Kind, chunk.BlockCount, dataLength, outputLength));
        }

        if (totalBlocks != details.Header.TotalBlocks)
        {
            throw new ProtocolException($"Sparse image describes {totalBlocks} output blocks, expected {details.Header.TotalBlocks}.");
        }

        return new SparseImageInfo(
            details.Header,
            chunks,
            encodedLength,
            checked((ulong)details.Header.BlockSize * details.Header.TotalBlocks));
    }

    private static HeaderDetails ParseHeader(ReadOnlySpan<byte> header)
    {
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(header[..4]);
        if (magic != Magic)
        {
            throw new ProtocolException("Stream is not an Android sparse image.");
        }

        var major = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4, 2));
        var fileHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(8, 2));
        var chunkHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(10, 2));
        var blockSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(12, 4));
        if (major != 1 || fileHeaderSize < HeaderLength || chunkHeaderSize < ChunkHeaderLength)
        {
            throw new ProtocolException("Unsupported Android sparse image header.");
        }

        if (blockSize == 0 || blockSize % 4 != 0)
        {
            throw new ProtocolException("Sparse image block size must be a non-zero multiple of four.");
        }

        return new HeaderDetails(
            new SparseImageHeader(
                blockSize,
                BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(16, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(20, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(24, 4))),
            fileHeaderSize,
            chunkHeaderSize);
    }

    private static ulong ValidateChunkHeader(SparseChunkHeader chunk, ushort chunkHeaderSize, uint blockSize, bool isStandaloneRead)
    {
        var dataLength = chunk.Kind switch
        {
            SparseChunkKind.Raw => isStandaloneRead ? 0UL : checked((ulong)chunk.BlockCount * blockSize),
            SparseChunkKind.Fill => 4UL,
            SparseChunkKind.DontCare => 0UL,
            SparseChunkKind.Crc32 => 4UL,
            _ => throw new ProtocolException($"Unsupported sparse chunk kind 0x{(ushort)chunk.Kind:x4}.")
        };

        if (!isStandaloneRead && chunk.TotalSize != dataLength + chunkHeaderSize)
        {
            throw new ProtocolException($"Sparse chunk size {chunk.TotalSize} does not match expected size {dataLength + chunkHeaderSize}.");
        }

        if (isStandaloneRead && chunk.TotalSize < chunkHeaderSize)
        {
            throw new ProtocolException("Sparse chunk total size is smaller than its header.");
        }

        return dataLength;
    }

    private static async ValueTask SkipExactlyAsync(Stream source, ulong count, CancellationToken cancellationToken)
    {
        if (count == 0)
        {
            return;
        }

        if (source.CanSeek)
        {
            source.Seek(checked((long)count), SeekOrigin.Current);
            return;
        }

        var buffer = new byte[(int)Math.Min(count, 81920)];
        var remaining = count;
        while (remaining != 0)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min((ulong)buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Sparse image ended before the expected number of bytes were skipped.");
            }

            remaining -= (uint)read;
        }
    }

    private sealed record HeaderDetails(SparseImageHeader Header, ushort FileHeaderSize, ushort ChunkHeaderSize);
}
