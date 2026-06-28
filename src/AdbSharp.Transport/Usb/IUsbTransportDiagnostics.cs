namespace AdbSharp.Transport.Usb;

/// <summary>
/// Provides structured diagnostics for an opened USB transport.
/// </summary>
public interface IUsbTransportDiagnostics
{
    /// <summary>
    /// Gets a snapshot of transport diagnostics.
    /// </summary>
    /// <returns>The diagnostic snapshot.</returns>
    UsbTransportDiagnosticSnapshot GetDiagnosticSnapshot();
}
