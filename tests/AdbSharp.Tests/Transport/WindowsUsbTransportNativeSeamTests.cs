using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AdbSharp.Common.Devices;
using AdbSharp.Platform.Windows.Usb;
using AdbSharp.Platform.Windows.Usb.Native;
using AdbSharp.Transport.Usb;
using Microsoft.Win32.SafeHandles;

namespace AdbSharp.Tests.Transport;

[SupportedOSPlatform("windows")]
public sealed class WindowsUsbTransportNativeSeamTests
{
    [Fact]
    public async Task ReadAsync_completes_immediate_overlapped_success()
    {
        var native = new FakeWindowsUsbNativeAdapter();
        native.EnqueueReadSuccess("ok"u8.ToArray());
        await using var transport = CreateTransport(native);
        var buffer = new byte[2];

        var bytesRead = await transport.ReadAsync(buffer);

        Assert.Equal(2, bytesRead);
        Assert.Equal("ok"u8.ToArray(), buffer);
        Assert.True(native.Reads[0].Overlapped != IntPtr.Zero);
        Assert.Equal(1, native.GetOverlappedResultCount);
    }

    [Fact]
    public async Task ReadAsync_completes_pending_overlapped_success()
    {
        var native = new FakeWindowsUsbNativeAdapter();
        native.EnqueueReadPendingSuccess("go"u8.ToArray());
        await using var transport = CreateTransport(native);
        var buffer = new byte[2];

        var readTask = transport.ReadAsync(buffer).AsTask();
        await native.PendingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        native.CompletePending();
        var bytesRead = await readTask;

        Assert.Equal(2, bytesRead);
        Assert.Equal("go"u8.ToArray(), buffer);
        Assert.Equal(1, native.WaitCount);
        Assert.Equal(1, native.GetOverlappedResultCount);
    }

    [Fact]
    public async Task ReadAsync_retries_timeout_without_poisoning_transport()
    {
        var native = new FakeWindowsUsbNativeAdapter();
        native.EnqueueReadTimeout();
        native.EnqueueReadSuccess("a"u8.ToArray());
        await using var transport = CreateTransport(native);
        var buffer = new byte[1];

        var bytesRead = await transport.ReadAsync(buffer);

        Assert.Equal(1, bytesRead);
        Assert.True(transport.GetDiagnosticSnapshot().IsOpen);
        Assert.Equal(2, native.Reads.Count);
        Assert.Equal(0, native.WinUsbFreeCount);
    }

    [Fact]
    public async Task ReadAsync_cancellation_calls_cancel_io_ex_without_poisoning_transport()
    {
        var native = new FakeWindowsUsbNativeAdapter();
        native.EnqueueReadPendingSuccess("x"u8.ToArray());
        native.EnqueueReadSuccess("y"u8.ToArray());
        await using var transport = CreateTransport(native);
        var buffer = new byte[1];
        using var cancellation = new CancellationTokenSource();

        var readTask = transport.ReadAsync(buffer, cancellation.Token).AsTask();
        await native.PendingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await readTask);

        var bytesRead = await transport.ReadAsync(buffer);

