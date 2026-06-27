namespace AdbSharp.Adb;

/// <summary>
/// Describes a completed Android package install session.
/// </summary>
/// <param name="SessionId">The package manager session id.</param>
/// <param name="Output">The package manager commit output.</param>
/// <param name="IsStaged">Indicates whether the install was staged for application after reboot.</param>
public sealed record AdbPackageInstallResult(int SessionId, string Output, bool IsStaged);
