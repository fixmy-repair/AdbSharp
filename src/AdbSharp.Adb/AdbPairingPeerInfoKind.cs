namespace AdbSharp.Adb;

/// <summary>
/// Identifies the kind of peer information exchanged during ADB wireless pairing.
/// </summary>
public enum AdbPairingPeerInfoKind : byte
{
    /// <summary>
    /// An ADB RSA public key payload.
    /// </summary>
    AdbRsaPublicKey = 0,

    /// <summary>
    /// A device GUID payload.
    /// </summary>
    DeviceGuid = 1
}
