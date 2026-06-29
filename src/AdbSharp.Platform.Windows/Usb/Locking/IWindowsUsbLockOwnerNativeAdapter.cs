namespace AdbSharp.Platform.Windows.Usb.Locking;

internal interface IWindowsUsbLockOwnerNativeAdapter
{
    WindowsUsbLockOwnerSnapshot FindOwners(string devicePath, CancellationToken cancellationToken);
}
