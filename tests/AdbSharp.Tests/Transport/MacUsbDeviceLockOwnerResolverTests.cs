using AdbSharp.Common.Devices;
using AdbSharp.Platform.Mac.Usb;
using AdbSharp.Platform.Mac.Usb.Locking;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Transport;

public sealed class MacUsbDeviceLockOwnerResolverTests
{
    [Fact]
    public async Task ResolveAsync_reports_known_android_tooling_processes()
    {
        var descriptor = CreateDescriptor();
        var native = new FakeMacProcessNativeAdapter(
            new MacProcessSnapshot(300, "adb", "/opt/android/platform-tools/adb"),
            new MacProcessSnapshot(301, "scrcpy", "/usr/local/bin/scrcpy"),
            new MacProcessSnapshot(302, "studio", "/Applications/Android Studio.app/Contents/MacOS/studio"),
            new MacProcessSnapshot(303, "unrelated", "/usr/bin/unrelated"));
        var resolver = new MacUsbDeviceLockOwnerResolver(native, requireMac: false);

        var resolution = await resolver.ResolveAsync(descriptor);

        Assert.Equal(UsbDeviceLockOwnerResolutionStatus.Resolved, resolution.Status);
        Assert.Equal(3, resolution.Owners.Count);
        Assert.Contains(resolution.Owners, static owner =>
            owner.Kind == UsbDeviceLockOwnerKind.AdbServer && owner.Confidence == UsbDeviceLockOwnerConfidence.High);
        Assert.Contains(resolution.Owners, static owner =>
            owner.Kind == UsbDeviceLockOwnerKind.Scrcpy && owner.Confidence == UsbDeviceLockOwnerConfidence.Medium);
        Assert.Contains(resolution.Owners, static owner =>
            owner.Kind == UsbDeviceLockOwnerKind.AndroidStudio && owner.Confidence == UsbDeviceLockOwnerConfidence.Medium);
    }

    [Fact]
    public async Task ResolveAsync_returns_no_owner_for_unrelated_processes()
    {
        var descriptor = CreateDescriptor();
        var resolver = new MacUsbDeviceLockOwnerResolver(
            new FakeMacProcessNativeAdapter(new MacProcessSnapshot(304, "unrelated", "/usr/bin/unrelated")),
            requireMac: false);

        var resolution = await resolver.ResolveAsync(descriptor);

        Assert.Equal(UsbDeviceLockOwnerResolutionStatus.NoOwnerFound, resolution.Status);
        Assert.Empty(resolution.Owners);
    }

    private static UsbDeviceDescriptor CreateDescriptor()
    {
        var id = new MacUsbTransportId(0x12345678, 0, 0x18d1, 0x4ee7);
        return new UsbDeviceDescriptor(
            id.Encode(),
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

    private sealed class FakeMacProcessNativeAdapter(params MacProcessSnapshot[] processes) : IMacProcessNativeAdapter
    {
        public IReadOnlyList<MacProcessSnapshot> EnumerateProcesses(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return processes;
        }
    }
}
