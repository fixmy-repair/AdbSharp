using AdbSharp.Common.Devices;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Transport;

public sealed class UsbTransportLifecycleTests
{
    [Fact]
    public async Task AbortAsync_is_idempotent_and_makes_transport_unusable()
    {
        var transport = new ScriptedUsbTransport(CreateAdbDescriptor());

        await transport.AbortAsync();
        await transport.AbortAsync();

        Assert.True(transport.Disposed);
        Assert.Equal(1, transport.AbortCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await transport.ReadAsync(new byte[1]));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await transport.WriteAsync(new byte[1]));
    }

    [Fact]
    public async Task ResetAsync_aborts_transport_once()
    {
        var transport = new ScriptedUsbTransport(CreateAdbDescriptor());

        await transport.ResetAsync();
        await transport.ResetAsync();

        Assert.True(transport.Disposed);
        Assert.Equal(2, transport.ResetCount);
        Assert.Equal(1, transport.AbortCount);
    }

    private static UsbDeviceDescriptor CreateAdbDescriptor()
    {
        return new UsbDeviceDescriptor(
            "lifecycle-transport",
            0x18d1,
            0x4ee7,
            0,
            AndroidUsbClass.VendorSpecificClass,
            AndroidUsbClass.AndroidSubClass,
            AndroidUsbClass.AdbProtocol,
            "serial",
            "Google",
            "Pixel");
    }
}
