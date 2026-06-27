using System.Runtime.InteropServices;

namespace AdbSharp.Platform.Mac.Usb.Native;

internal static unsafe class MacUsbInterface
{
    private const int QueryInterfaceIndex = 0;
    private const int ReleaseIndex = 2;
    private const int OpenIndex = 7;
    private const int CloseIndex = 8;
    private const int GetInterfaceClassIndex = 9;
    private const int GetInterfaceSubClassIndex = 10;
    private const int GetInterfaceProtocolIndex = 11;
    private const int GetDeviceVendorIndex = 12;
    private const int GetDeviceProductIndex = 13;
    private const int GetInterfaceNumberIndex = 16;
    private const int GetAlternateSettingIndex = 17;
    private const int GetNumEndpointsIndex = 18;
    private const int GetLocationIdIndex = 19;
    private const int GetPipePropertiesIndex = 25;
    private const int ReadPipeIndex = 30;
    private const int WritePipeIndex = 31;
    private const int ReadPipeToIndex = 38;
    private const int WritePipeToIndex = 39;

    public static int QueryInterface(IntPtr interfacePointer, MacCfUuidBytes interfaceId, out IntPtr result)
    {
        var function = (delegate* unmanaged<IntPtr, MacCfUuidBytes, out IntPtr, int>)GetFunction(interfacePointer, QueryInterfaceIndex);
        return function(interfacePointer, interfaceId, out result);
    }

    public static uint Release(IntPtr interfacePointer)
    {
        var function = (delegate* unmanaged<IntPtr, uint>)GetFunction(interfacePointer, ReleaseIndex);
        return function(interfacePointer);
    }

    public static uint Open(IntPtr interfacePointer)
    {
        var function = (delegate* unmanaged<IntPtr, uint>)GetFunction(interfacePointer, OpenIndex);
        return function(interfacePointer);
    }

    public static uint Close(IntPtr interfacePointer)
    {
        var function = (delegate* unmanaged<IntPtr, uint>)GetFunction(interfacePointer, CloseIndex);
        return function(interfacePointer);
    }

    public static uint GetInterfaceClass(IntPtr interfacePointer, out byte value)
    {
        return GetByte(interfacePointer, GetInterfaceClassIndex, out value);
    }

    public static uint GetInterfaceSubClass(IntPtr interfacePointer, out byte value)
    {
        return GetByte(interfacePointer, GetInterfaceSubClassIndex, out value);
    }

    public static uint GetInterfaceProtocol(IntPtr interfacePointer, out byte value)
    {
        return GetByte(interfacePointer, GetInterfaceProtocolIndex, out value);
    }

    public static uint GetDeviceVendor(IntPtr interfacePointer, out ushort value)
    {
        return GetUShort(interfacePointer, GetDeviceVendorIndex, out value);
    }

    public static uint GetDeviceProduct(IntPtr interfacePointer, out ushort value)
    {
        return GetUShort(interfacePointer, GetDeviceProductIndex, out value);
    }

    public static uint GetInterfaceNumber(IntPtr interfacePointer, out byte value)
    {
        return GetByte(interfacePointer, GetInterfaceNumberIndex, out value);
    }

    public static uint GetAlternateSetting(IntPtr interfacePointer, out byte value)
    {
        return GetByte(interfacePointer, GetAlternateSettingIndex, out value);
    }

    public static uint GetNumEndpoints(IntPtr interfacePointer, out byte value)
    {
        return GetByte(interfacePointer, GetNumEndpointsIndex, out value);
    }

    public static uint GetLocationId(IntPtr interfacePointer, out uint value)
    {
        var function = (delegate* unmanaged<IntPtr, uint*, uint>)GetFunction(interfacePointer, GetLocationIdIndex);
        uint localValue = 0;
        var result = function(interfacePointer, &localValue);
        value = localValue;
        return result;
    }

    public static uint GetPipeProperties(IntPtr interfacePointer, byte pipeReference, out MacUsbPipeProperties properties)
    {
        var function = (delegate* unmanaged<IntPtr, byte, byte*, byte*, byte*, ushort*, byte*, uint>)GetFunction(interfacePointer, GetPipePropertiesIndex);
        byte direction = 0;
        byte number = 0;
        byte transferType = 0;
        ushort maxPacketSize = 0;
        byte interval = 0;
        var result = function(interfacePointer, pipeReference, &direction, &number, &transferType, &maxPacketSize, &interval);
        properties = new MacUsbPipeProperties(pipeReference, direction, number, transferType, maxPacketSize, interval);
        return result;
    }

    public static uint ReadPipe(IntPtr interfacePointer, byte pipeReference, byte[] buffer, uint length, out uint transferred)
    {
        var function = (delegate* unmanaged<IntPtr, byte, byte*, uint*, uint>)GetFunction(interfacePointer, ReadPipeIndex);
        var localLength = length;
        fixed (byte* bufferPointer = buffer)
        {
            var result = function(interfacePointer, pipeReference, bufferPointer, &localLength);
            transferred = localLength;
            return result;
        }
    }

    public static uint WritePipe(IntPtr interfacePointer, byte pipeReference, byte[] buffer, uint length)
    {
        var function = (delegate* unmanaged<IntPtr, byte, byte*, uint, uint>)GetFunction(interfacePointer, WritePipeIndex);
        fixed (byte* bufferPointer = buffer)
        {
            return function(interfacePointer, pipeReference, bufferPointer, length);
        }
    }

    public static uint ReadPipeTo(
        IntPtr interfacePointer,
        byte pipeReference,
        byte[] buffer,
        uint length,
        uint noDataTimeoutMilliseconds,
        uint completionTimeoutMilliseconds,
        out uint transferred)
    {
        var function = (delegate* unmanaged<IntPtr, byte, byte*, uint*, uint, uint, uint>)GetFunction(interfacePointer, ReadPipeToIndex);
        var localLength = length;
        fixed (byte* bufferPointer = buffer)
        {
            var result = function(interfacePointer, pipeReference, bufferPointer, &localLength, noDataTimeoutMilliseconds, completionTimeoutMilliseconds);
            transferred = localLength;
            return result;
        }
    }

    public static uint WritePipeTo(
        IntPtr interfacePointer,
        byte pipeReference,
        byte[] buffer,
        uint length,
        uint noDataTimeoutMilliseconds,
        uint completionTimeoutMilliseconds)
    {
        var function = (delegate* unmanaged<IntPtr, byte, byte*, uint, uint, uint, uint>)GetFunction(interfacePointer, WritePipeToIndex);
        fixed (byte* bufferPointer = buffer)
        {
            return function(interfacePointer, pipeReference, bufferPointer, length, noDataTimeoutMilliseconds, completionTimeoutMilliseconds);
        }
    }

    private static uint GetByte(IntPtr interfacePointer, int index, out byte value)
    {
        var function = (delegate* unmanaged<IntPtr, byte*, uint>)GetFunction(interfacePointer, index);
        byte localValue = 0;
        var result = function(interfacePointer, &localValue);
        value = localValue;
        return result;
    }

    private static uint GetUShort(IntPtr interfacePointer, int index, out ushort value)
    {
        var function = (delegate* unmanaged<IntPtr, ushort*, uint>)GetFunction(interfacePointer, index);
        ushort localValue = 0;
        var result = function(interfacePointer, &localValue);
        value = localValue;
        return result;
    }

    private static IntPtr GetFunction(IntPtr interfacePointer, int index)
    {
        var table = Marshal.ReadIntPtr(interfacePointer);
        return Marshal.ReadIntPtr(table, index * IntPtr.Size);
    }
}
