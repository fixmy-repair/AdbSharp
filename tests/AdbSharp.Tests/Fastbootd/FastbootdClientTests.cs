using System.Text;
using AdbSharp.Common.Devices;
using AdbSharp.Fastboot;
using AdbSharp.Fastbootd;
using AdbSharp.Tests.Transport;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Fastbootd;

public sealed class FastbootdClientTests
{
    [Fact]
    public async Task ConnectAsync_detects_userspace_capabilities()
    {
        var descriptor = CreateDescriptor();
        var transport = new ScriptedUsbTransport(descriptor);
        transport.OnWrite = bytes =>
        {
            switch (Encoding.ASCII.GetString(bytes))
            {
                case "getvar:is-userspace":
                    transport.EnqueueRead("OKAYyes"u8);
                    break;
                case "getvar:super-partition-name":
                    transport.EnqueueRead("OKAYsuper"u8);
                    break;
                case "getvar:snapshot-update-status":
                    transport.EnqueueRead("OKAYnone"u8);
                    break;
            }
        };

        var device = CreateDevice(descriptor, DeviceMode.Fastbootd);
        await using var client = await FastbootdClient.ConnectAsync(device, new FastbootdClientOptions { FastbootOptions = new FastbootClientOptions { TransportFactory = transport } });

        Assert.True(client.Capabilities.IsUserspace);
        Assert.True(client.Capabilities.SupportsDynamicPartitions);
        Assert.True(client.Capabilities.SupportsLogicalPartitions);
        Assert.True(client.Capabilities.SupportsSnapshotUpdates);
        Assert.Equal("super", client.Capabilities.SuperPartitionName);
        Assert.Equal(DeviceMode.Fastbootd, client.Device.Mode);
        Assert.True(client.Device.Capabilities.SupportsFastbootd);
        Assert.True(client.Device.Capabilities.SupportsLogicalPartitions);
    }

    [Fact]
    public async Task ConnectAsync_transitions_from_bootloader_fastboot()
    {
        var descriptor = CreateDescriptor();
        var transport = new ScriptedUsbTransport(descriptor);
        var userspaceProbeCount = 0;
        var rediscoveryCount = 0;
        transport.OnWrite = bytes =>
        {
            switch (Encoding.ASCII.GetString(bytes))
            {
                case "getvar:is-userspace":
                    userspaceProbeCount++;
                    transport.EnqueueRead(userspaceProbeCount == 1 ? "FAILunknown"u8 : "OKAYyes"u8);
                    break;
                case "reboot-fastboot":
                    transport.EnqueueRead("OKAY"u8);
                    break;
                case "getvar:super-partition-name":
                    transport.EnqueueRead("OKAYsuper"u8);
                    break;
                case "getvar:snapshot-update-status":
                    transport.EnqueueRead("FAILunknown"u8);
                    break;
            }
        };

        var device = CreateDevice(descriptor, DeviceMode.Fastboot);
        var options = new FastbootdClientOptions
        {
            FastbootOptions = new FastbootClientOptions { TransportFactory = transport },
            RediscoverFastbootdAsync = (original, _) =>
            {
                rediscoveryCount++;
                return ValueTask.FromResult<AndroidDevice?>(original);
            }
        };

        await using var client = await FastbootdClient.ConnectAsync(device, options);

        Assert.Equal(1, rediscoveryCount);
        Assert.Equal(DeviceMode.Fastbootd, client.Device.Mode);
        Assert.True(client.Capabilities.IsUserspace);
        Assert.Contains(transport.Writes, static write => Encoding.ASCII.GetString(write) == "reboot-fastboot");
    }

    [Fact]
    public async Task CreateLogicalPartitionAsync_delegates_to_fastboot_command()
    {
        var descriptor = CreateDescriptor();
        var transport = new ScriptedUsbTransport(descriptor);
        transport.OnWrite = bytes =>
        {
            switch (Encoding.ASCII.GetString(bytes))
            {
                case "getvar:is-userspace":
                    transport.EnqueueRead("OKAYyes"u8);
                    break;
                case "getvar:super-partition-name":
                    transport.EnqueueRead("OKAYsuper"u8);
                    break;
                case "getvar:snapshot-update-status":
                case "create-logical-partition:system_a:4096":
                    transport.EnqueueRead("OKAY"u8);
                    break;
            }
        };

        var device = CreateDevice(descriptor, DeviceMode.Fastbootd);
        await using var client = await FastbootdClient.ConnectAsync(device, new FastbootdClientOptions { FastbootOptions = new FastbootClientOptions { TransportFactory = transport } });

        await client.CreateLogicalPartitionAsync("system_a", 4096);

        Assert.Contains(transport.Writes, static write => Encoding.ASCII.GetString(write) == "create-logical-partition:system_a:4096");
    }

