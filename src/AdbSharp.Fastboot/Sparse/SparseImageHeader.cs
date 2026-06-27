namespace AdbSharp.Fastboot.Sparse;

/// <summary>
/// Parsed Android sparse image header.
/// </summary>
/// <param name="BlockSize">The sparse block size.</param>
/// <param name="TotalBlocks">The expanded block count.</param>
/// <param name="TotalChunks">The number of chunks.</param>
/// <param name="ImageChecksum">The image checksum when present.</param>
public sealed record SparseImageHeader(uint BlockSize, uint TotalBlocks, uint TotalChunks, uint ImageChecksum);
