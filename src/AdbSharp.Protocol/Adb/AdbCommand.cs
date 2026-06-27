namespace AdbSharp.Protocol.Adb;

/// <summary>
/// ADB packet command identifiers.
/// </summary>
public enum AdbCommand : uint
{
    /// <summary>
    /// Synchronization command used by older protocol revisions.
    /// </summary>
    Sync = 0x434e5953,

    /// <summary>
    /// Connection negotiation command.
    /// </summary>
    Connect = 0x4e584e43,

    /// <summary>
    /// Opens a logical ADB stream.
    /// </summary>
    Open = 0x4e45504f,

    /// <summary>
    /// Acknowledges a stream or write operation.
    /// </summary>
    Okay = 0x59414b4f,

    /// <summary>
    /// Closes a logical ADB stream.
    /// </summary>
    Close = 0x45534c43,

    /// <summary>
    /// Writes stream payload bytes.
    /// </summary>
    Write = 0x45545257,

    /// <summary>
    /// Carries an authentication token, signature, or public key.
    /// </summary>
    Auth = 0x48545541,

    /// <summary>
    /// Starts stream-based TLS when both peers support it.
    /// </summary>
    StartTls = 0x534c5453
}
