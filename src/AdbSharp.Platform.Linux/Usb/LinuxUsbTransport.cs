using System.Buffers;
using AdbSharp.Common.Devices;
using AdbSharp.Platform.Linux.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Linux.Usb;

internal sealed class LinuxUsbTransport(
    IntPtr context,
    IntPtr handle,
    UsbDeviceDescriptor descriptor,
    LinuxUsbTransportId id,
    bool detachedKernelDriver) : IUsbTransport
{
    private const uint TransferTimeoutMilliseconds = 1000;
    private readonly byte interfaceNumber = id.InterfaceNumber;
    private readonly SemaphoreSlim readGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private bool disposed;

    public UsbDeviceDescriptor Descriptor { get; } = descriptor;

    public UsbEndpoint BulkInEndpoint { get; } = id.BulkIn;

    public UsbEndpoint BulkOutEndpoint { get; } = id.BulkOut;

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
                var result = LibUsbNative.libusb_bulk_transfer(handle, BulkInEndpoint.Address, rented, buffer.Length, out var transferred, TransferTimeoutMilliseconds);
                if (result == LibUsbNative.Success)
                {
                    rented.AsMemory(0, transferred).CopyTo(buffer);
                    return transferred;
                }

                if (result != LibUsbNative.ErrorTimeout)
                {
                    throw LinuxUsbErrors.Create(result, "read from Linux USB bulk endpoint");
                }
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

                var result = LibUsbNative.libusb_bulk_transfer(handle, BulkOutEndpoint.Address, rented, remaining, out var transferred, TransferTimeoutMilliseconds);
                if (result == LibUsbNative.Success)
                {
                    if (transferred == 0)
                    {
                        throw new UsbTransportException(UsbTransportError.Io, "libusb bulk write completed without transferring data.");
                    }

                    offset += transferred;
                    continue;
                }

                if (result != LibUsbNative.ErrorTimeout)
                {
                    throw LinuxUsbErrors.Create(result, "write to Linux USB bulk endpoint");
                }
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
        _ = LibUsbNative.libusb_release_interface(handle, interfaceNumber);
        if (detachedKernelDriver)
        {
            _ = LibUsbNative.libusb_attach_kernel_driver(handle, interfaceNumber);
        }

        LibUsbNative.libusb_close(handle);
        LibUsbNative.libusb_exit(context);
        readGate.Dispose();
        writeGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
