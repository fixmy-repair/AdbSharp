namespace AdbSharp.Fastboot.Sparse;

/// <summary>
/// Android sparse image chunk kind.
/// </summary>
public enum SparseChunkKind : ushort
{
    /// <summary>
    /// Raw data chunk.
    /// </summary>
    Raw = 0xcac1,

    /// <summary>
    /// Fill pattern chunk.
    /// </summary>
    Fill = 0xcac2,

    /// <summary>
    /// Empty skip chunk.
    /// </summary>
    DontCare = 0xcac3,

    /// <summary>
    /// CRC chunk.
    /// </summary>
    Crc32 = 0xcac4
}
