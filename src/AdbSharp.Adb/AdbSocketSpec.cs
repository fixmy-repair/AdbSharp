using System.Globalization;

namespace AdbSharp.Adb;

/// <summary>
/// Represents an ADB socket specification such as <c>tcp:5555</c> or <c>localabstract:debug</c>.
/// </summary>
public sealed record AdbSocketSpec
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdbSocketSpec"/> class.
    /// </summary>
    /// <param name="value">The encoded socket specification sent to the device.</param>
    public AdbSocketSpec(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>
    /// Gets the encoded socket specification sent to the device.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a TCP socket specification.
    /// </summary>
    /// <param name="port">The TCP port. Use zero for an automatically selected local port where supported.</param>
    /// <returns>The socket specification.</returns>
    public static AdbSocketSpec Tcp(int port)
    {
        ValidatePort(port);
        return new AdbSocketSpec($"tcp:{port.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Creates a Linux abstract namespace local socket specification.
    /// </summary>
    /// <param name="name">The local socket name.</param>
    /// <returns>The socket specification.</returns>
    public static AdbSocketSpec LocalAbstract(string name)
    {
        return new AdbSocketSpec($"localabstract:{ValidateName(name)}");
    }

    /// <summary>
    /// Creates a Linux reserved namespace local socket specification.
    /// </summary>
    /// <param name="name">The local socket name.</param>
    /// <returns>The socket specification.</returns>
    public static AdbSocketSpec LocalReserved(string name)
    {
        return new AdbSocketSpec($"localreserved:{ValidateName(name)}");
    }

    /// <summary>
    /// Creates a Linux filesystem namespace local socket specification.
    /// </summary>
    /// <param name="path">The device-side socket path.</param>
    /// <returns>The socket specification.</returns>
    public static AdbSocketSpec LocalFileSystem(string path)
    {
        return new AdbSocketSpec($"localfilesystem:{ValidateName(path)}");
    }

    /// <summary>
    /// Creates a device file socket specification.
    /// </summary>
    /// <param name="path">The device path.</param>
    /// <returns>The socket specification.</returns>
    public static AdbSocketSpec Device(string path)
    {
        return new AdbSocketSpec($"dev:{ValidateName(path)}");
    }

    /// <summary>
    /// Creates a JDWP process socket specification.
    /// </summary>
    /// <param name="processId">The target process identifier.</param>
    /// <returns>The socket specification.</returns>
    public static AdbSocketSpec Jdwp(int processId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        return new AdbSocketSpec($"jdwp:{processId.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    private static void ValidatePort(int port)
    {
        if (port is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "TCP ports must be in the range 0 through 65535.");
        }
    }

    private static string ValidateName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains(';', StringComparison.Ordinal))
        {
            throw new ArgumentException("ADB socket specifications cannot contain ';'.", nameof(value));
        }

        return value;
    }
}
