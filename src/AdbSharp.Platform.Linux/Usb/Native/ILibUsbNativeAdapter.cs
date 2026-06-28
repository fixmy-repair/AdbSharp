namespace AdbSharp.Platform.Linux.Usb.Native;

internal interface ILibUsbNativeAdapter
{
    int BulkTransfer(IntPtr handle, byte endpoint, byte[] data, int length, out int transferred, uint timeout);

    int ResetDevice(IntPtr handle);

    int ReleaseInterface(IntPtr handle, int interfaceNumber);

    int AttachKernelDriver(IntPtr handle, int interfaceNumber);

    void Close(IntPtr handle);

    void Exit(IntPtr context);
}
