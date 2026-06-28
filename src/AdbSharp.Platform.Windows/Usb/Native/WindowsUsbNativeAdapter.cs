using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AdbSharp.Platform.Windows.Usb.Native;

internal sealed class WindowsUsbNativeAdapter : IWindowsUsbNativeAdapter
{
    public static WindowsUsbNativeAdapter Instance { get; } = new();

    private WindowsUsbNativeAdapter()
    {
    }

    public WindowsUsbCallResult ReadPipe(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, int bufferLength, IntPtr overlapped)
    {
        var success = WindowsUsbNative.WinUsbReadPipe(interfaceHandle, pipeId, buffer, bufferLength, IntPtr.Zero, overlapped);
        return new WindowsUsbCallResult(success, Marshal.GetLastPInvokeError());
    }

    public WindowsUsbCallResult WritePipe(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, int bufferLength, IntPtr overlapped)
    {
        var success = WindowsUsbNative.WinUsbWritePipe(interfaceHandle, pipeId, buffer, bufferLength, IntPtr.Zero, overlapped);
        return new WindowsUsbCallResult(success, Marshal.GetLastPInvokeError());
    }

    public WindowsUsbOverlappedResult GetOverlappedResult(SafeFileHandle fileHandle, IntPtr overlapped, bool wait)
    {
        var success = WindowsUsbNative.GetOverlappedResult(fileHandle, overlapped, out var bytesTransferred, wait);
        return new WindowsUsbOverlappedResult(success, bytesTransferred, Marshal.GetLastPInvokeError());
    }

    public WindowsUsbCallResult CancelIoEx(SafeFileHandle fileHandle, IntPtr overlapped)
    {
        var success = WindowsUsbNative.CancelIoEx(fileHandle, overlapped);
        return new WindowsUsbCallResult(success, Marshal.GetLastPInvokeError());
    }

    public bool WinUsbFree(IntPtr interfaceHandle)
    {
        return WindowsUsbNative.WinUsbFree(interfaceHandle);
    }

    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The registered wait is unregistered before the task completes.")]
    public ValueTask WaitForOverlappedAsync(WaitHandle waitHandle, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new RegisteredWaitState(completion);
        state.Registration = ThreadPool.RegisterWaitForSingleObject(
            waitHandle,
            static (context, _) =>
            {
                var state = (RegisteredWaitState)context!;
                state.Registration?.Unregister(null);
                state.Completion.TrySetResult();
            },
            state,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: true);
        return new ValueTask(completion.Task.WaitAsync(cancellationToken));
    }

    private sealed class RegisteredWaitState(TaskCompletionSource completion)
    {
        public TaskCompletionSource Completion { get; } = completion;

        public RegisteredWaitHandle? Registration { get; set; }
    }
}
