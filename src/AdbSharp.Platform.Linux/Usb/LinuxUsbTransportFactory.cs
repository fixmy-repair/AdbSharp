using System.Runtime.InteropServices;
using System.Text;
using AdbSharp.Common.Devices;
using AdbSharp.Platform.Linux.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Linux.Usb;

/// <summary>
/// Linux USB provider backed by libusb.
/// </summary>
public sealed class LinuxUsbTransportFactory : IUsbDeviceEnumerator, IUsbTransportFactory
{
    /// <inheritdoc />
    public string PlatformName => "Linux";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<UsbDeviceDescriptor>> FindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux())
        {
            return ValueTask.FromResult<IReadOnlyList<UsbDeviceDescriptor>>([]);
        }

        try
        {
            return ValueTask.FromResult<IReadOnlyList<UsbDeviceDescriptor>>(Enumerate(cancellationToken));
        }
        catch (DllNotFoundException ex)
        {
            throw new UsbTransportException(UsbTransportError.PlatformDependencyMissing, "libusb-1.0 is required for Linux USB discovery.", ex);
        }
    }

    /// <inheritdoc />
    public bool CanOpen(UsbDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return OperatingSystem.IsLinux() && LinuxUsbTransportId.TryParse(descriptor.TransportId, out _);
    }

    /// <inheritdoc />
    public ValueTask<IUsbTransport> OpenAsync(UsbDeviceDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Linux USB transport can only be opened on Linux.");
        }

        if (!LinuxUsbTransportId.TryParse(descriptor.TransportId, out var id))
        {
            throw new UsbTransportException($"Invalid Linux transport id '{descriptor.TransportId}'.");
        }

        try
        {
            return ValueTask.FromResult<IUsbTransport>(Open(descriptor, id, cancellationToken));
        }
        catch (DllNotFoundException ex)
        {
            throw new UsbTransportException(UsbTransportError.PlatformDependencyMissing, "libusb-1.0 is required for Linux USB transport.", ex);
        }
    }

    private static IReadOnlyList<UsbDeviceDescriptor> Enumerate(CancellationToken cancellationToken)
    {
        Ensure(LibUsbNative.libusb_init(out var context), "initialize libusb");
        try
        {
            var count = LibUsbNative.libusb_get_device_list(context, out var list);
            if (count < 0)
            {
                throw LinuxUsbErrors.Create(checked((int)count), "enumerate Linux USB devices");
            }

            try
            {
                var devices = new List<UsbDeviceDescriptor>();
                for (nint index = 0; index < count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var device = Marshal.ReadIntPtr(list, checked((int)index * IntPtr.Size));
                    if (device != IntPtr.Zero)
                    {
                        AddDeviceDescriptors(context, device, devices);
                    }
                }

                return devices;
            }
            finally
            {
                LibUsbNative.libusb_free_device_list(list, 1);
            }
        }
        finally
        {
            LibUsbNative.libusb_exit(context);
        }
    }

    private static IUsbTransport Open(UsbDeviceDescriptor descriptor, LinuxUsbTransportId id, CancellationToken cancellationToken)
    {
        Ensure(LibUsbNative.libusb_init(out var context), "initialize libusb");
        try
        {
            var count = LibUsbNative.libusb_get_device_list(context, out var list);
            if (count < 0)
            {
                throw LinuxUsbErrors.Create(checked((int)count), "enumerate Linux USB devices");
            }

            try
            {
                for (nint index = 0; index < count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var device = Marshal.ReadIntPtr(list, checked((int)index * IntPtr.Size));
                    if (device == IntPtr.Zero
                        || LibUsbNative.libusb_get_bus_number(device) != id.BusNumber
                        || LibUsbNative.libusb_get_device_address(device) != id.DeviceAddress)
                    {
                        continue;
                    }

                    Ensure(LibUsbNative.libusb_open(device, out var handle), "open USB device");
                    var detached = false;
                    try
                    {
                        var setConfiguration = LibUsbNative.libusb_set_configuration(handle, id.ConfigurationValue);
                        if (setConfiguration is not (LibUsbNative.Success or LibUsbNative.ErrorBusy))
                        {
                            Ensure(setConfiguration, "set USB configuration");
                        }

                        if (LibUsbNative.libusb_kernel_driver_active(handle, id.InterfaceNumber) == 1)
                        {
                            Ensure(LibUsbNative.libusb_detach_kernel_driver(handle, id.InterfaceNumber), "detach kernel USB driver");
                            detached = true;
                        }

                        Ensure(LibUsbNative.libusb_claim_interface(handle, id.InterfaceNumber), "claim USB interface");
                        if (id.AlternateSetting != 0)
                        {
                            Ensure(LibUsbNative.libusb_set_interface_alt_setting(handle, id.InterfaceNumber, id.AlternateSetting), "set USB alternate interface");
                        }

                        return new LinuxUsbTransport(context, handle, descriptor, id, detached);
                    }
                    catch
                    {
                        if (detached)
                        {
                            _ = LibUsbNative.libusb_attach_kernel_driver(handle, id.InterfaceNumber);
                        }

                        LibUsbNative.libusb_close(handle);
                        throw;
                    }
                }
            }
            finally
            {
                LibUsbNative.libusb_free_device_list(list, 1);
            }

            throw new UsbTransportException(UsbTransportError.DeviceNotFound, $"Linux USB device '{descriptor.TransportId}' was not found.");
        }
        catch
        {
            LibUsbNative.libusb_exit(context);
            throw;
        }
    }

    private static void AddDeviceDescriptors(IntPtr context, IntPtr device, List<UsbDeviceDescriptor> devices)
    {
        if (LibUsbNative.libusb_get_device_descriptor(device, out var deviceDescriptor) != LibUsbNative.Success)
        {
            return;
        }

        for (byte configIndex = 0; configIndex < deviceDescriptor.NumConfigurations; configIndex++)
        {
            if (LibUsbNative.libusb_get_config_descriptor(device, configIndex, out var configPointer) != LibUsbNative.Success)
            {
                continue;
            }

            try
            {
                var config = Marshal.PtrToStructure<LibUsbNative.ConfigDescriptor>(configPointer);
                for (var interfaceIndex = 0; interfaceIndex < config.NumInterfaces; interfaceIndex++)
                {
                    var interfacePointer = IntPtr.Add(config.Interfaces, interfaceIndex * Marshal.SizeOf<LibUsbNative.Interface>());
                    var usbInterface = Marshal.PtrToStructure<LibUsbNative.Interface>(interfacePointer);
                    for (var alternateIndex = 0; alternateIndex < usbInterface.NumAltSettings; alternateIndex++)
                    {
                        var descriptorPointer = IntPtr.Add(usbInterface.AltSettings, alternateIndex * Marshal.SizeOf<LibUsbNative.InterfaceDescriptor>());
                        var interfaceDescriptor = Marshal.PtrToStructure<LibUsbNative.InterfaceDescriptor>(descriptorPointer);
                        if (!IsAndroidInterface(interfaceDescriptor)
                            || !TryGetBulkEndpoints(interfaceDescriptor, out var bulkIn, out var bulkOut))
                        {
                            continue;
                        }

                        var id = new LinuxUsbTransportId(
                            LibUsbNative.libusb_get_bus_number(device),
                            LibUsbNative.libusb_get_device_address(device),
                            config.ConfigurationValue,
                            interfaceDescriptor.InterfaceNumber,
                            interfaceDescriptor.AlternateSetting,
                            bulkIn,
                            bulkOut);

                        using var opened = TryOpenDevice(context, device);
                        devices.Add(new UsbDeviceDescriptor(
                            id.Encode(),
                            deviceDescriptor.VendorId,
                            deviceDescriptor.ProductId,
                            interfaceDescriptor.InterfaceNumber,
                            interfaceDescriptor.InterfaceClass,
                            interfaceDescriptor.InterfaceSubClass,
                            interfaceDescriptor.InterfaceProtocol,
                            ReadString(opened.Handle, deviceDescriptor.SerialNumberIndex),
                            ReadString(opened.Handle, deviceDescriptor.ManufacturerIndex),
                            ReadString(opened.Handle, deviceDescriptor.ProductIndex)));
                    }
                }
            }
            finally
            {
                LibUsbNative.libusb_free_config_descriptor(configPointer);
            }
        }
    }

    private static bool IsAndroidInterface(LibUsbNative.InterfaceDescriptor descriptor)
    {
        return descriptor.InterfaceClass == AndroidUsbClass.VendorSpecificClass
            && descriptor.InterfaceSubClass == AndroidUsbClass.AndroidSubClass
            && descriptor.InterfaceProtocol is AndroidUsbClass.AdbProtocol or AndroidUsbClass.FastbootProtocol;
    }

    private static bool TryGetBulkEndpoints(LibUsbNative.InterfaceDescriptor descriptor, out UsbEndpoint bulkIn, out UsbEndpoint bulkOut)
    {
        bulkIn = default!;
        bulkOut = default!;

        for (var endpointIndex = 0; endpointIndex < descriptor.NumEndpoints; endpointIndex++)
        {
            var endpointPointer = IntPtr.Add(descriptor.Endpoints, endpointIndex * Marshal.SizeOf<LibUsbNative.EndpointDescriptor>());
            var endpoint = Marshal.PtrToStructure<LibUsbNative.EndpointDescriptor>(endpointPointer);
            if ((endpoint.Attributes & LibUsbNative.TransferTypeMask) != LibUsbNative.TransferTypeBulk)
            {
                continue;
            }

            if ((endpoint.EndpointAddress & LibUsbNative.EndpointIn) == LibUsbNative.EndpointIn)
            {
                bulkIn = new UsbEndpoint(endpoint.EndpointAddress, UsbEndpointDirection.In, UsbTransferKind.Bulk, endpoint.MaxPacketSize);
            }
            else
            {
                bulkOut = new UsbEndpoint(endpoint.EndpointAddress, UsbEndpointDirection.Out, UsbTransferKind.Bulk, endpoint.MaxPacketSize);
            }
        }

        return bulkIn is not null && bulkOut is not null;
    }

    private static OpenedHandle TryOpenDevice(IntPtr context, IntPtr device)
    {
        _ = context;
        return LibUsbNative.libusb_open(device, out var handle) == LibUsbNative.Success
            ? new OpenedHandle(handle)
            : new OpenedHandle(IntPtr.Zero);
    }

    private static string? ReadString(IntPtr handle, byte descriptorIndex)
    {
        if (handle == IntPtr.Zero || descriptorIndex == 0)
        {
            return null;
        }

        var buffer = new byte[256];
        var length = LibUsbNative.libusb_get_string_descriptor_ascii(handle, descriptorIndex, buffer, buffer.Length);
        return length > 0 ? Encoding.UTF8.GetString(buffer, 0, length) : null;
    }

    private static void Ensure(int result, string operation)
    {
        if (result != LibUsbNative.Success)
        {
            throw LinuxUsbErrors.Create(result, operation);
        }
    }

    private readonly struct OpenedHandle(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; } = handle;

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                LibUsbNative.libusb_close(Handle);
            }
        }
    }
}
