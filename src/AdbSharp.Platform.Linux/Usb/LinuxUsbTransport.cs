using System.Buffers;
using System.Globalization;
using AdbSharp.Common.Devices;
using AdbSharp.Platform.Linux.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Linux.Usb;

internal sealed class LinuxUsbTransport(
    IntPtr context,
    IntPtr handle,
    UsbDeviceDescriptor descriptor,
    LinuxUsbTransportId id,
    bool detachedKernelDriver,
    ILibUsbNativeAdapter? nativeAdapter = null) : IUsbTransport, IUsbTransportDiagnostics
{
    private const uint TransferTimeoutMilliseconds = 1000;
    private readonly ILibUsbNativeAdapter native = nativeAdapter ?? LibUsbNativeAdapter.Instance;
    private readonly byte interfaceNumber = id.InterfaceNumber;
    private readonly SemaphoreSlim readGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private int closed;
    private int resourcesDisposed;
    private UsbTransportError? lastError;

    public UsbDeviceDescriptor Descriptor { get; } = descriptor;

    public UsbEndpoint BulkInEndpoint { get; } = id.BulkIn;

    public UsbEndpoint BulkOutEndpoint { get; } = id.BulkOut;

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        if (buffer.Length == 0)
        {
            return 0;
        }

        await readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = native.BulkTransfer(handle, BulkInEndpoint.Address, rented, buffer.Length, out var transferred, TransferTimeoutMilliseconds);
                if (result == LibUsbNative.Success)
                {
                    rented.AsMemory(0, transferred).CopyTo(buffer);
                    return transferred;
                }

                if (result == LibUsbNative.ErrorTimeout)
                {
                    lastError = UsbTransportError.Timeout;
                    continue;
                }

                var exception = LinuxUsbErrors.Create(result, "read from Linux USB bulk endpoint");
                lastError = exception.Error;
                await AbortAsync(CancellationToken.None).ConfigureAwait(false);
                throw exception;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
            readGate.Release();
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var rented = ArrayPool<byte>.Shared.Rent(Math.Max(buffer.Length, 1));
        try
        {
            if (buffer.Length == 0)
            {
                return;
            }

            buffer.CopyTo(rented);
            var offset = 0;
            while (offset < buffer.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = buffer.Length - offset;
                if (offset != 0)
                {
                    Array.Copy(rented, offset, rented, 0, remaining);
                }

                var result = native.BulkTransfer(handle, BulkOutEndpoint.Address, rented, remaining, out var transferred, TransferTimeoutMilliseconds);
                if (result == LibUsbNative.Success)
                {
                    if (transferred == 0)
                    {
                        var exception = new UsbTransportException(UsbTransportError.Io, "libusb bulk write completed without transferring data.");
                        lastError = exception.Error;
                        await AbortAsync(CancellationToken.None).ConfigureAwait(false);
                        throw exception;
                    }

                    offset += transferred;
                    continue;
                }

                if (result == LibUsbNative.ErrorTimeout)
                {
                    lastError = UsbTransportError.Timeout;
                    continue;
                }

                var writeException = LinuxUsbErrors.Create(result, "write to Linux USB bulk endpoint");
                lastError = writeException.Error;
                await AbortAsync(CancellationToken.None).ConfigureAwait(false);
                throw writeException;
            }

            if (buffer.Length % BulkOutEndpoint.MaxPacketSize == 0)
            {
                await WriteZeroLengthPacketAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
            writeGate.Release();
        }
    }

    public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsClosed)
        {
            var result = native.ResetDevice(handle);
            await AbortAsync(CancellationToken.None).ConfigureAwait(false);
            if (result != LibUsbNative.Success)
            {
                throw LinuxUsbErrors.Create(result, "reset Linux USB device");
            }
        }
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
            "libusb",
            Descriptor.TransportId,
            BulkInEndpoint.Address,
            BulkInEndpoint.MaxPacketSize,
            BulkOutEndpoint.Address,
            BulkOutEndpoint.MaxPacketSize,
            !IsClosed,
            IsClosed ? "closed" : "open",
            new Dictionary<string, string>
            {
                ["interfaceNumber"] = interfaceNumber.ToString(CultureInfo.InvariantCulture),
                ["detachedKernelDriver"] = detachedKernelDriver.ToString(),
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

        _ = native.ReleaseInterface(handle, interfaceNumber);
        if (detachedKernelDriver)
        {
            _ = native.AttachKernelDriver(handle, interfaceNumber);
        }

        native.Close(handle);
        native.Exit(context);
    }

    private void DisposeManagedResources()
    {
        if (Interlocked.Exchange(ref resourcesDisposed, 1) != 0)
        {
            return;
        }

        readGate.Dispose();
        writeGate.Dispose();
    }

    private async ValueTask WriteZeroLengthPacketAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = native.BulkTransfer(handle, BulkOutEndpoint.Address, [], 0, out _, TransferTimeoutMilliseconds);
            if (result == LibUsbNative.Success)
            {
                return;
            }

            if (result == LibUsbNative.ErrorTimeout)
            {
                lastError = UsbTransportError.Timeout;
                continue;
            }

            var exception = LinuxUsbErrors.Create(result, "write zero-length packet to Linux USB bulk endpoint");
            lastError = exception.Error;
            await AbortAsync(CancellationToken.None).ConfigureAwait(false);
            throw exception;
        }
    }
}
