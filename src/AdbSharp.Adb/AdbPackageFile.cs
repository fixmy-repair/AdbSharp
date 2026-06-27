using AdbSharp.Adb.Internal;

namespace AdbSharp.Adb;

/// <summary>
/// Describes an APK or split APK stream written into an Android package install session.
/// </summary>
/// <param name="contents">The APK stream.</param>
/// <param name="splitName">The split name used by package manager, such as <c>base.apk</c>.</param>
/// <param name="size">The number of bytes to write. When omitted, the remaining length of a seekable stream is used.</param>
public sealed class AdbPackageFile(Stream contents, string splitName, long? size = null)
{
    /// <summary>
    /// Gets the APK stream.
    /// </summary>
    public Stream Contents { get; } = contents ?? throw new ArgumentNullException(nameof(contents));

    /// <summary>
    /// Gets the split name used by package manager.
    /// </summary>
    public string SplitName { get; } = AdbPackageNameValidation.ValidateSplitName(splitName);

    /// <summary>
    /// Gets the number of bytes to write.
    /// </summary>
    public long Size { get; } = ResolveSize(contents, size);

    private static long ResolveSize(Stream contents, long? size)
    {
        if (size is not null)
        {
            if (size.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "Package file size must be positive.");
            }

            return size.Value;
        }

        if (!contents.CanSeek)
        {
            throw new ArgumentException("Package file size is required for non-seekable streams.", nameof(contents));
        }

        var remaining = contents.Length - contents.Position;
        if (remaining <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contents), "Package file stream must contain at least one byte.");
        }

        return remaining;
    }
}
