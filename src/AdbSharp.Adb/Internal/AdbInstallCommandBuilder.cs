using System.Globalization;
using System.Text;

namespace AdbSharp.Adb.Internal;

internal static class AdbInstallCommandBuilder
{
    public static string CreateSession(AdbInstallOptions options, long? totalSize)
    {
        var command = new StringBuilder("pm install-create");
        AppendInstallOptions(command, options);
        if (totalSize is not null)
        {
            if (totalSize.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalSize), "Install session size must be positive.");
            }

            command.Append(" -S ");
            command.Append(totalSize.Value.ToString(CultureInfo.InvariantCulture));
        }

        return command.ToString();
    }

    public static string WriteSplit(int sessionId, string splitName, long size)
    {
        if (sessionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId), "Install session id must be positive.");
        }

        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Package file size must be positive.");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"pm install-write -S {size} {sessionId} {AdbPackageNameValidation.ValidateSplitName(splitName)} -");
    }

    public static string Commit(int sessionId, TimeSpan? stagedReadyTimeout)
    {
        if (sessionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId), "Install session id must be positive.");
        }

        if (stagedReadyTimeout is null)
        {
            return string.Create(CultureInfo.InvariantCulture, $"pm install-commit {sessionId}");
        }

        var timeoutMilliseconds = checked((long)stagedReadyTimeout.Value.TotalMilliseconds);
        if (timeoutMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stagedReadyTimeout), "Staged ready timeout cannot be negative.");
        }

        return string.Create(CultureInfo.InvariantCulture, $"pm install-commit --staged-ready-timeout {timeoutMilliseconds} {sessionId}");
    }

    public static string Abandon(int sessionId)
    {
        if (sessionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId), "Install session id must be positive.");
        }

        return string.Create(CultureInfo.InvariantCulture, $"pm install-abandon {sessionId}");
    }

    private static void AppendInstallOptions(StringBuilder command, AdbInstallOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        command.Append(options.ReplaceExisting ? " -r" : " -R");

        if (options.AllowTestPackages)
        {
            command.Append(" -t");
        }

        if (options.AllowDowngrade)
        {
            command.Append(" -d");
        }

        if (options.GrantRuntimePermissions)
        {
            command.Append(" -g");
        }

        if (options.DontKill)
        {
            command.Append(" --dont-kill");
        }

        if (options.Staged)
        {
            command.Append(" --staged");
        }

        if (options.EnableRollback)
        {
            command.Append(" --enable-rollback");
        }

        AppendPackageOption(command, "-i", options.InstallerPackageName);
        AppendPackageOption(command, "--pkg", options.PackageName);
        AppendPackageOption(command, "-p", options.InheritPackageName);

        if (options.UserId is not null)
        {
            if (options.UserId.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Android user id cannot be negative.");
            }

            command.Append(" --user ");
            command.Append(options.UserId.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendPackageOption(StringBuilder command, string option, string? packageName)
    {
        if (packageName is null)
        {
            return;
        }

        command.Append(' ');
        command.Append(option);
        command.Append(' ');
        command.Append(AdbPackageNameValidation.ValidatePackageIdentifier(packageName));
    }
}
