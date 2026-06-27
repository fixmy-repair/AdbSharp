namespace AdbSharp.Fastboot.Sparse;

/// <summary>
/// Describes a validated Android sparse image.
/// </summary>
/// <param name="Header">The parsed sparse image header.</param>
/// <param name="Chunks">The validated sparse chunks.</param>
/// <param name="EncodedLength">The sparse image byte length.</param>
/// <param name="ExpandedLength">The expanded raw image byte length.</param>
public sealed record SparseImageInfo(
    SparseImageHeader Header,
    IReadOnlyList<SparseImageChunk> Chunks,
    ulong EncodedLength,
    ulong ExpandedLength);
