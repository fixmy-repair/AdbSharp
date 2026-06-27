using AdbSharp.Common;

namespace AdbSharp.Fastboot;

/// <summary>
/// Represents a failed Fastboot command.
/// </summary>
/// <param name="command">The command that failed.</param>
/// <param name="message">The bootloader failure message.</param>
public sealed class FastbootCommandException(string command, string message)
    : AdbSharpException($"Fastboot command '{command}' failed: {message}")
{
    /// <summary>
    /// Gets the command that failed.
    /// </summary>
    public string Command { get; } = command;

    /// <summary>
    /// Gets the bootloader failure message.
    /// </summary>
    public string BootloaderMessage { get; } = message;
}
