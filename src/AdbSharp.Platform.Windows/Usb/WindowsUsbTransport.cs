using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AdbSharp.Common.Devices;
using AdbSharp.Platform.Windows.Usb.Native;
using AdbSharp.Transport.Usb;
using Microsoft.Win32.SafeHandles;

namespace AdbSharp.Platform.Windows.Usb;

[SupportedOSPlatform("windows")]
internal sealed class WindowsUsbTransport(
    SafeFileHandle deviceHandle,
    IntPtr winUsbHandle,
    UsbDeviceDescriptor descriptor,
    UsbEndpoint bulkIn,
    UsbEndpoint bulkOut,
    IWindowsUsbNativeAdapter? nativeAdapter = null) : IUsbTransport, IUsbTransportDiagnostics
{
    private readonly IWindowsUsbNativeAdapter native = nativeAdapter ?? WindowsUsbNativeAdapter.Instance;
    private readonly SemaphoreSlim readGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private int closed;
    private int resourcesDisposed;
    private int pendingTransfers;
    private UsbTransportError? lastError;

    public UsbDeviceDescriptor Descriptor { get; } = descriptor;

    public UsbEndpoint BulkInEndpoint { get; } = bulkIn;

    public UsbEndpoint BulkOutEndpoint { get; } = bulkOut;

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        if (buffer.Length == 0)
        {
            return 0;
        }

        await readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var nativeBuffer = IntPtr.Zero;
        try
        {
            nativeBuffer = Marshal.AllocHGlobal(buffer.Length);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var transferred = await TransferAsync(
                        read: true,
                        BulkInEndpoint.Address,
                        nativeBuffer,
                        buffer.Length,
                        "WinUSB overlapped bulk read failed.",
                        cancellationToken).ConfigureAwait(false);

                    if (transferred > 0)
                    {
                        var bytes = new byte[transferred];
                        Marshal.Copy(nativeBuffer, bytes, 0, transferred);
                        bytes.CopyTo(buffer);
                        return transferred;
                    }
                }
                catch (UsbTransportException ex) when (ex.Error == UsbTransportError.Timeout)
                {
                    lastError = ex.Error;
                }
                catch (UsbTransportException ex)
                {
                    lastError = ex.Error;
                    await AbortAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(nativeBuffer);
            readGate.Release();
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var nativeBuffer = IntPtr.Zero;
        try
        {
            if (buffer.Length == 0)
            {
                return;
            }

            nativeBuffer = Marshal.AllocHGlobal(buffer.Length);
            var managed = buffer.ToArray();
            Marshal.Copy(managed, 0, nativeBuffer, managed.Length);

            var offset = 0;
            while (offset < buffer.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = buffer.Length - offset;
                var pointer = IntPtr.Add(nativeBuffer, offset);
                try
                {
                    var transferred = await TransferAsync(
                        read: false,
                        BulkOutEndpoint.Address,
                        pointer,
                        remaining,
                        "WinUSB overlapped bulk write failed.",
                        cancellationToken).ConfigureAwait(false);
                    if (transferred == 0)
                    {
                        throw new UsbTransportException(UsbTransportError.Io, "WinUSB bulk write completed without transferring data.");
                    }

                    offset += transferred;
                }
                catch (UsbTransportException ex) when (ex.Error == UsbTransportError.Timeout)
                {
                    lastError = ex.Error;
                }
                catch (UsbTransportException ex)
                {
                    lastError = ex.Error;
                    await AbortAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }

            if (buffer.Length % BulkOutEndpoint.MaxPacketSize == 0)
            {
                await WriteZeroLengthPacketAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(nativeBuffer);
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
        if (Interlocked.Exchange(ref closed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _ = native.CancelIoEx(deviceHandle, IntPtr.Zero);
        _ = native.WinUsbFree(winUsbHandle);
        deviceHandle.Dispose();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await AbortAsync(CancellationToken.None).ConfigureAwait(false);
        DisposeManagedResources();
    }

    public UsbTransportDiagnosticSnapshot GetDiagnosticSnapshot()
    {
        return new UsbTransportDiagnosticSnapshot(
            "WinUSB",
            Descriptor.TransportId,
            BulkInEndpoint.Address,
            BulkInEndpoint.MaxPacketSize,
            BulkOutEndpoint.Address,
            BulkOutEndpoint.MaxPacketSize,
            !IsClosed,
            IsClosed ? "closed" : "open",
            new Dictionary<string, string>
            {
                ["pendingTransfers"] = Volatile.Read(ref pendingTransfers).ToString(CultureInfo.InvariantCulture),
                ["lastError"] = lastError?.ToString() ?? string.Empty,
                ["overlapped"] = "true"
            });
    }

    private bool IsClosed => Volatile.Read(ref closed) != 0;

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
        try
        {
            _ = await TransferAsync(
                read: false,
                BulkOutEndpoint.Address,
                IntPtr.Zero,
                0,
                "WinUSB overlapped zero-length bulk write failed.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (UsbTransportException ex) when (ex.Error == UsbTransportError.Timeout)
        {
            lastError = ex.Error;
        }
        catch (UsbTransportException ex)
        {
            lastError = ex.Error;
            await AbortAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The registered wait task is always observed before the manual reset event is disposed.")]
    private async ValueTask<int> TransferAsync(
        bool read,
        byte endpoint,
        IntPtr buffer,
        int length,
        string operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        using var completionEvent = new ManualResetEvent(false);
        var overlapped = AllocateOverlapped(completionEvent);
        Interlocked.Increment(ref pendingTransfers);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = read
                ? native.ReadPipe(winUsbHandle, endpoint, buffer, length, overlapped)
                : native.WritePipe(winUsbHandle, endpoint, buffer, length, overlapped);
            if (!started.Success && started.ErrorCode != WindowsUsbNative.ErrorIoPending)
            {
                throw WindowsUsbErrors.Create(started.ErrorCode, operation);
            }

            if (!started.Success)
            {
                try
                {
                    await native.WaitForOverlappedAsync(completionEvent, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _ = native.CancelIoEx(deviceHandle, overlapped);
                    await native.WaitForOverlappedAsync(completionEvent, CancellationToken.None).ConfigureAwait(false);
                    _ = native.GetOverlappedResult(deviceHandle, overlapped, wait: true);
                    throw;
                }
            }

            var result = native.GetOverlappedResult(deviceHandle, overlapped, wait: true);
            if (!result.Success)
            {
                throw WindowsUsbErrors.Create(result.ErrorCode, operation);
            }

            return result.BytesTransferred;
        }
        finally
        {
            Marshal.FreeHGlobal(overlapped);
            Interlocked.Decrement(ref pendingTransfers);
        }
    }

    private static IntPtr AllocateOverlapped(WaitHandle waitHandle)
    {
        var overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        var value = new NativeOverlapped
        {
            EventHandle = waitHandle.SafeWaitHandle.DangerousGetHandle()
        };
        Marshal.StructureToPtr(value, overlapped, false);
        return overlapped;
    }

}
