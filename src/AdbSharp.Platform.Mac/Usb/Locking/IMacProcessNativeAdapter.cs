namespace AdbSharp.Platform.Mac.Usb.Locking;

internal interface IMacProcessNativeAdapter
{
    IReadOnlyList<MacProcessSnapshot> EnumerateProcesses(CancellationToken cancellationToken);
}
