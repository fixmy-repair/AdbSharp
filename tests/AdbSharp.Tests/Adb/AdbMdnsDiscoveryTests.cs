using System.Buffers.Binary;
using System.Net;
using System.Text;
using AdbSharp.Adb;
using AdbSharp.Adb.Internal;

namespace AdbSharp.Tests.Adb;

public sealed class AdbMdnsDiscoveryTests
{
    [Fact]
    public void CreateQuery_includes_android_wireless_service_types()
    {
        var query = AdbMdnsProtocol.CreateQuery([AdbMdnsServiceKind.Pairing, AdbMdnsServiceKind.Connect]);

        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(4)));
        var text = Encoding.ASCII.GetString(query);
        Assert.Contains("_adb-tls-pairing", text, StringComparison.Ordinal);
        Assert.Contains("_adb-tls-connect", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseResponse_returns_pairing_and_connect_services()
    {
        var response = CreateResponse(
            CreatePtrRecord("_adb-tls-pairing._tcp.local", "Pixel._adb-tls-pairing._tcp.local"),
            CreateSrvRecord("Pixel._adb-tls-pairing._tcp.local", 37123, "pixel.local"),
            CreateTxtRecord("Pixel._adb-tls-pairing._tcp.local", "v=1", "name=Pixel"),
            CreateARecord("pixel.local", IPAddress.Parse("192.168.1.42")),
            CreatePtrRecord("_adb-tls-connect._tcp.local", "Pixel._adb-tls-connect._tcp.local"),
            CreateSrvRecord("Pixel._adb-tls-connect._tcp.local", 39211, "pixel.local"),
            CreateTxtRecord("Pixel._adb-tls-connect._tcp.local", "v=1"),
            CreateARecord("pixel.local", IPAddress.Parse("192.168.1.42")));

        var services = AdbMdnsProtocol.ParseResponse(response);

        var pairing = Assert.Single(services, static service => service.Kind == AdbMdnsServiceKind.Pairing);
        Assert.Equal("Pixel", pairing.InstanceName);
        Assert.Equal(AdbMdnsServiceTypes.Pairing, pairing.ServiceType);
        Assert.Equal("pixel.local", pairing.TargetHost);
        Assert.Equal("192.168.1.42", pairing.Host);
        Assert.Equal(37123, pairing.Port);
        Assert.Equal("1", pairing.TxtRecords["v"]);
        Assert.Equal("Pixel", pairing.TxtRecords["name"]);

        var connect = Assert.Single(services, static service => service.Kind == AdbMdnsServiceKind.Connect);
        Assert.Equal("Pixel", connect.InstanceName);
        Assert.Equal(39211, connect.Port);
        Assert.Equal("192.168.1.42", connect.Host);
    }

    [Fact]
    public void ConnectWirelessAsync_rejects_pairing_mdns_service()
    {
        var service = new AdbMdnsService(
            "Pixel",
            AdbMdnsServiceTypes.Pairing,
            AdbMdnsServiceKind.Pairing,
            "pixel.local",
            37123);

        var exception = Assert.Throws<ArgumentException>(() =>
            AdbClient.ConnectWirelessAsync(service).AsTask().GetAwaiter().GetResult());

        Assert.Equal("service", exception.ParamName);
    }

    [Fact]
    public void PairAsync_rejects_connect_mdns_service()
    {
        var service = new AdbMdnsService(
            "Pixel",
            AdbMdnsServiceTypes.Connect,
            AdbMdnsServiceKind.Connect,
            "pixel.local",
            39211);

        using var keyPair = AdbSharp.Authentication.Adb.AdbKeyPair.Create();
        var exception = Assert.Throws<ArgumentException>(() =>
            AdbPairingClient.PairAsync(service, "123456", keyPair).AsTask().GetAwaiter().GetResult());

        Assert.Equal("service", exception.ParamName);
    }

    [Fact]
    public async Task FindAsync_rejects_disabled_ip_versions()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            AdbMdnsDiscovery.FindAsync(new AdbMdnsDiscoveryOptions { IncludeIPv4 = false, IncludeIPv6 = false }).AsTask());

        Assert.Equal("options", exception.ParamName);
    }

    private static byte[] CreateResponse(params byte[][] records)
    {
        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x8400);
        BinaryPrimitives.WriteUInt16BigEndian(header[6..], checked((ushort)records.Length));
        stream.Write(header);
        foreach (var record in records)
        {
            stream.Write(record);
        }

        return stream.ToArray();
    }

    private static byte[] CreatePtrRecord(string name, string instance)
    {
        return CreateRecord(name, 12, stream => WriteName(stream, instance));
    }

    private static byte[] CreateSrvRecord(string name, int port, string targetHost)
    {
        return CreateRecord(
            name,
            33,
            stream =>
            {
                Span<byte> fields = stackalloc byte[6];
                BinaryPrimitives.WriteUInt16BigEndian(fields[4..], checked((ushort)port));
                stream.Write(fields);
                WriteName(stream, targetHost);
            });
    }

    private static byte[] CreateTxtRecord(string name, params string[] values)
    {
        return CreateRecord(
            name,
            16,
            stream =>
            {
                foreach (var value in values)
                {
                    var bytes = Encoding.UTF8.GetBytes(value);
                    stream.WriteByte(checked((byte)bytes.Length));
                    stream.Write(bytes);
                }
            });
    }

    private static byte[] CreateARecord(string name, IPAddress address)
    {
        return CreateRecord(name, 1, stream => stream.Write(address.GetAddressBytes()));
    }

    private static byte[] CreateRecord(string name, ushort type, Action<Stream> writeData)
    {
        using var data = new MemoryStream();
        writeData(data);

        using var record = new MemoryStream();
        WriteName(record, name);
        Span<byte> fields = stackalloc byte[10];
        BinaryPrimitives.WriteUInt16BigEndian(fields, type);
        BinaryPrimitives.WriteUInt16BigEndian(fields[2..], 1);
        BinaryPrimitives.WriteUInt32BigEndian(fields[4..], 120);
        BinaryPrimitives.WriteUInt16BigEndian(fields[8..], checked((ushort)data.Length));
        record.Write(fields);
        record.Write(data.ToArray());
        return record.ToArray();
    }

    private static void WriteName(Stream stream, string name)
    {
        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            stream.WriteByte(checked((byte)bytes.Length));
            stream.Write(bytes);
        }

        stream.WriteByte(0);
    }
}
