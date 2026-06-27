namespace AdbSharp.Adb;

/// <summary>
/// Represents the fixed-size peer information exchanged after ADB wireless pairing authentication succeeds.
/// </summary>
/// <param name="kind">The peer information kind.</param>
/// <param name="data">The peer information payload.</param>
public sealed class AdbPairingPeerInfo(AdbPairingPeerInfoKind kind, ReadOnlyMemory<byte> data)
{
    /// <summary>
    /// The exact size of an ADB pairing peer-info record on the wire.
    /// </summary>
    public const int WireSize = 8192;

    /// <summary>
    /// The maximum peer-info payload size.
    /// </summary>
    public const int MaxDataSize = WireSize - 1;

    private readonly byte[] data = CopyData(data.Span);

    /// <summary>
    /// Gets the peer information kind.
    /// </summary>
    public AdbPairingPeerInfoKind Kind { get; } = kind;

    /// <summary>
    /// Gets the peer information payload without trailing zero padding.
    /// </summary>
    public ReadOnlyMemory<byte> Data => data;

    internal static AdbPairingPeerInfo FromWireBytes(ReadOnlySpan<byte> wireBytes)
    {
        if (wireBytes.Length != WireSize)
        {
            throw new ArgumentException($"ADB pairing peer-info records must be exactly {WireSize} bytes.", nameof(wireBytes));
        }

        var kind = (AdbPairingPeerInfoKind)wireBytes[0];
        var payload = wireBytes[1..];
        var length = payload.Length;
        while (length > 0 && payload[length - 1] == 0)
        {
            length--;
        }

        return new AdbPairingPeerInfo(kind, payload[..length].ToArray());
    }

    internal byte[] ToWireBytes()
    {
        var wireBytes = new byte[WireSize];
        wireBytes[0] = (byte)Kind;
        data.CopyTo(wireBytes.AsSpan(1));
        return wireBytes;
    }

    private static byte[] CopyData(ReadOnlySpan<byte> value)
    {
        if (value.Length > MaxDataSize)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"ADB pairing peer-info payloads cannot exceed {MaxDataSize} bytes.");
        }

        return value.ToArray();
    }
}
