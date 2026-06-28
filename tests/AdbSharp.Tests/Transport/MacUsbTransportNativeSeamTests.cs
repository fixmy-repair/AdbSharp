using AdbSharp.Common.Devices;
using AdbSharp.Platform.Mac.Usb;
using AdbSharp.Platform.Mac.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Transport;

public sealed class MacUsbTransportNativeSeamTests
{
    [Fact]
    public async Task IOUSBLib_WriteAsync_sends_zero_length_packet_for_packet_aligned_write()
    {
        var native = new FakeMacUsbInterfaceAdapter();
        await using var transport = CreateLegacyTransport(native, maxPacketSize: 4);

        await transport.WriteAsync("test"u8.ToArray());

        Assert.Collection(
            native.Writes,
            write => Assert.Equal(4u, write.Length),
            write => Assert.Equal(0u, write.Length));
    }

    [Fact]
    public async Task IOUSBLib_WriteAsync_non_timeout_failure_aborts_transport()
    {
        var native = new FakeMacUsbInterfaceAdapter();
        native.EnqueueWriteResult(MacNative.IoReturnNoDevice);
        await using var transport = CreateLegacyTransport(native);

        var exception = await Assert.ThrowsAsync<UsbTransportException>(async () => await transport.WriteAsync("x"u8.ToArray()));

        Assert.Equal(UsbTransportError.DeviceDisconnected, exception.Error);
        Assert.True(native.AbortPipeCount >= 2);
        Assert.False(transport.GetDiagnosticSnapshot().IsOpen);
    }

    [Fact]
    public async Task IOUSBHost_WriteAsync_sends_zero_length_packet_for_packet_aligned_write()
    {
        var native = new FakeMacUsbHostNativeAdapter();
        await using var transport = CreateHostTransport(native, maxPacketSize: 4);

        await transport.WriteAsync("test"u8.ToArray());

        Assert.Collection(
            native.SendIoRequestLengths,
            length => Assert.Equal(4u, length),
            length => Assert.Equal(0u, length));
    }

    [Fact]
    public async Task IOUSBHost_WriteAsync_non_timeout_failure_aborts_transport()
    {
        var native = new FakeMacUsbHostNativeAdapter
        {
            SendIoRequestFailure = new UsbTransportException(UsbTransportError.Io, "host write failed")
        };
        await using var transport = CreateHostTransport(native);

        var exception = await Assert.ThrowsAsync<UsbTransportException>(async () => await transport.WriteAsync("x"u8.ToArray()));

        Assert.Equal(UsbTransportError.Io, exception.Error);
        Assert.True(native.AbortPipeCount >= 2);
        Assert.False(transport.GetDiagnosticSnapshot().IsOpen);
    }

    private static MacUsbTransport CreateLegacyTransport(FakeMacUsbInterfaceAdapter native, ushort maxPacketSize = 512)
    {
        var bulkIn = new UsbEndpoint(0x81, UsbEndpointDirection.In, UsbTransferKind.Bulk, maxPacketSize);
        var bulkOut = new UsbEndpoint(0x01, UsbEndpointDirection.Out, UsbTransferKind.Bulk, maxPacketSize);
        var state = new MacUsbLegacyOpenState(new IntPtr(10), bulkIn, bulkOut, 1, 2, "test", "unit");
        var id = new MacUsbTransportId(1, 0, 0x18d1, 0x4ee7);
        return new MacUsbTransport(id, CreateDescriptor(id.Encode()), state, native, startAsyncRunLoop: false);
    }

