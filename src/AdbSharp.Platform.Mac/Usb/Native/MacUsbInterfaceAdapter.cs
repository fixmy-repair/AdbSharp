using System.Runtime.InteropServices;

namespace AdbSharp.Platform.Mac.Usb.Native;

internal sealed class MacUsbInterfaceAdapter : IMacUsbInterfaceAdapter
{
    public static MacUsbInterfaceAdapter Instance { get; } = new();

    private MacUsbInterfaceAdapter()
    {
    }

    public async ValueTask<MacUsbAsyncTransferResult> ReadPipeAsyncTo(
        IntPtr interfacePointer,
        byte pipeReference,
        IntPtr buffer,
        uint length,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<MacUsbAsyncTransferResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(completion);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var cancellationRegistration = RegisterAbort(interfacePointer, pipeReference, cancellationToken);
            unsafe
            {
                var result = MacUsbInterface.ReadPipeAsyncTo(
                    interfacePointer,
                    pipeReference,
                    buffer,
                    length,
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

    public uint WritePipe(IntPtr interfacePointer, byte pipeReference, IntPtr buffer, uint length)
    {
        return MacUsbInterface.WritePipe(interfacePointer, pipeReference, buffer, length);
    }

    public uint WritePipe(IntPtr interfacePointer, byte pipeReference, byte[] buffer, uint length)
    {
        return MacUsbInterface.WritePipe(interfacePointer, pipeReference, buffer, length);
    }

    public uint AbortPipe(IntPtr interfacePointer, byte pipeReference)
    {
        return MacUsbInterface.AbortPipe(interfacePointer, pipeReference);
    }

    public uint ClearPipeStall(IntPtr interfacePointer, byte pipeReference)
    {
        return MacUsbInterface.ClearPipeStall(interfacePointer, pipeReference);
    }

    public uint GetPipeStatus(IntPtr interfacePointer, byte pipeReference)
    {
        return MacUsbInterface.GetPipeStatus(interfacePointer, pipeReference);
    }

    public uint Close(IntPtr interfacePointer)
    {
        return MacUsbInterface.Close(interfacePointer);
    }

    public uint Release(IntPtr interfacePointer)
    {
        return MacUsbInterface.Release(interfacePointer);
    }

    public uint CreateInterfaceAsyncEventSource(IntPtr interfacePointer, out IntPtr source)
    {
        return MacUsbInterface.CreateInterfaceAsyncEventSource(interfacePointer, out source);
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

    private CancellationTokenRegistration RegisterAbort(IntPtr interfacePointer, byte pipeReference, CancellationToken cancellationToken)
    {
        return cancellationToken.CanBeCanceled
            ? cancellationToken.Register(static state =>
            {
                var (interfacePointer, pipe) = ((IntPtr InterfacePointer, byte Pipe))state!;
                _ = MacUsbInterface.AbortPipe(interfacePointer, pipe);
            }, (interfacePointer, pipeReference))
            : default;
    }
}
