using System.Security.Cryptography.X509Certificates;

namespace AdbSharp.Adb;

/// <summary>
/// Creates secure ADB pairing connections.
/// </summary>
/// <remarks>
/// Implementations are responsible for the ADB pairing TLS handshake, TLS keying-material export,
/// SPAKE2 message generation, and AES-GCM peer-info encryption.
/// </remarks>
public interface IAdbPairingBackend
{
    /// <summary>
    /// Opens a secure pairing connection over an already-connected transport stream.
    /// </summary>
    /// <param name="transport">The connected TCP transport stream.</param>
    /// <param name="clientCertificate">The client certificate derived from the ADB host key.</param>
    /// <param name="pairingCode">The pairing code entered by the user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The secure pairing connection.</returns>
    ValueTask<IAdbPairingSecureConnection> ConnectAsync(
        Stream transport,
        X509Certificate2 clientCertificate,
        ReadOnlyMemory<byte> pairingCode,
        CancellationToken cancellationToken = default);
}
