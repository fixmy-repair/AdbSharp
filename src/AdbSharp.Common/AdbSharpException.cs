namespace AdbSharp.Common;

/// <summary>
/// Base exception for AdbSharp failures.
/// </summary>
public class AdbSharpException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdbSharpException"/> class.
    /// </summary>
    public AdbSharpException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AdbSharpException"/> class.
    /// </summary>
    /// <param name="message">The failure message.</param>
    public AdbSharpException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AdbSharpException"/> class.
    /// </summary>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public AdbSharpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
