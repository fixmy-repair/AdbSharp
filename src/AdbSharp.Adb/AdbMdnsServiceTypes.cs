namespace AdbSharp.Adb;

/// <summary>
/// Android Debug Bridge DNS-SD service type constants.
/// </summary>
public static class AdbMdnsServiceTypes
{
    /// <summary>
    /// The legacy unencrypted ADB TCP service type.
    /// </summary>
    public const string LegacyAdb = "_adb._tcp";

    /// <summary>
    /// The Android 11+ wireless debugging pairing service type.
    /// </summary>
    public const string Pairing = "_adb-tls-pairing._tcp";

    /// <summary>
    /// The Android 11+ paired wireless debugging connection service type.
    /// </summary>
    public const string Connect = "_adb-tls-connect._tcp";
}
