using System.Globalization;
using AdbSharp.Common.Devices;
using AdbSharp.Platform.Linux.Usb.Locking;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Linux.Usb;

/// <summary>
/// Resolves Linux processes that hold Android USB device nodes.
/// </summary>
public sealed class LinuxUsbDeviceLockOwnerResolver : IUsbDeviceLockOwnerResolver
{
    private readonly ILinuxProcFileSystem fileSystem;
    private readonly bool requireLinux;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxUsbDeviceLockOwnerResolver" /> class.
    /// </summary>
    public LinuxUsbDeviceLockOwnerResolver()
        : this(new LinuxProcFileSystem(), requireLinux: true)
    {
    }

    internal LinuxUsbDeviceLockOwnerResolver(ILinuxProcFileSystem fileSystem, bool requireLinux = true)
    {
        this.fileSystem = fileSystem;
        this.requireLinux = requireLinux;
    }

    /// <inheritdoc />
    public string PlatformName => "Linux";

    /// <inheritdoc />
    public bool CanResolve(UsbDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return OperatingSystem.IsLinux() && LinuxUsbTransportId.TryParse(descriptor.TransportId, out _);
    }

    /// <inheritdoc />
    public ValueTask<UsbDeviceLockOwnerResolution> ResolveAsync(
        UsbDeviceDescriptor descriptor,
        UsbTransportException? openFailure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        if (!LinuxUsbTransportId.TryParse(descriptor.TransportId, out var id))
        {
            return ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.Unsupported,
                message: $"Invalid Linux USB transport id '{descriptor.TransportId}'."));
        }

        var deviceNode = GetDeviceNode(id);
        if (requireLinux && !OperatingSystem.IsLinux())
        {
            return ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.Unsupported,
                deviceNode,
                "Linux USB lock owner resolution is only available on Linux."));
        }

        if (!fileSystem.DirectoryExists("/proc"))
        {
            return ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.Unsupported,
                deviceNode,
                "The /proc filesystem is not available."));
        }

        try
        {
            var owners = new List<UsbDeviceLockOwner>();
            var seen = new HashSet<int>();
            var partial = false;
            var deviceIdentity = fileSystem.GetIdentity(deviceNode);
            foreach (var processId in fileSystem.EnumerateProcessIds())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matched = false;
                try
                {
                    foreach (var descriptorPath in fileSystem.EnumerateFileDescriptors(processId))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var target = fileSystem.ResolveLinkTarget(descriptorPath);
                        if (IsDeviceMatch(deviceNode, deviceIdentity, descriptorPath, target))
                        {
                            matched = true;
                            break;
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    partial = true;
                }
                catch (IOException)
                {
                    partial = true;
                }

                if (!matched || !seen.Add(processId))
                {
                    continue;
                }

                var processName = fileSystem.ReadProcessName(processId);
                var executablePath = fileSystem.ReadExecutablePath(processId);
                owners.Add(new UsbDeviceLockOwner(
                    processId,
                    processName,
                    executablePath,
                    ClassifyProcess(processName, executablePath),
                    UsbDeviceLockOwnerConfidence.Exact,
                    deviceNode));
            }

            var status = owners.Count > 0
                ? UsbDeviceLockOwnerResolutionStatus.Resolved
                : partial
                    ? UsbDeviceLockOwnerResolutionStatus.Partial
                    : UsbDeviceLockOwnerResolutionStatus.NoOwnerFound;
            var message = partial ? "Some /proc entries could not be inspected." : null;
            return ValueTask.FromResult(new UsbDeviceLockOwnerResolution(descriptor, status, owners, deviceNode, message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.AccessDenied,
                deviceNode,
                ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ValueTask.FromResult(UsbDeviceLockOwnerResolution.Empty(
                descriptor,
                UsbDeviceLockOwnerResolutionStatus.Failed,
                deviceNode,
                ex.Message));
        }
    }

    private static string GetDeviceNode(LinuxUsbTransportId id)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"/dev/bus/usb/{id.BusNumber:000}/{id.DeviceAddress:000}");
    }

    private bool IsDeviceMatch(
        string deviceNode,
        LinuxFileIdentity? deviceIdentity,
        string descriptorPath,
        string? target)
    {
        if (string.Equals(target, deviceNode, StringComparison.Ordinal))
        {
            return true;
        }

        if (deviceIdentity is null)
        {
            return false;
        }

        var descriptorIdentity = fileSystem.GetIdentity(descriptorPath);
        return descriptorIdentity == deviceIdentity;
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
