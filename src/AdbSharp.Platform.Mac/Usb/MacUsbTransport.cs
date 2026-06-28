using System.Buffers;
using System.Runtime.InteropServices;
using AdbSharp.Common.Devices;
using AdbSharp.Platform.Mac.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Mac.Usb;

internal sealed class MacUsbTransport : IUsbTransport
{
    private const int MaxRecoverableReadAttempts = 3;
    private IntPtr interfacePointer;
    private byte bulkInPipeReference;
    private byte bulkOutPipeReference;
    private string usbInterfaceRevision = string.Empty;
    private string usbOpenMode = string.Empty;
    private uint bulkInPipeStatusAtOpen;
    private uint bulkOutPipeStatusAtOpen;
    private Thread? asyncRunLoopThread;
    private IntPtr asyncRunLoop;
    private byte[]? pendingReadBuffer;
    private int pendingReadOffset;
    private int pendingReadLength;
    private uint lastBulkInPipeStatusAfterWrite = uint.MaxValue;
    private uint lastBulkOutPipeStatusAfterWrite = uint.MaxValue;
    private bool disposed;

    public MacUsbTransport(MacUsbTransportId id, UsbDeviceDescriptor descriptor, MacUsbLegacyOpenState state)
    {
        Id = id;
        Descriptor = descriptor;
        ApplyState(state);
    }

    private MacUsbTransportId Id { get; }

    private SemaphoreSlim ReadGate { get; } = new(1, 1);

    private SemaphoreSlim WriteGate { get; } = new(1, 1);

    public UsbDeviceDescriptor Descriptor { get; }

    public UsbEndpoint BulkInEndpoint { get; private set; } = default!;

    public UsbEndpoint BulkOutEndpoint { get; private set; } = default!;

