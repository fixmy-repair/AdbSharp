namespace AdbSharp.Authentication.Adb;

/// <summary>
/// Supplies ADB authentication signatures and public keys.
/// </summary>
public interface IAdbAuthenticator
{
    /// <summary>
    /// Signs an ADB authentication token.
    /// </summary>
    /// <param name="token">The token sent by the device.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The signature bytes, or <see langword="null"/> when no key is available.</returns>
    ValueTask<byte[]?> SignTokenAsync(ReadOnlyMemory<byte> token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the public key payload to send to an Android device.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The null-terminated public key payload, or <see langword="null"/> when no key is available.</returns>
    ValueTask<byte[]?> GetPublicKeyAsync(CancellationToken cancellationToken = default);
}
