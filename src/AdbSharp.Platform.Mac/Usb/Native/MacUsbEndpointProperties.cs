using System.Runtime.InteropServices;

namespace AdbSharp.Platform.Mac.Usb.Native;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct MacUsbEndpointProperties
{
    public byte Version;
    public byte AlternateSetting;
    public byte Direction;
    public byte EndpointNumber;
    public byte TransferType;
    public byte UsageType;
    public byte SyncType;
    public byte Interval;
    public ushort MaxPacketSize;
    public byte MaxBurst;
    public byte MaxStreams;
    public byte Mult;
    public ushort BytesPerInterval;
}
