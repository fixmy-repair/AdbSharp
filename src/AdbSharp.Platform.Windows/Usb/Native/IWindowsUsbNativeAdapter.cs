using Microsoft.Win32.SafeHandles;

namespace AdbSharp.Platform.Windows.Usb.Native;

internal interface IWindowsUsbNativeAdapter
{
    WindowsUsbCallResult ReadPipe(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, int bufferLength, IntPtr overlapped);

    WindowsUsbCallResult WritePipe(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, int bufferLength, IntPtr overlapped);

    WindowsUsbOverlappedResult GetOverlappedResult(SafeFileHandle fileHandle, IntPtr overlapped, bool wait);

    WindowsUsbCallResult CancelIoEx(SafeFileHandle fileHandle, IntPtr overlapped);

    bool WinUsbFree(IntPtr interfaceHandle);

    ValueTask WaitForOverlappedAsync(WaitHandle waitHandle, CancellationToken cancellationToken);
}
