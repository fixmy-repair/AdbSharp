using System.Text;
using AdbSharp.Common;
using AdbSharp.Protocol.Adb;

namespace AdbSharp.Tests.Adb;

public sealed class AdbPacketCodecTests
{
    [Fact]
    public void Creates_expected_header_values()
    {
        var payload = Encoding.ASCII.GetBytes("host::");
        var packet = AdbPacket.Create(AdbCommand.Connect, AdbConstants.Version, AdbConstants.MaxPayload, payload);

        Assert.Equal(AdbCommand.Connect, packet.Header.Command);
        Assert.Equal((uint)payload.Length, packet.Header.PayloadLength);
        Assert.Equal((uint)payload.Sum(b => b), packet.Header.PayloadChecksum);
        Assert.Equal((uint)AdbCommand.Connect ^ uint.MaxValue, packet.Header.Magic);
    }

    [Fact]
    public void Round_trips_packet()
    {
        var packet = AdbPacket.Create(AdbCommand.Write, 1, 2, Encoding.UTF8.GetBytes("hello"));
        var buffer = new byte[AdbPacketCodec.GetEncodedLength(packet)];

        var written = AdbPacketCodec.Write(packet, buffer);
        var parsed = AdbPacketCodec.Read(buffer);

        Assert.Equal(buffer.Length, written);
        Assert.Equal(packet.Header, parsed.Header);
        Assert.Equal(packet.Payload.ToArray(), parsed.Payload.ToArray());
    }

    [Fact]
    public void Creates_zero_checksum_when_requested()
    {
        var packet = AdbPacket.Create(AdbCommand.Write, 1, 2, Encoding.UTF8.GetBytes("hello"), skipChecksum: true);

        Assert.Equal(0u, packet.Header.PayloadChecksum);
    }

    [Fact]
    public void Accepts_modern_connect_with_zero_checksum_before_negotiation()
    {
        var payload = Encoding.UTF8.GetBytes("device::features=shell_v2");
        var header = AdbPacketHeader.Create(AdbCommand.Connect, AdbConstants.VersionSkipChecksum, AdbConstants.MaxPayload, payload, skipChecksum: true);

        var packet = new AdbPacket(header, payload);

        Assert.Equal(AdbCommand.Connect, packet.Header.Command);
        Assert.Equal(payload, packet.Payload.ToArray());
    }

    [Fact]
    public void Accepts_zero_checksum_when_negotiated()
    {
        var payload = Encoding.UTF8.GetBytes("hello");
        var header = AdbPacketHeader.Create(AdbCommand.Write, 1, 2, payload, skipChecksum: true);

        var packet = AdbPacket.FromWire(header, payload, allowZeroChecksum: true);

        Assert.Equal(AdbCommand.Write, packet.Header.Command);
        Assert.Equal(payload, packet.Payload.ToArray());
    }

    [Fact]
    public void Rejects_zero_checksum_when_not_negotiated()
    {
        var payload = Encoding.UTF8.GetBytes("hello");
        var header = AdbPacketHeader.Create(AdbCommand.Write, 1, 2, payload, skipChecksum: true);

        Assert.Throws<ProtocolException>(() => AdbPacket.FromWire(header, payload, allowZeroChecksum: false));
    }
}
