namespace AdbSharp.Transport.Usb;

/// <summary>
/// Describes the release mechanism used for a USB lock owner.
/// </summary>
public enum UsbDeviceLockReleaseKind
{
    /// <summary>
    /// The lock was released by sending a host kill request to the local ADB server.
    /// </summary>
    GracefulAdbServerKill,

    /// <summary>
    /// The lock was released by terminating the owner process.
    /// </summary>
    ProcessTerminate
}
