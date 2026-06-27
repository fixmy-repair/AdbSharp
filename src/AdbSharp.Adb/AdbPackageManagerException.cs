using AdbSharp.Common;

namespace AdbSharp.Adb;

/// <summary>
/// Represents a device-side Android package manager failure.
/// </summary>
/// <param name="message">The package manager failure message.</param>
/// <param name="output">The raw package manager output.</param>
public sealed class AdbPackageManagerException(string message, string output) : AdbSharpException(message)
{
    /// <summary>
    /// Gets the raw package manager output.
    /// </summary>
    public string Output { get; } = output;
}
