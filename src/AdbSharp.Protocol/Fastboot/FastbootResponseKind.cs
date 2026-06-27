namespace AdbSharp.Protocol.Fastboot;

/// <summary>
/// Fastboot response tag.
/// </summary>
public enum FastbootResponseKind
{
    /// <summary>
    /// The command completed successfully.
    /// </summary>
    Okay,

    /// <summary>
    /// The command failed.
    /// </summary>
    Fail,

    /// <summary>
    /// A data phase is requested.
    /// </summary>
    Data,

    /// <summary>
    /// Informational progress text.
    /// </summary>
    Info,

    /// <summary>
    /// Unformatted text output.
    /// </summary>
    Text
}
