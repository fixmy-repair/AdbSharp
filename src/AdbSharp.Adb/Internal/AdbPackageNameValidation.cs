namespace AdbSharp.Adb.Internal;

internal static class AdbPackageNameValidation
{
    public static string ValidatePackageIdentifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
            {
                throw new ArgumentException("Package identifiers may contain only ASCII letters, digits, '.', '_', and '-'.", nameof(value));
            }
        }

        return value;
    }

    public static string ValidateSplitName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
            {
                throw new ArgumentException("Split names may contain only ASCII letters, digits, '.', '_', and '-'.", nameof(value));
            }
        }

        return value;
    }
}
