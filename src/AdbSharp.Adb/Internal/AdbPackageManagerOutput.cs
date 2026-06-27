using System.Globalization;
using System.Text.RegularExpressions;

namespace AdbSharp.Adb.Internal;

internal static partial class AdbPackageManagerOutput
{
    public static int ParseCreatedSessionId(string output)
    {
        var match = InstallSessionRegex().Match(output);
        if (!match.Success)
        {
            ThrowIfFailure(output);
            throw new AdbPackageManagerException("Package manager did not report an install session id.", output);
        }

        return int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture);
    }

    public static string EnsureSuccess(string output)
    {
        ThrowIfFailure(output);
        var trimmed = output.Trim();
        if (!trimmed.StartsWith("Success", StringComparison.Ordinal))
        {
            throw new AdbPackageManagerException("Package manager did not report success.", output);
        }

        return trimmed;
    }

    private static void ThrowIfFailure(string output)
    {
        var trimmed = output.Trim();
        if (trimmed.StartsWith("Failure [", StringComparison.Ordinal) ||
            trimmed.StartsWith("Error [", StringComparison.Ordinal) ||
            trimmed.StartsWith("Exception", StringComparison.Ordinal))
        {
            throw new AdbPackageManagerException(ExtractFailureMessage(trimmed), output);
        }
    }

    private static string ExtractFailureMessage(string output)
    {
        var bracketStart = output.IndexOf('[', StringComparison.Ordinal);
        var bracketEnd = output.LastIndexOf(']', StringComparison.Ordinal);
        if (bracketStart >= 0 && bracketEnd > bracketStart)
        {
            return output[(bracketStart + 1)..bracketEnd];
        }

        return output;
    }

    [GeneratedRegex(@"Success:\s+created install session \[(?<id>\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex InstallSessionRegex();
}
