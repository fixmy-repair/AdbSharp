namespace AdbSharp.Adb;

/// <summary>
/// Captures the result returned by the ADB shell v2 protocol.
/// </summary>
/// <param name="ExitCode">The process exit code, or <see langword="null" /> when the device closed the stream before reporting one.</param>
/// <param name="StandardOutput">The UTF-8 decoded standard output stream.</param>
/// <param name="StandardError">The UTF-8 decoded standard error stream.</param>
public sealed record AdbShellResult(int? ExitCode, string StandardOutput, string StandardError);
