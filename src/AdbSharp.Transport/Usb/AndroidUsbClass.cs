using AdbSharp.Common.Devices;

namespace AdbSharp.Transport.Usb;

/// <summary>
/// Android USB interface classification helpers.
/// </summary>
public static class AndroidUsbClass
{
    /// <summary>
    /// Android vendor-specific interface class.
    /// </summary>
    public const byte VendorSpecificClass = 0xff;

    /// <summary>
    /// Android vendor-specific subclass used by ADB and Fastboot.
    /// </summary>
    public const byte AndroidSubClass = 0x42;

    /// <summary>
    /// ADB interface protocol value.
    /// </summary>
    public const byte AdbProtocol = 0x01;

    /// <summary>
    /// Fastboot interface protocol value.
    /// </summary>
    public const byte FastbootProtocol = 0x03;

    /// <summary>
    /// Classifies a USB descriptor into a device mode.
    /// </summary>
    /// <param name="descriptor">The USB descriptor.</param>
    /// <returns>The inferred device mode.</returns>
    public static DeviceMode Classify(UsbDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.InterfaceClass == VendorSpecificClass && descriptor.InterfaceSubClass == AndroidSubClass)
        {
            return descriptor.InterfaceProtocol switch
            {
                AdbProtocol => DeviceMode.Adb,
                FastbootProtocol => DeviceMode.Fastboot,
                _ => DeviceMode.Unknown
            };
        }

        return DeviceMode.Unknown;
    }
}
