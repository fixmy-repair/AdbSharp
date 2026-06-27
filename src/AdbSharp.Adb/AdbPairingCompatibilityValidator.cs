using System.Diagnostics;
using System.Text;
using AdbSharp.Authentication.Adb;

namespace AdbSharp.Adb;

/// <summary>
/// Runs an Android 11+ wireless debugging pairing compatibility check against a real device endpoint.
/// </summary>
public static class AdbPairingCompatibilityValidator
{
    /// <summary>
    /// Pairs with a wireless debugging endpoint and optionally verifies the resulting ADB TCP endpoint.
    /// </summary>
    /// <param name="endpoint">The hardware endpoint to validate.</param>
    /// <param name="keyPair">The ADB host key used for pairing and optional ADB authorization.</param>
    /// <param name="options">Compatibility validation options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A sanitized compatibility validation result.</returns>
    public static async ValueTask<AdbPairingCompatibilityResult> ValidateAsync(
        AdbPairingCompatibilityEndpoint endpoint,
        AdbKeyPair keyPair,
        AdbPairingCompatibilityOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(keyPair);
        endpoint.Validate();
        options ??= new AdbPairingCompatibilityOptions();
        ValidatePort(options.DefaultAdbPort, nameof(options.DefaultAdbPort));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var pairingOptions = options.PairingOptions ?? new AdbPairingOptions();
            var pairingResult = await AdbPairingClient.PairAsync(
                endpoint.Host,
                endpoint.PairingPort,
                endpoint.PairingCode,
                keyPair,
                pairingOptions,
                cancellationToken).ConfigureAwait(false);

            var adbPort = endpoint.AdbPort ?? options.DefaultAdbPort;
            var adbConnectionTested = options.VerifyAdbConnection;
            var adbConnectionSucceeded = false;
            string? manufacturer = null;
            string? model = null;
            bool? manufacturerMatched = null;
            bool? modelMatched = null;

            if (adbConnectionTested)
            {
                try
                {
                    (adbConnectionSucceeded, manufacturer, model) = await VerifyAdbConnectionAsync(
                        endpoint,
                        keyPair,
                        pairingOptions.PublicKeyComment,
                        adbPort,
                        options.AdbOptions,
                        cancellationToken).ConfigureAwait(false);

                    manufacturerMatched = MatchesExpected(endpoint.ExpectedManufacturer, manufacturer);
                    modelMatched = MatchesExpected(endpoint.ExpectedModel, model);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    return new AdbPairingCompatibilityResult(
                        endpoint.Vendor,
                        endpoint.Model,
                        endpoint.Host,
                        endpoint.PairingPort,
                        true,
                        adbConnectionTested,
                        false,
                        stopwatch.Elapsed)
                    {
                        AdbPort = adbPort,
                        PeerInfoKind = pairingResult.PeerInfo.Kind,
                        DeviceGuid = pairingResult.PeerInfo.Kind == AdbPairingPeerInfoKind.DeviceGuid
                            ? Encoding.UTF8.GetString(pairingResult.PeerInfo.Data.Span)
                            : null,
                        ErrorType = ex.GetType().Name,
                        ErrorMessage = ex.Message
                    };
                }
            }

            stopwatch.Stop();
            return new AdbPairingCompatibilityResult(
                endpoint.Vendor,
                endpoint.Model,
                endpoint.Host,
                endpoint.PairingPort,
                true,
                adbConnectionTested,
                adbConnectionSucceeded,
                stopwatch.Elapsed)
            {
                AdbPort = adbConnectionTested ? adbPort : null,
                PeerInfoKind = pairingResult.PeerInfo.Kind,
                DeviceGuid = pairingResult.PeerInfo.Kind == AdbPairingPeerInfoKind.DeviceGuid
                    ? Encoding.UTF8.GetString(pairingResult.PeerInfo.Data.Span)
                    : null,
                ProductManufacturer = manufacturer,
                ProductModel = model,
                ExpectedManufacturerMatched = manufacturerMatched,
                ExpectedModelMatched = modelMatched
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new AdbPairingCompatibilityResult(
                endpoint.Vendor,
                endpoint.Model,
                endpoint.Host,
                endpoint.PairingPort,
                false,
                false,
                false,
                stopwatch.Elapsed)
            {
                AdbPort = endpoint.AdbPort,
                ErrorType = ex.GetType().Name,
                ErrorMessage = ex.Message
            };
        }
    }

    private static async ValueTask<(bool Succeeded, string? Manufacturer, string? Model)> VerifyAdbConnectionAsync(
        AdbPairingCompatibilityEndpoint endpoint,
        AdbKeyPair keyPair,
        string publicKeyComment,
        int adbPort,
        AdbClientOptions? options,
        CancellationToken cancellationToken)
    {
        using var authenticator = CreateAuthenticator(keyPair, publicKeyComment);
        var adbOptions = WithAuthenticator(options, authenticator);
        await using var client = await AdbClient.ConnectWirelessAsync(endpoint.Host, adbPort, adbOptions, cancellationToken).ConfigureAwait(false);
        var manufacturer = await client.GetPropertyAsync("ro.product.manufacturer", cancellationToken).ConfigureAwait(false);
        var model = await client.GetPropertyAsync("ro.product.model", cancellationToken).ConfigureAwait(false);
        return (true, manufacturer, model);
    }

    private static AdbRsaAuthenticator CreateAuthenticator(AdbKeyPair keyPair, string publicKeyComment)
    {
        var keyClone = AdbKeyPair.ImportPrivateKeyPem(keyPair.ExportPrivateKeyPem());
        return new AdbRsaAuthenticator(keyClone, publicKeyComment);
    }

    private static AdbClientOptions WithAuthenticator(AdbClientOptions? options, AdbRsaAuthenticator authenticator)
    {
        if (options is null)
        {
            return new AdbClientOptions { Authenticator = authenticator };
        }

        return new AdbClientOptions
        {
            Authenticator = options.Authenticator ?? authenticator,
            EnableTls = options.EnableTls,
            MaxPayload = options.MaxPayload,
            SystemIdentity = options.SystemIdentity,
            TlsCertificateProvider = options.TlsCertificateProvider,
            TlsTargetHost = options.TlsTargetHost,
            TransportFactory = options.TransportFactory
        };
    }

    private static bool? MatchesExpected(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return null;
        }

        return string.Equals(expected.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidatePort(int port, string parameterName)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(parameterName, "TCP ports must be in the range 1 through 65535.");
        }
    }
}
