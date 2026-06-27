using AdbSharp.Common;
using AdbSharp.Common.Devices;
using AdbSharp.Common.Diagnostics;
using AdbSharp.Fastboot;
using AdbSharp.Fastboot.Sparse;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Fastbootd;

/// <summary>
/// Client for userspace Fastboot operations.
/// </summary>
public sealed class FastbootdClient : IAsyncDisposable
{
    private readonly FastbootClient fastboot;

    private FastbootdClient(AndroidDevice device, FastbootClient fastboot, FastbootdCapabilities capabilities)
    {
        Device = device;
        this.fastboot = fastboot;
        Capabilities = capabilities;
    }

    /// <summary>
    /// Gets the connected userspace Fastboot device.
    /// </summary>
    public AndroidDevice Device { get; }

    /// <summary>
    /// Gets the capabilities discovered from the connected userspace Fastboot implementation.
    /// </summary>
    public FastbootdCapabilities Capabilities { get; }

    /// <summary>
    /// Connects to userspace Fastboot, optionally transitioning from bootloader Fastboot first.
    /// </summary>
    /// <param name="device">The device to connect.</param>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The connected userspace Fastboot client.</returns>
    public static async ValueTask<FastbootdClient> ConnectAsync(AndroidDevice device, FastbootdClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        options ??= new FastbootdClientOptions();

        var candidate = device;
        var fastboot = await FastbootClient.ConnectAsync(candidate, options.FastbootOptions, cancellationToken).ConfigureAwait(false);
        try
        {
            var isUserspace = await FastbootdCapabilityProbe.TryIsUserspaceAsync(fastboot, cancellationToken).ConfigureAwait(false);
            if (!isUserspace)
            {
                if (!options.AllowAutomaticTransition)
                {
                    throw new DeviceConnectionException("The connected device is not running userspace Fastboot.");
                }

                await fastboot.RebootFastbootAsync(cancellationToken).ConfigureAwait(false);
                await fastboot.DisposeAsync().ConfigureAwait(false);
                candidate = await RediscoverAsync(device, options, cancellationToken).ConfigureAwait(false);
                fastboot = await FastbootClient.ConnectAsync(candidate, options.FastbootOptions, cancellationToken).ConfigureAwait(false);
                if (!await FastbootdCapabilityProbe.TryIsUserspaceAsync(fastboot, cancellationToken).ConfigureAwait(false))
                {
                    throw new DeviceConnectionException("The rediscovered device is still not running userspace Fastboot.");
                }
            }

            var capabilities = await DetectCapabilitiesAsync(fastboot, cancellationToken).ConfigureAwait(false);
            if (!capabilities.IsUserspace)
            {
                throw new DeviceConnectionException("The connected device did not confirm userspace Fastboot during capability probing.");
            }

            return new FastbootdClient(CreateFastbootdDevice(candidate, capabilities), fastboot, capabilities);
        }
        catch
        {
            await fastboot.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Probes userspace Fastboot capabilities without taking ownership of the client.
    /// </summary>
    /// <param name="fastboot">The connected Fastboot client.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The discovered userspace Fastboot capabilities.</returns>
    public static async ValueTask<FastbootdCapabilities> DetectCapabilitiesAsync(FastbootClient fastboot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fastboot);

        var isUserspace = await FastbootdCapabilityProbe.TryIsUserspaceAsync(fastboot, cancellationToken).ConfigureAwait(false);
        var superPartitionName = await FastbootdCapabilityProbe.TryGetVarAsync(fastboot, "super-partition-name", cancellationToken).ConfigureAwait(false);
        var snapshotUpdateStatus = await FastbootdCapabilityProbe.TryGetVarAsync(fastboot, "snapshot-update-status", cancellationToken).ConfigureAwait(false);
        var supportsDynamicPartitions = !string.IsNullOrWhiteSpace(superPartitionName);
        var supportsLogicalPartitions = supportsDynamicPartitions
            || await FastbootdCapabilityProbe.TryIsLogicalPartitionAsync(fastboot, "system", cancellationToken).ConfigureAwait(false)
            || await FastbootdCapabilityProbe.TryIsLogicalPartitionAsync(fastboot, "system_a", cancellationToken).ConfigureAwait(false);
        var supportsSnapshotUpdates = !string.IsNullOrWhiteSpace(snapshotUpdateStatus);

        return new FastbootdCapabilities(
            isUserspace,
            supportsDynamicPartitions,
            supportsLogicalPartitions,
            supportsSnapshotUpdates,
            SupportsVirtualAb: supportsSnapshotUpdates,
            superPartitionName,
            snapshotUpdateStatus);
    }

    /// <summary>
    /// Flashes a logical partition.
    /// </summary>
    /// <param name="partition">The logical partition name.</param>
    /// <param name="image">The image stream.</param>
    /// <param name="length">The image length. When omitted, the stream must be seekable.</param>
    /// <param name="progress">Optional transfer progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask FlashLogicalPartitionAsync(string partition, Stream image, long? length = null, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return fastboot.FlashPartitionAsync(partition, image, length, progress, cancellationToken);
    }

