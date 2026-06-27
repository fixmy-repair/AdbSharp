namespace AdbSharp.Fastboot.Sparse;

/// <summary>
/// Describes one Android sparse image chunk.
/// </summary>
/// <param name="Kind">The chunk kind.</param>
/// <param name="BlockCount">The number of output blocks represented by the chunk.</param>
/// <param name="EncodedDataLength">The encoded payload length after the chunk header.</param>
/// <param name="OutputLength">The expanded output length in bytes.</param>
public sealed record SparseImageChunk(
    SparseChunkKind Kind,
    uint BlockCount,
    ulong EncodedDataLength,
    ulong OutputLength);
