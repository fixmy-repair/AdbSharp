namespace AdbSharp.Adb;

/// <summary>
/// Represents an ADB file sync protocol or device-side filesystem failure.
/// </summary>
/// <param name="message">The failure message.</param>
/// <param name="remotePath">The remote path associated with the failure.</param>
/// <param name="deviceErrorCode">The device-side error code when reported.</param>
public sealed class AdbSyncException(string message, string? remotePath = null, int? deviceErrorCode = null) : IOException(message)
{
    /// <summary>
    /// Gets the remote path associated with the failure.
    /// </summary>
    public string? RemotePath { get; } = remotePath;

    /// <summary>
    /// Gets the device-side error code when reported.
    /// </summary>
    public int? DeviceErrorCode { get; } = deviceErrorCode;
}
