using AdbSharp.Common.Devices;
using AdbSharp.Platform.Mac.Usb.Locking;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Mac.Usb;

/// <summary>
/// Resolves likely macOS processes that may hold Android USB interfaces.
/// </summary>
public sealed class MacUsbDeviceLockOwnerResolver : IUsbDeviceLockOwnerResolver
{
    private readonly IMacProcessNativeAdapter native;
    private readonly bool requireMac;

    /// <summary>
    /// Initializes a new instance of the <see cref="MacUsbDeviceLockOwnerResolver" /> class.
    /// </summary>
    public MacUsbDeviceLockOwnerResolver()
        : this(new MacProcessNativeAdapter(), requireMac: true)
    {
    }

    internal MacUsbDeviceLockOwnerResolver(IMacProcessNativeAdapter native, bool requireMac = true)
    {
        this.native = native;
        this.requireMac = requireMac;
    }

    /// <inheritdoc />
    public string PlatformName => "macOS";

    /// <inheritdoc />
    public bool CanResolve(UsbDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return OperatingSystem.IsMacOS() && MacUsbTransportId.TryParse(descriptor.TransportId, out _);
    }

    /// <inheritdoc />
    public ValueTask<UsbDeviceLockOwnerResolution> ResolveAsync(
        UsbDeviceDescriptor descriptor,
        UsbTransportException? openFailure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        if (!MacUsbTransportId.TryParse(descriptor.TransportId, out _))
        {
            return ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.Unsupported,
                message: $"Invalid macOS USB transport id '{descriptor.TransportId}'."));
        }

        if (requireMac && !OperatingSystem.IsMacOS())
        {
            return ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.Unsupported,
                descriptor.TransportId,
                "macOS USB lock owner resolution is only available on macOS."));
        }

        try
        {
            var owners = native.EnumerateProcesses(cancellationToken)
                .Select(ProcessToOwner)
                .Where(static owner => owner is not null)
                .Cast<UsbDeviceLockOwner>()
                .GroupBy(static owner => owner.ProcessId)
                .Select(static group => group.First())
                .ToArray();
            var status = owners.Length == 0
                ? UsbDeviceLockOwnerResolutionStatus.NoOwnerFound
                : UsbDeviceLockOwnerResolutionStatus.Resolved;
            return ValueTask.FromResult(new UsbDeviceLockOwnerResolution(
                descriptor,
                status,
                owners,
                descriptor.TransportId,
                "macOS owner detection is best effort and focused on known Android tooling processes."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.AccessDenied,
                descriptor.TransportId,
                ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.Failed,
                descriptor.TransportId,
                ex.Message));
        }
    }

    private static UsbDeviceLockOwner? ProcessToOwner(MacProcessSnapshot process)
    {
        var name = Path.GetFileNameWithoutExtension(process.ProcessName ?? process.ExecutablePath ?? string.Empty);
        var path = process.ExecutablePath ?? string.Empty;
        if (string.Equals(name, "adb", StringComparison.OrdinalIgnoreCase))
        {
            return CreateOwner(process, UsbDeviceLockOwnerKind.AdbServer, UsbDeviceLockOwnerConfidence.High, "known adb process");
        }

        if (string.Equals(name, "fastboot", StringComparison.OrdinalIgnoreCase))
        {
            return CreateOwner(process, UsbDeviceLockOwnerKind.Fastboot, UsbDeviceLockOwnerConfidence.High, "known fastboot process");
        }

        if (string.Equals(name, "scrcpy", StringComparison.OrdinalIgnoreCase))
        {
            return CreateOwner(process, UsbDeviceLockOwnerKind.Scrcpy, UsbDeviceLockOwnerConfidence.Medium, "known scrcpy process");
        }

        return path.Contains("Android Studio", StringComparison.OrdinalIgnoreCase)
            || name.Contains("studio", StringComparison.OrdinalIgnoreCase)
            ? CreateOwner(process, UsbDeviceLockOwnerKind.AndroidStudio, UsbDeviceLockOwnerConfidence.Medium, "known Android Studio process")
            : null;
    }

    private static UsbDeviceLockOwner CreateOwner(
        MacProcessSnapshot process,
        UsbDeviceLockOwnerKind kind,
        UsbDeviceLockOwnerConfidence confidence,
        string evidence)
    {
        return new UsbDeviceLockOwner(process.ProcessId, process.ProcessName, process.ExecutablePath, kind, confidence, evidence);
    }
}
