using AdbSharp.Common.Devices;

namespace AdbSharp.Transport.Usb;

/// <summary>
/// Process-wide registry for USB device lock owner resolvers.
/// </summary>
public static class UsbDeviceLockOwnerResolverRegistry
{
    private static readonly Lock Gate = new();
    private static readonly List<IUsbDeviceLockOwnerResolver> Resolvers = [];

    /// <summary>
    /// Registers a USB device lock owner resolver.
    /// </summary>
    /// <param name="resolver">The resolver to register.</param>
    public static void RegisterResolver(IUsbDeviceLockOwnerResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        lock (Gate)
        {
            if (!ContainsReference(Resolvers, resolver))
            {
                Resolvers.Add(resolver);
            }
        }
    }

    /// <summary>
    /// Clears registered resolvers. Intended for tests and custom hosts.
    /// </summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Resolvers.Clear();
        }
    }

    /// <summary>
    /// Finds a registered resolver for the descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor to inspect.</param>
    /// <returns>The matching resolver, or <see langword="null" /> when no resolver matches.</returns>
    public static IUsbDeviceLockOwnerResolver? FindResolver(UsbDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        IUsbDeviceLockOwnerResolver[] snapshot;
        lock (Gate)
        {
            snapshot = [.. Resolvers];
        }

        foreach (var resolver in snapshot)
        {
            if (resolver.CanResolve(descriptor))
            {
                return resolver;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves lock owners using the registered resolver for the descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor to inspect.</param>
    /// <param name="openFailure">The open failure that triggered resolution, when available.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The owner resolution result.</returns>
    public static ValueTask<UsbDeviceLockOwnerResolution> ResolveAsync(
        UsbDeviceDescriptor descriptor,
        UsbTransportException? openFailure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        var resolver = FindResolver(descriptor);
        return resolver is null
            ? ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.Unsupported,
                message: $"No USB lock owner resolver is registered for '{descriptor.TransportId}'."))
            : resolver.ResolveAsync(descriptor, openFailure, cancellationToken);
    }

    private static bool ContainsReference<T>(List<T> values, T value)
        where T : class
    {
        foreach (var candidate in values)
        {
            if (ReferenceEquals(candidate, value))
            {
                return true;
            }
        }

        return false;
    }
}
