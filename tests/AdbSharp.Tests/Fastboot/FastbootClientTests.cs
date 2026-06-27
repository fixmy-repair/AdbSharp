using System.Text;
using System.Buffers.Binary;
using AdbSharp.Common.Devices;
using AdbSharp.Fastboot;
using AdbSharp.Fastboot.Sparse;
using AdbSharp.Tests.Transport;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Fastboot;

public sealed class FastbootClientTests
{
    [Fact]
    public async Task GetVarAsync_returns_okay_payload()
    {
        var descriptor = new UsbDeviceDescriptor("fastboot-1", 0x18d1, 0x4ee0, 0, AndroidUsbClass.VendorSpecificClass, AndroidUsbClass.AndroidSubClass, AndroidUsbClass.FastbootProtocol, "serial", "Google", "Pixel");
        var transport = new ScriptedUsbTransport(descriptor);
        transport.EnqueueRead(Encoding.ASCII.GetBytes("OKAYpixel"));
        var device = new AndroidDevice(new DeviceIdentity("serial", "Google", "Pixel", "Pixel", descriptor.TransportId), DeviceMode.Fastboot, DeviceCapabilities.Empty with { SupportsFastboot = true }, descriptor);

        await using var client = await FastbootClient.ConnectAsync(device, new FastbootClientOptions { TransportFactory = transport });
        var product = await client.GetVarAsync("product");

        Assert.Equal("pixel", product);
        Assert.Equal("getvar:product", Encoding.ASCII.GetString(transport.Writes[0]));
    }

    [Fact]
    public async Task GetUInt64VarAsync_parses_hex_values()
    {
        var (client, transport) = CreateClient("OKAY0x00001000");
        await using var _ = client;

        var value = await client.GetUInt64VarAsync("max-download-size");

        Assert.Equal(4096UL, value);
        Assert.Equal("getvar:max-download-size", Encoding.ASCII.GetString(transport.Writes[0]));
    }

    [Fact]
    public async Task CreateLogicalPartitionAsync_sends_expected_command()
    {
        var (client, transport) = CreateClient("OKAY");
        await using var _ = client;

        await client.CreateLogicalPartitionAsync("system_a", 1234);

        Assert.Equal("create-logical-partition:system_a:1234", Encoding.ASCII.GetString(transport.Writes[0]));
    }

    [Fact]
    public async Task FetchPartitionAsync_streams_to_destination()
    {
        var descriptor = CreateDescriptor();
        var transport = new ScriptedUsbTransport(descriptor);
        var device = CreateDevice(descriptor);
        var payload = "abc"u8.ToArray();
        transport.OnWrite = bytes =>
        {
            var command = Encoding.ASCII.GetString(bytes);
            if (command.StartsWith("fetch:", StringComparison.Ordinal))
            {
                transport.EnqueueRead(Encoding.ASCII.GetBytes("DATA00000003"));
                _ = Task.Run(async () =>
                {
                    await Task.Delay(10);
                    transport.EnqueueRead(payload);
                    await Task.Delay(10);
                    transport.EnqueueRead(Encoding.ASCII.GetBytes("OKAY"));
                });
            }
        };

        await using var client = await FastbootClient.ConnectAsync(device, new FastbootClientOptions { TransportFactory = transport, ChunkSize = 2 });
        await using var destination = new MemoryStream();

        await client.FetchPartitionAsync("boot", destination, offset: 16, length: 3);

        Assert.Equal("abc", Encoding.UTF8.GetString(destination.ToArray()));
        Assert.Equal("fetch:boot:0x00000010:0x00000003", Encoding.ASCII.GetString(transport.Writes[0]));
    }

    [Fact]
    public async Task FlashSparsePartitionAsync_validates_and_flashes_sparse_image()
    {
        var descriptor = CreateDescriptor();
        var transport = new ScriptedUsbTransport(descriptor);
        var device = CreateDevice(descriptor);
        transport.OnWrite = bytes =>
        {
            var command = Encoding.ASCII.GetString(bytes);
            if (command == "download:00001034")
            {
                transport.EnqueueRead(Encoding.ASCII.GetBytes("DATA00001034"));
            }
            else if (bytes.Length == 0x1034)
            {
                transport.EnqueueRead(Encoding.ASCII.GetBytes("OKAY"));
            }
            else if (command == "flash:system")
            {
                transport.EnqueueRead(Encoding.ASCII.GetBytes("OKAY"));
            }
        };

        await using var client = await FastbootClient.ConnectAsync(device, new FastbootClientOptions { TransportFactory = transport });
        await using var sparse = new MemoryStream(CreateSparseImage());

        var info = await client.FlashSparsePartitionAsync("system", sparse);

        Assert.Equal(2u, info.Header.TotalChunks);
        Assert.Equal(0x1034UL, info.EncodedLength);
        Assert.Equal(0x2000UL, info.ExpandedLength);
        Assert.Contains(transport.Writes, write => Encoding.ASCII.GetString(write) == "flash:system");
    }

    [Fact]
    public async Task SparseImageReader_rejects_bad_chunk_size()
    {
        var sparse = CreateSparseImage();
        BinaryPrimitives.WriteUInt32LittleEndian(sparse.AsSpan(36, 4), 12);

        await using var stream = new MemoryStream(sparse);

        await Assert.ThrowsAsync<AdbSharp.Common.ProtocolException>(() => SparseImageReader.ReadInfoAsync(stream).AsTask());
    }

    private static (FastbootClient Client, ScriptedUsbTransport Transport) CreateClient(string response)
    {
        var descriptor = CreateDescriptor();
        var transport = new ScriptedUsbTransport(descriptor);
        transport.EnqueueRead(Encoding.ASCII.GetBytes(response));
        var device = CreateDevice(descriptor);
        var client = FastbootClient.ConnectAsync(device, new FastbootClientOptions { TransportFactory = transport }).AsTask().GetAwaiter().GetResult();
        return (client, transport);
    }

    private static UsbDeviceDescriptor CreateDescriptor()
    {
        return new UsbDeviceDescriptor("fastboot-1", 0x18d1, 0x4ee0, 0, AndroidUsbClass.VendorSpecificClass, AndroidUsbClass.AndroidSubClass, AndroidUsbClass.FastbootProtocol, "serial", "Google", "Pixel");
    }

    private static AndroidDevice CreateDevice(UsbDeviceDescriptor descriptor)
    {
        return new AndroidDevice(new DeviceIdentity("serial", "Google", "Pixel", "Pixel", descriptor.TransportId), DeviceMode.Fastboot, DeviceCapabilities.Empty with { SupportsFastboot = true }, descriptor);
    }

    private static byte[] CreateSparseImage()
    {
        var bytes = new byte[0x1034];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), SparseImageReader.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), 28);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10, 2), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), 4096);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28, 2), (ushort)SparseChunkKind.Raw);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36, 4), 4108);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4136, 2), (ushort)SparseChunkKind.DontCare);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4140, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4144, 4), 12);
        return bytes;
    }
}
