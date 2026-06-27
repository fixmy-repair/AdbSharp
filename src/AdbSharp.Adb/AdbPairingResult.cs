namespace AdbSharp.Adb;

/// <summary>
/// Describes a completed ADB wireless pairing exchange.
/// </summary>
/// <param name="Host">The paired endpoint host.</param>
/// <param name="Port">The paired endpoint port.</param>
/// <param name="PeerInfo">The peer information returned by the Android device.</param>
public sealed record AdbPairingResult(string Host, int Port, AdbPairingPeerInfo PeerInfo);
