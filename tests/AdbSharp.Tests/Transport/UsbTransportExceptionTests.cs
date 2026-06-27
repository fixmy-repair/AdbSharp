using AdbSharp.Transport.Usb;

namespace AdbSharp.Tests.Transport;

public sealed class UsbTransportExceptionTests
{
    [Fact]
    public void Typed_constructor_preserves_error_classification()
    {
        var exception = new UsbTransportException(UsbTransportError.PermissionDenied, "denied");

        Assert.Equal(UsbTransportError.PermissionDenied, exception.Error);
        Assert.Equal("denied", exception.Message);
    }

    [Fact]
    public void Legacy_constructor_defaults_to_unknown_error()
    {
        var exception = new UsbTransportException("failed");

        Assert.Equal(UsbTransportError.Unknown, exception.Error);
    }

    [Fact]
    public void Invalid_endpoint_error_is_available_for_transport_metadata_failures()
    {
        var exception = new UsbTransportException(UsbTransportError.InvalidEndpoint, "invalid endpoint");

        Assert.Equal(UsbTransportError.InvalidEndpoint, exception.Error);
    }
}
