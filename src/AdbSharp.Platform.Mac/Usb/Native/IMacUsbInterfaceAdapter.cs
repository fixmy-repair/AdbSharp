namespace AdbSharp.Platform.Mac.Usb.Native;

internal interface IMacUsbInterfaceAdapter
{
    ValueTask<MacUsbAsyncTransferResult> ReadPipeAsyncTo(
        IntPtr interfacePointer,
        byte pipeReference,
        IntPtr buffer,
        uint length,
        CancellationToken cancellationToken);

    uint WritePipe(IntPtr interfacePointer, byte pipeReference, IntPtr buffer, uint length);

    uint WritePipe(IntPtr interfacePointer, byte pipeReference, byte[] buffer, uint length);

    uint AbortPipe(IntPtr interfacePointer, byte pipeReference);

    uint ClearPipeStall(IntPtr interfacePointer, byte pipeReference);

    uint GetPipeStatus(IntPtr interfacePointer, byte pipeReference);

    uint Close(IntPtr interfacePointer);

    uint Release(IntPtr interfacePointer);

    uint CreateInterfaceAsyncEventSource(IntPtr interfacePointer, out IntPtr source);
}
