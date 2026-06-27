using System.Security.Cryptography.X509Certificates;

namespace AdbSharp.Authentication.Adb;

/// <summary>
/// ADB authenticator backed by a single RSA key pair.
/// </summary>
/// <param name="keyPair">The RSA key pair.</param>
/// <param name="comment">The public key comment.</param>
public sealed class AdbRsaAuthenticator(AdbKeyPair keyPair, string comment = "AdbSharp") : IAdbTlsAuthenticator, IDisposable
{
    private readonly AdbKeyPair keyPair = keyPair ?? throw new ArgumentNullException(nameof(keyPair));
    private readonly string comment = comment;
    private X509Certificate2? clientCertificate;

    /// <inheritdoc />
    public ValueTask<byte[]?> SignTokenAsync(ReadOnlyMemory<byte> token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<byte[]?>(keyPair.SignToken(token.Span));
    }

    /// <inheritdoc />
    public ValueTask<byte[]?> GetPublicKeyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<byte[]?>(keyPair.ExportAdbPublicKey(comment));
    }

    /// <inheritdoc />
    public ValueTask<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        clientCertificate ??= keyPair.CreateSelfSignedCertificate();
        return ValueTask.FromResult<X509Certificate2?>(clientCertificate);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        clientCertificate?.Dispose();
        keyPair.Dispose();
    }
}