    private static MacUsbHostTransport CreateHostTransport(FakeMacUsbHostNativeAdapter native, ushort maxPacketSize = 512)
    {
        var bulkIn = new UsbEndpoint(0x81, UsbEndpointDirection.In, UsbTransferKind.Bulk, maxPacketSize);
        var bulkOut = new UsbEndpoint(0x01, UsbEndpointDirection.Out, UsbTransferKind.Bulk, maxPacketSize);
        return new MacUsbHostTransport(
            new IntPtr(10),
            new IntPtr(11),
            new IntPtr(12),
            CreateDescriptor("mac-host:test"),
            bulkIn,
            bulkOut,
            native);
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

    private sealed class FakeMacUsbInterfaceAdapter : IMacUsbInterfaceAdapter
    {
        private readonly Queue<uint> writeResults = [];

        public List<WriteCall> Writes { get; } = [];

        public int AbortPipeCount { get; private set; }

        public void EnqueueWriteResult(uint result)
        {
            writeResults.Enqueue(result);
        }

        public ValueTask<MacUsbAsyncTransferResult> ReadPipeAsyncTo(
            IntPtr interfacePointer,
            byte pipeReference,
            IntPtr buffer,
            uint length,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new MacUsbAsyncTransferResult(MacNative.Success, 0));
        }

        public uint WritePipe(IntPtr interfacePointer, byte pipeReference, IntPtr buffer, uint length)
        {
            Writes.Add(new WriteCall(pipeReference, length));
            return writeResults.Count == 0 ? MacNative.Success : writeResults.Dequeue();
        }

        public uint WritePipe(IntPtr interfacePointer, byte pipeReference, byte[] buffer, uint length)
        {
            Writes.Add(new WriteCall(pipeReference, length));
            return writeResults.Count == 0 ? MacNative.Success : writeResults.Dequeue();
        }

        public uint AbortPipe(IntPtr interfacePointer, byte pipeReference)
        {
            AbortPipeCount++;
            return MacNative.Success;
        }

        public uint ClearPipeStall(IntPtr interfacePointer, byte pipeReference)
        {
            return MacNative.Success;
        }

        public uint GetPipeStatus(IntPtr interfacePointer, byte pipeReference)
        {
            return MacNative.Success;
        }

        public uint Close(IntPtr interfacePointer)
        {
            return MacNative.Success;
        }

        public uint Release(IntPtr interfacePointer)
        {
            return MacNative.Success;
        }

        public uint CreateInterfaceAsyncEventSource(IntPtr interfacePointer, out IntPtr source)
        {
            source = IntPtr.Zero;
            return MacNative.Success;
        }

        public readonly record struct WriteCall(byte PipeReference, uint Length);
    }

    private sealed class FakeMacUsbHostNativeAdapter : IMacUsbHostNativeAdapter
    {
        private nint nextData = 100;
        private readonly Dictionary<IntPtr, nuint> lengths = [];

        public List<nuint> SendIoRequestLengths { get; } = [];

        public int AbortPipeCount { get; private set; }

        public UsbTransportException? SendIoRequestFailure { get; set; }

        public IntPtr CreateMutableData(nuint length)
        {
            var data = new IntPtr(nextData++);
            lengths[data] = length;
            return data;
        }

        public IntPtr CreateMutableData(ReadOnlySpan<byte> data)
        {
            return CreateMutableData((nuint)data.Length);
        }

        public IntPtr MutableBytes(IntPtr data)
        {
            return IntPtr.Zero;
        }

        public bool SendIoRequest(IntPtr pipe, IntPtr data, double completionTimeout, out nuint bytesTransferred, out IntPtr error)
        {
            bytesTransferred = lengths[data];
            SendIoRequestLengths.Add(bytesTransferred);
            error = SendIoRequestFailure is null ? IntPtr.Zero : new IntPtr(1);
            return SendIoRequestFailure is null;
        }

        public ValueTask<MacUsbHostTransferResult> EnqueueIoRequestAsync(IntPtr pipe, IntPtr data, double completionTimeout, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new MacUsbHostTransferResult(MacNative.Success, 0));
        }

        public bool AbortPipe(IntPtr pipe, out IntPtr error)
        {
            AbortPipeCount++;
            error = IntPtr.Zero;
            return true;
        }

        public bool ClearStall(IntPtr pipe, out IntPtr error)
        {
            error = IntPtr.Zero;
            return true;
        }

        public IntPtr CopyPipeWithAddress(IntPtr usbHostInterface, byte endpointAddress, out IntPtr error)
        {
            error = IntPtr.Zero;
            return IntPtr.Zero;
        }

        public void Release(IntPtr value)
        {
        }

        public void Destroy(IntPtr value)
        {
        }

        public UsbTransportException CreateException(IntPtr error, string operation)
        {
            return SendIoRequestFailure ?? new UsbTransportException(operation);
        }
    }
}
