using AdbSharp.Common.Devices;
using AdbSharp.Discovery;
using AdbSharp.Transport.Usb;

namespace AdbSharp;

/// <summary>
/// Discovers Android devices using registered USB transports.
/// </summary>
public static class DeviceManager
{
    /// <summary>
    /// Finds attached Android devices.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The discovered devices.</returns>
    public static async ValueTask<IReadOnlyList<AndroidDevice>> FindAsync(CancellationToken cancellationToken = default)
    {
        DefaultTransportProvider.RegisterBuiltInTransports();

        var descriptors = await UsbTransportRegistry.FindAsync(cancellationToken).ConfigureAwait(false);
        var devices = new List<AndroidDevice>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            var mode = AndroidUsbClass.Classify(descriptor);
            if (mode == DeviceMode.Unknown)
            {
                continue;
            }

            var capabilities = mode switch
            {
                DeviceMode.Adb => DeviceCapabilities.Empty with { SupportsAdb = true, SupportsFileSync = true },
                DeviceMode.Fastboot => DeviceCapabilities.Empty with { SupportsFastboot = true },
                DeviceMode.Fastbootd => DeviceCapabilities.Empty with
                {
                    SupportsFastboot = true,
                    SupportsFastbootd = true,
                    SupportsDynamicPartitions = true,
                    SupportsLogicalPartitions = true
                },
                _ => DeviceCapabilities.Empty
            };

            devices.Add(new AndroidDevice(
                new DeviceIdentity(descriptor.SerialNumber, descriptor.Manufacturer, descriptor.Product, descriptor.Product, descriptor.TransportId),
                mode,
                capabilities,
                descriptor));
        }

        return devices;
    }
}
