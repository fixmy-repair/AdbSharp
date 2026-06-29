using AdbSharp.Common.Devices;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Transport;

public sealed class UsbDeviceLockConflictHandlerTests
{
    [Fact]
    public async Task OpenAsync_without_options_preserves_original_open_failure()
    {
        var descriptor = CreateDescriptor();
        var factory = new ScriptedOpenFactory(descriptor);
        factory.EnqueueFailure(new UsbTransportException(UsbTransportError.ExclusiveAccess, "busy"));

        var exception = await Assert.ThrowsAsync<UsbTransportException>(async () => await UsbDeviceLockConflictHandler.OpenAsync(factory, descriptor));

        Assert.Equal(UsbTransportError.ExclusiveAccess, exception.Error);
        Assert.Equal(1, factory.OpenCount);
    }

    [Fact]
    public async Task OpenAsync_resolves_releases_and_retries_when_enabled()
    {
        var descriptor = CreateDescriptor();
        var transport = new ScriptedUsbTransport(descriptor);
        var factory = new ScriptedOpenFactory(descriptor);
        factory.EnqueueFailure(new UsbTransportException(UsbTransportError.Busy, "locked"));
        factory.EnqueueSuccess(transport);
        var resolver = new StaticResolver(CreateResolution(descriptor));
        var releaser = new RecordingReleaser(success: true);
        var options = new UsbDeviceLockConflictOptions
        {
            ResolveOwners = true,
            ReleaseAdbServer = true,
            RetryDelay = TimeSpan.Zero,
            OwnerResolver = resolver,
            OwnerReleaser = releaser,
            ReleaseOptions = new UsbDeviceLockReleaseOptions { AllowProcessTermination = true }
        };

        var opened = await UsbDeviceLockConflictHandler.OpenAsync(factory, descriptor, options);

        Assert.Same(transport, opened);
        Assert.Equal(2, factory.OpenCount);
        Assert.Equal(1, resolver.ResolveCount);
        Assert.Equal(1, releaser.ReleaseCount);
        Assert.NotNull(releaser.LastOptions);
        Assert.False(releaser.LastOptions.AllowProcessTermination);
    }

    [Fact]
    public async Task OpenAsync_throws_conflict_exception_when_retry_still_fails()
    {
        var descriptor = CreateDescriptor();
        var factory = new ScriptedOpenFactory(descriptor);
        factory.EnqueueFailure(new UsbTransportException(UsbTransportError.Busy, "locked"));
        factory.EnqueueFailure(new UsbTransportException(UsbTransportError.Busy, "still locked"));
        var resolution = CreateResolution(descriptor);
        var options = new UsbDeviceLockConflictOptions
        {
            ResolveOwners = true,
            ReleaseAdbServer = true,
            RetryDelay = TimeSpan.Zero,
            OwnerResolver = new StaticResolver(resolution),
            OwnerReleaser = new RecordingReleaser(success: true)
        };

        var exception = await Assert.ThrowsAsync<UsbDeviceLockConflictException>(async () => await UsbDeviceLockConflictHandler.OpenAsync(factory, descriptor, options));

        Assert.Equal(UsbTransportError.Busy, exception.Error);
        Assert.Same(resolution, exception.Resolution);
        Assert.Single(exception.ReleaseResults);
        Assert.Equal(2, factory.OpenCount);
    }

    private static UsbDeviceDescriptor CreateDescriptor()
    {
        return new UsbDeviceDescriptor(
            "test-lock",
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

    private static UsbDeviceLockOwnerResolution CreateResolution(UsbDeviceDescriptor descriptor)
    {
        return new UsbDeviceLockOwnerResolution(
            descriptor,
            UsbDeviceLockOwnerResolutionStatus.Resolved,
            [new UsbDeviceLockOwner(42, "adb", "/usr/bin/adb", UsbDeviceLockOwnerKind.AdbServer, UsbDeviceLockOwnerConfidence.Exact, "test")],
            "/dev/bus/usb/001/002",
            null);
    }

    private sealed class ScriptedOpenFactory(UsbDeviceDescriptor descriptor) : IUsbTransportFactory
    {
        private readonly Queue<object> scripts = [];
        private readonly UsbDeviceDescriptor expectedDescriptor = descriptor;

        public string PlatformName => "Test";

        public int OpenCount { get; private set; }

        public bool CanOpen(UsbDeviceDescriptor descriptor)
        {
            return descriptor.TransportId == expectedDescriptor.TransportId;
        }

        public void EnqueueFailure(UsbTransportException exception)
        {
            scripts.Enqueue(exception);
        }

        public void EnqueueSuccess(IUsbTransport transport)
        {
            scripts.Enqueue(transport);
        }

        public ValueTask<IUsbTransport> OpenAsync(UsbDeviceDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            var script = scripts.Dequeue();
            if (script is UsbTransportException exception)
            {
                throw exception;
            }

            return ValueTask.FromResult((IUsbTransport)script);
        }
    }

    private sealed class StaticResolver(UsbDeviceLockOwnerResolution resolution) : IUsbDeviceLockOwnerResolver
    {
        public string PlatformName => "Test";

        public int ResolveCount { get; private set; }

        public bool CanResolve(UsbDeviceDescriptor descriptor)
        {
            return descriptor.TransportId == resolution.Descriptor.TransportId;
        }

        public ValueTask<UsbDeviceLockOwnerResolution> ResolveAsync(
            UsbDeviceDescriptor descriptor,
            UsbTransportException? openFailure = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveCount++;
            return ValueTask.FromResult(resolution);
        }
    }

    private sealed class RecordingReleaser(bool success) : IUsbDeviceLockOwnerReleaser
    {
        public int ReleaseCount { get; private set; }

        public UsbDeviceLockReleaseOptions? LastOptions { get; private set; }

        public ValueTask<UsbDeviceLockReleaseResult> ReleaseAsync(
            UsbDeviceLockOwner owner,
            UsbDeviceLockReleaseOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;
            LastOptions = options;
            return ValueTask.FromResult(new UsbDeviceLockReleaseResult(
                owner,
                UsbDeviceLockReleaseKind.GracefulAdbServerKill,
                success,
                "test"));
        }
    }
}
