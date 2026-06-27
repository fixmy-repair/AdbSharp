using System.Security.Cryptography.X509Certificates;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Adb.Internal;

internal sealed class UsbAdbTransport(IUsbTransport transport) : IAdbTransport
{
    public bool SupportsTlsUpgrade => false;

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return transport.ReadAsync(buffer, cancellationToken);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return transport.WriteAsync(buffer, cancellationToken);
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
