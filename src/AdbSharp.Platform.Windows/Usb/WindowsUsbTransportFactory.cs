using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AdbSharp.Common.Devices;
using AdbSharp.Platform.Windows.Usb.Native;
using AdbSharp.Transport.Usb;
using Microsoft.Win32.SafeHandles;

namespace AdbSharp.Platform.Windows.Usb;

/// <summary>
/// Windows USB provider backed by SetupAPI and WinUSB.
/// </summary>
public sealed partial class WindowsUsbTransportFactory : IUsbDeviceEnumerator, IUsbTransportFactory
{
    private static readonly Guid AndroidWinUsbInterfaceGuid = new("F72FE0D4-CBCB-407d-8814-9ED673D0DD6B");

    /// <inheritdoc />
    public string PlatformName => "Windows";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<UsbDeviceDescriptor>> FindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult<IReadOnlyList<UsbDeviceDescriptor>>([]);
        }

        return ValueTask.FromResult<IReadOnlyList<UsbDeviceDescriptor>>(Enumerate(cancellationToken));
    }

    /// <inheritdoc />
    public bool CanOpen(UsbDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return OperatingSystem.IsWindows() && WindowsUsbTransportId.TryParse(descriptor.TransportId, out _);
    }

    /// <inheritdoc />
    public ValueTask<IUsbTransport> OpenAsync(UsbDeviceDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows USB transport can only be opened on Windows.");
        }

        if (!WindowsUsbTransportId.TryParse(descriptor.TransportId, out var id))
        {
            throw new UsbTransportException($"Invalid Windows transport id '{descriptor.TransportId}'.");
        }

        var opened = OpenWinUsb(id.DevicePath);
        try
        {
            if (!TryQueryAndroidInterface(opened.WinUsbHandle, out _, out var bulkIn, out var bulkOut))
            {
                throw new UsbTransportException("The selected WinUSB interface is not an Android ADB/Fastboot interface.");
            }

            ConfigurePipeTimeout(opened.WinUsbHandle, bulkIn);
            ConfigurePipeTimeout(opened.WinUsbHandle, bulkOut);
            return ValueTask.FromResult<IUsbTransport>(new WindowsUsbTransport(opened.DeviceHandle, opened.WinUsbHandle, descriptor, bulkIn, bulkOut));
        }
        catch
        {
            opened.Dispose();
            throw;
        }
    }

    private static IReadOnlyList<UsbDeviceDescriptor> Enumerate(CancellationToken cancellationToken)
    {
        var deviceInfoSet = WindowsUsbNative.SetupDiGetClassDevs(
            AndroidWinUsbInterfaceGuid,
            null,
            IntPtr.Zero,
            WindowsUsbNative.DigcfPresent | WindowsUsbNative.DigcfDeviceInterface);

        if (deviceInfoSet == WindowsUsbNative.InvalidHandleValue)
        {
            throw WindowsUsbErrors.Create("SetupDiGetClassDevs failed.");
        }

        try
        {
            var devices = new List<UsbDeviceDescriptor>();
            for (uint index = 0; ; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var interfaceData = new WindowsUsbNative.DeviceInterfaceData
                {
                    Size = Marshal.SizeOf<WindowsUsbNative.DeviceInterfaceData>()
                };

                if (!WindowsUsbNative.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, AndroidWinUsbInterfaceGuid, index, ref interfaceData))
                {
                    break;
                }

                var path = GetDevicePath(deviceInfoSet, ref interfaceData);
                using var opened = OpenWinUsb(path);
                if (!TryQueryAndroidInterface(opened.WinUsbHandle, out var interfaceDescriptor, out _, out _))
                {
                    continue;
                }

                var id = new WindowsUsbTransportId(path);
                var (vendorId, productId, interfaceNumber, serial) = ParseDevicePath(path);
                devices.Add(new UsbDeviceDescriptor(
                    id.Encode(),
                    vendorId,
                    productId,
                    interfaceNumber ?? interfaceDescriptor.InterfaceNumber,
                    interfaceDescriptor.InterfaceClass,
                    interfaceDescriptor.InterfaceSubClass,
                    interfaceDescriptor.InterfaceProtocol,
                    serial,
                    null,
                    null));
            }

            return devices;
        }
        finally
        {
            _ = WindowsUsbNative.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static string GetDevicePath(IntPtr deviceInfoSet, ref WindowsUsbNative.DeviceInterfaceData interfaceData)
    {
        _ = WindowsUsbNative.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
        var detailData = Marshal.AllocHGlobal(requiredSize);
        try
        {
            Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);
            if (!WindowsUsbNative.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailData, requiredSize, out _, IntPtr.Zero))
            {
                throw WindowsUsbErrors.Create("SetupDiGetDeviceInterfaceDetail failed.");
            }

            return Marshal.PtrToStringUni(IntPtr.Add(detailData, 4))
                ?? throw new UsbTransportException("SetupAPI returned an empty device path.");
        }
        finally
        {
            Marshal.FreeHGlobal(detailData);
        }
    }

    private static OpenedWinUsb OpenWinUsb(string path)
    {
        var deviceHandle = WindowsUsbNative.CreateFile(
            path,
            WindowsUsbNative.GenericRead | WindowsUsbNative.GenericWrite,
            WindowsUsbNative.FileShareRead | WindowsUsbNative.FileShareWrite,
            IntPtr.Zero,
            WindowsUsbNative.OpenExisting,
            WindowsUsbNative.FileAttributeNormal | WindowsUsbNative.FileFlagOverlapped,
            IntPtr.Zero);

        if (deviceHandle.IsInvalid)
        {
            throw WindowsUsbErrors.Create($"Failed to open WinUSB device '{path}'.");
        }

        if (!WindowsUsbNative.WinUsbInitialize(deviceHandle, out var winUsbHandle))
        {
            deviceHandle.Dispose();
            throw WindowsUsbErrors.Create("WinUsb_Initialize failed.");
        }

        return new OpenedWinUsb(deviceHandle, winUsbHandle);
    }

    private static bool TryQueryAndroidInterface(
        IntPtr winUsbHandle,
        out WindowsUsbNative.UsbInterfaceDescriptor descriptor,
        out UsbEndpoint bulkIn,
        out UsbEndpoint bulkOut)
    {
        bulkIn = default!;
        bulkOut = default!;

        if (!WindowsUsbNative.WinUsbQueryInterfaceSettings(winUsbHandle, 0, out descriptor))
        {
            throw WindowsUsbErrors.Create("WinUsb_QueryInterfaceSettings failed.");
        }

        if (descriptor.InterfaceClass != AndroidUsbClass.VendorSpecificClass
            || descriptor.InterfaceSubClass != AndroidUsbClass.AndroidSubClass
            || descriptor.InterfaceProtocol is not (AndroidUsbClass.AdbProtocol or AndroidUsbClass.FastbootProtocol))
        {
            return false;
        }

        for (byte pipeIndex = 0; pipeIndex < descriptor.NumEndpoints; pipeIndex++)
        {
            if (!WindowsUsbNative.WinUsbQueryPipe(winUsbHandle, 0, pipeIndex, out var pipe))
            {
                throw WindowsUsbErrors.Create("WinUsb_QueryPipe failed.");
            }

            if (pipe.PipeType != WindowsUsbNative.UsbdPipeTypeBulk)
            {
                continue;
            }

            if ((pipe.PipeId & WindowsUsbNative.UsbEndpointDirectionMask) == WindowsUsbNative.UsbEndpointDirectionMask)
            {
                bulkIn = new UsbEndpoint(pipe.PipeId, UsbEndpointDirection.In, UsbTransferKind.Bulk, pipe.MaximumPacketSize);
            }
            else
            {
                bulkOut = new UsbEndpoint(pipe.PipeId, UsbEndpointDirection.Out, UsbTransferKind.Bulk, pipe.MaximumPacketSize);
            }
        }

        return bulkIn is not null && bulkOut is not null;
    }

    private static void ConfigurePipeTimeout(IntPtr winUsbHandle, UsbEndpoint endpoint)
    {
        var timeout = WindowsUsbNative.TransferTimeoutMilliseconds;
        if (!WindowsUsbNative.WinUsbSetPipePolicy(
            winUsbHandle,
            endpoint.Address,
            WindowsUsbNative.PipeTransferTimeoutPolicy,
            sizeof(uint),
            ref timeout))
        {
            throw WindowsUsbErrors.Create("WinUsb_SetPipePolicy PIPE_TRANSFER_TIMEOUT failed.");
        }
    }

    private static (ushort VendorId, ushort ProductId, byte? InterfaceNumber, string? Serial) ParseDevicePath(string path)
    {
        var match = WindowsDevicePathRegex().Match(path);
        var vendor = match.Groups["vid"].Success ? Convert.ToUInt16(match.Groups["vid"].Value, 16) : (ushort)0;
        var product = match.Groups["pid"].Success ? Convert.ToUInt16(match.Groups["pid"].Value, 16) : (ushort)0;
        var interfaceNumber = match.Groups["mi"].Success ? Convert.ToByte(match.Groups["mi"].Value, 16) : (byte?)null;
        var serial = match.Groups["serial"].Success ? match.Groups["serial"].Value : null;
        return (vendor, product, interfaceNumber, serial);
    }

    [GeneratedRegex(@"vid_(?<vid>[0-9a-f]{4})&pid_(?<pid>[0-9a-f]{4})(?:&mi_(?<mi>[0-9a-f]{2}))?#(?<serial>[^#\\]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsDevicePathRegex();

    private readonly struct OpenedWinUsb(SafeFileHandle deviceHandle, IntPtr winUsbHandle) : IDisposable
    {
        public SafeFileHandle DeviceHandle { get; } = deviceHandle;

        public IntPtr WinUsbHandle { get; } = winUsbHandle;

        public void Dispose()
        {
            if (WinUsbHandle != IntPtr.Zero)
            {
                _ = WindowsUsbNative.WinUsbFree(WinUsbHandle);
            }

            DeviceHandle.Dispose();
        }
    }
}
