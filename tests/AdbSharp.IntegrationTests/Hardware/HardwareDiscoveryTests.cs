using System.Text;
using AdbSharp;
using AdbSharp.Adb;
using AdbSharp.Common;
using AdbSharp.Common.Devices;
using AdbSharp.Fastboot;
using AdbSharp.Transport.Usb;

namespace AdbSharp.IntegrationTests.Hardware;

public sealed class HardwareDiscoveryTests
{
    [Fact]
    public async Task Finds_devices_when_hardware_tests_are_enabled()
    {
        if (!HardwareTestsEnabled())
        {
            return;
        }

        using var timeout = CreateTimeout();
        var devices = await DeviceManager.FindAsync(timeout.Token);

        Assert.NotNull(devices);
    }

    [Fact]
    public async Task Adb_shell_getprop_when_adb_device_is_attached()
    {
        if (!HardwareTestsEnabled())
        {
            return;
        }

        using var timeout = CreateTimeout();
        var device = await FindDeviceAsync(static device => device.Mode is DeviceMode.Adb or DeviceMode.Recovery, timeout.Token);
        if (device is null)
        {
            return;
        }

        try
        {
            await using var client = await AdbClient.ConnectAsync(device, cancellationToken: timeout.Token);
            var model = await client.GetPropertyAsync("ro.product.model", timeout.Token);
            Assert.False(string.IsNullOrWhiteSpace(model));
        }
        catch (DeviceConnectionException ex) when (ex.Message.Contains("authorization", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    [Fact]
    public async Task Adb_push_pull_small_file_roundtrip_when_adb_device_is_attached()
    {
        if (!HardwareTestsEnabled())
        {
            return;
        }

        using var timeout = CreateTimeout();
        var device = await FindDeviceAsync(static device => device.Mode is DeviceMode.Adb or DeviceMode.Recovery, timeout.Token);
        if (device is null)
        {
            return;
        }

        var remotePath = $"/data/local/tmp/adbsharp-{Guid.NewGuid():N}.txt";
        var payload = Encoding.UTF8.GetBytes("AdbSharp hardware roundtrip\n");
        try
        {
            await using var client = await AdbClient.ConnectAsync(device, cancellationToken: timeout.Token);
            await using var source = new MemoryStream(payload);
            await client.PushAsync(source, remotePath, cancellationToken: timeout.Token);
            await using var destination = new MemoryStream();
            await client.PullAsync(remotePath, destination, cancellationToken: timeout.Token);
            Assert.Equal(payload, destination.ToArray());
            _ = await client.ShellAsync($"rm -f {remotePath}", timeout.Token);
        }
        catch (DeviceConnectionException ex) when (ex.Message.Contains("authorization", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    [Fact]
    public async Task Fastboot_getvar_product_when_fastboot_device_is_attached()
    {
        if (!HardwareTestsEnabled())
        {
            return;
        }

        using var timeout = CreateTimeout();
        var device = await FindDeviceAsync(static device => device.Mode is DeviceMode.Fastboot or DeviceMode.Bootloader, timeout.Token);
        if (device is null)
        {
            return;
        }

        await using var client = await FastbootClient.ConnectAsync(device, cancellationToken: timeout.Token);
        var product = await client.GetVarAsync("product", timeout.Token);
        Assert.NotNull(product);
    }

    [Fact]
    public async Task Fastbootd_is_userspace_when_fastbootd_device_is_attached()
    {
        if (!HardwareTestsEnabled())
        {
            return;
        }

        using var timeout = CreateTimeout();
        var device = await FindDeviceAsync(static device => device.Mode is DeviceMode.Fastboot or DeviceMode.Fastbootd, timeout.Token);
        if (device is null)
        {
            return;
        }

        await using var client = await FastbootClient.ConnectAsync(device, cancellationToken: timeout.Token);
        try
        {
            var isUserspace = await client.GetVarAsync("is-userspace", timeout.Token);
            Assert.Equal("yes", isUserspace, StringComparer.OrdinalIgnoreCase);
        }
        catch (FastbootCommandException)
        {
        }
    }

    [Fact]
    public async Task Opened_transport_has_valid_bulk_endpoint_metadata_when_device_is_attached()
    {
        if (!HardwareTestsEnabled())
        {
            return;
        }

        using var timeout = CreateTimeout();
        var device = await FindDeviceAsync(static device => device.Mode is DeviceMode.Adb or DeviceMode.Fastboot or DeviceMode.Fastbootd or DeviceMode.Bootloader, timeout.Token);
        if (device is null)
        {
            return;
        }

        var factory = UsbTransportRegistry.FindFactory(device.Usb);
        await using var transport = await factory.OpenAsync(device.Usb, timeout.Token);

        UsbTransportValidator.ValidateOpenedTransport(transport);
        Assert.Equal(device.Usb.TransportId, transport.Descriptor.TransportId);
    }

    private static bool HardwareTestsEnabled()
    {
        return string.Equals(Environment.GetEnvironmentVariable("ADBSHARP_HARDWARE_TESTS"), "1", StringComparison.Ordinal);
    }

    private static CancellationTokenSource CreateTimeout()
    {
        return new CancellationTokenSource(TimeSpan.FromSeconds(20));
    }

    private static async ValueTask<AndroidDevice?> FindDeviceAsync(Func<AndroidDevice, bool> predicate, CancellationToken cancellationToken)
    {
        var devices = await DeviceManager.FindAsync(cancellationToken);
        return devices.FirstOrDefault(predicate);
    }
}
