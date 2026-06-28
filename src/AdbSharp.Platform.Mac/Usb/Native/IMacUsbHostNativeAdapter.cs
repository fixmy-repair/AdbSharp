using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Mac.Usb.Native;

internal interface IMacUsbHostNativeAdapter
{
    IntPtr CreateMutableData(nuint length);

    IntPtr CreateMutableData(ReadOnlySpan<byte> data);

    IntPtr MutableBytes(IntPtr data);

    bool SendIoRequest(IntPtr pipe, IntPtr data, double completionTimeout, out nuint bytesTransferred, out IntPtr error);

    ValueTask<MacUsbHostTransferResult> EnqueueIoRequestAsync(IntPtr pipe, IntPtr data, double completionTimeout, CancellationToken cancellationToken);

    bool AbortPipe(IntPtr pipe, out IntPtr error);

    bool ClearStall(IntPtr pipe, out IntPtr error);

    IntPtr CopyPipeWithAddress(IntPtr usbHostInterface, byte endpointAddress, out IntPtr error);

    void Release(IntPtr value);

    void Destroy(IntPtr value);

    UsbTransportException CreateException(IntPtr error, string operation);
}
