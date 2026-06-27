namespace AdbSharp.Adb;

/// <summary>
/// Represents a directory entry returned by the ADB file sync list operation.
/// </summary>
/// <param name="Name">The entry name as reported by the device.</param>
/// <param name="Statistics">The entry metadata.</param>
public sealed record AdbDirectoryEntry(string Name, AdbFileStat Statistics);
