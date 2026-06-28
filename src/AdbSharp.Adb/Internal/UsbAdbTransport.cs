using System.Buffers;
using System.Security.Cryptography.X509Certificates;
using AdbSharp.Protocol.Adb;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Adb.Internal;

internal sealed class UsbAdbTransport(IUsbTransport transport) : IAdbTransport
{
    private readonly SemaphoreSlim readGate = new(1, 1);
    private readonly bool usePacketSizeReadBuffering = ShouldUsePacketSizeReadBuffering(transport);
    private byte[]? pendingReadBuffer;
    private int pendingReadOffset;
    private int pendingReadLength;

    public bool SupportsTlsUpgrade => false;

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        await readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryDrainPendingRead(buffer, out var pendingBytesRead))
            {
                return pendingBytesRead;
            }

            if (!usePacketSizeReadBuffering)
            {
                return await transport.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }

            var transferLength = GetPacketAlignedReadLength(buffer.Length, transport.BulkInEndpoint.MaxPacketSize);
            var rented = ArrayPool<byte>.Shared.Rent(transferLength);
            try
            {
                var bytesRead = await transport.ReadAsync(rented.AsMemory(0, transferLength), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return 0;
                }

                return CopyReadResult(rented.AsSpan(0, bytesRead), buffer);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
        finally
        {
            readGate.Release();
        }
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

    public async ValueTask DisposeAsync()
    {
        ReturnPendingReadBuffer();
        readGate.Dispose();
        await transport.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask UpgradeToTlsClientAsync(X509Certificate2 clientCertificate, string targetHost, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("ADB TLS upgrade is only supported on stream transports.");
    }

    private static bool ShouldUsePacketSizeReadBuffering(IUsbTransport transport)
    {
        if (transport is not IUsbTransportDiagnostics diagnostics)
        {
            return true;
        }

        var snapshot = diagnostics.GetDiagnosticSnapshot();
        return !snapshot.Backend.StartsWith("IOUSB", StringComparison.Ordinal);
    }

    private static int GetPacketAlignedReadLength(int requestedLength, ushort packetSize)
    {
        var effectivePacketSize = Math.Max(1, (int)packetSize);
        var remainder = requestedLength % effectivePacketSize;
        return remainder == 0
            ? requestedLength
            : checked(requestedLength + effectivePacketSize - remainder);
    }

    private bool TryDrainPendingRead(Memory<byte> buffer, out int bytesRead)
    {
        bytesRead = 0;
        if (pendingReadBuffer is null || pendingReadLength == 0)
        {
            return false;
        }

        bytesRead = Math.Min(buffer.Length, pendingReadLength);
        pendingReadBuffer.AsMemory(pendingReadOffset, bytesRead).CopyTo(buffer);
        pendingReadOffset += bytesRead;
        pendingReadLength -= bytesRead;
        if (pendingReadLength == 0)
        {
            ReturnPendingReadBuffer();
        }

        return true;
    }

    private int CopyReadResult(ReadOnlySpan<byte> source, Memory<byte> destination)
    {
        var bytesRead = Math.Min(source.Length, destination.Length);
        source[..bytesRead].CopyTo(destination.Span);

        var extraLength = source.Length - bytesRead;
        if (extraLength > 0)
        {
            pendingReadBuffer = ArrayPool<byte>.Shared.Rent(extraLength);
            source[bytesRead..].CopyTo(pendingReadBuffer);
            pendingReadOffset = 0;
            pendingReadLength = extraLength;
        }

        return bytesRead;
    }

    private void ReturnPendingReadBuffer()
    {
        if (pendingReadBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(pendingReadBuffer);
            pendingReadBuffer = null;
        }

        pendingReadOffset = 0;
        pendingReadLength = 0;
    }
}
