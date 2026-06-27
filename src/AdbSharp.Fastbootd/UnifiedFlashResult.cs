using AdbSharp.Common.Devices;
using AdbSharp.Fastboot.Sparse;

namespace AdbSharp.Fastbootd;

/// <summary>
/// Result returned by a unified Fastboot or Fastbootd flash operation.
/// </summary>
/// <param name="Device">The device used for the terminal flash command.</param>
/// <param name="Partition">The partition that was flashed.</param>
/// <param name="UsedFastbootd">Indicates whether userspace Fastboot handled the flash command.</param>
/// <param name="PartitionWasLogical">Indicates whether the partition was detected as logical.</param>
/// <param name="SparseImage">Sparse image metadata when a sparse image was flashed.</param>
public sealed record UnifiedFlashResult(
    AndroidDevice Device,
    string Partition,
    bool UsedFastbootd,
    bool PartitionWasLogical,
    SparseImageInfo? SparseImage = null);
