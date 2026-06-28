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
    UsbEndpoint bulkOutEndpoint,
    IMacUsbHostNativeAdapter? nativeAdapter = null) : IUsbTransport, IUsbTransportDiagnostics
{
    private const int MaxReadRecoveryAttempts = 3;
    private readonly IMacUsbHostNativeAdapter native = nativeAdapter ?? MacUsbHostNativeAdapter.Instance;
    private readonly SemaphoreSlim readGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private IntPtr interfaceObjectHandle = interfaceObject;
    private IntPtr bulkInPipeObject = bulkInPipe;
    private IntPtr bulkOutPipeObject = bulkOutPipe;
    private byte[]? pendingReadBuffer;
    private int pendingReadOffset;
    private int pendingReadLength;
    private int closed;
    private int resourcesDisposed;
    private UsbTransportError? lastError;

    public UsbDeviceDescriptor Descriptor { get; } = descriptor;

    public UsbEndpoint BulkInEndpoint { get; } = bulkInEndpoint;

    public UsbEndpoint BulkOutEndpoint { get; } = bulkOutEndpoint;

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
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
                data = native.CreateMutableData((nuint)transferLength);
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
                    native.Release(data);
                    data = IntPtr.Zero;
                    recoveryAttempts++;
                    RecoverReadPipe();
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (result.Status == MacNative.Success)
                {
                    var transferredLength = checked((int)result.BytesTransferred);
                    var bytes = native.MutableBytes(data);
                    if (bytes == IntPtr.Zero && transferredLength != 0)
                    {
                        var emptyBufferException = new UsbTransportException("macOS IOUSBHost returned an empty read buffer.");
                        lastError = emptyBufferException.Error;
                        await AbortAsync(CancellationToken.None).ConfigureAwait(false);
                        throw emptyBufferException;
                    }

                    unsafe
                    {
                        CopyReadResult(new ReadOnlySpan<byte>((void*)bytes, transferredLength), buffer, out var bytesRead);
                        return bytesRead;
                    }
                }

                native.Release(data);
                data = IntPtr.Zero;

                var exception = MacUsbErrors.Create(result.Status, $"read from macOS IOUSBHost bulk endpoint {BulkInEndpoint.Address:x2}");
                if (exception.Error == UsbTransportError.Timeout)
                {
                    lastError = exception.Error;
                    cancellationToken.ThrowIfCancellationRequested();
                    continue;
                }

                if (!IsRecoverableReadError(exception.Error, result.Status) || recoveryAttempts >= MaxReadRecoveryAttempts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lastError = exception.Error;
                    await AbortAsync(CancellationToken.None).ConfigureAwait(false);
                    throw exception;
                }

                recoveryAttempts++;
                RecoverReadPipe();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            native.Release(data);
            readGate.Release();
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
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
            data = native.CreateMutableData(rented.AsSpan(0, buffer.Length));
            if (data == IntPtr.Zero)
            {
                throw new UsbTransportException("Failed to allocate macOS USB write buffer.");
            }

            using var cancellationRegistration = RegisterAbort(bulkOutPipeObject, cancellationToken);
            if (!native.SendIoRequest(bulkOutPipeObject, data, MacNative.TransferTimeoutSeconds, out var bytesTransferred, out var error))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var exception = native.CreateException(error, $"write to macOS IOUSBHost bulk endpoint {BulkOutEndpoint.Address:x2}");
                lastError = exception.Error;
                await AbortAsync(CancellationToken.None).ConfigureAwait(false);
                throw exception;
            }

            if (bytesTransferred != (nuint)buffer.Length)
            {
                var exception = new UsbTransportException(
                    UsbTransportError.Io,
                    $"macOS IOUSBHost wrote {bytesTransferred} of {buffer.Length} bytes to bulk endpoint {BulkOutEndpoint.Address:x2}.");
                lastError = exception.Error;
                await AbortAsync(CancellationToken.None).ConfigureAwait(false);
                throw exception;
            }

            if (buffer.Length % BulkOutEndpoint.MaxPacketSize == 0)
            {
                await WriteZeroLengthPacketAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            native.Release(data);
            ArrayPool<byte>.Shared.Return(rented);
            writeGate.Release();
        }
    }

    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return AbortAsync(cancellationToken);
    }

    public ValueTask AbortAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AbortNativeResources();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        AbortNativeResources();
        DisposeManagedResources();
        return ValueTask.CompletedTask;
    }

    public UsbTransportDiagnosticSnapshot GetDiagnosticSnapshot()
    {
        return new UsbTransportDiagnosticSnapshot(
            "IOUSBHost",
            Descriptor.TransportId,
            BulkInEndpoint.Address,
            BulkInEndpoint.MaxPacketSize,
            BulkOutEndpoint.Address,
            BulkOutEndpoint.MaxPacketSize,
            !IsClosed,
            IsClosed ? "closed" : "open",
            new Dictionary<string, string>
            {
                ["experimental"] = "true",
                ["interfaceObject"] = interfaceObjectHandle == IntPtr.Zero ? string.Empty : $"0x{interfaceObjectHandle.ToInt64():x}",
                ["bulkInPipeObject"] = bulkInPipeObject == IntPtr.Zero ? string.Empty : $"0x{bulkInPipeObject.ToInt64():x}",
                ["bulkOutPipeObject"] = bulkOutPipeObject == IntPtr.Zero ? string.Empty : $"0x{bulkOutPipeObject.ToInt64():x}",
                ["lastError"] = lastError?.ToString() ?? string.Empty
            });
    }

    private bool IsClosed => Volatile.Read(ref closed) != 0;

    private void AbortNativeResources()
    {
        if (Interlocked.Exchange(ref closed, 1) != 0)
        {
            return;
        }

        _ = native.AbortPipe(bulkInPipeObject, out _);
        _ = native.AbortPipe(bulkOutPipeObject, out _);
        native.Release(bulkInPipeObject);
        native.Release(bulkOutPipeObject);
        native.Destroy(interfaceObjectHandle);
        native.Release(interfaceObjectHandle);
        bulkInPipeObject = IntPtr.Zero;
        bulkOutPipeObject = IntPtr.Zero;
        interfaceObjectHandle = IntPtr.Zero;
    }

    private void DisposeManagedResources()
    {
        if (Interlocked.Exchange(ref resourcesDisposed, 1) != 0)
        {
            return;
        }

        pendingReadBuffer = null;
        pendingReadOffset = 0;
        pendingReadLength = 0;
        readGate.Dispose();
        writeGate.Dispose();
    }

    private CancellationTokenRegistration RegisterAbort(IntPtr pipe, CancellationToken cancellationToken)
    {
        return cancellationToken.CanBeCanceled
            ? cancellationToken.Register(static state =>
            {
                var (native, pipe) = ((IMacUsbHostNativeAdapter Native, IntPtr Pipe))state!;
                _ = native.AbortPipe(pipe, out _);
            }, (native, pipe))
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

    private void RecoverPipe(IntPtr pipe)
    {
        if (!native.ClearStall(pipe, out _))
        {
            _ = native.AbortPipe(pipe, out _);
        }
    }

    private void RecoverReadPipe()
    {
        RecoverPipe(bulkInPipeObject);
        var replacement = native.CopyPipeWithAddress(interfaceObjectHandle, BulkInEndpoint.Address, out _);
        if (replacement == IntPtr.Zero)
        {
            return;
        }

        native.Release(bulkInPipeObject);
        bulkInPipeObject = replacement;
    }

    private async ValueTask WriteZeroLengthPacketAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = native.CreateMutableData(0);
        if (data == IntPtr.Zero)
        {
            var allocationException = new UsbTransportException("Failed to allocate macOS IOUSBHost zero-length write buffer.");
            lastError = allocationException.Error;
            await AbortAsync(CancellationToken.None).ConfigureAwait(false);
            throw allocationException;
        }

        try
        {
            using var cancellationRegistration = RegisterAbort(bulkOutPipeObject, cancellationToken);
            if (native.SendIoRequest(bulkOutPipeObject, data, MacNative.TransferTimeoutSeconds, out _, out var error))
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var exception = native.CreateException(error, $"write zero-length packet to macOS IOUSBHost bulk endpoint {BulkOutEndpoint.Address:x2}");
            lastError = exception.Error;
            await AbortAsync(CancellationToken.None).ConfigureAwait(false);
            throw exception;
        }
        finally
        {
            native.Release(data);
        }
    }

    private async ValueTask<MacUsbHostTransferResult> EnqueueReadAsync(IntPtr data, CancellationToken cancellationToken)
    {
        try
        {
            return await native.EnqueueIoRequestAsync(bulkInPipeObject, data, MacNative.TransferTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        }
        catch (UsbTransportException ex) when (ex.Message.Contains("macOS IOUSBHost I/O", StringComparison.Ordinal))
        {
            throw new UsbTransportException(ex.Error, $"Failed to enqueue read from macOS IOUSBHost bulk endpoint {BulkInEndpoint.Address:x2}.", ex);
        }
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
