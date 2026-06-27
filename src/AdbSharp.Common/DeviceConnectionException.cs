namespace AdbSharp.Common;

/// <summary>
/// Represents a device connection failure.
/// </summary>
public sealed class DeviceConnectionException : AdbSharpException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceConnectionException"/> class.
    /// </summary>
    /// <param name="message">The connection failure message.</param>
    public DeviceConnectionException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceConnectionException"/> class.
    /// </summary>
    /// <param name="message">The connection failure message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public DeviceConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
