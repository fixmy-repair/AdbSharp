using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AdbSharp.Platform.Mac.Usb;
using AdbSharp.Platform.Mac.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Transport;

public sealed class MacUsbInterfaceTests
{
    [Fact]
    public void CfUuidBytesUsesNativeSize()
    {
        Assert.Equal(16, Marshal.SizeOf<MacCfUuidBytes>());
    }

    [Fact]
    public void EndpointPropertiesUsesPackedNativeSize()
    {
        Assert.Equal(15, Marshal.SizeOf<MacUsbEndpointProperties>());
    }

    [Fact]
    public void NotRespondingMapsToRetryableTimeout()
    {
        Assert.Equal(UsbTransportError.Timeout, MacUsbErrors.Map(MacNative.IoReturnNotResponding));
    }

    [Fact]
    public void NotReadyMapsToRetryableTimeout()
    {
        Assert.Equal(UsbTransportError.Timeout, MacUsbErrors.Map(MacNative.IoReturnNotReady));
    }

    [Fact]
    public void UnknownPipeMapsToInvalidEndpoint()
    {
        Assert.Equal(UsbTransportError.InvalidEndpoint, MacUsbErrors.Map(MacNative.UsbReturnUnknownPipe));
    }

    [Fact]
    public void HostDescriptorParserReadsSelectedBulkEndpoints()
    {
        byte[] configurationDescriptor =
        [
            9, 2, 32, 0, 1, 1, 0, 0x80, 50,
            9, 4, 4, 0, 2, 0xff, 0x42, 1, 0,
            7, 5, 0x05, 2, 0, 2, 0,
            7, 5, 0x86, 2, 0, 2, 0
        ];

        var result = MacUsbHostDescriptors.TryReadBulkEndpoints(configurationDescriptor, 4, 0, out var bulkIn, out var bulkOut);

        Assert.True(result);
        Assert.Equal(0x86, bulkIn.Address);
        Assert.Equal(UsbEndpointDirection.In, bulkIn.Direction);
        Assert.Equal(512, bulkIn.MaxPacketSize);
        Assert.Equal(0x05, bulkOut.Address);
        Assert.Equal(UsbEndpointDirection.Out, bulkOut.Direction);
        Assert.Equal(512, bulkOut.MaxPacketSize);
    }

    [Fact]
    public void HostDescriptorParserIgnoresOtherAlternateSettings()
    {
        byte[] configurationDescriptor =
        [
            9, 2, 32, 0, 1, 1, 0, 0x80, 50,
            9, 4, 4, 1, 2, 0xff, 0x42, 1, 0,
            7, 5, 0x05, 2, 0, 2, 0,
            7, 5, 0x86, 2, 0, 2, 0
        ];

        var result = MacUsbHostDescriptors.TryReadBulkEndpoints(configurationDescriptor, 4, 0, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void QueryInterfaceUsesComSlotAfterReservedPointer()
    {
        var table = Marshal.AllocHGlobal(IntPtr.Size * 2);
        var instance = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(table, 0, IntPtr.Zero);
            Marshal.WriteIntPtr(
                table,
                IntPtr.Size,
                (IntPtr)(delegate* unmanaged<IntPtr, MacCfUuidBytes, IntPtr*, int>)&QueryInterfaceStub);
            Marshal.WriteIntPtr(instance, table);

            var status = MacUsbInterface.QueryInterface(instance, default, out var result);

            Assert.Equal(0, status);
            Assert.Equal(new IntPtr(0x1234), result);
        }
        finally
        {
            Marshal.FreeHGlobal(instance);
            Marshal.FreeHGlobal(table);
        }
    }

    [Fact]
    public void QueryInterfaceRejectsNullInterfacePointer()
    {
        var exception = Assert.Throws<UsbTransportException>(() =>
            MacUsbInterface.QueryInterface(IntPtr.Zero, default, out _));

        Assert.Equal(UsbTransportError.Unknown, exception.Error);
    }

    [UnmanagedCallersOnly]
    private static unsafe int QueryInterfaceStub(IntPtr interfacePointer, MacCfUuidBytes interfaceId, IntPtr* result)
    {
        _ = interfacePointer;
        _ = interfaceId;
        *result = new IntPtr(0x1234);
        return 0;
    }
}
