namespace AdbSharp.Adb.Sync;

internal enum AdbSyncCompression : uint
{
    None = 0,
    Brotli = 1,
    Lz4 = 2,
    Zstd = 4
}
