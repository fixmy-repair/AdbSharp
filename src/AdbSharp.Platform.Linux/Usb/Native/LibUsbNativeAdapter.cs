namespace AdbSharp.Platform.Linux.Usb.Native;

internal sealed class LibUsbNativeAdapter : ILibUsbNativeAdapter
{
    public static LibUsbNativeAdapter Instance { get; } = new();

    private LibUsbNativeAdapter()
    {
    }

    public int BulkTransfer(IntPtr handle, byte endpoint, byte[] data, int length, out int transferred, uint timeout)
    {
        return LibUsbNative.libusb_bulk_transfer(handle, endpoint, data, length, out transferred, timeout);
    }

    public int ResetDevice(IntPtr handle)
    {
        return LibUsbNative.libusb_reset_device(handle);
    }

    public int ReleaseInterface(IntPtr handle, int interfaceNumber)
    {
        return LibUsbNative.libusb_release_interface(handle, interfaceNumber);
    }

    public int AttachKernelDriver(IntPtr handle, int interfaceNumber)
    {
        return LibUsbNative.libusb_attach_kernel_driver(handle, interfaceNumber);
    }

    public void Close(IntPtr handle)
    {
        LibUsbNative.libusb_close(handle);
    }

    public void Exit(IntPtr context)
    {
        LibUsbNative.libusb_exit(context);
    }
}
