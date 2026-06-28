using AdbSharp.Common.Devices;
using AdbSharp.Platform.Linux.Usb;
using AdbSharp.Platform.Linux.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Transport;

public sealed class LinuxUsbTransportNativeSeamTests
{
    [Fact]
    public async Task ReadAsync_retries_timeout_without_aborting()
    {
        var native = new FakeLibUsbNativeAdapter();
        native.EnqueueTransfer(LibUsbNative.ErrorTimeout, 0);
        native.EnqueueTransfer(LibUsbNative.Success, 1, "z"u8.ToArray());
        await using var transport = CreateTransport(native);
        var buffer = new byte[1];

        var bytesRead = await transport.ReadAsync(buffer);

        Assert.Equal(1, bytesRead);
        Assert.Equal((byte)'z', buffer[0]);
        Assert.Equal(0, native.CloseCount);
        Assert.True(transport.GetDiagnosticSnapshot().IsOpen);
    }

    [Fact]
    public async Task ReadAsync_non_timeout_failure_aborts_transport()
    {
        var native = new FakeLibUsbNativeAdapter();
        native.EnqueueTransfer(LibUsbNative.ErrorNoDevice, 0);
        await using var transport = CreateTransport(native);

        var exception = await Assert.ThrowsAsync<UsbTransportException>(async () => await transport.ReadAsync(new byte[1]));

        Assert.Equal(UsbTransportError.DeviceDisconnected, exception.Error);
        Assert.Equal(1, native.ReleaseInterfaceCount);
        Assert.Equal(1, native.CloseCount);
        Assert.False(transport.GetDiagnosticSnapshot().IsOpen);
    }

    [Fact]
    public async Task WriteAsync_sends_zero_length_packet_for_packet_aligned_write()
    {
        var native = new FakeLibUsbNativeAdapter();
        await using var transport = CreateTransport(native, maxPacketSize: 4);

        await transport.WriteAsync("test"u8.ToArray());

        Assert.Collection(
            native.Calls,
            call => Assert.Equal(4, call.Length),
            call => Assert.Equal(0, call.Length));
    }

    [Fact]
    public async Task ResetAsync_calls_libusb_reset_then_invalidates_transport()
    {
        var native = new FakeLibUsbNativeAdapter();
        await using var transport = CreateTransport(native);

        await transport.ResetAsync();

        Assert.Equal(1, native.ResetCount);
        Assert.Equal(1, native.ReleaseInterfaceCount);
        Assert.False(transport.GetDiagnosticSnapshot().IsOpen);
    }

    private static LinuxUsbTransport CreateTransport(FakeLibUsbNativeAdapter native, ushort maxPacketSize = 512)
    {
        var bulkIn = new UsbEndpoint(0x81, UsbEndpointDirection.In, UsbTransferKind.Bulk, maxPacketSize);
        var bulkOut = new UsbEndpoint(0x01, UsbEndpointDirection.Out, UsbTransferKind.Bulk, maxPacketSize);
        var id = new LinuxUsbTransportId(1, 2, 1, 0, 0, bulkIn, bulkOut);
        return new LinuxUsbTransport(IntPtr.Zero, new IntPtr(24), CreateDescriptor(id.Encode()), id, detachedKernelDriver: true, native);
    }

    private static UsbDeviceDescriptor CreateDescriptor(string transportId)
    {
        return new UsbDeviceDescriptor(
            transportId,
            0x18d1,
            0x4ee7,
            0,
            AndroidUsbClass.VendorSpecificClass,
            AndroidUsbClass.AndroidSubClass,
            AndroidUsbClass.AdbProtocol,
            "serial",
            "Google",
            "Pixel");
    }

    private sealed class FakeLibUsbNativeAdapter : ILibUsbNativeAdapter
    {
        private readonly Queue<TransferScript> transfers = [];

        public List<TransferCall> Calls { get; } = [];

        public int ResetCount { get; private set; }

        public int ReleaseInterfaceCount { get; private set; }

        public int AttachKernelDriverCount { get; private set; }

        public int CloseCount { get; private set; }

        public int ExitCount { get; private set; }

        public void EnqueueTransfer(int result, int transferred, byte[]? bytes = null)
        {
            transfers.Enqueue(new TransferScript(result, transferred, bytes));
        }

        public int BulkTransfer(IntPtr handle, byte endpoint, byte[] data, int length, out int transferred, uint timeout)
        {
            Calls.Add(new TransferCall(endpoint, length));
            var script = transfers.Count == 0
                ? new TransferScript(LibUsbNative.Success, length, null)
                : transfers.Dequeue();

            transferred = script.Transferred;
            script.Bytes?.CopyTo(data, 0);

            return script.Result;
        }

        public int ResetDevice(IntPtr handle)
        {
            ResetCount++;
            return LibUsbNative.Success;
        }

        public int ReleaseInterface(IntPtr handle, int interfaceNumber)
        {
            ReleaseInterfaceCount++;
            return LibUsbNative.Success;
        }

        public int AttachKernelDriver(IntPtr handle, int interfaceNumber)
        {
            AttachKernelDriverCount++;
            return LibUsbNative.Success;
        }

        public void Close(IntPtr handle)
        {
            CloseCount++;
        }

        public void Exit(IntPtr context)
        {
            ExitCount++;
        }

        public readonly record struct TransferCall(byte Endpoint, int Length);

        private readonly record struct TransferScript(int Result, int Transferred, byte[]? Bytes);
    }
}
