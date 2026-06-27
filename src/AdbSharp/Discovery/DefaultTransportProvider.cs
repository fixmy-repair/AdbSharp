using AdbSharp.Platform.Linux.Usb;
using AdbSharp.Platform.Mac.Usb;
using AdbSharp.Platform.Windows.Usb;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Discovery;

/// <summary>
/// Registers the native platform USB providers included with the facade package.
/// </summary>
public static class DefaultTransportProvider
{
    private static int registered;

    /// <summary>
    /// Registers built-in Windows, Linux, and macOS transport providers once.
    /// </summary>
    public static void RegisterBuiltInTransports()
    {
        if (Interlocked.Exchange(ref registered, 1) == 1)
        {
            return;
        }

        Register(new WindowsUsbTransportFactory());
        Register(new LinuxUsbTransportFactory());
        Register(new MacUsbTransportFactory());
    }

    private static void Register(IUsbDeviceEnumerator provider)
    {
        UsbTransportRegistry.RegisterEnumerator(provider);
        if (provider is IUsbTransportFactory factory)
        {
            UsbTransportRegistry.RegisterFactory(factory);
        }
    }
}
