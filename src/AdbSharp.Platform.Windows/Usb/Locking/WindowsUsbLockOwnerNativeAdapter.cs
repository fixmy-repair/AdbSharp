using System.Globalization;
using System.Runtime.InteropServices;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Windows.Usb.Locking;

internal sealed class WindowsUsbLockOwnerNativeAdapter : IWindowsUsbLockOwnerNativeAdapter
{
    public WindowsUsbLockOwnerSnapshot FindOwners(string devicePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return new WindowsUsbLockOwnerSnapshot([], IsPartial: false, "Windows handle owner detection is only available on Windows.");
        }

        var normalizedTarget = NormalizeDevicePath(devicePath);
        var identityTokens = ExtractIdentityTokens(normalizedTarget);
        var systemHandles = QuerySystemHandles();
        if (systemHandles == IntPtr.Zero)
        {
            return new WindowsUsbLockOwnerSnapshot([], IsPartial: true, "Windows system handle enumeration failed.");
        }

        try
        {
            return FindOwners(systemHandles, normalizedTarget, identityTokens, cancellationToken);
        }
        finally
        {
            Marshal.FreeHGlobal(systemHandles);
        }
    }

    internal static string NormalizeDevicePath(string path)
    {
        var normalized = path.Trim().Replace('/', '\\').TrimEnd('\\');
        foreach (var prefix in new[] { @"\\?\", @"\??\", @"\GLOBAL??\", @"GLOBAL??\" })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        }

        return normalized.ToUpperInvariant();
    }

    private static WindowsUsbLockOwnerSnapshot FindOwners(
        IntPtr systemHandles,
        string normalizedTarget,
        IReadOnlyList<string> identityTokens,
        CancellationToken cancellationToken)
    {
        var owners = new List<WindowsUsbLockOwnerCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isPartial = false;
        var handleCount = Marshal.ReadIntPtr(systemHandles).ToInt64();
        var entrySize = Marshal.SizeOf<WindowsUsbLockOwnerNative.SystemHandleTableEntryInfoEx>();
        var entries = IntPtr.Add(systemHandles, IntPtr.Size * 2);
        var currentProcess = WindowsUsbLockOwnerNative.GetCurrentProcess();

        for (long index = 0; index < handleCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryPointer = IntPtr.Add(entries, checked((int)(index * entrySize)));
            var entry = Marshal.PtrToStructure<WindowsUsbLockOwnerNative.SystemHandleTableEntryInfoEx>(entryPointer);
            var processId = entry.UniqueProcessId.ToInt64();
            if (processId is <= 0 or > int.MaxValue)
            {
                continue;
            }

            var processHandle = WindowsUsbLockOwnerNative.OpenProcess(
                WindowsUsbLockOwnerNative.ProcessDuplicateHandle | WindowsUsbLockOwnerNative.ProcessQueryLimitedInformation,
                inheritHandle: false,
                checked((int)processId));
            if (processHandle == IntPtr.Zero)
            {
                if (Marshal.GetLastPInvokeError() == WindowsUsbLockOwnerNative.ErrorAccessDenied)
                {
                    isPartial = true;
                }

                continue;
            }

            try
            {
                if (!WindowsUsbLockOwnerNative.DuplicateHandle(
                    processHandle,
                    entry.HandleValue,
                    currentProcess,
                    out var duplicatedHandle,
                    desiredAccess: 0,
                    inheritHandle: false,
                    WindowsUsbLockOwnerNative.DuplicateSameAccess))
                {
                    if (Marshal.GetLastPInvokeError() == WindowsUsbLockOwnerNative.ErrorAccessDenied)
                    {
                        isPartial = true;
                    }

                    continue;
                }

                try
                {
                    var objectName = QueryObjectName(duplicatedHandle);
                    if (!TryMatch(normalizedTarget, identityTokens, objectName, out var confidence))
                    {
                        continue;
                    }

                    var executablePath = QueryProcessPath(processHandle);
                    var processName = executablePath is null ? null : Path.GetFileNameWithoutExtension(executablePath);
                    var key = string.Create(CultureInfo.InvariantCulture, $"{processId}:{objectName}");
                    if (seen.Add(key))
                    {
                        owners.Add(new WindowsUsbLockOwnerCandidate(
                            checked((int)processId),
                            processName,
                            executablePath,
                            objectName,
                            confidence));
                    }
                }
                finally
                {
                    _ = WindowsUsbLockOwnerNative.CloseHandle(duplicatedHandle);
                }
            }
            finally
            {
                _ = WindowsUsbLockOwnerNative.CloseHandle(processHandle);
            }
        }

        return new WindowsUsbLockOwnerSnapshot(owners, isPartial, isPartial ? "Some process handles could not be inspected." : null);
    }

    private static IntPtr QuerySystemHandles()
    {
        var length = 1024 * 1024;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(length);
            var status = WindowsUsbLockOwnerNative.NtQuerySystemInformation(
                WindowsUsbLockOwnerNative.SystemExtendedHandleInformation,
                buffer,
                length,
                out var requiredLength);
            if (status == WindowsUsbLockOwnerNative.StatusSuccess)
            {
                return buffer;
            }

            Marshal.FreeHGlobal(buffer);
            if (status is not (WindowsUsbLockOwnerNative.StatusInfoLengthMismatch
                or WindowsUsbLockOwnerNative.StatusBufferOverflow
                or WindowsUsbLockOwnerNative.StatusBufferTooSmall))
            {
                return IntPtr.Zero;
            }

            length = Math.Max(length * 2, requiredLength);
        }

        return IntPtr.Zero;
    }

    private static string? QueryObjectName(IntPtr handle)
    {
        _ = WindowsUsbLockOwnerNative.NtQueryObject(
            handle,
            WindowsUsbLockOwnerNative.ObjectNameInformation,
            IntPtr.Zero,
            0,
            out var requiredLength);
        if (requiredLength <= 0)
        {
            requiredLength = 4096;
        }

        var buffer = Marshal.AllocHGlobal(requiredLength);
        try
        {
            var status = WindowsUsbLockOwnerNative.NtQueryObject(
                handle,
                WindowsUsbLockOwnerNative.ObjectNameInformation,
                buffer,
                requiredLength,
                out _);
            if (status != WindowsUsbLockOwnerNative.StatusSuccess)
            {
                return null;
            }

            var value = Marshal.PtrToStructure<WindowsUsbLockOwnerNative.UnicodeString>(buffer);
            return value.Length == 0 || value.Buffer == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUni(value.Buffer, value.Length / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? QueryProcessPath(IntPtr processHandle)
    {
        var size = 32768;
        var buffer = Marshal.AllocHGlobal(size * sizeof(char));
        try
        {
            return WindowsUsbLockOwnerNative.QueryFullProcessImageName(processHandle, 0, buffer, ref size)
                ? Marshal.PtrToStringUni(buffer, size)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryMatch(
        string normalizedTarget,
        IReadOnlyList<string> identityTokens,
        string? objectName,
        out UsbDeviceLockOwnerConfidence confidence)
    {
        confidence = UsbDeviceLockOwnerConfidence.Low;
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        var normalizedObject = NormalizeDevicePath(objectName);
        if (string.Equals(normalizedTarget, normalizedObject, StringComparison.OrdinalIgnoreCase)
            || normalizedObject.EndsWith('\\' + normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            confidence = UsbDeviceLockOwnerConfidence.Exact;
            return true;
        }

        if (identityTokens.Count > 0 && identityTokens.All(token => normalizedObject.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            confidence = UsbDeviceLockOwnerConfidence.High;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> ExtractIdentityTokens(string normalizedTarget)
    {
        var tokens = normalizedTarget
            .Split(['\\', '#', '&', '{', '}', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static token => token.StartsWith("VID_", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("PID_", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return tokens;
    }
}