    /// <summary>
    /// Flashes a sparse image to a logical partition.
    /// </summary>
    /// <param name="partition">The logical partition name.</param>
    /// <param name="sparseImage">The seekable sparse image stream.</param>
    /// <param name="progress">Optional transfer progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The parsed sparse image metadata.</returns>
    public ValueTask<SparseImageInfo> FlashSparseLogicalPartitionAsync(string partition, Stream sparseImage, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return fastboot.FlashSparsePartitionAsync(partition, sparseImage, progress, cancellationToken);
    }

    /// <summary>
    /// Creates a logical partition.
    /// </summary>
    /// <param name="name">The partition name.</param>
    /// <param name="sizeBytes">The partition size in bytes.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask CreateLogicalPartitionAsync(string name, long sizeBytes, CancellationToken cancellationToken = default)
    {
        return fastboot.CreateLogicalPartitionAsync(name, sizeBytes, cancellationToken);
    }

    /// <summary>
    /// Deletes a logical partition.
    /// </summary>
    /// <param name="name">The partition name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask DeleteLogicalPartitionAsync(string name, CancellationToken cancellationToken = default)
    {
        return fastboot.DeleteLogicalPartitionAsync(name, cancellationToken);
    }

    /// <summary>
    /// Resizes a logical partition.
    /// </summary>
    /// <param name="name">The partition name.</param>
    /// <param name="sizeBytes">The requested size in bytes.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask ResizeLogicalPartitionAsync(string name, long sizeBytes, CancellationToken cancellationToken = default)
    {
        return fastboot.ResizeLogicalPartitionAsync(name, sizeBytes, cancellationToken);
    }

    /// <summary>
    /// Updates super partition metadata using a previously downloaded image.
    /// </summary>
    /// <param name="superPartition">The super partition name.</param>
    /// <param name="wipe">Whether to wipe existing logical partitions.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public ValueTask UpdateSuperAsync(string superPartition, bool wipe = false, CancellationToken cancellationToken = default)
    {
        return fastboot.UpdateSuperAsync(superPartition, wipe, cancellationToken);
    }