    public override string ToString()
    {
        return $"{nameof(MacUsbTransport)}({usbInterfaceRevision}, {usbOpenMode}, in=0x{bulkInPipeStatusAtOpen:x8}, out=0x{bulkOutPipeStatusAtOpen:x8})";
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (buffer.Length == 0)
        {
            return 0;
        }

        await ReadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var nativeBuffer = IntPtr.Zero;
        try
        {
            if (TryDrainPendingRead(buffer, out var pendingBytesRead))
            {
                return pendingBytesRead;
            }

            var transferLength = buffer.Length;
            nativeBuffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(transferLength);
            cancellationToken.ThrowIfCancellationRequested();
            var recoverableAttempts = 0;
            uint transferred;
            while (true)
            {
                var transfer = await ReadPipeAsync(bulkInPipeReference, nativeBuffer, checked((uint)transferLength), cancellationToken).ConfigureAwait(false);
                var result = transfer.Status;
                transferred = checked((uint)transfer.BytesTransferred);

                if (result == MacNative.Success)
                {
                    break;
                }

                var exception = CreateReadException(result);
                if (exception.Error == UsbTransportError.Timeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    continue;
                }

                if (!IsRecoverableReadError(exception.Error) || recoverableAttempts >= MaxRecoverableReadAttempts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw exception;
                }

                recoverableAttempts++;
                if (recoverableAttempts == 1 && TryGetEndpointPipeReference(BulkInEndpoint, out var endpointPipeReference))
                {
                    transfer = await ReadPipeAsync(endpointPipeReference, nativeBuffer, checked((uint)transferLength), cancellationToken).ConfigureAwait(false);
                    if (transfer.Status == MacNative.Success)
                    {
                        transferred = checked((uint)transfer.BytesTransferred);
                        break;
                    }
                }

                if (exception.Error is UsbTransportError.InvalidEndpoint or UsbTransportError.DeviceDisconnected or UsbTransportError.DeviceNotFound)
                {
                    await ReopenInterfaceAsync(cancellationToken).ConfigureAwait(false);
                    throw new UsbTransportException(
                        UsbTransportError.OperationAborted,
                        "macOS USB interface was reopened after a transient read failure; retry the protocol operation.");
                }
                else
                {
                    _ = MacUsbInterface.ClearPipeStall(interfacePointer, bulkInPipeReference);
                    _ = MacUsbInterface.AbortPipe(interfacePointer, bulkInPipeReference);
                }

                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            unsafe
            {
                return CopyReadResult(new ReadOnlySpan<byte>((void*)nativeBuffer, checked((int)transferred)), buffer);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(nativeBuffer);
            ReadGate.Release();
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await WriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (buffer.Length == 0)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var cancellationRegistration = RegisterAbort(bulkOutPipeReference, cancellationToken);
                WriteZeroLengthPacket(cancellationToken);
                return;
            }
            finally
            {
                WriteGate.Release();
            }
        }

        var nativeBuffer = IntPtr.Zero;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            nativeBuffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(buffer.Length);
            unsafe
            {
                buffer.Span.CopyTo(new Span<byte>((void*)nativeBuffer, buffer.Length));
            }

            using var cancellationRegistration = RegisterAbort(bulkOutPipeReference, cancellationToken);
            var result = MacUsbInterface.WritePipe(
                interfacePointer,
                bulkOutPipeReference,
                nativeBuffer,
                checked((uint)buffer.Length));
            if (result == MacNative.Success)
            {
                CapturePipeStatusAfterWrite();
                if (buffer.Length % BulkOutEndpoint.MaxPacketSize == 0)
                {
                    WriteZeroLengthPacket(cancellationToken);
                }

                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw MacUsbErrors.Create(result, $"write to macOS USB bulk pipe {bulkOutPipeReference} ({BulkOutEndpoint.Address:x2})");
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(nativeBuffer);
            WriteGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        disposed = true;
        CloseCurrentInterface();
        if (pendingReadBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(pendingReadBuffer);
            pendingReadBuffer = null;
        }

        ReadGate.Dispose();
        WriteGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async ValueTask<MacUsbAsyncTransferResult> ReadPipeAsync(byte pipeReference, IntPtr nativeBuffer, uint transferLength, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<MacUsbAsyncTransferResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(completion);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var cancellationRegistration = RegisterAbort(pipeReference, cancellationToken);
            unsafe
            {
                var result = MacUsbInterface.ReadPipeAsyncTo(
                    interfacePointer,
                    pipeReference,
                    nativeBuffer,
                    transferLength,
                    MacNative.TransferNoDataTimeoutMilliseconds,
                    MacNative.TransferCompletionTimeoutMilliseconds,
                    &CompleteAsyncTransfer,
                    GCHandle.ToIntPtr(handle));

                if (result != MacNative.Success)
                {
                    return new MacUsbAsyncTransferResult(result, 0);
                }
            }

            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }

    [UnmanagedCallersOnly]
    private static void CompleteAsyncTransfer(IntPtr refcon, uint result, IntPtr bytesTransferred)
    {
        var handle = GCHandle.FromIntPtr(refcon);
        if (handle.Target is TaskCompletionSource<MacUsbAsyncTransferResult> completion)
        {
            completion.TrySetResult(new MacUsbAsyncTransferResult(result, (nuint)bytesTransferred));
        }
    }

    private CancellationTokenRegistration RegisterAbort(byte pipeReference, CancellationToken cancellationToken)
    {
        var pointer = interfacePointer;
        return cancellationToken.CanBeCanceled
            ? cancellationToken.Register(static state =>
            {
                var (interfacePointer, pipe) = ((IntPtr InterfacePointer, byte Pipe))state!;
                _ = MacUsbInterface.AbortPipe(interfacePointer, pipe);
            }, (pointer, pipeReference))
            : default;
    }

    private static bool IsRecoverableReadError(UsbTransportError error)
    {
        return error is UsbTransportError.EndpointStalled
            or UsbTransportError.Busy
            or UsbTransportError.OperationAborted
            or UsbTransportError.InvalidEndpoint
            or UsbTransportError.DeviceDisconnected
            or UsbTransportError.DeviceNotFound;
    }

    private bool TryGetEndpointPipeReference(UsbEndpoint endpoint, out byte pipeReference)
    {
        pipeReference = (byte)(endpoint.Address & 0x0f);
        return pipeReference != 0 && pipeReference != bulkInPipeReference;
    }

    private static uint GetPipeStatus(IntPtr interfacePointer, byte pipeReference)
    {
        try
        {
            return MacUsbInterface.GetPipeStatus(interfacePointer, pipeReference);
        }
        catch (UsbTransportException)
        {
            return uint.MaxValue;
        }
    }

    private UsbTransportException CreateReadException(uint result)
    {
        var exception = MacUsbErrors.Create(result, $"read from macOS USB bulk pipe {bulkInPipeReference} ({BulkInEndpoint.Address:x2})");
        if (result != MacNative.UsbReturnUnknownPipe)
        {
            return exception;
        }

        uint pipeStatus;
        try
        {
            pipeStatus = MacUsbInterface.GetPipeStatus(interfacePointer, bulkInPipeReference);
        }
        catch (UsbTransportException)
        {
            return exception;
        }

        return new UsbTransportException(
            exception.Error,
            $"{exception.Message} GetPipeStatus returned IOReturn 0x{pipeStatus:x8}. Last write pipe statuses: in=0x{lastBulkInPipeStatusAfterWrite:x8}, out=0x{lastBulkOutPipeStatusAfterWrite:x8}.");
    }

    private async ValueTask ReopenInterfaceAsync(CancellationToken cancellationToken)
    {
        await WriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CloseCurrentInterface();
            UsbTransportException? failure = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    var state = MacUsbTransportFactory.OpenLegacyInterface(Id, cancellationToken);
                    ApplyState(state);
                    lastBulkInPipeStatusAfterWrite = uint.MaxValue;
                    lastBulkOutPipeStatusAfterWrite = uint.MaxValue;
                    return;
                }
                catch (UsbTransportException ex)
                {
                    failure = ex;
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }

            throw failure ?? new UsbTransportException($"macOS USB device '{Id.Encode()}' was not found during interface recovery.");
        }
        finally
        {
            WriteGate.Release();
        }
    }

    private void ApplyState(MacUsbLegacyOpenState state)
    {
        interfacePointer = state.InterfacePointer;
        BulkInEndpoint = state.BulkInEndpoint;
        BulkOutEndpoint = state.BulkOutEndpoint;
        bulkInPipeReference = state.BulkInPipeReference;
        bulkOutPipeReference = state.BulkOutPipeReference;
        usbInterfaceRevision = state.UsbInterfaceRevision;
        usbOpenMode = state.UsbOpenMode;
        bulkInPipeStatusAtOpen = GetPipeStatus(interfacePointer, bulkInPipeReference);
        bulkOutPipeStatusAtOpen = GetPipeStatus(interfacePointer, bulkOutPipeReference);
        StartAsyncRunLoop();
    }

    private void CloseCurrentInterface()
    {
        if (interfacePointer == IntPtr.Zero)
        {
            return;
        }

        StopAsyncRunLoop();
        _ = MacUsbInterface.Close(interfacePointer);
        _ = MacUsbInterface.Release(interfacePointer);
        interfacePointer = IntPtr.Zero;
    }

    private void StartAsyncRunLoop()
    {
        StopAsyncRunLoop();
        var ready = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pointer = interfacePointer;
        asyncRunLoopThread = new Thread(() => RunAsyncLoop(pointer, ready))
        {
            IsBackground = true,
            Name = "AdbSharp macOS USB async run loop"
        };
        asyncRunLoopThread.Start();

        var result = ready.Task.GetAwaiter().GetResult();
        if (result != MacNative.Success)
        {
            StopAsyncRunLoop();
            throw MacUsbErrors.Create(result, "create macOS USB async event source");
        }
    }

    private void StopAsyncRunLoop()
    {
        var runLoop = asyncRunLoop;
        if (runLoop != IntPtr.Zero)
        {
            MacNative.CFRunLoopStop(runLoop);
            MacNative.CFRunLoopWakeUp(runLoop);
        }

        if (asyncRunLoopThread is not null)
        {
            _ = asyncRunLoopThread.Join(TimeSpan.FromSeconds(1));
            asyncRunLoopThread = null;
        }

        asyncRunLoop = IntPtr.Zero;
    }

    private void RunAsyncLoop(IntPtr pointer, TaskCompletionSource<uint> ready)
    {
        var mode = MacNative.CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", MacNative.Utf8Encoding);
        var source = IntPtr.Zero;
        try
        {
            var result = MacUsbInterface.CreateInterfaceAsyncEventSource(pointer, out source);
            if (result != MacNative.Success)
            {
                ready.TrySetResult(result);
                return;
            }

            asyncRunLoop = MacNative.CFRunLoopGetCurrent();
            MacNative.CFRunLoopAddSource(asyncRunLoop, source, mode);
            ready.TrySetResult(MacNative.Success);
            MacNative.CFRunLoopRun();
        }
        finally
        {
            if (source != IntPtr.Zero)
            {
                MacNative.CFRelease(source);
            }

            if (mode != IntPtr.Zero)
            {
                MacNative.CFRelease(mode);
            }
        }
    }

    private void WriteZeroLengthPacket(CancellationToken cancellationToken)
    {
        var result = MacUsbInterface.WritePipe(
            interfacePointer,
            bulkOutPipeReference,
            [],
            0);
        if (result == MacNative.Success)
        {
            CapturePipeStatusAfterWrite();
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw MacUsbErrors.Create(result, $"write zero-length packet to macOS USB bulk pipe {bulkOutPipeReference} ({BulkOutEndpoint.Address:x2})");
    }

    private void CapturePipeStatusAfterWrite()
    {
        lastBulkInPipeStatusAfterWrite = GetPipeStatus(interfacePointer, bulkInPipeReference);
        lastBulkOutPipeStatusAfterWrite = GetPipeStatus(interfacePointer, bulkOutPipeReference);
    }

    private bool TryDrainPendingRead(Memory<byte> buffer, out int bytesRead)
    {
        bytesRead = 0;
        if (pendingReadBuffer is null || pendingReadLength == 0)
        {
            return false;
        }

        bytesRead = Math.Min(buffer.Length, pendingReadLength);
        pendingReadBuffer.AsMemory(pendingReadOffset, bytesRead).CopyTo(buffer);
        pendingReadOffset += bytesRead;
        pendingReadLength -= bytesRead;
        if (pendingReadLength == 0)
        {
            ArrayPool<byte>.Shared.Return(pendingReadBuffer);
            pendingReadBuffer = null;
            pendingReadOffset = 0;
        }

        return true;
    }

    private int CopyReadResult(ReadOnlySpan<byte> source, Memory<byte> destination)
    {
        var bytesRead = Math.Min(source.Length, destination.Length);
        source[..bytesRead].CopyTo(destination.Span);

        var extraLength = source.Length - bytesRead;
        if (extraLength > 0)
        {
            pendingReadBuffer = ArrayPool<byte>.Shared.Rent(extraLength);
            source[bytesRead..].CopyTo(pendingReadBuffer);
            pendingReadOffset = 0;
            pendingReadLength = extraLength;
        }

        return bytesRead;
    }

    private readonly record struct MacUsbAsyncTransferResult(uint Status, nuint BytesTransferred);
}
