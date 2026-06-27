namespace AdbSharp.Common.Devices;

/// <summary>
/// Represents a detected Android device and the host transport descriptor needed to connect to it.
/// </summary>
/// <param name="Identity">The device identity.</param>
/// <param name="Mode">The detected communication mode.</param>
/// <param name="Capabilities">The capabilities discovered so far.</param>
/// <param name="Usb">The USB descriptor selected for device communication.</param>
public sealed record AndroidDevice(
    DeviceIdentity Identity,
    DeviceMode Mode,
    DeviceCapabilities Capabilities,
    UsbDeviceDescriptor Usb);
