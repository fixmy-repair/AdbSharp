using System.Text;
using AdbSharp.Common.Devices;
using AdbSharp.Platform.Mac.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Mac.Usb;

/// <summary>
/// macOS USB provider backed by IOKit discovery.
/// </summary>
public sealed class MacUsbTransportFactory : IUsbDeviceEnumerator, IUsbTransportFactory
{
    private static readonly string[] InterfaceServiceNames = ["IOUSBHostInterface", "IOUSBInterface"];

    /// <inheritdoc />
    public string PlatformName => "macOS";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<UsbDeviceDescriptor>> FindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS())
        {
            return ValueTask.FromResult<IReadOnlyList<UsbDeviceDescriptor>>([]);
        }

        var devices = Enumerate("IOUSBHostInterface", cancellationToken);
        if (devices.Count == 0)
        {
            devices = Enumerate("IOUSBInterface", cancellationToken);
        }

        return ValueTask.FromResult<IReadOnlyList<UsbDeviceDescriptor>>(devices);
    }

    /// <inheritdoc />
    public bool CanOpen(UsbDeviceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return OperatingSystem.IsMacOS() && MacUsbTransportId.TryParse(descriptor.TransportId, out _);
    }

    /// <inheritdoc />
    public ValueTask<IUsbTransport> OpenAsync(UsbDeviceDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("macOS USB transport can only be opened on macOS.");
        }

        if (!MacUsbTransportId.TryParse(descriptor.TransportId, out var id))
        {
            throw new UsbTransportException($"Invalid macOS transport id '{descriptor.TransportId}'.");
        }

        return ValueTask.FromResult<IUsbTransport>(Open(descriptor, id, cancellationToken));
    }

    private static IUsbTransport Open(UsbDeviceDescriptor descriptor, MacUsbTransportId id, CancellationToken cancellationToken)
    {
        UsbTransportException? openFailure = null;
        foreach (var serviceName in InterfaceServiceNames)
        {
            var matching = MacNative.IOServiceMatching(serviceName);
            if (matching == IntPtr.Zero)
            {
                continue;
            }

            var result = MacNative.IOServiceGetMatchingServices(IntPtr.Zero, matching, out var iterator);
            if (result != MacNative.Success)
            {
                continue;
            }

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var service = MacNative.IOIteratorNext(iterator);
                    if (service == 0)
                    {
                        break;
                    }

                    try
                    {
                        if (!Matches(service, id))
                        {
                            continue;
                        }

                        try
                        {
                            return OpenService(service, descriptor, id);
                        }
                        catch (UsbTransportException ex)
                        {
                            openFailure = ex;
                        }
                    }
                    finally
                    {
                        _ = MacNative.IOObjectRelease(service);
                    }
                }
            }
            finally
            {
                _ = MacNative.IOObjectRelease(iterator);
            }
        }

        throw openFailure ?? new UsbTransportException($"macOS USB device '{descriptor.TransportId}' was not found.");
    }

    private static bool Matches(uint interfaceService, MacUsbTransportId id)
    {
        return ReadByteProperty(interfaceService, "bInterfaceNumber") == id.InterfaceNumber
            && ReadAncestorUIntProperty(interfaceService, "locationID") == id.LocationId
            && ReadAncestorUShortProperty(interfaceService, "idVendor") == id.VendorId
            && ReadAncestorUShortProperty(interfaceService, "idProduct") == id.ProductId;
    }

    private static IUsbTransport OpenService(uint service, UsbDeviceDescriptor descriptor, MacUsbTransportId id)
    {
        var interfacePointer = CreateUsbInterface(service);
        var opened = false;
        try
        {
            EnsureAndroidInterface(interfacePointer, id);
            Ensure(MacUsbInterface.Open(interfacePointer), "open macOS USB interface");
            opened = true;

            if (!TryGetBulkPipes(interfacePointer, out var bulkInPipe, out var bulkIn, out var bulkOutPipe, out var bulkOut))
            {
                throw new UsbTransportException("The selected macOS USB interface does not expose bulk IN and OUT pipes.");
            }

            return new MacUsbTransport(interfacePointer, descriptor, bulkIn, bulkOut, bulkInPipe, bulkOutPipe);
        }
        catch
        {
            if (opened)
            {
                _ = MacUsbInterface.Close(interfacePointer);
            }

            _ = MacUsbInterface.Release(interfacePointer);
            throw;
        }
    }

    private static IntPtr CreateUsbInterface(uint service)
    {
        var pluginType = MacNative.CreateUsbInterfaceUserClientTypeId();
        var interfaceType = MacNative.CreateCfPluginInterfaceId();
        Ensure(MacNative.IOCreatePlugInInterfaceForService(service, pluginType, interfaceType, out var pluginInterface, out _), "create macOS USB plugin interface");
        if (pluginInterface == IntPtr.Zero)
        {
            throw new UsbTransportException("IOKit returned an empty macOS USB plugin interface.");
        }

        try
        {
            var interfaceId = MacNative.CFUUIDGetUUIDBytes(MacNative.CreateUsbInterfaceInterfaceId182());
            var queryResult = MacUsbInterface.QueryInterface(pluginInterface, interfaceId, out var interfacePointer);
            if (queryResult != 0 || interfacePointer == IntPtr.Zero)
            {
                throw new UsbTransportException(UsbTransportError.Unknown, $"IOKit QueryInterface for IOUSBInterfaceInterface182 failed with HRESULT 0x{queryResult:x8}.");
            }

            return interfacePointer;
        }
        finally
        {
            _ = MacNative.IODestroyPlugInInterface(pluginInterface);
        }
    }

    private static void EnsureAndroidInterface(IntPtr interfacePointer, MacUsbTransportId id)
    {
        Ensure(MacUsbInterface.GetInterfaceClass(interfacePointer, out var interfaceClass), "read macOS USB interface class");
        Ensure(MacUsbInterface.GetInterfaceSubClass(interfacePointer, out var interfaceSubClass), "read macOS USB interface subclass");
        Ensure(MacUsbInterface.GetInterfaceProtocol(interfacePointer, out var interfaceProtocol), "read macOS USB interface protocol");
        Ensure(MacUsbInterface.GetInterfaceNumber(interfacePointer, out var interfaceNumber), "read macOS USB interface number");
        Ensure(MacUsbInterface.GetDeviceVendor(interfacePointer, out var vendorId), "read macOS USB vendor id");
        Ensure(MacUsbInterface.GetDeviceProduct(interfacePointer, out var productId), "read macOS USB product id");
        Ensure(MacUsbInterface.GetLocationId(interfacePointer, out var locationId), "read macOS USB location id");

        if (interfaceClass != AndroidUsbClass.VendorSpecificClass
            || interfaceSubClass != AndroidUsbClass.AndroidSubClass
            || interfaceProtocol is not (AndroidUsbClass.AdbProtocol or AndroidUsbClass.FastbootProtocol)
            || interfaceNumber != id.InterfaceNumber
            || vendorId != id.VendorId
            || productId != id.ProductId
            || locationId != id.LocationId)
        {
            throw new UsbTransportException("The selected macOS USB interface is not the requested Android ADB/Fastboot interface.");
        }
    }

    private static bool TryGetBulkPipes(
        IntPtr interfacePointer,
        out byte bulkInPipe,
        out UsbEndpoint bulkIn,
        out byte bulkOutPipe,
        out UsbEndpoint bulkOut)
    {
        bulkInPipe = 0;
        bulkIn = default!;
        bulkOutPipe = 0;
        bulkOut = default!;

        Ensure(MacUsbInterface.GetNumEndpoints(interfacePointer, out var endpointCount), "read macOS USB endpoint count");
        for (byte pipeReference = 1; pipeReference <= endpointCount; pipeReference++)
        {
            Ensure(MacUsbInterface.GetPipeProperties(interfacePointer, pipeReference, out var properties), "read macOS USB pipe properties");
            if (properties.TransferType != MacNative.UsbTransferTypeBulk)
            {
                continue;
            }

            if (properties.Direction == MacNative.UsbDirectionIn)
            {
                bulkInPipe = properties.PipeReference;
                bulkIn = new UsbEndpoint((byte)(MacNative.UsbEndpointInMask | properties.Number), UsbEndpointDirection.In, UsbTransferKind.Bulk, properties.MaxPacketSize);
            }
            else if (properties.Direction == MacNative.UsbDirectionOut)
            {
                bulkOutPipe = properties.PipeReference;
                bulkOut = new UsbEndpoint(properties.Number, UsbEndpointDirection.Out, UsbTransferKind.Bulk, properties.MaxPacketSize);
            }
        }

        return bulkIn is not null && bulkOut is not null;
    }

    private static List<UsbDeviceDescriptor> Enumerate(string serviceName, CancellationToken cancellationToken)
    {
        var matching = MacNative.IOServiceMatching(serviceName);
        if (matching == IntPtr.Zero)
        {
            return [];
        }

        var result = MacNative.IOServiceGetMatchingServices(IntPtr.Zero, matching, out var iterator);
        if (result != MacNative.Success)
        {
            return [];
        }

        try
        {
            var devices = new List<UsbDeviceDescriptor>();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var service = MacNative.IOIteratorNext(iterator);
                if (service == 0)
                {
                    return devices;
                }

                try
                {
                    AddDescriptor(service, devices);
                }
                finally
                {
                    _ = MacNative.IOObjectRelease(service);
                }
            }
        }
        finally
        {
            _ = MacNative.IOObjectRelease(iterator);
        }
    }

    private static void AddDescriptor(uint interfaceService, List<UsbDeviceDescriptor> devices)
    {
        var interfaceClass = ReadByteProperty(interfaceService, "bInterfaceClass");
        var interfaceSubClass = ReadByteProperty(interfaceService, "bInterfaceSubClass");
        var interfaceProtocol = ReadByteProperty(interfaceService, "bInterfaceProtocol");
        var interfaceNumber = ReadByteProperty(interfaceService, "bInterfaceNumber");

        if (interfaceClass != AndroidUsbClass.VendorSpecificClass
            || interfaceSubClass != AndroidUsbClass.AndroidSubClass
            || interfaceProtocol is not (AndroidUsbClass.AdbProtocol or AndroidUsbClass.FastbootProtocol)
            || interfaceNumber is null)
        {
            return;
        }

        var vendorId = ReadAncestorUShortProperty(interfaceService, "idVendor") ?? 0;
        var productId = ReadAncestorUShortProperty(interfaceService, "idProduct") ?? 0;
        var locationId = ReadAncestorUIntProperty(interfaceService, "locationID") ?? 0;
        var id = new MacUsbTransportId(locationId, interfaceNumber.Value, vendorId, productId);

        devices.Add(new UsbDeviceDescriptor(
            id.Encode(),
            vendorId,
            productId,
            interfaceNumber.Value,
            interfaceClass.Value,
            interfaceSubClass.Value,
            interfaceProtocol.Value,
            ReadAncestorStringProperty(interfaceService, "USB Serial Number"),
            ReadAncestorStringProperty(interfaceService, "USB Vendor Name"),
            ReadAncestorStringProperty(interfaceService, "USB Product Name")));
    }

    private static byte? ReadByteProperty(uint entry, string key)
    {
        var value = ReadIntProperty(entry, key);
        return value is >= byte.MinValue and <= byte.MaxValue ? (byte)value.Value : null;
    }

    private static ushort? ReadAncestorUShortProperty(uint entry, string key)
    {
        var value = ReadAncestorIntProperty(entry, key);
        return value is >= ushort.MinValue and <= ushort.MaxValue ? (ushort)value.Value : null;
    }

    private static uint? ReadAncestorUIntProperty(uint entry, string key)
    {
        var value = ReadAncestorIntProperty(entry, key);
        return value >= 0 ? (uint)value.Value : null;
    }

    private static int? ReadAncestorIntProperty(uint entry, string key)
    {
        for (var current = entry; current != 0;)
        {
            var value = ReadIntProperty(current, key);
            if (value is not null)
            {
                if (current != entry)
                {
                    _ = MacNative.IOObjectRelease(current);
                }

                return value;
            }

            if (MacNative.IORegistryEntryGetParentEntry(current, "IOService", out var parent) != MacNative.Success)
            {
                if (current != entry)
                {
                    _ = MacNative.IOObjectRelease(current);
                }

                return null;
            }

            if (current != entry)
            {
                _ = MacNative.IOObjectRelease(current);
            }

            current = parent;
        }

        return null;
    }

    private static string? ReadAncestorStringProperty(uint entry, string key)
    {
        for (var current = entry; current != 0;)
        {
            var value = ReadStringProperty(current, key);
            if (value is not null)
            {
                if (current != entry)
                {
                    _ = MacNative.IOObjectRelease(current);
                }

                return value;
            }

            if (MacNative.IORegistryEntryGetParentEntry(current, "IOService", out var parent) != MacNative.Success)
            {
                if (current != entry)
                {
                    _ = MacNative.IOObjectRelease(current);
                }

                return null;
            }

            if (current != entry)
            {
                _ = MacNative.IOObjectRelease(current);
            }

            current = parent;
        }

        return null;
    }

    private static int? ReadIntProperty(uint entry, string key)
    {
        var property = CreateProperty(entry, key);
        if (property == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return MacNative.CFGetTypeID(property) == MacNative.CFNumberGetTypeID()
                && MacNative.CFNumberGetValue(property, MacNative.SInt32NumberType, out var value)
                    ? value
                    : null;
        }
        finally
        {
            MacNative.CFRelease(property);
        }
    }

    private static string? ReadStringProperty(uint entry, string key)
    {
        var property = CreateProperty(entry, key);
        if (property == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (MacNative.CFGetTypeID(property) != MacNative.CFStringGetTypeID())
            {
                return null;
            }

            var buffer = new byte[512];
            if (!MacNative.CFStringGetCString(property, buffer, buffer.Length, MacNative.Utf8Encoding))
            {
                return null;
            }

            var terminator = Array.IndexOf(buffer, (byte)0);
            return Encoding.UTF8.GetString(buffer, 0, terminator >= 0 ? terminator : buffer.Length);
        }
        finally
        {
            MacNative.CFRelease(property);
        }
    }

    private static IntPtr CreateProperty(uint entry, string key)
    {
        var keyString = MacNative.CFStringCreateWithCString(IntPtr.Zero, key, MacNative.Utf8Encoding);
        if (keyString == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            return MacNative.IORegistryEntryCreateCFProperty(entry, keyString, IntPtr.Zero, 0);
        }
        finally
        {
            MacNative.CFRelease(keyString);
        }
    }

    private static void Ensure(uint result, string operation)
    {
        if (result != MacNative.Success)
        {
            throw MacUsbErrors.Create(result, operation);
        }
    }
}
