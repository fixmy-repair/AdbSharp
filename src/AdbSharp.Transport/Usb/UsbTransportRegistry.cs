using AdbSharp.Common.Devices;

namespace AdbSharp.Transport.Usb;

/// <summary>
/// Process-wide registry for USB enumerators and transport factories.
/// </summary>
public static class UsbTransportRegistry
{
    private static readonly object Gate = new();
    private static readonly List<IUsbDeviceEnumerator> Enumerators = [];
    private static readonly List<IUsbTransportFactory> Factories = [];

    /// <summary>
    /// Registers a USB device enumerator.
    /// </summary>
    /// <param name="enumerator">The enumerator to register.</param>
    public static void RegisterEnumerator(IUsbDeviceEnumerator enumerator)
    {
        ArgumentNullException.ThrowIfNull(enumerator);

        lock (Gate)
        {
            if (!Enumerators.Contains(enumerator))
            {
                Enumerators.Add(enumerator);
            }
        }
    }

    /// <summary>
    /// Registers a USB transport factory.
    /// </summary>
    /// <param name="factory">The factory to register.</param>
    public static void RegisterFactory(IUsbTransportFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (Gate)
        {
            if (!Factories.Contains(factory))
            {
                Factories.Add(factory);
            }
        }
    }

    /// <summary>
    /// Clears registered transports. Intended for tests and custom hosts.
    /// </summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Enumerators.Clear();
            Factories.Clear();
        }
    }

    /// <summary>
    /// Enumerates registered USB device descriptors.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The descriptors returned by all registered enumerators.</returns>
    public static async ValueTask<IReadOnlyList<UsbDeviceDescriptor>> FindAsync(CancellationToken cancellationToken = default)
    {
        IUsbDeviceEnumerator[] snapshot;
        lock (Gate)
        {
            snapshot = [.. Enumerators];
        }

        var devices = new List<UsbDeviceDescriptor>();
        foreach (var enumerator in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            devices.AddRange(await enumerator.FindAsync(cancellationToken).ConfigureAwait(false));
        }

        return devices;
    }

    /// <summary>
    /// Enumerates registered USB device descriptors and captures per-enumerator failures as diagnostics.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Successful descriptors and discovery issues from failed enumerators.</returns>
    public static async ValueTask<UsbDiscoveryResult> FindWithDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        IUsbDeviceEnumerator[] snapshot;
        lock (Gate)
        {
            snapshot = [.. Enumerators];
        }

        var devices = new List<UsbDeviceDescriptor>();
        var issues = new List<UsbDiscoveryIssue>();
        foreach (var enumerator in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                devices.AddRange(await enumerator.FindAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (UsbTransportException ex)
            {
                issues.Add(new UsbDiscoveryIssue(GetEnumeratorName(enumerator), ex.Error, ex.Message, ex));
            }
            catch (Exception ex)
            {
                issues.Add(new UsbDiscoveryIssue(GetEnumeratorName(enumerator), UsbTransportError.Unknown, ex.Message, ex));
            }
        }

        return new UsbDiscoveryResult(devices, issues);
    }

    /// <summary>
    /// Finds a registered factory for the descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor to open.</param>
    /// <returns>A matching factory.</returns>
    /// <exception cref="UsbTransportException">No registered factory can open the descriptor.</exception>
    public static IUsbTransportFactory FindFactory(UsbDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        lock (Gate)
        {
            foreach (var factory in Factories)
            {
                if (factory.CanOpen(descriptor))
                {
                    return factory;
                }
            }
        }

        throw new UsbTransportException($"No USB transport factory is registered for '{descriptor.TransportId}'.");
    }

    private static string GetEnumeratorName(IUsbDeviceEnumerator enumerator)
    {
        return enumerator.GetType().FullName ?? enumerator.GetType().Name;
    }
}
