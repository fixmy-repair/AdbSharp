using AdbSharp.Common.Devices;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Transport;

public sealed class UsbTransportRegistryTests
{
    private static readonly object RegistryGate = new();

    [Fact]
    public async Task FindWithDiagnosticsAsync_returns_devices_and_failed_enumerator_issues()
    {
        lock (RegistryGate)
        {
            UsbTransportRegistry.Clear();
            UsbTransportRegistry.RegisterEnumerator(new ThrowingEnumerator());
            UsbTransportRegistry.RegisterEnumerator(new StaticEnumerator(CreateDescriptor()));
        }

        try
        {
            var result = await UsbTransportRegistry.FindWithDiagnosticsAsync();

            var descriptor = Assert.Single(result.Devices);
            Assert.Equal("diagnostic-transport", descriptor.TransportId);
            var issue = Assert.Single(result.Issues);
            Assert.Equal(UsbTransportError.PermissionDenied, issue.Error);
            Assert.Contains(nameof(ThrowingEnumerator), issue.EnumeratorName, StringComparison.Ordinal);
            Assert.IsType<UsbTransportException>(issue.Exception);
        }
        finally
        {
            lock (RegistryGate)
            {
                UsbTransportRegistry.Clear();
            }
        }
    }

    [Fact]
    public async Task FindAsync_keeps_fail_fast_behavior()
    {
        lock (RegistryGate)
        {
            UsbTransportRegistry.Clear();
            UsbTransportRegistry.RegisterEnumerator(new ThrowingEnumerator());
        }

        try
        {
            var exception = await Assert.ThrowsAsync<UsbTransportException>(() => UsbTransportRegistry.FindAsync().AsTask());

            Assert.Equal(UsbTransportError.PermissionDenied, exception.Error);
        }
        finally
        {
            lock (RegistryGate)
            {
                UsbTransportRegistry.Clear();
            }
        }
    }

    private static UsbDeviceDescriptor CreateDescriptor()
    {
        return new UsbDeviceDescriptor("diagnostic-transport", 0x18d1, 0x4ee7, 0, AndroidUsbClass.VendorSpecificClass, AndroidUsbClass.AndroidSubClass, AndroidUsbClass.AdbProtocol, "serial", "Google", "Pixel");
    }

    private sealed class ThrowingEnumerator : IUsbDeviceEnumerator
    {
        public ValueTask<IReadOnlyList<UsbDeviceDescriptor>> FindAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new UsbTransportException(UsbTransportError.PermissionDenied, "denied by test enumerator");
        }
    }

    private sealed class StaticEnumerator(UsbDeviceDescriptor descriptor) : IUsbDeviceEnumerator
    {
        public ValueTask<IReadOnlyList<UsbDeviceDescriptor>> FindAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<UsbDeviceDescriptor>>([descriptor]);
        }
    }
}
