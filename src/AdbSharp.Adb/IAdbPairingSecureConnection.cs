namespace AdbSharp.Adb;

/// <summary>
/// Represents an authenticated secure channel for the ADB wireless pairing packet exchange.
/// </summary>
public interface IAdbPairingSecureConnection : IAsyncDisposable
{
    /// <summary>
    /// Creates the local SPAKE2 pairing message.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The local SPAKE2 message.</returns>
    ValueTask<byte[]> CreateClientMessageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes peer-info encryption from the peer SPAKE2 message.
    /// </summary>
    /// <param name="peerMessage">The peer SPAKE2 message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask InitializeCipherAsync(ReadOnlyMemory<byte> peerMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encrypts a pairing peer-info payload.
    /// </summary>
    /// <param name="plaintext">The plaintext payload.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The encrypted payload.</returns>
    ValueTask<byte[]> EncryptAsync(ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts a pairing peer-info payload.
    /// </summary>
    /// <param name="ciphertext">The encrypted payload.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The decrypted payload.</returns>
    ValueTask<byte[]> DecryptAsync(ReadOnlyMemory<byte> ciphertext, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads exactly the requested number of bytes from the secure transport.
    /// </summary>
    /// <param name="buffer">The destination buffer.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes bytes to the secure transport.
    /// </summary>
    /// <param name="buffer">The source buffer.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
}
