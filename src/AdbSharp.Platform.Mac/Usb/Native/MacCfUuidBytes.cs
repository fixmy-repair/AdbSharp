using System.Runtime.InteropServices;

namespace AdbSharp.Platform.Mac.Usb.Native;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct MacCfUuidBytes
{
    public readonly byte Byte0;
    public readonly byte Byte1;
    public readonly byte Byte2;
    public readonly byte Byte3;
    public readonly byte Byte4;
    public readonly byte Byte5;
    public readonly byte Byte6;
    public readonly byte Byte7;
    public readonly byte Byte8;
    public readonly byte Byte9;
    public readonly byte Byte10;
    public readonly byte Byte11;
    public readonly byte Byte12;
    public readonly byte Byte13;
    public readonly byte Byte14;
    public readonly byte Byte15;
}
