using AdbSharp.Common.Devices;
using AdbSharp.Common.Diagnostics;
using AdbSharp.Fastboot;
using AdbSharp.Fastboot.Sparse;

namespace AdbSharp.Fastbootd;

/// <summary>
/// Provides unified flashing helpers that route operations to bootloader Fastboot or userspace Fastboot.
/// </summary>
public static class UnifiedFastbootFlasher
{
    /// <summary>
    /// Flashes a partition using bootloader Fastboot or userspace Fastboot based on device capability probes.
    /// </summary>
    /// <param name="device">The Fastboot-capable device.</param>
    /// <param name="partition">The partition name.</param>
    /// <param name="image">The image stream.</param>
    /// <param name="length">The image length. When omitted, the stream must be seekable.</param>
    /// <param name="options">Optional Fastbootd transition options.</param>
    /// <param name="progress">Optional transfer progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The unified flash result.</returns>
    public static async ValueTask<UnifiedFlashResult> FlashPartitionAsync(
        AndroidDevice device,
        string partition,
        Stream image,
        long? length = null,
        FastbootdClientOptions? options = null,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(partition);
        ArgumentNullException.ThrowIfNull(image);

        options ??= new FastbootdClientOptions();
        var route = await ProbeRouteAsync(device, partition, options, cancellationToken).ConfigureAwait(false);
        if (!route.ShouldUseFastbootd)
        {
            AndroidDevice fastbootDevice;
            try
            {
                await route.Fastboot.FlashPartitionAsync(partition, image, length, progress, cancellationToken).ConfigureAwait(false);
                fastbootDevice = route.Fastboot.Device;
            }
            finally
            {
                await route.Fastboot.DisposeAsync().ConfigureAwait(false);
            }

            return new UnifiedFlashResult(fastbootDevice, partition, UsedFastbootd: false, route.PartitionWasLogical);
        }

        await route.Fastboot.DisposeAsync().ConfigureAwait(false);

        if (route.IsUserspace)
        {
            await using var fastbootdFromDevice = await FastbootdClient.ConnectAsync(device, options, cancellationToken).ConfigureAwait(false);
            await fastbootdFromDevice.FlashLogicalPartitionAsync(partition, image, length, progress, cancellationToken).ConfigureAwait(false);
            return new UnifiedFlashResult(fastbootdFromDevice.Device, partition, UsedFastbootd: true, route.PartitionWasLogical);
        }

        await using var fastbootd = await FastbootdClient.ConnectAsync(device, options, cancellationToken).ConfigureAwait(false);
        await fastbootd.FlashLogicalPartitionAsync(partition, image, length, progress, cancellationToken).ConfigureAwait(false);
        return new UnifiedFlashResult(fastbootd.Device, partition, UsedFastbootd: true, route.PartitionWasLogical);
    }

    /// <summary>
    /// Flashes a sparse image using bootloader Fastboot or userspace Fastboot based on device capability probes.
    /// </summary>
    /// <param name="device">The Fastboot-capable device.</param>
    /// <param name="partition">The partition name.</param>
    /// <param name="sparseImage">The seekable sparse image stream.</param>
    /// <param name="options">Optional Fastbootd transition options.</param>
    /// <param name="progress">Optional transfer progress.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The unified flash result.</returns>
    public static async ValueTask<UnifiedFlashResult> FlashSparsePartitionAsync(
        AndroidDevice device,
        string partition,
        Stream sparseImage,
        FastbootdClientOptions? options = null,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(partition);
        ArgumentNullException.ThrowIfNull(sparseImage);

        options ??= new FastbootdClientOptions();
        var route = await ProbeRouteAsync(device, partition, options, cancellationToken).ConfigureAwait(false);
        if (!route.ShouldUseFastbootd)
        {
            SparseImageInfo sparseInfo;
            AndroidDevice fastbootDevice;
            try
            {
                sparseInfo = await route.Fastboot.FlashSparsePartitionAsync(partition, sparseImage, progress, cancellationToken).ConfigureAwait(false);
                fastbootDevice = route.Fastboot.Device;
            }
            finally
            {
                await route.Fastboot.DisposeAsync().ConfigureAwait(false);
            }

            return new UnifiedFlashResult(fastbootDevice, partition, UsedFastbootd: false, route.PartitionWasLogical, sparseInfo);
        }

        await route.Fastboot.DisposeAsync().ConfigureAwait(false);

        if (route.IsUserspace)
        {
            await using var fastbootdFromDevice = await FastbootdClient.ConnectAsync(device, options, cancellationToken).ConfigureAwait(false);
            var sparseInfo = await fastbootdFromDevice.FlashSparseLogicalPartitionAsync(partition, sparseImage, progress, cancellationToken).ConfigureAwait(false);
            return new UnifiedFlashResult(fastbootdFromDevice.Device, partition, UsedFastbootd: true, route.PartitionWasLogical, sparseInfo);
        }

        await using var fastbootd = await FastbootdClient.ConnectAsync(device, options, cancellationToken).ConfigureAwait(false);
        var info = await fastbootd.FlashSparseLogicalPartitionAsync(partition, sparseImage, progress, cancellationToken).ConfigureAwait(false);
        return new UnifiedFlashResult(fastbootd.Device, partition, UsedFastbootd: true, route.PartitionWasLogical, info);
    }

    private static async ValueTask<FlashRoute> ProbeRouteAsync(AndroidDevice device, string partition, FastbootdClientOptions options, CancellationToken cancellationToken)
    {
        var fastboot = await FastbootClient.ConnectAsync(device, options.FastbootOptions, cancellationToken).ConfigureAwait(false);
        try
        {
            var isUserspace = await FastbootdCapabilityProbe.TryIsUserspaceAsync(fastboot, cancellationToken).ConfigureAwait(false);
            var isLogical = await FastbootdCapabilityProbe.TryIsLogicalPartitionAsync(fastboot, partition, cancellationToken).ConfigureAwait(false);
            return new FlashRoute(fastboot, isUserspace, isLogical, isUserspace || isLogical);
        }
        catch
        {
            await fastboot.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed record FlashRoute(FastbootClient Fastboot, bool IsUserspace, bool PartitionWasLogical, bool ShouldUseFastbootd);
}
