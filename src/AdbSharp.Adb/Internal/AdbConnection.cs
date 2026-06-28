using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AdbSharp.Authentication.Adb;
using AdbSharp.Common;
using AdbSharp.Protocol.Adb;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Adb.Internal;

internal sealed class AdbConnection(IAdbTransport transport, AdbClientOptions options) : IAsyncDisposable
{
    private const int MaxHandshakeTransportRetries = 3;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly ConcurrentDictionary<uint, AdbStream> streams = new();
    private readonly CancellationTokenSource disposeCts = new();
    private readonly HashSet<string> features = [];
    private int nextLocalId;
    private Task? readerTask;
    private bool disposed;

    public AdbClientOptions Options { get; } = options;

    public uint DeviceVersion { get; private set; }

    public int MaxPayload { get; private set; } = AdbConstants.MaxPayloadV1;

    public bool TlsActive { get; private set; }

    public bool HasFeature(string feature)
    {
        return features.Contains(feature);
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        var connectPayload = Encoding.UTF8.GetBytes(Options.SystemIdentity);
        var signatureSent = false;
        var publicKeyOffered = false;
        using var handshakeReadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var nextPacketTask = StartReadPacketTask(handshakeReadCts.Token);
        try
        {
            await SendPacketAsync(AdbCommand.Connect, AdbConstants.Version, checked((uint)Options.MaxPayload), connectPayload, cancellationToken).ConfigureAwait(false);

            var handshakeTransportRetries = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AdbPacket packet;
                try
                {
                    packet = await nextPacketTask.ConfigureAwait(false);
                }
                catch (UsbTransportException ex) when (handshakeTransportRetries < MaxHandshakeTransportRetries && IsProtocolOperationRetryRequested(ex))
                {
                    handshakeTransportRetries++;
                    nextPacketTask = StartReadPacketTask(handshakeReadCts.Token);
                    await SendPacketAsync(AdbCommand.Connect, AdbConstants.Version, checked((uint)Options.MaxPayload), connectPayload, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                switch (packet.Header.Command)
                {
                    case AdbCommand.Connect:
                        DeviceVersion = packet.Header.Arg0;
                        MaxPayload = checked((int)Math.Min(packet.Header.Arg1, (uint)Options.MaxPayload));
                        ParseFeatures(packet.Payload.Span);
                        readerTask = Task.Run(() => ReaderLoopAsync(disposeCts.Token), CancellationToken.None);
                        return;

                    case AdbCommand.StartTls:
                        await UpgradeToTlsAsync(packet, cancellationToken).ConfigureAwait(false);
                        nextPacketTask = StartReadPacketTask(handshakeReadCts.Token);
                        break;

                    case AdbCommand.Auth when packet.Header.Arg0 == (uint)AdbAuthKind.Token:
                        if (TlsActive)
                        {
                            nextPacketTask = StartReadPacketTask(handshakeReadCts.Token);
                            break;
                        }

                        if (Options.Authenticator is null)
                        {
                            throw new DeviceConnectionException("ADB device requires authorization. Configure AdbClientOptions.Authenticator with an ADB key store and accept the host key on the device.");
                        }

                        if (publicKeyOffered)
                        {
                            throw new DeviceConnectionException("ADB device rejected the offered public key. Confirm the authorization prompt on the device or provide a trusted ADB key.");
                        }

                        if (!signatureSent)
                        {
                            var signature = await Options.Authenticator.SignTokenAsync(packet.Payload, cancellationToken).ConfigureAwait(false);
                            if (signature is not null)
                            {
                                nextPacketTask = StartReadPacketTask(handshakeReadCts.Token);
                                await SendPacketAsync(AdbCommand.Auth, (uint)AdbAuthKind.Signature, 0, signature, cancellationToken).ConfigureAwait(false);
                                signatureSent = true;
                                break;
                            }
                        }

                        var publicKey = await Options.Authenticator.GetPublicKeyAsync(cancellationToken).ConfigureAwait(false);
                        if (publicKey is null)
                        {
                            throw new DeviceConnectionException("ADB authenticator did not provide a public key.");
                        }

                        nextPacketTask = StartReadPacketTask(handshakeReadCts.Token);
                        await SendPacketAsync(AdbCommand.Auth, (uint)AdbAuthKind.PublicKey, 0, publicKey, cancellationToken).ConfigureAwait(false);
                        publicKeyOffered = true;
                        break;

                    default:
                        throw new ProtocolException($"Unexpected ADB packet '{packet.Header.Command}' during connection.");
                }
            }
        }
        catch
        {
            await handshakeReadCts.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask UpgradeToTlsAsync(AdbPacket packet, CancellationToken cancellationToken)
    {
        if (TlsActive)
        {
            throw new ProtocolException("ADB peer requested TLS after TLS was already active.");
        }

        if (!Options.EnableTls)
        {
            throw new DeviceConnectionException("ADB peer requested TLS, but TLS upgrades are disabled.");
        }

        if (packet.Header.Arg0 < AdbConstants.StartTlsVersionMin)
        {
            throw new ProtocolException($"ADB peer requested unsupported STLS version 0x{packet.Header.Arg0:x8}.");
        }

        if (!transport.SupportsTlsUpgrade)
        {
            throw new DeviceConnectionException("ADB peer requested TLS, but the current transport cannot be upgraded.");
        }

        var certificate = await GetTlsClientCertificateAsync(cancellationToken).ConfigureAwait(false);
        if (certificate is null)
        {
            throw new DeviceConnectionException("ADB peer requested TLS, but no TLS client certificate is configured. Use AdbRsaAuthenticator or AdbClientOptions.TlsCertificateProvider.");
        }

        await SendPacketAsync(AdbCommand.StartTls, AdbConstants.StartTlsVersion, 0, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        await transport.UpgradeToTlsClientAsync(certificate, Options.TlsTargetHost, cancellationToken).ConfigureAwait(false);
        TlsActive = true;
    }

    private ValueTask<X509Certificate2?> GetTlsClientCertificateAsync(CancellationToken cancellationToken)
    {
        if (Options.TlsCertificateProvider is not null)
        {
            return Options.TlsCertificateProvider(cancellationToken);
        }

        return Options.Authenticator is IAdbTlsAuthenticator tlsAuthenticator
            ? tlsAuthenticator.GetClientCertificateAsync(cancellationToken)
            : ValueTask.FromResult<X509Certificate2?>(null);
    }

    public async ValueTask<AdbStream> OpenStreamAsync(string service, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var localId = checked((uint)Interlocked.Increment(ref nextLocalId));
        var stream = new AdbStream(this, localId);
        if (!streams.TryAdd(localId, stream))
        {
            throw new ProtocolException($"Duplicate ADB local stream id {localId}.");
        }

        try
        {
            await SendPacketAsync(AdbCommand.Open, localId, 0, AdbServiceEncoding.EncodeOpenPayload(service), cancellationToken).ConfigureAwait(false);
            await stream.WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
            return stream;
        }
        catch
        {
            streams.TryRemove(localId, out _);
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask SendWriteAsync(uint localId, uint remoteId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.Length > MaxPayload)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), $"ADB write payload exceeds negotiated max payload {MaxPayload}.");
        }

        await SendPacketAsync(AdbCommand.Write, localId, remoteId, payload, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask SendCloseAsync(uint localId, uint remoteId, CancellationToken cancellationToken)
    {
        streams.TryRemove(localId, out _);
        return SendPacketAsync(AdbCommand.Close, localId, remoteId, ReadOnlyMemory<byte>.Empty, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await disposeCts.CancelAsync().ConfigureAwait(false);
        if (readerTask is not null)
        {
            try
            {
                await readerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        foreach (var stream in streams.Values)
        {
            stream.Abort(new ObjectDisposedException(nameof(AdbConnection)));
        }

        streams.Clear();
        writeGate.Dispose();
        disposeCts.Dispose();
        await transport.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ReaderLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var packet = await ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                await RoutePacketAsync(packet, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            foreach (var stream in streams.Values)
            {
                stream.Abort(ex);
            }
        }
    }

    private async ValueTask RoutePacketAsync(AdbPacket packet, CancellationToken cancellationToken)
    {
        var localId = packet.Header.Arg1;
        switch (packet.Header.Command)
        {
            case AdbCommand.Okay:
                if (streams.TryGetValue(localId, out var okayStream))
                {
                    okayStream.SetRemoteId(packet.Header.Arg0);
                    okayStream.NotifyOkay();
                }

                break;

            case AdbCommand.Write:
                if (streams.TryGetValue(localId, out var writeStream))
                {
                    writeStream.SetRemoteId(packet.Header.Arg0);
                    await SendPacketAsync(AdbCommand.Okay, localId, packet.Header.Arg0, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                    writeStream.Enqueue(packet.Payload);
                }

                break;

            case AdbCommand.Close:
                if (streams.TryRemove(localId, out var closeStream))
                {
                    closeStream.SetRemoteId(packet.Header.Arg0);
                    closeStream.Complete();
                }

                break;
        }
    }

    private async ValueTask SendPacketAsync(AdbCommand command, uint arg0, uint arg1, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var packet = AdbPacket.Create(command, arg0, arg1, payload, ShouldSkipChecksumForOutbound());
        var length = AdbPacketCodec.GetEncodedLength(packet);
        var rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var written = AdbPacketCodec.Write(packet, rented);
            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await transport.WriteAsync(rented.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writeGate.Release();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private async ValueTask<AdbPacket> ReadPacketAsync(CancellationToken cancellationToken)
    {
        var headerBytes = ArrayPool<byte>.Shared.Rent(AdbConstants.HeaderLength);
        AdbPacketHeader header;
        try
        {
            await ReadExactAsync(headerBytes.AsMemory(0, AdbConstants.HeaderLength), cancellationToken).ConfigureAwait(false);
            header = AdbPacketHeader.Read(headerBytes.AsSpan(0, AdbConstants.HeaderLength));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBytes);
        }

        var payload = header.PayloadLength == 0
            ? []
            : new byte[checked((int)header.PayloadLength)];
        if (payload.Length != 0)
        {
            await ReadExactAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        return AdbPacket.FromWire(header, payload, ShouldAllowSkippedChecksum(header));
    }

    private Task<AdbPacket> StartReadPacketTask(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(async () => await ReadPacketAsync(cancellationToken).ConfigureAwait(false), CancellationToken.None);
    }

    private static bool IsProtocolOperationRetryRequested(UsbTransportException exception)
    {
        return exception.Error == UsbTransportError.OperationAborted
            && exception.Message.Contains("retry the protocol operation", StringComparison.Ordinal);
    }

    private bool ShouldAllowSkippedChecksum(AdbPacketHeader header)
    {
        return header.PayloadChecksum == 0
            && (DeviceVersion >= AdbConstants.VersionSkipChecksum
                || header.Command == AdbCommand.Auth
                || (header.Command == AdbCommand.Connect && header.Arg0 >= AdbConstants.VersionSkipChecksum));
    }

    private bool ShouldSkipChecksumForOutbound()
    {
        return DeviceVersion >= AdbConstants.VersionSkipChecksum;
    }

    private async ValueTask ReadExactAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await transport.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new DeviceConnectionException("ADB transport closed while reading data.");
            }

            offset += read;
        }
    }

    private void ParseFeatures(ReadOnlySpan<byte> payload)
    {
        features.Clear();
        var banner = Encoding.UTF8.GetString(payload).TrimEnd('\0');
        var featuresIndex = banner.IndexOf("features=", StringComparison.Ordinal);
        if (featuresIndex < 0)
        {
            return;
        }

        var start = featuresIndex + "features=".Length;
        var end = banner.IndexOf(';', start, StringComparison.Ordinal);
        var featureList = end < 0 ? banner[start..] : banner[start..end];
        foreach (var feature in featureList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            features.Add(feature);
        }
    }
}
