namespace AdbSharp.Protocol.Adb;

/// <summary>
/// ADB authentication packet kinds.
/// </summary>
public enum AdbAuthKind : uint
{
    /// <summary>
    /// The device is sending a token for the host to sign.
    /// </summary>
    Token = 1,

    /// <summary>
    /// The host is sending a signature for the token.
    /// </summary>
    Signature = 2,

    /// <summary>
    /// The host is sending its public key.
    /// </summary>
    PublicKey = 3
}
