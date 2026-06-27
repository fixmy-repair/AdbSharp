using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AdbSharp.Common;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace AdbSharp.Adb.Internal;

internal sealed class AdbPairingBackend : IAdbPairingBackend
{
    private const int ExportedKeySize = 64;
    private const string ExportedKeyLabel = "adb-label\0";

    public static AdbPairingBackend Default { get; } = new();

    public async ValueTask<IAdbPairingSecureConnection> ConnectAsync(
        Stream transport,
        X509Certificate2 clientCertificate,
        ReadOnlyMemory<byte> pairingCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(clientCertificate);
        if (pairingCode.IsEmpty)
        {
            throw new ArgumentException("ADB wireless pairing code cannot be empty.", nameof(pairingCode));
        }

        var crypto = new BcTlsCrypto();
        var protocol = new TlsClientProtocol(transport);
        var client = new PairingTlsClient(crypto, clientCertificate);
        await RunBlockingAsync(
            static state => state.Protocol.Connect(state.Client),
            (Protocol: protocol, Client: client),
            cancellationToken).ConfigureAwait(false);

        var context = client.Context ?? throw new DeviceConnectionException("ADB wireless pairing TLS context was not initialized.");
        var tlsKeyMaterial = context.ExportKeyingMaterial(ExportedKeyLabel, null, ExportedKeySize);
        if (tlsKeyMaterial.Length != ExportedKeySize)
        {
            protocol.Close();
            throw new DeviceConnectionException("ADB wireless pairing TLS keying-material export failed.");
        }

        var password = new byte[pairingCode.Length + tlsKeyMaterial.Length];
        pairingCode.CopyTo(password);
        tlsKeyMaterial.CopyTo(password.AsSpan(pairingCode.Length));

        try
        {
            return new AdbPairingSecureConnection(protocol, AdbPairingAuth.CreateClient(password));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
            CryptographicOperations.ZeroMemory(tlsKeyMaterial);
        }
    }

    private static async ValueTask RunBlockingAsync<TState>(Action<TState> action, TState state, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() => action(state), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or TlsException or AuthenticationException or CryptographicException)
        {
            throw new DeviceConnectionException("ADB wireless pairing TLS handshake failed.", ex);
        }
    }

    private sealed class PairingTlsClient(BcTlsCrypto crypto, X509Certificate2 clientCertificate) : DefaultTlsClient(crypto)
    {
        public TlsClientContext? Context { get; private set; }

        public override void Init(TlsClientContext context)
        {
            base.Init(context);
            Context = context;
        }

        public override ProtocolVersion[] GetProtocolVersions()
        {
            return [ProtocolVersion.TLSv13];
        }

        public override TlsAuthentication GetAuthentication()
        {
            return new PairingTlsAuthentication(
                (BcTlsCrypto)Crypto,
                clientCertificate,
                () => Context ?? throw new InvalidOperationException("TLS client context is not initialized."));
        }
    }

    private sealed class PairingTlsAuthentication(
        BcTlsCrypto crypto,
        X509Certificate2 clientCertificate,
        Func<TlsClientContext> contextAccessor) : TlsAuthentication
    {
        public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
        {
            ArgumentNullException.ThrowIfNull(serverCertificate);
        }

        public TlsCredentials GetClientCredentials(Org.BouncyCastle.Tls.CertificateRequest certificateRequest)
        {
            ArgumentNullException.ThrowIfNull(certificateRequest);
            if (certificateRequest.CertificateTypes is { Length: > 0 }
                && !certificateRequest.CertificateTypes.Contains(ClientCertificateType.rsa_sign))
            {
                return null!;
            }

            var rsa = clientCertificate.GetRSAPrivateKey()
                ?? throw new DeviceConnectionException("ADB wireless pairing certificate does not contain an RSA private key.");
            using (rsa)
            {
                var keyPair = DotNetUtilities.GetRsaKeyPair(rsa);
                var certificate = new Certificate([new BcTlsCertificate(crypto, clientCertificate.RawData)]);
                var signatureAlgorithm = SelectSignatureAlgorithm(certificateRequest);
                return new BcDefaultTlsCredentialedSigner(
                    new TlsCryptoParameters(contextAccessor()),
                    crypto,
                    keyPair.Private,
                    certificate,
                    signatureAlgorithm);
            }
        }

        private static SignatureAndHashAlgorithm SelectSignatureAlgorithm(Org.BouncyCastle.Tls.CertificateRequest certificateRequest)
        {
            if (certificateRequest.SupportedSignatureAlgorithms is not null)
            {
                foreach (var algorithm in certificateRequest.SupportedSignatureAlgorithms)
                {
                    if (algorithm.Signature
                        is SignatureAlgorithm.rsa_pss_rsae_sha256
                        or SignatureAlgorithm.rsa_pss_rsae_sha384
                        or SignatureAlgorithm.rsa_pss_rsae_sha512
                        or SignatureAlgorithm.rsa)
                    {
                        return algorithm;
                    }
                }
            }

            return SignatureAndHashAlgorithm.rsa_pss_rsae_sha256;
        }
    }

    private sealed class AdbPairingSecureConnection(TlsClientProtocol protocol, AdbPairingAuth auth) : IAdbPairingSecureConnection
    {
        public ValueTask<byte[]> CreateClientMessageAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(auth.CreateMessage());
        }

        public ValueTask InitializeCipherAsync(ReadOnlyMemory<byte> peerMessage, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            auth.InitializeCipher(peerMessage.Span);
            return ValueTask.CompletedTask;
        }

        public ValueTask<byte[]> EncryptAsync(ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(auth.Encrypt(plaintext.Span));
        }

        public ValueTask<byte[]> DecryptAsync(ReadOnlyMemory<byte> ciphertext, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(auth.Decrypt(ciphertext.Span));
        }

        public async ValueTask ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (!buffer.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await Task.Run(() => protocol.ReadApplicationData(buffer.Span), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new EndOfStreamException("ADB wireless pairing TLS stream closed before the requested bytes were read.");
                }

                buffer = buffer[read..];
            }
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => protocol.WriteApplicationData(buffer.Span), cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            protocol.Close();
            return ValueTask.CompletedTask;
        }
    }
}
