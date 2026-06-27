namespace AdbSharp.Common;

/// <summary>
/// Represents malformed protocol data or an unexpected protocol transition.
/// </summary>
public sealed class ProtocolException : AdbSharpException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProtocolException"/> class.
    /// </summary>
    /// <param name="message">The protocol failure message.</param>
    public ProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtocolException"/> class.
    /// </summary>
    /// <param name="message">The protocol failure message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