        Assert.Equal(1, bytesRead);
        Assert.Equal((byte)'y', buffer[0]);
        Assert.Equal(1, native.CancelOverlappedCount);
        Assert.True(transport.GetDiagnosticSnapshot().IsOpen);
    }

    [Fact]
    public async Task WriteAsync_non_timeout_failure_aborts_transport()
    {
        var native = new FakeWindowsUsbNativeAdapter();
        native.EnqueueWriteStartFailure(WindowsUsbNative.ErrorGenFailure);
        await using var transport = CreateTransport(native);

        var exception = await Assert.ThrowsAsync<UsbTransportException>(async () => await transport.WriteAsync("x"u8.ToArray()));

        Assert.Equal(UsbTransportError.Io, exception.Error);
        Assert.Equal(1, native.WinUsbFreeCount);
        Assert.False(transport.GetDiagnosticSnapshot().IsOpen);
    }

    [Fact]
    public async Task WriteAsync_sends_zero_length_packet_for_packet_aligned_write()
    {
        var native = new FakeWindowsUsbNativeAdapter();
        await using var transport = CreateTransport(native, maxPacketSize: 4);

        await transport.WriteAsync("test"u8.ToArray());

        Assert.Collection(
            native.Writes,
            write => Assert.Equal(4, write.Length),
            write => Assert.Equal(0, write.Length));
    }

    private static WindowsUsbTransport CreateTransport(FakeWindowsUsbNativeAdapter native, ushort maxPacketSize = 512)
    {
        return new WindowsUsbTransport(
            new SafeFileHandle(IntPtr.Zero, ownsHandle: false),
            new IntPtr(42),
            CreateDescriptor("windows:test"),
            new UsbEndpoint(0x81, UsbEndpointDirection.In, UsbTransferKind.Bulk, maxPacketSize),
            new UsbEndpoint(0x01, UsbEndpointDirection.Out, UsbTransferKind.Bulk, maxPacketSize),
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

    private sealed class FakeWindowsUsbNativeAdapter : IWindowsUsbNativeAdapter
    {
        private readonly Queue<Script> reads = [];
        private readonly Queue<Script> writes = [];
        private Script? active;

        public TaskCompletionSource PendingStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<TransferCall> Reads { get; } = [];

        public List<TransferCall> Writes { get; } = [];

        public int WaitCount { get; private set; }

        public int CancelOverlappedCount { get; private set; }

        public int CancelAllCount { get; private set; }

        public int GetOverlappedResultCount { get; private set; }

        public int WinUsbFreeCount { get; private set; }

        public void EnqueueReadSuccess(byte[] bytes)
        {
            reads.Enqueue(Script.ImmediateSuccess(bytes));
        }

        public void EnqueueReadPendingSuccess(byte[] bytes)
        {
            reads.Enqueue(Script.PendingSuccess(bytes));
        }

        public void EnqueueReadTimeout()
        {
            reads.Enqueue(Script.ImmediateOverlappedFailure(WindowsUsbNative.ErrorSemTimeout));
        }

        public void EnqueueWriteStartFailure(int errorCode)
        {
            writes.Enqueue(Script.StartFailure(errorCode));
        }

        public void CompletePending()
        {
            if (active?.ReadBytes is { } bytes && active.Buffer != IntPtr.Zero)
            {
                Marshal.Copy(bytes, 0, active.Buffer, bytes.Length);
            }

            active?.PendingCompletion?.TrySetResult();
        }

        public WindowsUsbCallResult ReadPipe(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, int bufferLength, IntPtr overlapped)
        {
            var script = reads.Count == 0 ? Script.ImmediateSuccess(new byte[bufferLength]) : reads.Dequeue();
            script.Buffer = buffer;
            Reads.Add(new TransferCall(pipeId, bufferLength, overlapped));
            return Start(script);
        }

        public WindowsUsbCallResult WritePipe(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, int bufferLength, IntPtr overlapped)
        {
            var script = writes.Count == 0 ? Script.ImmediateSuccess([]) : writes.Dequeue();
            script.Buffer = buffer;
            script.BytesTransferred = bufferLength;
            Writes.Add(new TransferCall(pipeId, bufferLength, overlapped));
            return Start(script);
        }

        public WindowsUsbOverlappedResult GetOverlappedResult(SafeFileHandle fileHandle, IntPtr overlapped, bool wait)
        {
            GetOverlappedResultCount++;
            var script = active ?? Script.ImmediateSuccess([]);
            active = null;
            return script.OverlappedResult;
        }

        public WindowsUsbCallResult CancelIoEx(SafeFileHandle fileHandle, IntPtr overlapped)
        {
            if (overlapped == IntPtr.Zero)
            {
                CancelAllCount++;
            }
            else
            {
                CancelOverlappedCount++;
                active?.PendingCompletion?.TrySetResult();
            }

            return new WindowsUsbCallResult(true, 0);
        }

        public bool WinUsbFree(IntPtr interfaceHandle)
        {
            WinUsbFreeCount++;
            return true;
        }

        public ValueTask WaitForOverlappedAsync(WaitHandle waitHandle, CancellationToken cancellationToken)
        {
            WaitCount++;
            return active?.PendingCompletion is { } completion
                ? new ValueTask(completion.Task.WaitAsync(cancellationToken))
                : ValueTask.CompletedTask;
        }

        private WindowsUsbCallResult Start(Script script)
        {
            active = script;
            if (script.ReadBytes is { } bytes && script.StartResult.Success && script.Buffer != IntPtr.Zero && !script.IsPending)
            {
                Marshal.Copy(bytes, 0, script.Buffer, bytes.Length);
            }

            if (script.IsPending)
            {
                PendingStarted.TrySetResult();
            }

            return script.StartResult;
        }

        public readonly record struct TransferCall(byte Endpoint, int Length, IntPtr Overlapped);

        private sealed class Script
        {
            private Script(WindowsUsbCallResult startResult, WindowsUsbOverlappedResult overlappedResult, byte[]? readBytes, TaskCompletionSource? pendingCompletion)
            {
                StartResult = startResult;
                OverlappedResult = overlappedResult;
                ReadBytes = readBytes;
                PendingCompletion = pendingCompletion;
            }

            public WindowsUsbCallResult StartResult { get; }

            public WindowsUsbOverlappedResult OverlappedResult { get; private set; }

            public byte[]? ReadBytes { get; }

            public TaskCompletionSource? PendingCompletion { get; }

            public IntPtr Buffer { get; set; }

            public int BytesTransferred
            {
                set => OverlappedResult = OverlappedResult with { BytesTransferred = value };
            }

            public bool IsPending => PendingCompletion is not null;

            public static Script ImmediateSuccess(byte[] readBytes)
            {
                return new Script(
                    new WindowsUsbCallResult(true, 0),
                    new WindowsUsbOverlappedResult(true, readBytes.Length, 0),
                    readBytes,
                    null);
            }

            public static Script PendingSuccess(byte[] readBytes)
            {
                return new Script(
                    new WindowsUsbCallResult(false, WindowsUsbNative.ErrorIoPending),
                    new WindowsUsbOverlappedResult(true, readBytes.Length, 0),
                    readBytes,
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            }

            public static Script ImmediateOverlappedFailure(int errorCode)
            {
                return new Script(
                    new WindowsUsbCallResult(true, 0),
                    new WindowsUsbOverlappedResult(false, 0, errorCode),
                    null,
                    null);
            }

            public static Script StartFailure(int errorCode)
            {
                return new Script(
                    new WindowsUsbCallResult(false, errorCode),
                    new WindowsUsbOverlappedResult(false, 0, errorCode),
                    null,
                    null);
            }
        }
    }
}
