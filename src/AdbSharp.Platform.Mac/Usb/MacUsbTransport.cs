using System.Buffers;
using AdbSharp.Common.Devices;
using AdbSharp.Platform.Mac.Usb.Native;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Mac.Usb;

internal sealed class MacUsbTransport(
    IntPtr interfacePointer,
    UsbDeviceDescriptor descriptor,
    UsbEndpoint bulkInEndpoint,
    UsbEndpoint bulkOutEndpoint,
    byte bulkInPipeReference,
    byte bulkOutPipeReference) : IUsbTransport
{
    private readonly SemaphoreSlim readGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
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
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = MacUsbInterface.ReadPipeTo(
                    interfacePointer,
                    bulkInPipeReference,
                    rented,
                    checked((uint)buffer.Length),
                    MacNative.TransferTimeoutMilliseconds,
                    MacNative.TransferTimeoutMilliseconds,
                    out var transferred);

                if (result == MacNative.Success)
                {
                    rented.AsMemory(0, checked((int)transferred)).CopyTo(buffer);
                    return checked((int)transferred);
                }

                var exception = MacUsbErrors.Create(result, "read from macOS USB bulk pipe");
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
            cancellationToken.ThrowIfCancellationRequested();
            buffer.CopyTo(rented);
            var result = MacUsbInterface.WritePipeTo(
                interfacePointer,
                bulkOutPipeReference,
                rented,
                checked((uint)buffer.Length),
                MacNative.TransferTimeoutMilliseconds,
                MacNative.TransferTimeoutMilliseconds);
            if (result == MacNative.Success)
            {
                return;
            }

            var exception = MacUsbErrors.Create(result, "write to macOS USB bulk pipe");
            if (exception.Error == UsbTransportError.Timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw exception;
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
        _ = MacUsbInterface.Close(interfacePointer);
        _ = MacUsbInterface.Release(interfacePointer);
        readGate.Dispose();
        writeGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
