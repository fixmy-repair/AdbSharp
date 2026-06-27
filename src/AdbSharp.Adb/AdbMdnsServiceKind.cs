namespace AdbSharp.Adb;

/// <summary>
/// Identifies Android Debug Bridge mDNS service categories.
/// </summary>
public enum AdbMdnsServiceKind
{
    /// <summary>
    /// The legacy unencrypted ADB TCP service, advertised as <c>_adb._tcp</c>.
    /// </summary>
    LegacyAdb = 0,

    /// <summary>
    /// The Android 11+ wireless debugging pairing service, advertised as <c>_adb-tls-pairing._tcp</c>.
    /// </summary>
    Pairing = 1,

    /// <summary>
    /// The Android 11+ paired wireless debugging connection service, advertised as <c>_adb-tls-connect._tcp</c>.
    /// </summary>
    Connect = 2
}
