using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Mac.Usb.Native;

internal sealed class MacUsbHostNativeAdapter : IMacUsbHostNativeAdapter
{
    public static MacUsbHostNativeAdapter Instance { get; } = new();

    private MacUsbHostNativeAdapter()
    {
    }

    public IntPtr CreateMutableData(nuint length)
    {
        return MacObjC.CreateMutableData(length);
    }

    public IntPtr CreateMutableData(ReadOnlySpan<byte> data)
    {
        return MacObjC.CreateMutableData(data);
    }

    public IntPtr MutableBytes(IntPtr data)
    {
        return MacObjC.MutableBytes(data);
    }

    public bool SendIoRequest(IntPtr pipe, IntPtr data, double completionTimeout, out nuint bytesTransferred, out IntPtr error)
    {
        return MacObjC.SendIoRequest(pipe, data, completionTimeout, out bytesTransferred, out error);
    }

    public async ValueTask<MacUsbHostTransferResult> EnqueueIoRequestAsync(
        IntPtr pipe,
        IntPtr data,
        double completionTimeout,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<MacUsbHostTransferResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var block = MacObjC.CreateIoCompletionBlock(completion);
        using var cancellationRegistration = RegisterAbort(pipe, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!MacObjC.EnqueueIoRequest(pipe, data, completionTimeout, block.Pointer, out var error))
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw MacObjC.CreateException(error, "enqueue macOS IOUSBHost I/O");
        }

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool AbortPipe(IntPtr pipe, out IntPtr error)
    {
        return MacObjC.AbortPipe(pipe, out error);
    }

    public bool ClearStall(IntPtr pipe, out IntPtr error)
    {
        return MacObjC.ClearStall(pipe, out error);
    }

    public IntPtr CopyPipeWithAddress(IntPtr usbHostInterface, byte endpointAddress, out IntPtr error)
    {
        return MacObjC.CopyPipeWithAddress(usbHostInterface, endpointAddress, out error);
    }

    public void Release(IntPtr value)
    {
        MacObjC.Release(value);
    }

    public void Destroy(IntPtr value)
    {
        MacObjC.Destroy(value);
    }

    public UsbTransportException CreateException(IntPtr error, string operation)
    {
        return MacObjC.CreateException(error, operation);
    }

    private CancellationTokenRegistration RegisterAbort(IntPtr pipe, CancellationToken cancellationToken)
    {
        return cancellationToken.CanBeCanceled
            ? cancellationToken.Register(static state =>
            {
                _ = MacObjC.AbortPipe((IntPtr)state!, out _);
            }, pipe)
            : default;
    }
}
