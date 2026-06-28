namespace AdbSharp.Transport.Usb;

/// <summary>
/// Structured diagnostics for an opened USB transport.
/// </summary>
/// <param name="Backend">The backend implementation name.</param>
/// <param name="TransportId">The platform transport id.</param>
/// <param name="BulkInEndpointAddress">The bulk input endpoint address.</param>
/// <param name="BulkInMaxPacketSize">The bulk input endpoint max packet size.</param>
/// <param name="BulkOutEndpointAddress">The bulk output endpoint address.</param>
/// <param name="BulkOutMaxPacketSize">The bulk output endpoint max packet size.</param>
/// <param name="IsOpen">True when the transport is currently open.</param>
/// <param name="State">A backend-specific state summary.</param>
/// <param name="Properties">Additional backend-specific diagnostic properties.</param>
public sealed record UsbTransportDiagnosticSnapshot(
    string Backend,
    string TransportId,
    byte BulkInEndpointAddress,
    ushort BulkInMaxPacketSize,
    byte BulkOutEndpointAddress,
    ushort BulkOutMaxPacketSize,
    bool IsOpen,
    string State,
    IReadOnlyDictionary<string, string> Properties);
