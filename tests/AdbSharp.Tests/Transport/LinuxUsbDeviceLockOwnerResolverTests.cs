using AdbSharp.Common.Devices;
using AdbSharp.Platform.Linux.Usb;
using AdbSharp.Platform.Linux.Usb.Locking;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Transport;

public sealed class LinuxUsbDeviceLockOwnerResolverTests
{
    [Fact]
    public async Task ResolveAsync_matches_proc_fd_target_to_usb_device_node()
    {
        var descriptor = CreateDescriptor();
        var fileSystem = new FakeLinuxProcFileSystem();
        fileSystem.ProcessIds.Add(200);
        fileSystem.FileDescriptors[200] = ["/proc/200/fd/9"];
        fileSystem.LinkTargets["/proc/200/fd/9"] = "/dev/bus/usb/001/002";
        fileSystem.ProcessNames[200] = "adb";
        fileSystem.ExecutablePaths[200] = "/usr/bin/adb";
        var resolver = new LinuxUsbDeviceLockOwnerResolver(fileSystem, requireLinux: false);

        var resolution = await resolver.ResolveAsync(descriptor);

        Assert.Equal(UsbDeviceLockOwnerResolutionStatus.Resolved, resolution.Status);
        var owner = Assert.Single(resolution.Owners);
        Assert.Equal(200, owner.ProcessId);
        Assert.Equal(UsbDeviceLockOwnerKind.AdbServer, owner.Kind);
        Assert.Equal(UsbDeviceLockOwnerConfidence.Exact, owner.Confidence);
        Assert.Equal("/dev/bus/usb/001/002", resolution.DevicePath);
    }

    [Fact]
    public async Task ResolveAsync_matches_proc_fd_identity_when_target_path_is_unavailable()
    {
        var descriptor = CreateDescriptor();
        var fileSystem = new FakeLinuxProcFileSystem();
        var identity = new LinuxFileIdentity(189, 1, 1234);
        fileSystem.ProcessIds.Add(201);
        fileSystem.FileDescriptors[201] = ["/proc/201/fd/4"];
        fileSystem.Identities["/dev/bus/usb/001/002"] = identity;
        fileSystem.Identities["/proc/201/fd/4"] = identity;
        fileSystem.ProcessNames[201] = "scrcpy";
        var resolver = new LinuxUsbDeviceLockOwnerResolver(fileSystem, requireLinux: false);

        var resolution = await resolver.ResolveAsync(descriptor);

        var owner = Assert.Single(resolution.Owners);
        Assert.Equal(UsbDeviceLockOwnerKind.Scrcpy, owner.Kind);
        Assert.Equal(UsbDeviceLockOwnerConfidence.Exact, owner.Confidence);
    }

    [Fact]
    public async Task ResolveAsync_reports_unsupported_when_proc_is_missing()
    {
        var descriptor = CreateDescriptor();
        var fileSystem = new FakeLinuxProcFileSystem { HasProc = false };
        var resolver = new LinuxUsbDeviceLockOwnerResolver(fileSystem, requireLinux: false);

        var resolution = await resolver.ResolveAsync(descriptor);

        Assert.Equal(UsbDeviceLockOwnerResolutionStatus.Unsupported, resolution.Status);
    }

    private static UsbDeviceDescriptor CreateDescriptor()
    {
        var bulkIn = new UsbEndpoint(0x81, UsbEndpointDirection.In, UsbTransferKind.Bulk, 512);
        var bulkOut = new UsbEndpoint(0x01, UsbEndpointDirection.Out, UsbTransferKind.Bulk, 512);
        var id = new LinuxUsbTransportId(1, 2, 1, 0, 0, bulkIn, bulkOut);
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

    private sealed class FakeLinuxProcFileSystem : ILinuxProcFileSystem
    {
        public bool HasProc { get; set; } = true;

        public List<int> ProcessIds { get; } = [];

        public Dictionary<int, IReadOnlyList<string>> FileDescriptors { get; } = [];

        public Dictionary<string, string> LinkTargets { get; } = [];

        public Dictionary<string, LinuxFileIdentity> Identities { get; } = [];

        public Dictionary<int, string> ProcessNames { get; } = [];

        public Dictionary<int, string> ExecutablePaths { get; } = [];

        public bool DirectoryExists(string path)
        {
            return path == "/proc" && HasProc;
        }

        public IEnumerable<int> EnumerateProcessIds()
        {
            return ProcessIds;
        }

        public IEnumerable<string> EnumerateFileDescriptors(int processId)
        {
            return FileDescriptors.TryGetValue(processId, out var descriptors) ? descriptors : [];
        }

        public string? ResolveLinkTarget(string path)
        {
            return LinkTargets.GetValueOrDefault(path);
        }

        public LinuxFileIdentity? GetIdentity(string path)
        {
            return Identities.TryGetValue(path, out var identity) ? identity : null;
        }

        public string? ReadProcessName(int processId)
        {
            return ProcessNames.GetValueOrDefault(processId);
        }

        public string? ReadExecutablePath(int processId)
        {
            return ExecutablePaths.GetValueOrDefault(processId);
        }
    }
}
