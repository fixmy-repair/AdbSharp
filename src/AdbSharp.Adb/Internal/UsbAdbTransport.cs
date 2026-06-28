using System.Security.Cryptography.X509Certificates;
using AdbSharp.Protocol.Adb;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Adb.Internal;

internal sealed class UsbAdbTransport(IUsbTransport transport) : IAdbTransport
{
    public bool SupportsTlsUpgrade => false;

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return transport.ReadAsync(buffer, cancellationToken);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length <= AdbConstants.HeaderLength)
        {
            await transport.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            return;
        }

        await transport.WriteAsync(buffer[..AdbConstants.HeaderLength], cancellationToken).ConfigureAwait(false);
        await transport.WriteAsync(buffer[AdbConstants.HeaderLength..], cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return transport.DisposeAsync();
    }

    public ValueTask UpgradeToTlsClientAsync(X509Certificate2 clientCertificate, string targetHost, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("ADB TLS upgrade is only supported on stream transports.");
    }
}
