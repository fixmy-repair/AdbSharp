using System.Runtime.InteropServices;

namespace AdbSharp.Platform.Mac.Usb.Native;

internal sealed class MacObjCBlock(IntPtr pointer, GCHandle handle) : IDisposable
{
    private bool disposed;

    public IntPtr Pointer { get; } = pointer;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        MacObjC.BlockRelease(Pointer);
        if (handle.IsAllocated)
        {
            handle.Free();
        }
    }
}
