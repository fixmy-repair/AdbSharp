using AdbSharp.Common.Devices;
using AdbSharp.Platform.Windows.Usb;
using AdbSharp.Platform.Windows.Usb.Locking;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Transport;

public sealed class WindowsUsbDeviceLockOwnerResolverTests
{
    [Fact]
    public async Task ResolveAsync_maps_exact_handle_owner()
    {
        const string devicePath = @"\\?\usb#vid_18d1&pid_4ee7#serial#{f72fe0d4-cbcb-407d-8814-9ed673d0dd6b}";
        var descriptor = CreateDescriptor(new WindowsUsbTransportId(devicePath).Encode());
        var native = new FakeWindowsLockOwnerNativeAdapter(new WindowsUsbLockOwnerSnapshot(
            [new WindowsUsbLockOwnerCandidate(100, "adb", @"C:\Android\platform-tools\adb", devicePath, UsbDeviceLockOwnerConfidence.Exact)],
            IsPartial: false,
            null));
        var resolver = new WindowsUsbDeviceLockOwnerResolver(native, requireWindows: false);

        var resolution = await resolver.ResolveAsync(descriptor);

        Assert.Equal(UsbDeviceLockOwnerResolutionStatus.Resolved, resolution.Status);
        var owner = Assert.Single(resolution.Owners);
        Assert.Equal(100, owner.ProcessId);
        Assert.Equal(UsbDeviceLockOwnerKind.AdbServer, owner.Kind);
        Assert.Equal(UsbDeviceLockOwnerConfidence.Exact, owner.Confidence);
        Assert.Equal(devicePath, resolution.DevicePath);
    }

    [Fact]
    public async Task ResolveAsync_reports_partial_when_handles_cannot_be_inspected()
    {
        const string devicePath = @"\\?\usb#vid_18d1&pid_4ee7#serial#{f72fe0d4-cbcb-407d-8814-9ed673d0dd6b}";
        var descriptor = CreateDescriptor(new WindowsUsbTransportId(devicePath).Encode());
        var resolver = new WindowsUsbDeviceLockOwnerResolver(
            new FakeWindowsLockOwnerNativeAdapter(new WindowsUsbLockOwnerSnapshot([], IsPartial: true, "partial")),
            requireWindows: false);

        var resolution = await resolver.ResolveAsync(descriptor);

        Assert.Equal(UsbDeviceLockOwnerResolutionStatus.Partial, resolution.Status);
        Assert.Equal("partial", resolution.Message);
    }

    [Fact]
    public async Task ResolveAsync_reports_access_denied()
    {
        const string devicePath = @"\\?\usb#vid_18d1&pid_4ee7#serial#{f72fe0d4-cbcb-407d-8814-9ed673d0dd6b}";
        var descriptor = CreateDescriptor(new WindowsUsbTransportId(devicePath).Encode());
        var resolver = new WindowsUsbDeviceLockOwnerResolver(new ThrowingWindowsLockOwnerNativeAdapter(), requireWindows: false);

        var resolution = await resolver.ResolveAsync(descriptor);

        Assert.Equal(UsbDeviceLockOwnerResolutionStatus.AccessDenied, resolution.Status);
    }

    private static UsbDeviceDescriptor CreateDescriptor(string transportId)
    {
        return new UsbDeviceDescriptor(
            transportId,
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

    private sealed class FakeWindowsLockOwnerNativeAdapter(WindowsUsbLockOwnerSnapshot snapshot) : IWindowsUsbLockOwnerNativeAdapter
    {
        public WindowsUsbLockOwnerSnapshot FindOwners(string devicePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return snapshot;
        }
    }

    private sealed class ThrowingWindowsLockOwnerNativeAdapter : IWindowsUsbLockOwnerNativeAdapter
    {
        public WindowsUsbLockOwnerSnapshot FindOwners(string devicePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new UnauthorizedAccessException("denied");
        }
    }
}
