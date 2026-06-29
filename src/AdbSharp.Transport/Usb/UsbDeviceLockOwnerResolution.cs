using AdbSharp.Common.Devices;

namespace AdbSharp.Transport.Usb;

/// <summary>
/// Result of resolving host processes that may hold a USB device interface lock.
/// </summary>
/// <param name="Descriptor">The descriptor that was inspected.</param>
/// <param name="Status">The resolution status.</param>
/// <param name="Owners">Resolved owner processes.</param>
/// <param name="DevicePath">The platform device path or node that was inspected.</param>
/// <param name="Message">A diagnostic message.</param>
public sealed record UsbDeviceLockOwnerResolution(
    UsbDeviceDescriptor Descriptor,
    UsbDeviceLockOwnerResolutionStatus Status,
    IReadOnlyList<UsbDeviceLockOwner> Owners,
    string? DevicePath,
    string? Message)
{
    /// <summary>
    /// Creates a resolution with no owner results.
    /// </summary>
    /// <param name="descriptor">The descriptor that was inspected.</param>
    /// <param name="status">The resolution status.</param>
    /// <param name="devicePath">The platform device path or node that was inspected.</param>
    /// <param name="message">A diagnostic message.</param>
    /// <returns>The resolution.</returns>
    public static UsbDeviceLockOwnerResolution Empty(
        UsbDeviceDescriptor descriptor,
        UsbDeviceLockOwnerResolutionStatus status,
        string? devicePath = null,
        string? message = null)
    {
        return new UsbDeviceLockOwnerResolution(descriptor, status, [], devicePath, message);
    }
}
