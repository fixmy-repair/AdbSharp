namespace AdbSharp.Protocol.Adb;

/// <summary>
/// ADB protocol constants.
/// </summary>
public static class AdbConstants
{
    /// <summary>
    /// Size of an ADB packet header in bytes.
    /// </summary>
    public const int HeaderLength = 24;

    /// <summary>
    /// Original protocol payload maximum.
    /// </summary>
    public const int MaxPayloadV1 = 4 * 1024;

    /// <summary>
    /// Current protocol payload maximum.
    /// </summary>
    public const int MaxPayload = 1024 * 1024;

    /// <summary>
    /// Minimum ADB protocol version.
    /// </summary>
    public const uint VersionMin = 0x01000000;

    /// <summary>
    /// Current ADB protocol version.
    /// </summary>
    public const uint Version = 0x01000001;

    /// <summary>
    /// Protocol version where peers may omit payload checksums by sending zero.
    /// </summary>
    public const uint VersionSkipChecksum = 0x01000001;

    /// <summary>
    /// Minimum stream TLS version.
    /// </summary>
    public const uint StartTlsVersionMin = 0x01000000;

    /// <summary>
    /// Current stream TLS version.
    /// </summary>
    public const uint StartTlsVersion = 0x01000000;
}
