namespace AdbSharp.Adb;

/// <summary>
/// Options used when creating Android package install sessions.
/// </summary>
public sealed class AdbInstallOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether an existing package can be replaced.
    /// </summary>
    public bool ReplaceExisting { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether test-only packages are allowed.
    /// </summary>
    public bool AllowTestPackages { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether version downgrade is allowed.
    /// </summary>
    public bool AllowDowngrade { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether all runtime permissions should be granted at install time.
    /// </summary>
    public bool GrantRuntimePermissions { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the session should be staged and applied after reboot.
    /// </summary>
    public bool Staged { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether rollback should be enabled for the install session.
    /// </summary>
    public bool EnableRollback { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether package manager should avoid killing the app during replacement.
    /// </summary>
    public bool DontKill { get; set; }

    /// <summary>
    /// Gets or sets the package name to install or update.
    /// </summary>
    public string? PackageName { get; set; }

    /// <summary>
    /// Gets or sets the package name to inherit from for partial installs.
    /// </summary>
    public string? InheritPackageName { get; set; }

    /// <summary>
    /// Gets or sets the installer package name.
    /// </summary>
    public string? InstallerPackageName { get; set; }

    /// <summary>
    /// Gets or sets the Android user id, such as <c>0</c>.
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// Gets or sets the staged-session ready timeout used when committing staged sessions.
    /// </summary>
    public TimeSpan? StagedReadyTimeout { get; set; }

    internal AdbInstallOptions Clone()
    {
        return new AdbInstallOptions
        {
            ReplaceExisting = ReplaceExisting,
            AllowTestPackages = AllowTestPackages,
            AllowDowngrade = AllowDowngrade,
            GrantRuntimePermissions = GrantRuntimePermissions,
            Staged = Staged,
            EnableRollback = EnableRollback,
            DontKill = DontKill,
            PackageName = PackageName,
            InheritPackageName = InheritPackageName,
            InstallerPackageName = InstallerPackageName,
            UserId = UserId,
            StagedReadyTimeout = StagedReadyTimeout
        };
    }
}
