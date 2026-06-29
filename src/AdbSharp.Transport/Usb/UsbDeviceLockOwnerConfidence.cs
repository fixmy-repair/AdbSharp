namespace AdbSharp.Transport.Usb;

/// <summary>
/// Describes how strongly a detected process is believed to own a USB device lock.
/// </summary>
public enum UsbDeviceLockOwnerConfidence
{
    /// <summary>
    /// The process was matched to the exact platform device path or device node.
    /// </summary>
    Exact,

    /// <summary>
    /// The process is highly likely to own the lock, but the platform could not prove an exact match.
    /// </summary>
    High,

    /// <summary>
    /// The process is plausibly related to the lock.
    /// </summary>
    Medium,

    /// <summary>
    /// The process is weakly related to the lock.
    /// </summary>
    Low
}
