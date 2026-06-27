using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using AdbSharp.Common;

namespace AdbSharp.Adb.Internal;

internal static class AdbShellV2Protocol
{
    private const int HeaderLength = 5;
    private const int MaxFrameLength = 16 * 1024 * 1024;
    private const byte StandardOutputPacket = 1;
    private const byte StandardErrorPacket = 2;
    private const byte ExitPacket = 3;

    public static async ValueTask<AdbShellResult> ReadResultAsync(AdbStream stream, CancellationToken cancellationToken)
    {
        await using var stdout = new PooledMemoryStream();
        await using var stderr = new PooledMemoryStream();
        int? exitCode = null;
        var header = new byte[HeaderLength];

        while (exitCode is null)
        {
            try
            {
                await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                break;
            }

            var packetId = header[0];
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(1)));
            if (length > MaxFrameLength)
            {
                throw new ProtocolException($"ADB shell v2 frame length {length} exceeds the maximum accepted length {MaxFrameLength}.");
            }

            var rented = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                var payload = rented.AsMemory(0, length);
                if (length != 0)
                {
                    await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
                }

                switch (packetId)
                {
                    case StandardOutputPacket:
                        stdout.Write(payload.Span);
                        break;

                    case StandardErrorPacket:
                        stderr.Write(payload.Span);
                        break;

                    case ExitPacket:
                        exitCode = length == 0 ? 0 : payload.Span[0];
                        break;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        return new AdbShellResult(
            exitCode,
            Encoding.UTF8.GetString(stdout.GetBuffer().AsSpan(0, checked((int)stdout.Length))),
            Encoding.UTF8.GetString(stderr.GetBuffer().AsSpan(0, checked((int)stderr.Length))));
    }

}
