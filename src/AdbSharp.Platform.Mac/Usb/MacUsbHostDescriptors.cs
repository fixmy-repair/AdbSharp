using System.Buffers.Binary;
using AdbSharp.Platform.Mac.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Mac.Usb;

internal static unsafe class MacUsbHostDescriptors
{
    private const byte ConfigurationDescriptorType = 2;
    private const byte InterfaceDescriptorType = 4;
    private const byte EndpointDescriptorType = 5;
    private const byte EndpointDirectionInMask = 0x80;
    private const byte EndpointTransferTypeMask = 0x03;
    private const byte EndpointTransferTypeBulk = 0x02;

    public static bool TryReadBulkEndpoints(
        IntPtr usbHostInterface,
        out UsbEndpoint bulkIn,
        out UsbEndpoint bulkOut)
    {
        bulkIn = default!;
        bulkOut = default!;

        var interfaceDescriptor = MacObjC.GetInterfaceDescriptor(usbHostInterface);
        var configurationDescriptor = MacObjC.GetConfigurationDescriptor(usbHostInterface);
        if (interfaceDescriptor == IntPtr.Zero || configurationDescriptor == IntPtr.Zero)
        {
            return false;
        }

        var interfaceBytes = new ReadOnlySpan<byte>((void*)interfaceDescriptor, 9);
        if (interfaceBytes[0] < 9 || interfaceBytes[1] != InterfaceDescriptorType)
        {
            return false;
        }

        var selectedInterfaceNumber = interfaceBytes[2];
        var selectedAlternateSetting = interfaceBytes[3];
        var configurationHeader = new ReadOnlySpan<byte>((void*)configurationDescriptor, 9);
        if (configurationHeader[0] < 9 || configurationHeader[1] != ConfigurationDescriptorType)
        {
            return false;
        }

        var totalLength = BinaryPrimitives.ReadUInt16LittleEndian(configurationHeader.Slice(2, 2));
        if (totalLength < 9)
        {
            return false;
        }

        return TryReadBulkEndpoints(
            new ReadOnlySpan<byte>((void*)configurationDescriptor, totalLength),
            selectedInterfaceNumber,
            selectedAlternateSetting,
            out bulkIn,
            out bulkOut);
    }

    public static bool TryReadAlternateSetting(IntPtr usbHostInterface, out byte alternateSetting)
    {
        alternateSetting = 0;
        var interfaceDescriptor = MacObjC.GetInterfaceDescriptor(usbHostInterface);
        if (interfaceDescriptor == IntPtr.Zero)
        {
            return false;
        }

        var interfaceBytes = new ReadOnlySpan<byte>((void*)interfaceDescriptor, 9);
        if (interfaceBytes[0] < 9 || interfaceBytes[1] != InterfaceDescriptorType)
        {
            return false;
        }

        alternateSetting = interfaceBytes[3];
        return true;
    }

    public static bool TryReadBulkEndpoints(
        ReadOnlySpan<byte> configurationDescriptor,
        byte selectedInterfaceNumber,
        byte selectedAlternateSetting,
        out UsbEndpoint bulkIn,
        out UsbEndpoint bulkOut)
    {
        bulkIn = default!;
        bulkOut = default!;

        var selected = false;
        for (var offset = 0; offset + 2 <= configurationDescriptor.Length;)
        {
            var length = configurationDescriptor[offset];
            var descriptorType = configurationDescriptor[offset + 1];
            if (length < 2 || offset + length > configurationDescriptor.Length)
            {
                return false;
            }

            var descriptor = configurationDescriptor.Slice(offset, length);
            if (descriptorType == InterfaceDescriptorType && length >= 9)
            {
                selected = descriptor[2] == selectedInterfaceNumber && descriptor[3] == selectedAlternateSetting;
            }
            else if (selected && descriptorType == EndpointDescriptorType && length >= 7)
            {
                AddEndpoint(descriptor, ref bulkIn, ref bulkOut);
            }

            offset += length;
        }

        return bulkIn is not null && bulkOut is not null;
    }

    private static void AddEndpoint(ReadOnlySpan<byte> descriptor, ref UsbEndpoint bulkIn, ref UsbEndpoint bulkOut)
    {
        if ((descriptor[3] & EndpointTransferTypeMask) != EndpointTransferTypeBulk)
        {
            return;
        }

        var address = descriptor[2];
        var maxPacketSize = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(4, 2));
        if ((address & EndpointDirectionInMask) != 0)
        {
            bulkIn = new UsbEndpoint(address, UsbEndpointDirection.In, UsbTransferKind.Bulk, maxPacketSize);
        }
        else
        {
            bulkOut = new UsbEndpoint(address, UsbEndpointDirection.Out, UsbTransferKind.Bulk, maxPacketSize);
        }
    }
}
