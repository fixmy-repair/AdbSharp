namespace AdbSharp.Common.Devices;

/// <summary>
/// Describes the Android communication mode exposed by a connected device.
/// </summary>
public enum DeviceMode
{
    /// <summary>
    /// The device mode is unknown.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The device exposes the Android Debug Bridge protocol.
    /// </summary>
    Adb,

    /// <summary>
    /// The device exposes ADB while in recovery.
    /// </summary>
    Recovery,

    /// <summary>
    /// The device is in the bootloader.
    /// </summary>
    Bootloader,

    /// <summary>
    /// The device exposes bootloader Fastboot.
    /// </summary>
    Fastboot,

    /// <summary>
    /// The device exposes userspace Fastboot.
    /// </summary>
    Fastbootd,

    /// <summary>
    /// The device exposes ADB sideload.
    /// </summary>
    Sideload
}
