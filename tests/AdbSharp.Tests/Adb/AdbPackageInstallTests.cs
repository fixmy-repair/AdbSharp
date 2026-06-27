using System.Text;
using AdbSharp.Adb;
using AdbSharp.Common.Devices;
using AdbSharp.Protocol.Adb;
using AdbSharp.Tests.Transport;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Adb;

public sealed class AdbPackageInstallTests
{
    [Fact]
    public async Task InstallPackagesAsync_streams_split_apks_and_commits_staged_session()
    {
        var descriptor = CreateDescriptor();
        var transport = CreateConnectedTransport(descriptor);
        var progressValues = new List<long>();
        var openedServices = new List<string>();
        var streamedSplits = new Dictionary<string, string>(StringComparer.Ordinal);
        ConfigurePackageManager(transport, service =>
        {
            openedServices.Add(service);
            return service switch
            {
                "shell:pm install-create -r -g --staged -S 7" => ServiceAction.Output("Success: created install session [42]\n"),
                "exec:pm install-write -S 4 42 base.apk -" => ServiceAction.StreamInput(4, bytes =>
                {
                    streamedSplits["base.apk"] = Encoding.UTF8.GetString(bytes);
                    return "Success: streamed 4 bytes\n";
                }),
                "exec:pm install-write -S 3 42 config.en.apk -" => ServiceAction.StreamInput(3, bytes =>
                {
                    streamedSplits["config.en.apk"] = Encoding.UTF8.GetString(bytes);
                    return "Success: streamed 3 bytes\n";
                }),
                "shell:pm install-commit --staged-ready-timeout 0 42" => ServiceAction.Output("Success. Reboot device to apply staged session\n"),
                _ => ServiceAction.Output($"Failure [unexpected service {service}]\n")
            };
        });

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });
        await using var baseApk = new MemoryStream("base"u8.ToArray());
        await using var splitApk = new MemoryStream("cfg"u8.ToArray());

        var result = await client.InstallPackagesAsync(
            [
                new AdbPackageFile(baseApk, "base.apk"),
                new AdbPackageFile(splitApk, "config.en.apk")
            ],
            new AdbInstallOptions
            {
                GrantRuntimePermissions = true,
                Staged = true,
                StagedReadyTimeout = TimeSpan.Zero
            },
            new RecordingProgress(progressValues));

        Assert.Equal(42, result.SessionId);
        Assert.True(result.IsStaged);
        Assert.Equal("Success. Reboot device to apply staged session", result.Output);
        Assert.Equal("base", streamedSplits["base.apk"]);
        Assert.Equal("cfg", streamedSplits["config.en.apk"]);
        Assert.Equal([4L, 7L], progressValues);
        Assert.Contains("shell:pm install-create -r -g --staged -S 7", openedServices);
        Assert.Contains("shell:pm install-commit --staged-ready-timeout 0 42", openedServices);
    }

    [Fact]
    public async Task InstallPackagesAsync_abandons_session_when_write_fails()
    {
        var descriptor = CreateDescriptor();
        var transport = CreateConnectedTransport(descriptor);
        var openedServices = new List<string>();
        ConfigurePackageManager(transport, service =>
        {
            openedServices.Add(service);
            return service switch
            {
                "shell:pm install-create -r -S 3" => ServiceAction.Output("Success: created install session [7]\n"),
                "exec:pm install-write -S 3 7 base.apk -" => ServiceAction.StreamInput(3, _ => "Failure [bad split]\n"),
                "shell:pm install-abandon 7" => ServiceAction.Output("Success\n"),
                _ => ServiceAction.Output($"Failure [unexpected service {service}]\n")
            };
        });

        var device = CreateDevice(descriptor);
        await using var client = await AdbClient.ConnectAsync(device, new AdbClientOptions { TransportFactory = transport });
        await using var baseApk = new MemoryStream("bad"u8.ToArray());

        var exception = await Assert.ThrowsAsync<AdbPackageManagerException>(() =>
            client.InstallPackagesAsync([new AdbPackageFile(baseApk, "base.apk")]).AsTask());

        Assert.Contains("bad split", exception.Message, StringComparison.Ordinal);
        Assert.Contains("shell:pm install-abandon 7", openedServices);
    }

    private static UsbDeviceDescriptor CreateDescriptor()
    {
        return new UsbDeviceDescriptor("mock-1", 0x18d1, 0x4ee7, 0, AndroidUsbClass.VendorSpecificClass, AndroidUsbClass.AndroidSubClass, AndroidUsbClass.AdbProtocol, "serial", "Google", "Pixel");
    }

    private static AndroidDevice CreateDevice(UsbDeviceDescriptor descriptor)
    {
        return new AndroidDevice(
            new DeviceIdentity("serial", "Google", "Pixel", "Pixel", descriptor.TransportId),
            DeviceMode.Adb,
            DeviceCapabilities.Empty with { SupportsAdb = true },
            descriptor);
    }

    private static ScriptedUsbTransport CreateConnectedTransport(UsbDeviceDescriptor descriptor)
    {
        var transport = new ScriptedUsbTransport(descriptor);
        transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Connect, AdbConstants.Version, AdbConstants.MaxPayload, Encoding.UTF8.GetBytes("device::features=shell_v2"))));
        return transport;
    }

    private static void ConfigurePackageManager(ScriptedUsbTransport transport, Func<string, ServiceAction> selectAction)
    {
        var active = new Dictionary<uint, ActiveService>();
        uint nextRemoteId = 50;
        transport.OnWrite = bytes =>
        {
            var packet = AdbPacketCodec.Read(bytes);
            switch (packet.Header.Command)
            {
                case AdbCommand.Open:
                    var service = GetService(packet);
                    var remoteId = nextRemoteId++;
                    var action = selectAction(service);
                    active[packet.Header.Arg0] = new ActiveService(remoteId, action);
                    transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Okay, remoteId, packet.Header.Arg0, ReadOnlyMemory<byte>.Empty)));
                    if (action.ExpectedInputLength is null)
                    {
                        WriteOutputAndClose(transport, remoteId, packet.Header.Arg0, action.CreateOutput([]));
                    }

                    break;

                case AdbCommand.Write when active.TryGetValue(packet.Header.Arg0, out var activeService):
                    transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Okay, activeService.RemoteId, packet.Header.Arg0, ReadOnlyMemory<byte>.Empty)));
                    activeService.Input.Write(packet.Payload.Span);
                    if (activeService.ExpectedInputLength is not null && activeService.Input.Length >= activeService.ExpectedInputLength.Value)
                    {
                        WriteOutputAndClose(transport, activeService.RemoteId, packet.Header.Arg0, activeService.CreateOutput(activeService.Input.ToArray()));
                    }

                    break;
            }
        };
    }

    private static void WriteOutputAndClose(ScriptedUsbTransport transport, uint remoteId, uint localId, string output)
    {
        transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Write, remoteId, localId, Encoding.UTF8.GetBytes(output))));
        transport.EnqueueRead(Encode(AdbPacket.Create(AdbCommand.Close, remoteId, localId, ReadOnlyMemory<byte>.Empty)));
    }

    private static byte[] Encode(AdbPacket packet)
    {
        var bytes = new byte[AdbPacketCodec.GetEncodedLength(packet)];
        AdbPacketCodec.Write(packet, bytes);
        return bytes;
    }

    private static string GetService(AdbPacket packet)
    {
        return Encoding.UTF8.GetString(packet.Payload.Span).TrimEnd('\0');
    }

    private sealed class ActiveService(uint remoteId, ServiceAction action)
    {
        public uint RemoteId { get; } = remoteId;

        public long? ExpectedInputLength => action.ExpectedInputLength;

        public MemoryStream Input { get; } = new();

        public string CreateOutput(byte[] input)
        {
            return action.CreateOutput(input);
        }
    }

    private sealed class ServiceAction(long? expectedInputLength, Func<byte[], string> createOutput)
    {
        public long? ExpectedInputLength { get; } = expectedInputLength;

        public string CreateOutput(byte[] input)
        {
            return createOutput(input);
        }

        public static ServiceAction Output(string output)
        {
            return new ServiceAction(null, _ => output);
        }

        public static ServiceAction StreamInput(long expectedInputLength, Func<byte[], string> createOutput)
        {
            return new ServiceAction(expectedInputLength, createOutput);
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
