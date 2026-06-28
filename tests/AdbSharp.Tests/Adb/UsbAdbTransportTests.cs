using System.Text;
using AdbSharp.Adb.Internal;
using AdbSharp.Common.Devices;
using AdbSharp.Protocol.Adb;
using AdbSharp.Tests.Transport;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Adb;

public sealed class UsbAdbTransportTests
{
    [Fact]
    public async Task ReadAsync_requests_packet_aligned_buffer_and_preserves_extra_bytes()
    {
        var scripted = new ScriptedUsbTransport(
            CreateAdbDescriptor(),
            new UsbEndpoint(0x81, UsbEndpointDirection.In, UsbTransferKind.Bulk, 8),
            new UsbEndpoint(0x01, UsbEndpointDirection.Out, UsbTransferKind.Bulk, 8));
        scripted.EnqueueRead("abcdefgh"u8);

        await using var transport = new UsbAdbTransport(scripted);

        var first = new byte[3];
        var firstBytesRead = await transport.ReadAsync(first);
        var second = new byte[5];
        var secondBytesRead = await transport.ReadAsync(second);

        Assert.Equal(3, firstBytesRead);
        Assert.Equal("abc"u8.ToArray(), first);
        Assert.Equal(5, secondBytesRead);
        Assert.Equal("defgh"u8.ToArray(), second);
        Assert.Equal(1, scripted.ReadCount);
    }

    [Fact]
    public async Task WriteAsync_preserves_separate_adb_header_and_payload_usb_writes()
    {
        var scripted = new ScriptedUsbTransport(CreateAdbDescriptor())
        {
            CoalesceAdbWrites = false
        };
        await using var transport = new UsbAdbTransport(scripted);
        var packet = AdbPacket.Create(AdbCommand.Connect, AdbConstants.Version, AdbConstants.MaxPayload, Encoding.UTF8.GetBytes("host::"));
        var encoded = new byte[AdbPacketCodec.GetEncodedLength(packet)];
        _ = AdbPacketCodec.Write(packet, encoded);

        await transport.WriteAsync(encoded);

        Assert.Collection(
            scripted.Writes,
            header =>
            {
                Assert.Equal(AdbConstants.HeaderLength, header.Length);
                Assert.Equal(encoded.AsSpan(0, AdbConstants.HeaderLength).ToArray(), header);
            },
            payload =>
            {
                Assert.Equal(encoded.Length - AdbConstants.HeaderLength, payload.Length);
                Assert.Equal(encoded.AsSpan(AdbConstants.HeaderLength).ToArray(), payload);
            });
    }

    private static UsbDeviceDescriptor CreateAdbDescriptor()
    {
        return new UsbDeviceDescriptor(
            "adb-transport",
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
