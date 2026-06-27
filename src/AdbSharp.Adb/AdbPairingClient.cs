using System.Net.Sockets;
using System.Text;
using AdbSharp.Adb.Internal;
using AdbSharp.Authentication.Adb;
using AdbSharp.Common;

namespace AdbSharp.Adb;

/// <summary>
/// Pairs an ADB host key with an Android wireless debugging pairing endpoint.
/// </summary>
public static class AdbPairingClient
{
    /// <summary>
    /// Pairs with an Android wireless debugging endpoint discovered through mDNS.
    /// </summary>
    /// <param name="service">The discovered pairing service.</param>
    /// <param name="pairingCode">The pairing code shown by Android wireless debugging.</param>
    /// <param name="keyPair">The ADB host key pair to authorize.</param>
    /// <param name="options">Pairing options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The completed pairing result.</returns>
    public static ValueTask<AdbPairingResult> PairAsync(
        AdbMdnsService service,
        string pairingCode,
        AdbKeyPair keyPair,
        AdbPairingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (service.Kind != AdbMdnsServiceKind.Pairing)
        {
            throw new ArgumentException("The mDNS service must be an ADB pairing service.", nameof(service));
        }

        return PairAsync(service.Host, service.Port, pairingCode, keyPair, options, cancellationToken);
    }

    /// <summary>
    /// Pairs with an Android wireless debugging endpoint.
    /// </summary>
    /// <param name="host">The pairing endpoint host name or IP address.</param>
    /// <param name="port">The pairing endpoint port shown by Android wireless debugging.</param>
    /// <param name="pairingCode">The pairing code shown by Android wireless debugging.</param>
    /// <param name="keyPair">The ADB host key pair to authorize.</param>
    /// <param name="options">Pairing options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The completed pairing result.</returns>
    public static async ValueTask<AdbPairingResult> PairAsync(
        string host,
        int port,
        string pairingCode,
        AdbKeyPair keyPair,
        AdbPairingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingCode);
        ArgumentNullException.ThrowIfNull(keyPair);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "TCP ports must be in the range 1 through 65535.");
        }

        options ??= new AdbPairingOptions();
        var backend = options.Backend ?? AdbPairingBackend.Default;

        var pairingCodeBytes = Encoding.UTF8.GetBytes(pairingCode);
        var localPeerInfo = new AdbPairingPeerInfo(
            AdbPairingPeerInfoKind.AdbRsaPublicKey,
            keyPair.ExportAdbPublicKey(options.PublicKeyComment));

        using var certificate = keyPair.CreateSelfSignedCertificate();
        using var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            throw new DeviceConnectionException($"Could not connect to ADB wireless pairing endpoint '{host}:{port}'.", ex);
        }

        await using var secureConnection = await backend.ConnectAsync(
            client.GetStream(),
            certificate,
            pairingCodeBytes,
            cancellationToken).ConfigureAwait(false);

        var peerInfo = await AdbPairingProtocol.ExchangeAsync(secureConnection, localPeerInfo, cancellationToken).ConfigureAwait(false);
        return new AdbPairingResult(host, port, peerInfo);
    }
}
