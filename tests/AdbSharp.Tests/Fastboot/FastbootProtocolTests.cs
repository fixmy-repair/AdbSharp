using System.Text;
using AdbSharp.Protocol.Fastboot;

namespace AdbSharp.Tests.Fastboot;

public sealed class FastbootProtocolTests
{
    [Fact]
    public void Parses_okay_response()
    {
        var response = FastbootProtocol.ParseResponse(Encoding.ASCII.GetBytes("OKAY0.4"));

        Assert.Equal(FastbootResponseKind.Okay, response.Kind);
        Assert.Equal("0.4", response.Payload);
    }

    [Fact]
    public void Parses_data_response()
    {
        var response = FastbootProtocol.ParseResponse(Encoding.ASCII.GetBytes("DATA00001234"));

        Assert.Equal(FastbootResponseKind.Data, response.Kind);
        Assert.Equal(0x1234, response.DataLength);
    }

    [Fact]
    public void Formats_download_command()
    {
        Assert.Equal("download:00001234", FastbootProtocol.FormatDownloadCommand(0x1234));
    }

    [Fact]
    public void Encodes_command_into_caller_buffer()
    {
        Span<byte> destination = stackalloc byte[32];

        var written = FastbootProtocol.EncodeCommand("getvar:product", destination);

        Assert.Equal(14, written);
        Assert.True(destination[..written].SequenceEqual("getvar:product"u8));
    }

    [Fact]
    public void Rejects_command_when_destination_is_too_small()
    {
        static void EncodeIntoSmallBuffer()
        {
            Span<byte> destination = stackalloc byte[4];
            FastbootProtocol.EncodeCommand("getvar:product", destination);
        }

        Assert.Throws<ArgumentException>(EncodeIntoSmallBuffer);
    }

    [Fact]
    public void Rejects_non_ascii_command()
    {
        Assert.Throws<ArgumentException>(() => FastbootProtocol.EncodeCommand("getvar:päroduct"));
    }
}
