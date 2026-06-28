using System.Buffers;
using AdbSharp.Common.Devices;
using AdbSharp.Platform.Mac.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Mac.Usb;

internal sealed class MacUsbHostTransport(
    IntPtr interfaceObject,
    IntPtr bulkInPipe,
    IntPtr bulkOutPipe,
    UsbDeviceDescriptor descriptor,
    UsbEndpoint bulkInEndpoint,
    UsbEndpoint bulkOutEndpoint) : IUsbTransport
{
    private const int MaxReadRecoveryAttempts = 3;
    private readonly SemaphoreSlim readGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private IntPtr bulkInPipeObject = bulkInPipe;
    private readonly IntPtr bulkOutPipeObject = bulkOutPipe;
    private byte[]? pendingReadBuffer;
    private int pendingReadOffset;
    private int pendingReadLength;
    private bool disposed;

    public UsbDeviceDescriptor Descriptor { get; } = descriptor;

    public UsbEndpoint BulkInEndpoint { get; } = bulkInEndpoint;

    public UsbEndpoint BulkOutEndpoint { get; } = bulkOutEndpoint;

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (buffer.Length == 0)
        {
            return 0;
        }

        await readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var data = IntPtr.Zero;
        try
        {
            if (TryDrainPendingRead(buffer, out var pendingBytesRead))
            {
                return pendingBytesRead;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var recoveryAttempts = 0;
            while (true)
            {
                var transferLength = Math.Max(buffer.Length, BulkInEndpoint.MaxPacketSize);
                data = MacObjC.CreateMutableData((nuint)transferLength);
                if (data == IntPtr.Zero)
                {
                    throw new UsbTransportException("Failed to allocate macOS USB read buffer.");
                }

                MacUsbHostTransferResult result;
                try
                {
                    result = await EnqueueReadAsync(data, cancellationToken).ConfigureAwait(false);
                }
                catch (UsbTransportException ex) when (IsRecoverableReadEnqueueError(ex) && recoveryAttempts < MaxReadRecoveryAttempts)
                {
                    MacObjC.Release(data);
                    data = IntPtr.Zero;
                    recoveryAttempts++;
                    RecoverReadPipe();
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (result.Status == MacNative.Success)
                {
                    var transferredLength = checked((int)result.BytesTransferred);
                    var bytes = MacObjC.MutableBytes(data);
                    if (bytes == IntPtr.Zero && transferredLength != 0)
                    {
                        throw new UsbTransportException("macOS IOUSBHost returned an empty read buffer.");
                    }

                    unsafe
                    {
                        CopyReadResult(new ReadOnlySpan<byte>((void*)bytes, transferredLength), buffer, out var bytesRead);
                        return bytesRead;
                    }
                }

                MacObjC.Release(data);
                data = IntPtr.Zero;

                var exception = MacUsbErrors.Create(result.Status, $"read from macOS IOUSBHost bulk endpoint {BulkInEndpoint.Address:x2}");
                if (exception.Error == UsbTransportError.Timeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    continue;
                }

                if (!IsRecoverableReadError(exception.Error, result.Status) || recoveryAttempts >= MaxReadRecoveryAttempts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw exception;
                }

                recoveryAttempts++;
                RecoverReadPipe();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            MacObjC.Release(data);
            readGate.Release();
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (buffer.Length == 0)
        {
            return;
        }

        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        var data = IntPtr.Zero;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer.CopyTo(rented);
            data = MacObjC.CreateMutableData(rented.AsSpan(0, buffer.Length));
            if (data == IntPtr.Zero)
            {
                throw new UsbTransportException("Failed to allocate macOS USB write buffer.");
            }

            using var cancellationRegistration = RegisterAbort(bulkOutPipeObject, cancellationToken);
            if (!MacObjC.SendIoRequest(bulkOutPipeObject, data, MacNative.TransferTimeoutSeconds, out var bytesTransferred, out var error))
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw MacObjC.CreateException(error, $"write to macOS IOUSBHost bulk endpoint {BulkOutEndpoint.Address:x2}");
            }

            if (bytesTransferred != (nuint)buffer.Length)
            {
                throw new UsbTransportException(
                    UsbTransportError.Io,
                    $"macOS IOUSBHost wrote {bytesTransferred} of {buffer.Length} bytes to bulk endpoint {BulkOutEndpoint.Address:x2}.");
            }
        }
        finally
        {
            MacObjC.Release(data);
            ArrayPool<byte>.Shared.Return(rented);
            writeGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        disposed = true;
        _ = MacObjC.AbortPipe(bulkInPipeObject, out _);
        _ = MacObjC.AbortPipe(bulkOutPipeObject, out _);
        MacObjC.Release(bulkInPipeObject);
        MacObjC.Release(bulkOutPipeObject);
        MacObjC.Destroy(interfaceObject);
        MacObjC.Release(interfaceObject);
        readGate.Dispose();
        writeGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationTokenRegistration RegisterAbort(IntPtr pipe, CancellationToken cancellationToken)
    {
        return cancellationToken.CanBeCanceled
            ? cancellationToken.Register(static state =>
            {
                _ = MacObjC.AbortPipe((IntPtr)state!, out _);
            }, pipe)
            : default;
    }

    private static bool IsRecoverableReadError(UsbTransportError error, uint errorCode)
    {
        return error is UsbTransportError.EndpointStalled or UsbTransportError.Busy
            || errorCode is MacNative.IoReturnNotFound or MacNative.MachSendInvalidDestination;
    }

    private static bool IsRecoverableReadEnqueueError(UsbTransportException exception)
    {
        return exception.Error == UsbTransportError.Timeout
            || (exception.Error is UsbTransportError.Unknown or UsbTransportError.DeviceNotFound
                && (exception.Message.Contains("Unable to enqueue IO", StringComparison.Ordinal)
                    || exception.Message.Contains("Unable to send IO", StringComparison.Ordinal)));
    }

    private static void RecoverPipe(IntPtr pipe)
    {
        if (!MacObjC.ClearStall(pipe, out _))
        {
            _ = MacObjC.AbortPipe(pipe, out _);
        }
    }

    private void RecoverReadPipe()
    {
        RecoverPipe(bulkInPipeObject);
        var replacement = MacObjC.CopyPipeWithAddress(interfaceObject, BulkInEndpoint.Address, out _);
        if (replacement == IntPtr.Zero)
        {
            return;
        }

        MacObjC.Release(bulkInPipeObject);
        bulkInPipeObject = replacement;
    }

    private async ValueTask<MacUsbHostTransferResult> EnqueueReadAsync(IntPtr data, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<MacUsbHostTransferResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var block = MacObjC.CreateIoCompletionBlock(completion);
        using var cancellationRegistration = RegisterAbort(bulkInPipeObject, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!MacObjC.EnqueueIoRequest(bulkInPipeObject, data, MacNative.TransferTimeoutSeconds, block.Pointer, out var error))
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw MacObjC.CreateException(error, $"enqueue read from macOS IOUSBHost bulk endpoint {BulkInEndpoint.Address:x2}");
        }

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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
            pendingReadBuffer = null;
            pendingReadOffset = 0;
        }

        return true;
    }

    private void CopyReadResult(ReadOnlySpan<byte> source, Memory<byte> destination, out int bytesRead)
    {
        bytesRead = Math.Min(source.Length, destination.Length);
        source[..bytesRead].CopyTo(destination.Span);

        var remaining = source.Length - bytesRead;
        if (remaining == 0)
        {
            return;
        }

        pendingReadBuffer = source[bytesRead..].ToArray();
        pendingReadOffset = 0;
        pendingReadLength = remaining;
    }
}
