using AdbSharp.Common.Devices;
using AdbSharp.Fastboot;
using AdbSharp.Tests.Transport;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Transport;

public sealed class UsbTransportValidatorTests
{
    [Fact]
    public void ValidateEndpointPair_accepts_bulk_in_out_pair()
    {
        UsbTransportValidator.ValidateEndpointPair(
            new UsbEndpoint(0x81, UsbEndpointDirection.In, UsbTransferKind.Bulk, 512),
            new UsbEndpoint(0x01, UsbEndpointDirection.Out, UsbTransferKind.Bulk, 512));
    }

    [Fact]
    public void ValidateEndpointPair_rejects_wrong_in_direction()
    {
        var exception = Assert.Throws<UsbTransportException>(() => UsbTransportValidator.ValidateEndpointPair(
            new UsbEndpoint(0x01, UsbEndpointDirection.Out, UsbTransferKind.Bulk, 512),
            new UsbEndpoint(0x02, UsbEndpointDirection.Out, UsbTransferKind.Bulk, 512)));

        Assert.Equal(UsbTransportError.InvalidEndpoint, exception.Error);
    }

    [Fact]
    public void ValidateEndpointPair_rejects_endpoint_zero()
    {
        var exception = Assert.Throws<UsbTransportException>(() => UsbTransportValidator.ValidateEndpointPair(
            new UsbEndpoint(0x80, UsbEndpointDirection.In, UsbTransferKind.Bulk, 512),
            new UsbEndpoint(0x01, UsbEndpointDirection.Out, UsbTransferKind.Bulk, 512)));

        Assert.Equal(UsbTransportError.InvalidEndpoint, exception.Error);
    }

    [Fact]
    public void ValidateEndpointPair_rejects_zero_packet_size()
    {
        var exception = Assert.Throws<UsbTransportException>(() => UsbTransportValidator.ValidateEndpointPair(
            new UsbEndpoint(0x81, UsbEndpointDirection.In, UsbTransferKind.Bulk, 0),
            new UsbEndpoint(0x01, UsbEndpointDirection.Out, UsbTransferKind.Bulk, 512)));

        Assert.Equal(UsbTransportError.InvalidEndpoint, exception.Error);
    }

    [Fact]
    public async Task Fastboot_connect_disposes_transport_when_endpoint_metadata_is_invalid()
    {
        var descriptor = CreateDescriptor();
        var transport = new ScriptedUsbTransport(
            descriptor,
            bulkInEndpoint: new UsbEndpoint(0x01, UsbEndpointDirection.Out, UsbTransferKind.Bulk, 512));
        var device = new AndroidDevice(
            new DeviceIdentity("serial", "Google", "Pixel", "Pixel", descriptor.TransportId),
            DeviceMode.Fastboot,
            DeviceCapabilities.Empty with { SupportsFastboot = true },
            descriptor);

        var exception = await Assert.ThrowsAsync<UsbTransportException>(() => FastbootClient.ConnectAsync(
            device,
            new FastbootClientOptions { TransportFactory = transport }).AsTask());

        Assert.Equal(UsbTransportError.InvalidEndpoint, exception.Error);
        Assert.True(transport.Disposed);
    }

    private static UsbDeviceDescriptor CreateDescriptor()
    {
        return new UsbDeviceDescriptor("fastboot-transport", 0x18d1, 0x4ee0, 0, AndroidUsbClass.VendorSpecificClass, AndroidUsbClass.AndroidSubClass, AndroidUsbClass.FastbootProtocol, "serial", "Google", "Pixel");
    }
}
