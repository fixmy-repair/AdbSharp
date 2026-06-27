namespace AdbSharp.Transport.Usb;

/// <summary>
/// Validates USB transport metadata before protocol clients use a transport.
/// </summary>
public static class UsbTransportValidator
{
    private const byte DirectionMask = 0x80;
    private const byte EndpointNumberMask = 0x0f;

    /// <summary>
    /// Validates the metadata exposed by an opened Android USB transport.
    /// </summary>
    /// <param name="transport">The opened transport.</param>
    /// <exception cref="UsbTransportException">The transport does not expose a valid bulk IN/OUT endpoint pair.</exception>
    public static void ValidateOpenedTransport(IUsbTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ValidateEndpointPair(transport.BulkInEndpoint, transport.BulkOutEndpoint);
    }

    /// <summary>
    /// Validates that two endpoints form an Android-compatible bulk IN/OUT pair.
    /// </summary>
    /// <param name="bulkIn">The bulk IN endpoint.</param>
    /// <param name="bulkOut">The bulk OUT endpoint.</param>
    /// <exception cref="UsbTransportException">The endpoints do not form a valid Android bulk pair.</exception>
    public static void ValidateEndpointPair(UsbEndpoint bulkIn, UsbEndpoint bulkOut)
    {
        ValidateEndpoint(bulkIn, UsbEndpointDirection.In, nameof(bulkIn));
        ValidateEndpoint(bulkOut, UsbEndpointDirection.Out, nameof(bulkOut));
    }

    private static void ValidateEndpoint(UsbEndpoint endpoint, UsbEndpointDirection expectedDirection, string endpointName)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.TransferKind != UsbTransferKind.Bulk)
        {
            throw Create(endpointName, endpoint, "Endpoint must be a bulk endpoint.");
        }

        if (endpoint.Direction != expectedDirection)
        {
            throw Create(endpointName, endpoint, $"Endpoint direction must be {expectedDirection}.");
        }

        if (endpoint.MaxPacketSize == 0)
        {
            throw Create(endpointName, endpoint, "Endpoint max packet size must be non-zero.");
        }

        if ((endpoint.Address & EndpointNumberMask) == 0)
        {
            throw Create(endpointName, endpoint, "Endpoint address must not reference endpoint zero.");
        }

        var hasInAddressBit = (endpoint.Address & DirectionMask) == DirectionMask;
        if (expectedDirection == UsbEndpointDirection.In && !hasInAddressBit)
        {
            throw Create(endpointName, endpoint, "IN endpoint address must include the USB direction bit.");
        }

        if (expectedDirection == UsbEndpointDirection.Out && hasInAddressBit)
        {
            throw Create(endpointName, endpoint, "OUT endpoint address must not include the USB direction bit.");
        }
    }

    private static UsbTransportException Create(string endpointName, UsbEndpoint endpoint, string reason)
    {
        return new UsbTransportException(
            UsbTransportError.InvalidEndpoint,
            $"{reason} {endpointName} address=0x{endpoint.Address:x2}, direction={endpoint.Direction}, kind={endpoint.TransferKind}, maxPacketSize={endpoint.MaxPacketSize}.");
    }
}
