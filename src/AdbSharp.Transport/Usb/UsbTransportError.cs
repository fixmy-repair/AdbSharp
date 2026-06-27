namespace AdbSharp.Transport.Usb;

/// <summary>
/// Classifies USB transport failures in a platform-neutral way.
/// </summary>
public enum UsbTransportError
{
    /// <summary>
    /// The failure could not be classified.
    /// </summary>
    Unknown,

    /// <summary>
    /// The host process does not have permission to access the USB interface.
    /// </summary>
    PermissionDenied,

    /// <summary>
    /// The selected USB device or interface was not found.
    /// </summary>
    DeviceNotFound,

    /// <summary>
    /// The device disconnected while an operation was in progress.
    /// </summary>
    DeviceDisconnected,

    /// <summary>
    /// The USB endpoint stalled.
    /// </summary>
    EndpointStalled,

    /// <summary>
    /// The native USB operation timed out.
    /// </summary>
    Timeout,

    /// <summary>
    /// The device or interface is busy.
    /// </summary>
    Busy,

    /// <summary>
    /// Another process has exclusive access to the device or interface.
    /// </summary>
    ExclusiveAccess,

    /// <summary>
    /// The operation was aborted by the operating system or driver.
    /// </summary>
    OperationAborted,

    /// <summary>
    /// The native USB operation failed with a general I/O error.
    /// </summary>
    Io,

    /// <summary>
    /// The transport reported endpoint metadata that cannot support Android bulk protocol traffic.
    /// </summary>
    InvalidEndpoint,

    /// <summary>
    /// A required platform USB dependency is missing.
    /// </summary>
    PlatformDependencyMissing
}
