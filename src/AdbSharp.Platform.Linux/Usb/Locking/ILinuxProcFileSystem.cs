namespace AdbSharp.Platform.Linux.Usb.Locking;

internal interface ILinuxProcFileSystem
{
    bool DirectoryExists(string path);

    IEnumerable<int> EnumerateProcessIds();

    IEnumerable<string> EnumerateFileDescriptors(int processId);

    string? ResolveLinkTarget(string path);

    LinuxFileIdentity? GetIdentity(string path);

    string? ReadProcessName(int processId);

    string? ReadExecutablePath(int processId);
}
