using System.Security.Cryptography.X509Certificates;

namespace AdbSharp.Adb.Internal;

internal interface IAdbTransport : IAsyncDisposable
{
    bool SupportsTlsUpgrade { get; }

    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    ValueTask UpgradeToTlsClientAsync(X509Certificate2 clientCertificate, string targetHost, CancellationToken cancellationToken = default);
}
