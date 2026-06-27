using System.Security.Cryptography.X509Certificates;

namespace AdbSharp.Authentication.Adb;

/// <summary>
/// Supplies client certificates for ADB TLS transports.
/// </summary>
public interface IAdbTlsAuthenticator : IAdbAuthenticator
{
    /// <summary>
    /// Gets the client certificate used for ADB TLS handshakes.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The client certificate, or <see langword="null"/> when no TLS credential is available.</returns>
    ValueTask<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken = default);
}
