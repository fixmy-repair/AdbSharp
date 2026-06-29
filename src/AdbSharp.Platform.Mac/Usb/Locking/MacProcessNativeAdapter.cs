using System.Runtime.InteropServices;

namespace AdbSharp.Platform.Mac.Usb.Locking;

internal sealed class MacProcessNativeAdapter : IMacProcessNativeAdapter
{
    public IReadOnlyList<MacProcessSnapshot> EnumerateProcesses(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS())
        {
            return [];
        }

        var bufferSize = 4096 * sizeof(int);
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                var used = MacProcessNative.ProcListPids(MacProcessNative.ProcAllPids, 0, buffer, bufferSize);
                if (used <= 0)
                {
                    return [];
                }

                if (used >= bufferSize)
                {
                    bufferSize *= 2;
                    continue;
                }

                var count = used / sizeof(int);
                var processes = new List<MacProcessSnapshot>(count);
                for (var index = 0; index < count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var processId = Marshal.ReadInt32(buffer, index * sizeof(int));
                    if (processId <= 0)
                    {
                        continue;
                    }

                    processes.Add(new MacProcessSnapshot(processId, ReadProcessName(processId), ReadProcessPath(processId)));
                }

                return processes;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return [];
    }

    private static string? ReadProcessName(int processId)
    {
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            var length = MacProcessNative.ProcName(processId, buffer, 256);
            return length <= 0 ? null : Marshal.PtrToStringUTF8(buffer, length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? ReadProcessPath(int processId)
    {
        var buffer = Marshal.AllocHGlobal(MacProcessNative.ProcPidPathInfoMaxSize);
        try
        {
            var length = MacProcessNative.ProcPidPath(processId, buffer, MacProcessNative.ProcPidPathInfoMaxSize);
            return length <= 0 ? null : Marshal.PtrToStringUTF8(buffer, length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