    [Fact]
    public async Task UnifiedFlashPartitionAsync_flashes_nonlogical_partition_in_bootloader_fastboot()
    {
        var descriptor = CreateDescriptor();
        var transport = new ScriptedUsbTransport(descriptor);
        transport.OnWrite = bytes =>
        {
            var text = Encoding.ASCII.GetString(bytes);
            switch (text)
            {
                case "getvar:is-userspace":
                case "getvar:is-logical:boot":
                    transport.EnqueueRead("FAILunknown"u8);
                    break;
                case "download:00000003":
                    transport.EnqueueRead("DATA00000003"u8);
                    break;
                case "flash:boot":
                    transport.EnqueueRead("OKAY"u8);
                    break;
                default:
                    if (bytes.Length == 3)
                    {
                        transport.EnqueueRead("OKAY"u8);
                    }

                    break;
            }
        };

        var device = CreateDevice(descriptor, DeviceMode.Fastboot);
        await using var image = new MemoryStream("abc"u8.ToArray());

        var result = await UnifiedFastbootFlasher.FlashPartitionAsync(
            device,
            "boot",
            image,
            options: new FastbootdClientOptions { FastbootOptions = new FastbootClientOptions { TransportFactory = transport } });

        Assert.False(result.UsedFastbootd);
        Assert.False(result.PartitionWasLogical);
        Assert.DoesNotContain(transport.Writes, static write => Encoding.ASCII.GetString(write) == "reboot-fastboot");
        Assert.Contains(transport.Writes, static write => Encoding.ASCII.GetString(write) == "flash:boot");
    }

    [Fact]
    public async Task UnifiedFlashPartitionAsync_transitions_for_logical_partition()
    {
        var descriptor = CreateDescriptor();
        var transport = new ScriptedUsbTransport(descriptor);
        var userspaceProbeCount = 0;
        var rediscoveryCount = 0;
        transport.OnWrite = bytes =>
        {
            var text = Encoding.ASCII.GetString(bytes);
            switch (text)
            {
                case "getvar:is-userspace":
                    userspaceProbeCount++;
                    transport.EnqueueRead(userspaceProbeCount <= 2 ? "FAILunknown"u8 : "OKAYyes"u8);
                    break;
                case "getvar:is-logical:system_a":
                    transport.EnqueueRead("OKAYyes"u8);
                    break;
                case "reboot-fastboot":
                    transport.EnqueueRead("OKAY"u8);
                    break;
                case "getvar:super-partition-name":
                    transport.EnqueueRead("OKAYsuper"u8);
                    break;
                case "getvar:snapshot-update-status":
                    transport.EnqueueRead("FAILunknown"u8);
                    break;
                case "download:00000003":
                    transport.EnqueueRead("DATA00000003"u8);
                    break;
                case "flash:system_a":
                    transport.EnqueueRead("OKAY"u8);
                    break;
                default:
                    if (bytes.Length == 3)
                    {
                        transport.EnqueueRead("OKAY"u8);
                    }

                    break;
            }
        };

        var device = CreateDevice(descriptor, DeviceMode.Fastboot);
        var options = new FastbootdClientOptions
        {
            FastbootOptions = new FastbootClientOptions { TransportFactory = transport },
            RediscoverFastbootdAsync = (original, _) =>
            {
                rediscoveryCount++;
                return ValueTask.FromResult<AndroidDevice?>(original);
            }
        };
        await using var image = new MemoryStream("abc"u8.ToArray());

        var result = await UnifiedFastbootFlasher.FlashPartitionAsync(device, "system_a", image, options: options);

        Assert.True(result.UsedFastbootd);
        Assert.True(result.PartitionWasLogical);
        Assert.Equal(1, rediscoveryCount);
        Assert.Equal(DeviceMode.Fastbootd, result.Device.Mode);
        Assert.Contains(transport.Writes, static write => Encoding.ASCII.GetString(write) == "reboot-fastboot");
        Assert.Contains(transport.Writes, static write => Encoding.ASCII.GetString(write) == "flash:system_a");
    }

    private static UsbDeviceDescriptor CreateDescriptor()
    {
        return new UsbDeviceDescriptor("fastbootd-1", 0x18d1, 0x4ee0, 0, AndroidUsbClass.VendorSpecificClass, AndroidUsbClass.AndroidSubClass, AndroidUsbClass.FastbootProtocol, "serial", "Google", "Pixel");
    }

    private static AndroidDevice CreateDevice(UsbDeviceDescriptor descriptor, DeviceMode mode)
    {
        return new AndroidDevice(
            new DeviceIdentity("serial", "Google", "Pixel", "Pixel", descriptor.TransportId),
            mode,
            DeviceCapabilities.Empty with
            {
                SupportsFastboot = true,
                SupportsFastbootd = mode == DeviceMode.Fastbootd
            },
            descriptor);
    }
}
