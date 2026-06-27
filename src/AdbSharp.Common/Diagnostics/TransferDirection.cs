namespace AdbSharp.Common.Diagnostics;

/// <summary>
/// Describes transfer direction for progress notifications.
/// </summary>
public enum TransferDirection
{
    /// <summary>
    /// Data is being sent to the device.
    /// </summary>
    Upload,

    /// <summary>
    /// Data is being read from the device.
    /// </summary>
    Download
}
