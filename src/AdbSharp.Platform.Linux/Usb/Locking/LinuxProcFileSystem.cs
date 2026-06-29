using System.Globalization;

namespace AdbSharp.Platform.Linux.Usb.Locking;

internal sealed class LinuxProcFileSystem : ILinuxProcFileSystem
{
    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public IEnumerable<int> EnumerateProcessIds()
    {
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(directory);
            if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
            {
                yield return processId;
            }
        }
    }

    public IEnumerable<string> EnumerateFileDescriptors(int processId)
    {
        var directory = string.Create(CultureInfo.InvariantCulture, $"/proc/{processId}/fd");
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory)
            : [];
    }

    public string? ResolveLinkTarget(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName
                ?? File.ResolveLinkTarget(path, returnFinalTarget: false)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or FileNotFoundException)
        {
            return null;
        }
    }

    public LinuxFileIdentity? GetIdentity(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            return LinuxProcNative.StatxFile(
                LinuxProcNative.AtFdcwd,
                path,
                flags: 0,
                LinuxProcNative.StatxBasicStats,
                out var statx) == 0
                ? new LinuxFileIdentity(statx.DeviceIdMajor, statx.DeviceIdMinor, statx.Inode)
                : null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    public string? ReadProcessName(int processId)
    {
        try
        {
            return File.ReadAllText(string.Create(CultureInfo.InvariantCulture, $"/proc/{processId}/comm")).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or FileNotFoundException)
        {
            return null;
        }
    }

    public string? ReadExecutablePath(int processId)
    {
        return ResolveLinkTarget(string.Create(CultureInfo.InvariantCulture, $"/proc/{processId}/exe"));
    }
}
