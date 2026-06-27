using AdbSharp.Common;

namespace AdbSharp.Transport.Usb;

/// <summary>
/// Represents a USB transport failure.
/// </summary>
public sealed class UsbTransportException : AdbSharpException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UsbTransportException"/> class.
    /// </summary>
    /// <param name="message">The failure message.</param>
    public UsbTransportException(string message)
        : this(UsbTransportError.Unknown, message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UsbTransportException"/> class.
    /// </summary>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public UsbTransportException(string message, Exception innerException)
        : this(UsbTransportError.Unknown, message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UsbTransportException"/> class.
    /// </summary>
    /// <param name="error">The platform-neutral USB error classification.</param>
    /// <param name="message">The failure message.</param>
    public UsbTransportException(UsbTransportError error, string message)
        : base(message)
    {
        Error = error;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UsbTransportException"/> class.
    /// </summary>
    /// <param name="error">The platform-neutral USB error classification.</param>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public UsbTransportException(UsbTransportError error, string message, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    /// <summary>
    /// Gets the platform-neutral USB error classification.
    /// </summary>
    public UsbTransportError Error { get; } = UsbTransportError.Unknown;
}
