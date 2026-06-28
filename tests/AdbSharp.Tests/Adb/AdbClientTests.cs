using System.Text;
using System.Buffers.Binary;
using System.IO.Compression;
using AdbSharp.Adb;
using AdbSharp.Authentication.Adb;
using AdbSharp.Common;
using AdbSharp.Common.Devices;
using AdbSharp.Protocol.Adb;
using AdbSharp.Tests.Transport;
using AdbSharp.Transport.Usb;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using ZstdCompressionStream = ZstdSharp.CompressionStream;
using ZstdDecompressionStream = ZstdSharp.DecompressionStream;

namespace AdbSharp.Tests.Adb;

public sealed class AdbClientTests
{
    [Fact]
    public async Task ShellAsync_routes_stream_output()
    {
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = new ScriptedUsbTransport(descriptor);
        transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Connect, AdbConstants.Version, AdbConstants.MaxPayload, Encoding.UTF8.GetBytes("device::features=shell_v2"))));
        transport.OnWrite = bytes =>
        {
            var packet = AdbPacketCodec.Read(bytes, allowZeroChecksum: true);
            if (packet.Header.Command != AdbCommand.Open)
            {
                return;
            }

            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Okay, 7, packet.Header.Arg0, ReadOnlyMemory<byte>.Empty)));
            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Write, 7, packet.Header.Arg0, Encoding.UTF8.GetBytes("hello\n"))));
            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Close, 7, packet.Header.Arg0, ReadOnlyMemory<byte>.Empty)));
        };

        var device = new AndroidDevice(
            new DeviceIdentity("serial", "Google", "Pixel", "Pixel", descriptor.TransportId),
            DeviceMode.Adb,
            DeviceCapabilities.Empty with { SupportsAdb = true },
            descriptor);

        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });
        var output = await client.ShellAsync("echo hello");

        Assert.Equal("hello\n", output);
        Assert.Contains(transport.Writes, write => Encoding.UTF8.GetString(write).Contains("shell:echo hello", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShellV2Async_returns_stdout_stderr_and_exit_code()
    {
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = CreateConnectedTransport(descriptor);
        transport.OnWrite = bytes =>
        {
            var packet = AdbPacketCodec.Read(bytes, allowZeroChecksum: true);
            if (packet.Header.Command != AdbCommand.Open)
            {
                return;
            }

            var payload = GetService(packet);
            if (!payload.StartsWith("shell,v2,raw:", StringComparison.Ordinal))
            {
                return;
            }

            var frames = Concat(
                ShellV2Frame(1, "out\n"u8.ToArray()),
                ShellV2Frame(2, "err\n"u8.ToArray()),
                ShellV2Frame(3, [42]));

            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Okay, 9, packet.Header.Arg0, ReadOnlyMemory<byte>.Empty)));
            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Write, 9, packet.Header.Arg0, frames)));
            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Close, 9, packet.Header.Arg0, ReadOnlyMemory<byte>.Empty)));
        };

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });

        var result = await client.ShellV2Async("test");

        Assert.Equal(42, result.ExitCode);
        Assert.Equal("out\n", result.StandardOutput);
        Assert.Equal("err\n", result.StandardError);
        Assert.Contains(transport.Writes, write => GetPacketService(write).Contains("shell,v2,raw:test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StreamWriteAsync_waits_for_okay_before_next_write()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = CreateConnectedTransport(descriptor);
        const uint remoteId = 23;
        uint localId = 0;
        var writePayloads = new List<string>();
        transport.OnWrite = bytes =>
        {
            var packet = AdbPacketCodec.Read(bytes, allowZeroChecksum: true);
            switch (packet.Header.Command)
            {
                case AdbCommand.Open when GetService(packet) == "shell:cat":
                    localId = packet.Header.Arg0;
                    transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Okay, remoteId, localId, ReadOnlyMemory<byte>.Empty)));
                    break;

                case AdbCommand.Write when packet.Header.Arg0 == localId:
                    writePayloads.Add(Encoding.UTF8.GetString(packet.Payload.Span));
                    break;
            }
        };

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport }, cancellation.Token);
        await using var stream = await client.OpenStreamAsync("shell:cat", cancellation.Token);

        await stream.WriteAsync("one"u8.ToArray(), cancellation.Token);
        var secondWrite = stream.WriteAsync("two"u8.ToArray(), cancellation.Token).AsTask();
        var completed = await Task.WhenAny(secondWrite, Task.Delay(TimeSpan.FromMilliseconds(100), cancellation.Token));

        Assert.NotSame(secondWrite, completed);
        Assert.Equal(["one"], writePayloads);

        transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Okay, remoteId, localId, ReadOnlyMemory<byte>.Empty)));
        await secondWrite.WaitAsync(cancellation.Token);

        Assert.Equal(["one", "two"], writePayloads);
    }

    [Fact]
    public async Task LogcatAsync_streams_lines()
    {
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = CreateConnectedTransport(descriptor);
        transport.OnWrite = bytes =>
        {
            var packet = AdbPacketCodec.Read(bytes, allowZeroChecksum: true);
            if (packet.Header.Command != AdbCommand.Open || GetService(packet) != "shell:logcat -d")
            {
                return;
            }

            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Okay, 11, packet.Header.Arg0, ReadOnlyMemory<byte>.Empty)));
            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Write, 11, packet.Header.Arg0, Encoding.UTF8.GetBytes("one\r\ntwo\nthree"))));
            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Close, 11, packet.Header.Arg0, ReadOnlyMemory<byte>.Empty)));
        };

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });

        var lines = new List<string>();
        await foreach (var line in client.LogcatAsync("-d"))
        {
            lines.Add(line);
        }

        Assert.Equal(["one", "two", "three"], lines);
    }

    [Fact]
    public async Task ReverseForwardAsync_sends_reverse_service_command()
    {
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = CreateConnectedTransport(descriptor);
        transport.OnWrite = bytes =>
        {
            var packet = AdbPacketCodec.Read(bytes, allowZeroChecksum: true);
            if (packet.Header.Command != AdbCommand.Open)
            {
                return;
            }

            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Okay, 13, packet.Header.Arg0, ReadOnlyMemory<byte>.Empty)));
            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Write, 13, packet.Header.Arg0, Encoding.UTF8.GetBytes("OKAY"))));
            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Close, 13, packet.Header.Arg0, ReadOnlyMemory<byte>.Empty)));
        };

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });

        var response = await client.ReverseForwardAsync(AdbSocketSpec.Tcp(7000), AdbSocketSpec.Tcp(8000), noRebind: true);

        Assert.Empty(response);
        Assert.Contains(transport.Writes, write => GetPacketService(write).Contains("reverse:forward:norebind:tcp:7000;tcp:8000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListPackagesAsync_parses_package_output()
    {
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = CreateConnectedTransport(descriptor);
        transport.OnWrite = bytes =>
        {
            var packet = AdbPacketCodec.Read(bytes, allowZeroChecksum: true);
            if (packet.Header.Command != AdbCommand.Open || GetService(packet) != "shell:pm list packages com.example")
            {
                return;
            }

            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Okay, 17, packet.Header.Arg0, ReadOnlyMemory<byte>.Empty)));
            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Write, 17, packet.Header.Arg0, Encoding.UTF8.GetBytes("package:com.example.one\npackage:com.example.two\n"))));
            transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Close, 17, packet.Header.Arg0, ReadOnlyMemory<byte>.Empty)));
        };

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });

        var packages = await client.ListPackagesAsync("com.example");

        Assert.Equal(["com.example.one", "com.example.two"], packages);
    }

    [Fact]
    public async Task LstatAsync_uses_stat_v2_when_supported()
    {
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = CreateConnectedTransport(descriptor, "device::features=stat_v2");
        ConfigureSyncService(transport, request =>
        {
            Assert.Equal("LST2", ReadSyncId(request));
            Assert.Equal("/data/local/tmp/file.txt", ReadSyncPath(request));
            return StatV2("LST2", 0x81a4, 123, 10, 20, 30);
        });

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });

        var stat = await client.LstatAsync("/data/local/tmp/file.txt");

        Assert.NotNull(stat);
        Assert.Equal(0x81a4u, stat.Mode);
        Assert.Equal(123, stat.Size);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(20), stat.ModifiedTime);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(10), stat.AccessTime);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(30), stat.ChangeTime);
    }

    [Fact]
    public async Task StatAsync_legacy_returns_null_for_missing_path()
    {
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = CreateConnectedTransport(descriptor);
        ConfigureSyncService(transport, request =>
        {
            Assert.Equal("STAT", ReadSyncId(request));
            Assert.Equal("/missing", ReadSyncPath(request));
            return StatV1(mode: 0, size: 0, modifiedTime: 0);
        });

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });

        var stat = await client.StatAsync("/missing");

        Assert.Null(stat);
    }

    [Fact]
    public async Task ListDirectoryAsync_uses_list_v2_when_supported()
    {
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = CreateConnectedTransport(descriptor, "device::features=ls_v2");
        ConfigureSyncService(transport, request =>
        {
            Assert.Equal("LIS2", ReadSyncId(request));
            Assert.Equal("/sdcard", ReadSyncPath(request));
            return Concat(DentV2("Download", 0x41ed, 4096, 100, 200, 300), DoneV2());
        });

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });

        var entries = await client.ListDirectoryAsync("/sdcard");

        var entry = Assert.Single(entries);
        Assert.Equal("Download", entry.Name);
        Assert.Equal("/sdcard/Download", entry.Statistics.Path);
        Assert.True(entry.Statistics.IsDirectory);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(200), entry.Statistics.ModifiedTime);
    }

    [Fact]
    public async Task PushAsync_uses_send_v2_when_supported()
    {
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = CreateConnectedTransport(descriptor, "device::features=sendrecv_v2");
        var progressValues = new List<long>();
        var phase = 0;
        ConfigureSyncService(transport, request =>
        {
            if (phase == 0)
            {
                phase++;
                Assert.Equal("SND2", ReadSyncId(request));
                Assert.Equal("/data/local/tmp/a.txt", ReadSyncPath(request));
                var setupOffset = 8 + Encoding.UTF8.GetByteCount("/data/local/tmp/a.txt");
                Assert.Equal("SND2", Encoding.ASCII.GetString(request.AsSpan(setupOffset, 4)));
                Assert.Equal(0x81a4u, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(setupOffset + 4, 4)));
                Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(setupOffset + 8, 4)));
                return null;
            }

            if (phase == 1)
            {
                phase++;
                Assert.Equal("DATA", ReadSyncId(request));
                Assert.Equal("abc", Encoding.UTF8.GetString(request.AsSpan(8)));
                return null;
            }

            phase++;
            Assert.Equal("DONE", ReadSyncId(request));
            return SyncStatus("OKAY", string.Empty);
        });

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });
        await using var source = new MemoryStream("abc"u8.ToArray());

        await client.PushAsync(source, "/data/local/tmp/a.txt", progress: new RecordingProgress(progressValues));

        Assert.Equal(3, phase);
        Assert.Equal([3L], progressValues);
    }

    [Theory]
    [InlineData("sendrecv_v2_brotli", 1u, "brotli")]
    [InlineData("sendrecv_v2_lz4", 2u, "lz4")]
    [InlineData("sendrecv_v2_zstd", 4u, "zstd")]
    public async Task PushAsync_compresses_send_v2_when_codec_is_supported(string feature, uint expectedFlag, string codec)
    {
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = CreateConnectedTransport(descriptor, $"device::features=sendrecv_v2,{feature}");
        var original = Enumerable.Range(0, 8192).Select(static value => (byte)(value % 251)).ToArray();
        await using var compressed = new MemoryStream();
        var progressValues = new List<long>();
        var sawData = false;
        ConfigureSyncService(transport, request =>
        {
            switch (ReadSyncId(request))
            {
                case "SND2":
                    Assert.Equal("/data/local/tmp/compressed.bin", ReadSyncPath(request));
                    var setupOffset = 8 + Encoding.UTF8.GetByteCount("/data/local/tmp/compressed.bin");
                    Assert.Equal(setupOffset + 12, request.Length);
                    Assert.Equal("SND2", Encoding.ASCII.GetString(request.AsSpan(setupOffset, 4)));
                    Assert.Equal(0x81a4u, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(setupOffset + 4, 4)));
                    Assert.Equal(expectedFlag, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(setupOffset + 8, 4)));
                    return null;

                case "DATA":
                    sawData = true;
                    compressed.Write(ReadSyncPayload(request));
                    return null;

                case "DONE":
                    Assert.True(sawData);
                    Assert.Equal(original, DecompressForTest(codec, compressed.ToArray()));
                    return SyncStatus("OKAY", string.Empty);

                default:
                    throw new ProtocolException($"Unexpected sync request '{ReadSyncId(request)}'.");
            }
        });

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });
        await using var source = new MemoryStream(original);

        await client.PushAsync(source, "/data/local/tmp/compressed.bin", progress: new RecordingProgress(progressValues));

        Assert.Equal([original.Length], progressValues);
    }

    [Theory]
    [InlineData("sendrecv_v2_brotli", 1u, "brotli")]
    [InlineData("sendrecv_v2_lz4", 2u, "lz4")]
    [InlineData("sendrecv_v2_zstd", 4u, "zstd")]
    public async Task PullAsync_decompresses_recv_v2_when_codec_is_supported(string feature, uint expectedFlag, string codec)
    {
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = CreateConnectedTransport(descriptor, $"device::features=sendrecv_v2,{feature}");
        var original = Enumerable.Range(0, 5000).Select(static value => (byte)(255 - (value % 199))).ToArray();
        var progressValues = new List<long>();
        ConfigureSyncService(transport, request =>
        {
            Assert.Equal("RCV2", ReadSyncId(request));
            Assert.Equal("/data/local/tmp/compressed.bin", ReadSyncPath(request));
            var setupOffset = 8 + Encoding.UTF8.GetByteCount("/data/local/tmp/compressed.bin");
            Assert.Equal(setupOffset + 8, request.Length);
            Assert.Equal("RCV2", Encoding.ASCII.GetString(request.AsSpan(setupOffset, 4)));
            Assert.Equal(expectedFlag, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(setupOffset + 4, 4)));
            return Concat(SyncData(CompressForTest(codec, original)), SyncDone());
        });

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });
        await using var destination = new MemoryStream();

        await client.PullAsync("/data/local/tmp/compressed.bin", destination, new RecordingProgress(progressValues));

        Assert.Equal(original, destination.ToArray());
        Assert.Equal([original.Length], progressValues);
    }

    [Fact]
    public async Task PullAsync_throws_sync_exception_on_fail()
    {
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = CreateConnectedTransport(descriptor);
        ConfigureSyncService(transport, request =>
        {
            Assert.Equal("RECV", ReadSyncId(request));
            Assert.Equal("/missing", ReadSyncPath(request));
            return SyncStatus("FAIL", "No such file or directory");
        });

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });
        await using var destination = new MemoryStream();

        var exception = await Assert.ThrowsAsync<AdbSyncException>(() => client.PullAsync("/missing", destination).AsTask());

        Assert.Equal("/missing", exception.RemotePath);
        Assert.Contains("No such file", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectAsync_reports_rejected_authorization_key()
    {
        var descriptor = CreateDescriptor(AndroidUsbClass.AdbProtocol);
        var transport = new ScriptedUsbTransport(descriptor);
        transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Auth, (uint)AdbAuthKind.Token, 0, "token1"u8.ToArray())));
        transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Auth, (uint)AdbAuthKind.Token, 0, "token2"u8.ToArray())));

        var device = CreateDevice(descriptor);
        var exception = await Assert.ThrowsAsync<DeviceConnectionException>(() =>
            AdbClient.ConnectAsync(device, new AdbClientOptions
            {
                TransportFactory = transport,
                Authenticator = new RejectingAuthenticator()
            }).AsTask());

        Assert.Contains("rejected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static UsbDeviceDescriptor CreateDescriptor(byte protocol)
    {
        return new UsbDeviceDescriptor("mock-1", 0x18d1, 0x4ee7, 0, AndroidUsbClass.VendorSpecificClass, AndroidUsbClass.AndroidSubClass, protocol, "serial", "Google", "Pixel");
    }

    private static AndroidDevice CreateDevice(UsbDeviceDescriptor descriptor)
    {
        return new AndroidDevice(
            new DeviceIdentity("serial", "Google", "Pixel", "Pixel", descriptor.TransportId),
            DeviceMode.Adb,
            DeviceCapabilities.Empty with { SupportsAdb = true },
            descriptor);
    }

    private static ScriptedUsbTransport CreateConnectedTransport(UsbDeviceDescriptor descriptor, string banner = "device::features=shell_v2")
    {
        var transport = new ScriptedUsbTransport(descriptor);
        transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Connect, AdbConstants.Version, AdbConstants.MaxPayload, Encoding.UTF8.GetBytes(banner))));
        return transport;
    }

    private static void ConfigureSyncService(ScriptedUsbTransport transport, Func<byte[], byte[]?> respond)
    {
        const uint remoteId = 41;
        uint localId = 0;
        transport.OnWrite = bytes =>
        {
            var packet = AdbPacketCodec.Read(bytes, allowZeroChecksum: true);
            switch (packet.Header.Command)
            {
                case AdbCommand.Open when GetService(packet) == "sync:":
                    localId = packet.Header.Arg0;
                    transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Okay, remoteId, localId, ReadOnlyMemory<byte>.Empty)));
                    break;

                case AdbCommand.Write when packet.Header.Arg0 == localId:
                    transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Okay, remoteId, localId, ReadOnlyMemory<byte>.Empty)));
                    var response = respond(packet.Payload.ToArray());
                    if (response is not null)
                    {
                        transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Write, remoteId, localId, response)));
                    }

                    break;
            }
        };
    }

    private static byte[] Encode(AdbPacket packet)
    {
        var bytes = new byte[AdbPacketCodec.GetEncodedLength(packet)];
        AdbPacketCodec.Write(packet, bytes);
        return bytes;
    }

    private static string GetPacketService(byte[] bytes)
    {
        var packet = AdbPacketCodec.Read(bytes, allowZeroChecksum: true);
        return packet.Header.Command == AdbCommand.Open ? GetService(packet) : string.Empty;
    }

    private static string GetService(AdbPacket packet)
    {
        return Encoding.UTF8.GetString(packet.Payload.Span).TrimEnd('\0');
    }

    private static byte[] ShellV2Frame(byte id, byte[] payload)
    {
        var result = new byte[5 + payload.Length];
        result[0] = id;
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(1), checked((uint)payload.Length));
        payload.CopyTo(result.AsSpan(5));
        return result;
    }

    private static byte[] Concat(params byte[][] buffers)
    {
        var length = buffers.Sum(static buffer => buffer.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var buffer in buffers)
        {
            buffer.CopyTo(result.AsSpan(offset));
            offset += buffer.Length;
        }

        return result;
    }

    private static string ReadSyncId(byte[] payload)
    {
        return Encoding.ASCII.GetString(payload.AsSpan(0, 4));
    }

    private static string ReadSyncPath(byte[] payload)
    {
        var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4, 4)));
        return Encoding.UTF8.GetString(payload.AsSpan(8, length));
    }

    private static ReadOnlySpan<byte> ReadSyncPayload(byte[] payload)
    {
        var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4, 4)));
        return payload.AsSpan(8, length);
    }

    private static byte[] StatV1(uint mode, uint size, uint modifiedTime)
    {
        var result = new byte[16];
        WriteSyncId(result, "STAT");
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), mode);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8, 4), size);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12, 4), modifiedTime);
        return result;
    }

    private static byte[] StatV2(string id, uint mode, ulong size, long accessTime, long modifiedTime, long changeTime)
    {
        var result = new byte[72];
        WriteSyncId(result, id);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(8, 8), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(16, 8), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24, 4), mode);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(28, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(32, 4), 2000);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(36, 4), 2000);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(40, 8), size);
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(48, 8), accessTime);
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(56, 8), modifiedTime);
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(64, 8), changeTime);
        return result;
    }

    private static byte[] DentV2(string name, uint mode, ulong size, long accessTime, long modifiedTime, long changeTime)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var result = new byte[76 + nameBytes.Length];
        StatV2("DNT2", mode, size, accessTime, modifiedTime, changeTime).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(72, 4), checked((uint)nameBytes.Length));
        nameBytes.CopyTo(result.AsSpan(76));
        return result;
    }

    private static byte[] DoneV2()
    {
        var result = new byte[76];
        WriteSyncId(result, "DONE");
        return result;
    }

    private static byte[] SyncData(byte[] payload)
    {
        var result = new byte[8 + payload.Length];
        WriteSyncId(result, "DATA");
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), checked((uint)payload.Length));
        payload.CopyTo(result.AsSpan(8));
        return result;
    }

    private static byte[] SyncDone()
    {
        var result = new byte[8];
        WriteSyncId(result, "DONE");
        return result;
    }

    private static byte[] SyncStatus(string id, string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var result = new byte[8 + messageBytes.Length];
        WriteSyncId(result, id);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), checked((uint)messageBytes.Length));
        messageBytes.CopyTo(result.AsSpan(8));
        return result;
    }

    private static void WriteSyncId(byte[] destination, string id)
    {
        Encoding.ASCII.GetBytes(id, destination);
    }

    private static byte[] CompressForTest(string codec, byte[] payload)
    {
        using var destination = new MemoryStream();
        using (var compressor = CreateCompressorForTest(codec, destination))
        {
            compressor.Write(payload);
        }

        return destination.ToArray();
    }

    private static byte[] DecompressForTest(string codec, byte[] payload)
    {
        using var source = new MemoryStream(payload);
        using var decompressor = CreateDecompressorForTest(codec, source);
        using var destination = new MemoryStream();
        decompressor.CopyTo(destination);
        return destination.ToArray();
    }

    private static Stream CreateCompressorForTest(string codec, Stream destination)
    {
        return codec switch
        {
            "brotli" => new BrotliStream(destination, CompressionLevel.Fastest, leaveOpen: true),
            "lz4" => LZ4Frame.Encode(destination, LZ4Level.L00_FAST, extraMemory: 0, leaveOpen: true).AsStream(false),
            "zstd" => new ZstdCompressionStream(destination, level: 1, bufferSize: 64 * 1024, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unknown test compression codec.")
        };
    }

    private static Stream CreateDecompressorForTest(string codec, Stream source)
    {
        return codec switch
        {
            "brotli" => new BrotliStream(source, CompressionMode.Decompress, leaveOpen: true),
            "lz4" => LZ4Frame.Decode(source, extraMemory: 0, leaveOpen: true).AsStream(false, true),
            "zstd" => new ZstdDecompressionStream(source, bufferSize: 64 * 1024, checkEndOfStream: true, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unknown test compression codec.")
        };
    }

    private sealed class RejectingAuthenticator : IAdbAuthenticator
    {
        public ValueTask<byte[]?> SignTokenAsync(ReadOnlyMemory<byte> token, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<byte[]?>(null);
        }

        public ValueTask<byte[]?> GetPublicKeyAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<byte[]?>("public-key\0"u8.ToArray());
        }
    }

    private sealed class RecordingProgress(List<long> values) : IProgress<long>
    {
        public void Report(long value)
        {
            values.Add(value);
        }
    }
}
