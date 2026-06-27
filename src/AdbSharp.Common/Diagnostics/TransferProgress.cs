namespace AdbSharp.Common.Diagnostics;

/// <summary>
/// Reports progress for a long-running USB transfer.
/// </summary>
/// <param name="Direction">The transfer direction.</param>
/// <param name="BytesTransferred">The number of bytes transferred so far.</param>
/// <param name="TotalBytes">The expected total length when known.</param>
/// <param name="Message">Optional contextual progress text.</param>
public sealed record TransferProgress(
    TransferDirection Direction,
    long BytesTransferred,
    long? TotalBytes,
    string? Message = null);
