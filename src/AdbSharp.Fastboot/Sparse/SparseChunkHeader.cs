namespace AdbSharp.Fastboot.Sparse;

/// <summary>
/// Parsed Android sparse chunk header.
/// </summary>
/// <param name="Kind">The chunk kind.</param>
/// <param name="BlockCount">The output block count.</param>
/// <param name="TotalSize">The total encoded chunk size.</param>
public sealed record SparseChunkHeader(SparseChunkKind Kind, uint BlockCount, uint TotalSize);
