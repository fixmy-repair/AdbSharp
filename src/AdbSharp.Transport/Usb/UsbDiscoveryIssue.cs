namespace AdbSharp.Transport.Usb;

/// <summary>
/// Describes a USB discovery issue reported by one registered enumerator.
/// </summary>
/// <param name="EnumeratorName">The enumerator type name.</param>
/// <param name="Error">The platform-neutral USB error classification.</param>
/// <param name="Message">The diagnostic message.</param>
/// <param name="Exception">The exception raised by the enumerator, when available.</param>
public sealed record UsbDiscoveryIssue(
    string EnumeratorName,
    UsbTransportError Error,
    string Message,
    Exception? Exception = null);
