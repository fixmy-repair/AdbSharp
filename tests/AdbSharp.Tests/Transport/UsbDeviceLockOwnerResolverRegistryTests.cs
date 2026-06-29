using AdbSharp.Common.Devices;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Transport;

public sealed class UsbDeviceLockOwnerResolverRegistryTests
{
    private static readonly Lock RegistryGate = new();

    [Fact]
    public async Task ResolveAsync_returns_unsupported_when_no_resolver_matches()
    {
        var descriptor = CreateDescriptor("missing");
        lock (RegistryGate)
        {
            UsbDeviceLockOwnerResolverRegistry.Clear();
        }

        try
        {
            var resolution = await UsbDeviceLockOwnerResolverRegistry.ResolveAsync(descriptor);

            Assert.Equal(UsbDeviceLockOwnerResolutionStatus.Unsupported, resolution.Status);
            Assert.Empty(resolution.Owners);
        }
        finally
        {
            lock (RegistryGate)
            {
                UsbDeviceLockOwnerResolverRegistry.Clear();
            }
        }
    }

    [Fact]
    public async Task ResolveAsync_uses_registered_matching_resolver()
    {
        var descriptor = CreateDescriptor("match");
        var expected = new UsbDeviceLockOwnerResolution(
            descriptor,
            UsbDeviceLockOwnerResolutionStatus.NoOwnerFound,
            [],
            "test",
            null);
        lock (RegistryGate)
        {
            UsbDeviceLockOwnerResolverRegistry.Clear();
            UsbDeviceLockOwnerResolverRegistry.RegisterResolver(new StaticResolver(descriptor.TransportId, expected));
        }

        try
        {
            var resolution = await UsbDeviceLockOwnerResolverRegistry.ResolveAsync(descriptor);

            Assert.Same(expected, resolution);
        }
        finally
        {
            lock (RegistryGate)
            {
                UsbDeviceLockOwnerResolverRegistry.Clear();
            }
        }
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

    private sealed class StaticResolver(string transportId, UsbDeviceLockOwnerResolution resolution) : IUsbDeviceLockOwnerResolver
    {
        public string PlatformName => "Test";

        public bool CanResolve(UsbDeviceDescriptor descriptor)
        {
            return descriptor.TransportId == transportId;
        }

        public ValueTask<UsbDeviceLockOwnerResolution> ResolveAsync(
            UsbDeviceDescriptor descriptor,
            UsbTransportException? openFailure = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(resolution);
        }
    }
}
