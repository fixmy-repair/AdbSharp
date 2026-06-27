using System.Buffers;
using AdbSharp.Common.Devices;
using AdbSharp.Platform.Windows.Usb.Native;
using AdbSharp.Transport.Usb;
using Microsoft.Win32.SafeHandles;

namespace AdbSharp.Platform.Windows.Usb;

internal sealed class WindowsUsbTransport(
    SafeFileHandle deviceHandle,
    IntPtr winUsbHandle,
    UsbDeviceDescriptor descriptor,
    UsbEndpoint bulkIn,
    UsbEndpoint bulkOut) : IUsbTransport
{
    private readonly SemaphoreSlim readGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private bool disposed;

    public UsbDeviceDescriptor Descriptor { get; } = descriptor;

    public UsbEndpoint BulkInEndpoint { get; } = bulkIn;

    public UsbEndpoint BulkOutEndpoint { get; } = bulkOut;

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
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
                if (WindowsUsbNative.WinUsbReadPipe(winUsbHandle, BulkInEndpoint.Address, rented, buffer.Length, out var transferred, IntPtr.Zero))
                {
                    rented.AsMemory(0, transferred).CopyTo(buffer);
                    return transferred;
                }

                var exception = WindowsUsbErrors.Create("WinUSB bulk read failed.");
                if (exception.Error != UsbTransportError.Timeout)
                {
                    throw exception;
                }

                cancellationToken.ThrowIfCancellationRequested();
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
        ObjectDisposedException.ThrowIf(disposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
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

                if (!WindowsUsbNative.WinUsbWritePipe(winUsbHandle, BulkOutEndpoint.Address, rented, remaining, out var transferred, IntPtr.Zero))
                {
                    var exception = WindowsUsbErrors.Create("WinUSB bulk write failed.");
                    if (exception.Error == UsbTransportError.Timeout)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        continue;
                    }

                    throw exception;
                }

                if (transferred == 0)
                {
                    throw new UsbTransportException(UsbTransportError.Io, "WinUSB bulk write completed without transferring data.");
                }

                offset += transferred;
            }
        }
        finally
        {
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
        _ = WindowsUsbNative.WinUsbFree(winUsbHandle);
        deviceHandle.Dispose();
        readGate.Dispose();
        writeGate.Dispose();
        return ValueTask.CompletedTask;
    }

}
