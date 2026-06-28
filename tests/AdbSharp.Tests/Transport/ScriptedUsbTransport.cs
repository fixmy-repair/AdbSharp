using AdbSharp.Common.Devices;
using AdbSharp.Protocol.Adb;
using AdbSharp.Transport.Usb;
using System.Threading.Channels;

namespace AdbSharp.Tests.Transport;

internal sealed class ScriptedUsbTransport(
    UsbDeviceDescriptor descriptor,
    UsbEndpoint? bulkInEndpoint = null,
    UsbEndpoint? bulkOutEndpoint = null) : IUsbTransport, IUsbTransportFactory
{
    private readonly Channel<byte> readable = Channel.CreateUnbounded<byte>();
    private byte[]? pendingAdbHeader;

    public string PlatformName => "Scripted";

    public UsbDeviceDescriptor Descriptor { get; } = descriptor;

    public UsbEndpoint BulkInEndpoint { get; } = bulkInEndpoint ?? new UsbEndpoint(0x81, UsbEndpointDirection.In, UsbTransferKind.Bulk, 512);

    public UsbEndpoint BulkOutEndpoint { get; } = bulkOutEndpoint ?? new UsbEndpoint(0x01, UsbEndpointDirection.Out, UsbTransferKind.Bulk, 512);

    public List<byte[]> Writes { get; } = [];

    public Action<byte[]>? OnWrite { get; set; }

    public bool Disposed { get; private set; }

    public bool CanOpen(UsbDeviceDescriptor descriptor) => descriptor.TransportId == Descriptor.TransportId;

    public ValueTask<IUsbTransport> OpenAsync(UsbDeviceDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IUsbTransport>(this);
    }

    public void EnqueueRead(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            readable.Writer.TryWrite(value);
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await readable.Reader.WaitToReadAsync(cancellationToken))
        {
            return 0;
        }

        var count = 0;
        while (count < buffer.Length && readable.Reader.TryRead(out var value))
        {
            buffer.Span[count] = value;
            count++;
        }

        return count;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = buffer.ToArray();
        if (TryCoalesceAdbWrite(bytes))
        {
            return ValueTask.CompletedTask;
        }

        Writes.Add(bytes);
        OnWrite?.Invoke(bytes);
        return ValueTask.CompletedTask;
    }

    private bool TryCoalesceAdbWrite(byte[] bytes)
    {
        if (pendingAdbHeader is not null)
        {
            var packet = new byte[pendingAdbHeader.Length + bytes.Length];
            pendingAdbHeader.CopyTo(packet, 0);
            bytes.CopyTo(packet, pendingAdbHeader.Length);
            pendingAdbHeader = null;
            Writes.Add(packet);
            OnWrite?.Invoke(packet);
            return true;
        }

        if (bytes.Length != AdbConstants.HeaderLength)
        {
            return false;
        }

        try
        {
            var header = AdbPacketHeader.Read(bytes);
            if (header.PayloadLength == 0)
            {
                return false;
            }

            pendingAdbHeader = bytes;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
