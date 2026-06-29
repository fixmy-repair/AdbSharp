using AdbSharp.Common.Devices;
using AdbSharp.Platform.Windows.Usb.Locking;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Windows.Usb;

/// <summary>
/// Resolves Windows processes that hold Android WinUSB interface handles.
/// </summary>
public sealed class WindowsUsbDeviceLockOwnerResolver : IUsbDeviceLockOwnerResolver
{
    private readonly IWindowsUsbLockOwnerNativeAdapter native;
    private readonly bool requireWindows;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsUsbDeviceLockOwnerResolver" /> class.
    /// </summary>
    public WindowsUsbDeviceLockOwnerResolver()
        : this(new WindowsUsbLockOwnerNativeAdapter(), requireWindows: true)
    {
    }

    internal WindowsUsbDeviceLockOwnerResolver(IWindowsUsbLockOwnerNativeAdapter native, bool requireWindows = true)
    {
        this.native = native;
        this.requireWindows = requireWindows;
    }

    /// <inheritdoc />
    public string PlatformName => "Windows";

    /// <inheritdoc />
    public bool CanResolve(UsbDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return OperatingSystem.IsWindows() && WindowsUsbTransportId.TryParse(descriptor.TransportId, out _);
    }

    /// <inheritdoc />
    public ValueTask<UsbDeviceLockOwnerResolution> ResolveAsync(
        UsbDeviceDescriptor descriptor,
        UsbTransportException? openFailure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowsUsbTransportId.TryParse(descriptor.TransportId, out var id))
        {
            return ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.Unsupported,
                message: $"Invalid Windows USB transport id '{descriptor.TransportId}'."));
        }

        if (requireWindows && !OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.Unsupported,
                id.DevicePath,
                "Windows USB lock owner resolution is only available on Windows."));
        }

        try
        {
            var snapshot = native.FindOwners(id.DevicePath, cancellationToken);
            var owners = snapshot.Owners
                .Select(static owner => new UsbDeviceLockOwner(
                    owner.ProcessId,
                    owner.ProcessName,
                    owner.ExecutablePath,
                    ClassifyProcess(owner.ProcessName, owner.ExecutablePath),
                    owner.Confidence,
                    owner.ObjectName))
                .ToArray();
            var status = owners.Length > 0
                ? UsbDeviceLockOwnerResolutionStatus.Resolved
                : snapshot.IsPartial
                    ? UsbDeviceLockOwnerResolutionStatus.Partial
                    : UsbDeviceLockOwnerResolutionStatus.NoOwnerFound;
            return ValueTask.FromResult(new UsbDeviceLockOwnerResolution(descriptor, status, owners, id.DevicePath, snapshot.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.AccessDenied,
                id.DevicePath,
                ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.Failed,
                id.DevicePath,
                ex.Message));
        }
    }

    private static UsbDeviceLockOwnerKind ClassifyProcess(string? processName, string? executablePath)
    {
        var name = Path.GetFileNameWithoutExtension(processName ?? executablePath ?? string.Empty);
        if (string.Equals(name, "adb", StringComparison.OrdinalIgnoreCase))
        {
            return UsbDeviceLockOwnerKind.AdbServer;
        }

        if (string.Equals(name, "fastboot", StringComparison.OrdinalIgnoreCase))
        {
            return UsbDeviceLockOwnerKind.Fastboot;
        }

        if (string.Equals(name, "scrcpy", StringComparison.OrdinalIgnoreCase))
        {
            return UsbDeviceLockOwnerKind.Scrcpy;
        }

        var path = executablePath ?? string.Empty;
        return path.Contains("Android Studio", StringComparison.OrdinalIgnoreCase)
            || name.Contains("studio", StringComparison.OrdinalIgnoreCase)
            ? UsbDeviceLockOwnerKind.AndroidStudio
            : UsbDeviceLockOwnerKind.Unknown;
    }
}
