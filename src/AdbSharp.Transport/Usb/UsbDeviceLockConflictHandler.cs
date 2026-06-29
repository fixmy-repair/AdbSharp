using AdbSharp.Common.Devices;

namespace AdbSharp.Transport.Usb;

/// <summary>
/// Opens USB transports with optional lock owner conflict handling.
/// </summary>
public static class UsbDeviceLockConflictHandler
{
    /// <summary>
    /// Opens a USB transport, optionally resolving and releasing lock owners when opening fails.
    /// </summary>
    /// <param name="factory">The USB transport factory.</param>
    /// <param name="descriptor">The USB descriptor to open.</param>
    /// <param name="options">Optional conflict handling options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The opened transport.</returns>
    public static async ValueTask<IUsbTransport> OpenAsync(
        IUsbTransportFactory factory,
        UsbDeviceDescriptor descriptor,
        UsbDeviceLockConflictOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (options?.ResolveOwners != true)
        {
            return await factory.OpenAsync(descriptor, cancellationToken).ConfigureAwait(false);
        }

        UsbTransportException openFailure;
        try
        {
            return await factory.OpenAsync(descriptor, cancellationToken).ConfigureAwait(false);
        }
        catch (UsbTransportException ex) when (IsLockLikeFailure(ex))
        {
            openFailure = ex;
        }

        var resolver = options.OwnerResolver ?? UsbDeviceLockOwnerResolverRegistry.FindResolver(descriptor);
        var resolution = resolver is null
            ? UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.Unsupported,
                message: $"No USB lock owner resolver is registered for '{descriptor.TransportId}'.")
            : await resolver.ResolveAsync(descriptor, openFailure, cancellationToken).ConfigureAwait(false);

        var releaseResults = new List<UsbDeviceLockReleaseResult>();
        if (options.ReleaseAdbServer)
        {
            var releaser = options.OwnerReleaser ?? UsbDeviceLockOwnerReleaser.Default;
            foreach (var owner in resolution.Owners)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!owner.SupportsGracefulAdbRelease)
                {
                    continue;
                }

                var releaseOptions = CreateGracefulReleaseOptions(options.ReleaseOptions);
                releaseResults.Add(await releaser.ReleaseAsync(owner, releaseOptions, cancellationToken).ConfigureAwait(false));
            }
        }

        if (options.RetryAfterRelease && releaseResults.Any(static result => result.Succeeded))
        {
            if (options.RetryDelay > TimeSpan.Zero)
            {
                await Task.Delay(options.RetryDelay, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await factory.OpenAsync(descriptor, cancellationToken).ConfigureAwait(false);
            }
            catch (UsbTransportException ex)
            {
                throw CreateConflictException(ex, resolution, releaseResults);
            }
        }

        throw CreateConflictException(openFailure, resolution, releaseResults);
    }

    /// <summary>
    /// Determines whether a USB transport error commonly indicates another process owns the interface.
    /// </summary>
    /// <param name="exception">The USB transport exception.</param>
    /// <returns><see langword="true" /> when the error is lock-like.</returns>
    public static bool IsLockLikeFailure(UsbTransportException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Error is UsbTransportError.Busy
            or UsbTransportError.ExclusiveAccess
            or UsbTransportError.PermissionDenied;
    }

    private static UsbDeviceLockConflictException CreateConflictException(
        UsbTransportException exception,
        UsbDeviceLockOwnerResolution resolution,
        IReadOnlyList<UsbDeviceLockReleaseResult> releaseResults)
    {
        var ownerCount = resolution.Owners.Count;
        var message = ownerCount == 0
            ? $"{exception.Message} USB lock owner resolution status: {resolution.Status}."
            : $"{exception.Message} Resolved {ownerCount} possible USB lock owner(s).";
        return new UsbDeviceLockConflictException(
            exception.Error,
            message,
            resolution,
            releaseResults,
            exception);
    }

    private static UsbDeviceLockReleaseOptions CreateGracefulReleaseOptions(UsbDeviceLockReleaseOptions source)
    {
        return new UsbDeviceLockReleaseOptions
        {
            AllowGracefulAdbServerKill = true,
            AllowProcessTermination = false,
            AdbServerHost = source.AdbServerHost,
            AdbServerPort = source.AdbServerPort,
            AdbServerTimeout = source.AdbServerTimeout
        };
    }
}
