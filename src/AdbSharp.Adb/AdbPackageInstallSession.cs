using System.Buffers;
using System.Text;
using AdbSharp.Adb.Internal;

namespace AdbSharp.Adb;

/// <summary>
/// Represents an active Android package install session.
/// </summary>
public sealed class AdbPackageInstallSession : IAsyncDisposable
{
    private readonly AdbClient client;
    private readonly AdbInstallOptions options;
    private bool completed;

    internal AdbPackageInstallSession(AdbClient client, int sessionId, AdbInstallOptions options)
    {
        this.client = client;
        SessionId = sessionId;
        this.options = options;
    }

    /// <summary>
    /// Gets the Android package manager session id.
    /// </summary>
    public int SessionId { get; }

    /// <summary>
    /// Gets a value indicating whether this is a staged install session.
    /// </summary>
    public bool IsStaged => options.Staged;

    /// <summary>
    /// Writes an APK or split APK into the install session.
    /// </summary>
    /// <param name="packageFile">The package file to write.</param>
    /// <param name="progress">Optional cumulative byte progress for this package file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The package manager output.</returns>
    public ValueTask<string> WriteAsync(AdbPackageFile packageFile, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageFile);
        return WriteAsync(packageFile.Contents, packageFile.SplitName, packageFile.Size, progress, cancellationToken);
    }

    /// <summary>
    /// Writes an APK or split APK stream into the install session.
    /// </summary>
    /// <param name="contents">The APK stream.</param>
    /// <param name="splitName">The split name used by package manager.</param>
    /// <param name="size">The number of bytes to write.</param>
    /// <param name="progress">Optional cumulative byte progress for this package file.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The package manager output.</returns>
    public async ValueTask<string> WriteAsync(
        Stream contents,
        string splitName,
        long size,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(completed, this);
        ArgumentNullException.ThrowIfNull(contents);
        var command = AdbInstallCommandBuilder.WriteSplit(SessionId, splitName, size);
        await using var stream = await client.OpenStreamAsync($"exec:{command}", cancellationToken).ConfigureAwait(false);
        await CopyExactlyAsync(contents, stream, size, progress, cancellationToken).ConfigureAwait(false);
        var output = Encoding.UTF8.GetString(await stream.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
        return AdbPackageManagerOutput.EnsureSuccess(output);
    }

    /// <summary>
    /// Commits the install session.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The package manager output.</returns>
    public async ValueTask<string> CommitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(completed, this);
        var output = await client.ShellAsync(AdbInstallCommandBuilder.Commit(SessionId, options.StagedReadyTimeout), cancellationToken).ConfigureAwait(false);
        completed = true;
        return AdbPackageManagerOutput.EnsureSuccess(output);
    }

    /// <summary>
    /// Abandons the install session.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The package manager output.</returns>
    public async ValueTask<string> AbandonAsync(CancellationToken cancellationToken = default)
    {
        if (completed)
        {
            return string.Empty;
        }

        var output = await client.ShellAsync(AdbInstallCommandBuilder.Abandon(SessionId), cancellationToken).ConfigureAwait(false);
        completed = true;
        return AdbPackageManagerOutput.EnsureSuccess(output);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (completed)
        {
            return;
        }

        try
        {
            await AbandonAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (AdbPackageManagerException)
        {
        }
    }

    private static async ValueTask CopyExactlyAsync(
        Stream source,
        AdbStream destination,
        long size,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Package file size must be positive.");
        }

        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        var remaining = size;
        var written = 0L;
        try
        {
            while (remaining > 0)
            {
                var readLimit = checked((int)Math.Min(buffer.Length, remaining));
                var read = await source.ReadAsync(buffer.AsMemory(0, readLimit), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException($"Package stream ended with {remaining} bytes left to write.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read;
                written += read;
                progress?.Report(written);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
