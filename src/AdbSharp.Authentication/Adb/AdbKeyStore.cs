namespace AdbSharp.Authentication.Adb;

/// <summary>
/// Loads and creates local ADB host keys.
/// </summary>
public static class AdbKeyStore
{
    /// <summary>
    /// Loads an existing key or creates a new key at the specified path.
    /// </summary>
    /// <param name="privateKeyPath">The private key PEM path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The loaded key pair.</returns>
    public static async ValueTask<AdbKeyPair> LoadOrCreateAsync(string privateKeyPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPath);

        if (File.Exists(privateKeyPath))
        {
            var pem = await File.ReadAllTextAsync(privateKeyPath, cancellationToken).ConfigureAwait(false);
            return AdbKeyPair.ImportPrivateKeyPem(pem);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(privateKeyPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var keyPair = AdbKeyPair.Create();
        await File.WriteAllTextAsync(privateKeyPath, keyPair.ExportPrivateKeyPem(), cancellationToken).ConfigureAwait(false);
        return keyPair;
    }
}
