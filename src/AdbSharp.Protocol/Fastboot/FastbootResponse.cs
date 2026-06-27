namespace AdbSharp.Protocol.Fastboot;

/// <summary>
/// Parsed Fastboot response packet.
/// </summary>
/// <param name="Kind">The response kind.</param>
/// <param name="Payload">The payload after the four-byte response tag.</param>
/// <param name="DataLength">The requested data phase length for <see cref="FastbootResponseKind.Data"/> responses.</param>
public sealed record FastbootResponse(
    FastbootResponseKind Kind,
    string Payload,
    long? DataLength = null);
