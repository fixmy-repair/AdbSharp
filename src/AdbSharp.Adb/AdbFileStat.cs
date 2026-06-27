namespace AdbSharp.Adb;

/// <summary>
/// Represents metadata returned by the ADB file sync protocol.
/// </summary>
/// <param name="Path">The device path for the file or directory entry.</param>
/// <param name="Mode">The Unix mode bits.</param>
/// <param name="Size">The file size in bytes.</param>
/// <param name="ModifiedTime">The modification time.</param>
/// <param name="AccessTime">The access time when reported by the device.</param>
/// <param name="ChangeTime">The metadata change time when reported by the device.</param>
/// <param name="DeviceId">The device identifier when reported by the device.</param>
/// <param name="Inode">The inode number when reported by the device.</param>
/// <param name="LinkCount">The hard-link count when reported by the device.</param>
/// <param name="UserId">The owner user identifier when reported by the device.</param>
/// <param name="GroupId">The owner group identifier when reported by the device.</param>
/// <param name="DeviceErrorCode">The device-side error code for directory entries that could not be inspected.</param>
public sealed record AdbFileStat(
    string Path,
    uint Mode,
    long Size,
    DateTimeOffset ModifiedTime,
    DateTimeOffset? AccessTime = null,
    DateTimeOffset? ChangeTime = null,
    ulong DeviceId = 0,
    ulong Inode = 0,
    uint LinkCount = 0,
    uint UserId = 0,
    uint GroupId = 0,
    int DeviceErrorCode = 0)
{
    /// <summary>
    /// Gets a value indicating whether the metadata describes an existing filesystem entry.
    /// </summary>
    public bool Exists => DeviceErrorCode == 0 && Mode != 0;

    /// <summary>
    /// Gets a value indicating whether the mode describes a directory.
    /// </summary>
    public bool IsDirectory => (Mode & 0xf000) == 0x4000;

    /// <summary>
    /// Gets a value indicating whether the mode describes a regular file.
    /// </summary>
    public bool IsRegularFile => (Mode & 0xf000) == 0x8000;

    /// <summary>
    /// Gets a value indicating whether the mode describes a symbolic link.
    /// </summary>
    public bool IsSymbolicLink => (Mode & 0xf000) == 0xa000;
}