    /// <summary>
    /// Downloads a super image and updates super partition metadata.
    /// </summary>
    /// <param name="superPartition">The super partition name.</param>
    /// <param name="image">The super image stream.</param>
    /// <param name="length">The image length. When omitted, the stream must be seekable.</param>
    /// <param name="wipe">Whether to wipe existing logical partitions.</param>
    /// <param name="progress">Optional transfer progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async ValueTask UpdateSuperAsync(string superPartition, Stream image, long? length = null, bool wipe = false, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var imageLength = length ?? (image.CanSeek ? image.Length - image.Position : throw new ArgumentException("Image length is required for non-seekable streams.", nameof(length)));
        await fastboot.DownloadAsync(image, imageLength, progress, cancellationToken).ConfigureAwait(false);
        await UpdateSuperAsync(superPartition, wipe, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a snapshot update command when supported by the device.
    /// </summary>
    /// <param name="operation">The snapshot operation payload.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The terminal OKAY payload.</returns>
    public ValueTask<string> SnapshotUpdateAsync(string operation, CancellationToken cancellationToken = default)
    {
        return fastboot.SnapshotUpdateAsync(operation, cancellationToken);
    }

    /// <summary>
    /// Gets a Fastboot variable from the userspace device.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The variable value.</returns>
    public ValueTask<string> GetVarAsync(string name, CancellationToken cancellationToken = default)
    {
        return fastboot.GetVarAsync(name, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return fastboot.DisposeAsync();
    }

    private static async ValueTask<AndroidDevice> RediscoverAsync(AndroidDevice original, FastbootdClientOptions options, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.TransitionTimeout);
        try
        {
            if (options.RediscoverFastbootdAsync is not null)
            {
                var rediscovered = await options.RediscoverFastbootdAsync(original, timeout.Token).ConfigureAwait(false);
                return rediscovered ?? throw new DeviceConnectionException("Device did not reappear in userspace Fastboot.");
            }

            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var descriptors = await UsbTransportRegistry.FindAsync(timeout.Token).ConfigureAwait(false);
                var candidates = descriptors
                    .Select(CreateCandidate)
                    .Where(static device => device.Mode is DeviceMode.Fastboot or DeviceMode.Bootloader)
                    .ToArray();

                var match = FindMatchingRediscoveredDevice(original, candidates);
                if (match is not null)
                {
                    return match;
                }

                await Task.Delay(options.RediscoveryPollInterval, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeviceConnectionException("Device did not reappear in userspace Fastboot before the transition timeout elapsed.");
        }
    }

    private static AndroidDevice CreateCandidate(UsbDeviceDescriptor descriptor)
    {
        var mode = AndroidUsbClass.Classify(descriptor);
        var capabilities = mode is DeviceMode.Fastboot or DeviceMode.Bootloader
            ? DeviceCapabilities.Empty with { SupportsFastboot = true }
            : DeviceCapabilities.Empty;

        return new AndroidDevice(
            new DeviceIdentity(descriptor.SerialNumber, descriptor.Manufacturer, descriptor.Product, descriptor.Product, descriptor.TransportId),
            mode,
            capabilities,
            descriptor);
    }

    private static AndroidDevice CreateFastbootdDevice(AndroidDevice candidate, FastbootdCapabilities capabilities)
    {
        return candidate with
        {
            Mode = DeviceMode.Fastbootd,
            Capabilities = candidate.Capabilities with
            {
                SupportsFastboot = true,
                SupportsFastbootd = capabilities.IsUserspace,
                SupportsDynamicPartitions = capabilities.SupportsDynamicPartitions || capabilities.SupportsLogicalPartitions,
                SupportsLogicalPartitions = capabilities.SupportsLogicalPartitions,
                SupportsSnapshotUpdates = capabilities.SupportsSnapshotUpdates,
                SupportsVirtualAb = capabilities.SupportsVirtualAb
            }
        };
    }

    private static AndroidDevice? FindMatchingRediscoveredDevice(AndroidDevice original, IReadOnlyList<AndroidDevice> candidates)
    {
        if (!string.IsNullOrWhiteSpace(original.Identity.SerialNumber))
        {
            return candidates.FirstOrDefault(candidate => string.Equals(candidate.Identity.SerialNumber, original.Identity.SerialNumber, StringComparison.Ordinal));
        }

        var sameUsbIdentity = candidates
            .Where(candidate => candidate.Usb.VendorId == original.Usb.VendorId && candidate.Usb.ProductId == original.Usb.ProductId)
            .ToArray();

        return sameUsbIdentity.Length == 1
            ? sameUsbIdentity[0]
            : candidates.Count == 1 ? candidates[0] : null;
    }
}
