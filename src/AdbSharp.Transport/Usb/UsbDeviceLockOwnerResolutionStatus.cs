namespace AdbSharp.Transport.Usb;

/// <summary>
/// Describes the outcome of resolving USB lock owners.
/// </summary>
public enum UsbDeviceLockOwnerResolutionStatus
{
    /// <summary>
    /// One or more owner processes were resolved.
    /// </summary>
    Resolved,

    /// <summary>
    /// Resolution completed and no owner process was found.
    /// </summary>
    NoOwnerFound,

    /// <summary>
    /// The current host platform or descriptor is not supported by the resolver.
    /// </summary>
    Unsupported,

    /// <summary>
    /// The host denied access to required process or device metadata.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// Resolution completed with partial results because some metadata was unavailable.
    /// </summary>
    Partial,

    /// <summary>
    /// Resolution failed unexpectedly.
    /// </summary>
    Failed
}
