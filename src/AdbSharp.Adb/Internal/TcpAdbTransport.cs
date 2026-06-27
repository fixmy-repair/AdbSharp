using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using AdbSharp.Common;

namespace AdbSharp.Adb.Internal;

internal sealed class TcpAdbTransport : IAdbTransport
{
    private readonly TcpClient client;
    private readonly SemaphoreSlim readGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private Stream stream;
    private bool disposed;

    private TcpAdbTransport(TcpClient client)
    {
        this.client = client;
        stream = client.GetStream();
    }

    public bool SupportsTlsUpgrade => true;

    public static async ValueTask<TcpAdbTransport> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            return new TcpAdbTransport(client);
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            client.Dispose();
            throw new DeviceConnectionException($"Could not connect to wireless ADB endpoint '{host}:{port}'.", ex);
        }
    }

    public async ValueTask UpgradeToTlsClientAsync(X509Certificate2 clientCertificate, string targetHost, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(clientCertificate);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetHost);

        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        await readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var certificates = new X509CertificateCollection { clientCertificate };
            var tls = new SslStream(
                stream,
                false,
                ValidateAdbServerCertificate);

            var options = new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                ClientCertificates = certificates,
                EnabledSslProtocols = SslProtocols.None,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            };

            try
            {
                await tls.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
                stream = tls;
            }
            catch (Exception ex) when (ex is IOException or AuthenticationException)
            {
                tls.Dispose();
                throw new DeviceConnectionException("ADB TLS handshake failed.", ex);
            }
        }
        catch (Exception ex) when (ex is IOException or AuthenticationException)
        {
            throw new DeviceConnectionException("ADB TLS handshake failed.", ex);
        }
        finally
        {
            readGate.Release();
            writeGate.Release();
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            readGate.Release();
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        disposed = true;
        stream.Dispose();
        client.Dispose();
        readGate.Dispose();
        writeGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private static bool ValidateAdbServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        // ADB peers use self-signed transport certificates tied to the ADB trust model, not public PKI.
        _ = sender;
        _ = chain;
        _ = sslPolicyErrors;
        return certificate is not null;
    }
}
