using System.Text;
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
}
