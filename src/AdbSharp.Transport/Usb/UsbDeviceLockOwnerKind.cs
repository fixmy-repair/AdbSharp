namespace AdbSharp.Transport.Usb;

/// <summary>
/// Classifies a detected USB lock owner.
/// </summary>
public enum UsbDeviceLockOwnerKind
{
    /// <summary>
    /// The process kind is unknown.
    /// </summary>
    Unknown,

    /// <summary>
    /// The process appears to be an ADB server.
    /// </summary>
    AdbServer,

    /// <summary>
    /// The process appears to be a Fastboot process.
    /// </summary>
    Fastboot,

    /// <summary>
    /// The process appears to be Android Studio or a related helper process.
    /// </summary>
    AndroidStudio,

    /// <summary>
    /// The process appears to be scrcpy.
    /// </summary>
    Scrcpy
}
