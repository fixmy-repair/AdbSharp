using System.Runtime.InteropServices;
using AdbSharp.Transport.Usb;

namespace AdbSharp.Platform.Mac.Usb.Native;

internal static unsafe class MacUsbInterface
{
    private const int QueryInterfaceIndex = 1;
    private const int ReleaseIndex = 3;
    private const int CreateInterfaceAsyncEventSourceIndex = 4;
    private const int OpenIndex = 8;
    private const int CloseIndex = 9;
    private const int GetInterfaceClassIndex = 10;
    private const int GetInterfaceSubClassIndex = 11;
    private const int GetInterfaceProtocolIndex = 12;
    private const int GetDeviceVendorIndex = 13;
    private const int GetDeviceProductIndex = 14;
    private const int GetInterfaceNumberIndex = 17;
    private const int GetAlternateSettingIndex = 18;
    private const int GetNumEndpointsIndex = 19;
    private const int GetLocationIdIndex = 20;
    private const int SetAlternateInterfaceIndex = 22;
    private const int GetPipePropertiesIndex = 26;
    private const int GetPipeStatusIndex = 27;
    private const int AbortPipeIndex = 28;
    private const int ResetPipeIndex = 29;
    private const int ClearPipeStallIndex = 30;
    private const int ReadPipeIndex = 31;
    private const int WritePipeIndex = 32;
    private const int ReadPipeAsyncIndex = 33;
    private const int ReadPipeToIndex = 39;
    private const int WritePipeToIndex = 40;
    private const int ReadPipeAsyncToIndex = 41;
    private const int OpenSeizeIndex = 44;
    private const int ClearPipeStallBothEndsIndex = 45;
    private const int GetPipePropertiesV3Index = 60;
    private const int GetEndpointPropertiesV3Index = 61;

    public static int QueryInterface(IntPtr interfacePointer, MacCfUuidBytes interfaceId, out IntPtr result)
    {
        var function = (delegate* unmanaged<IntPtr, MacCfUuidBytes, IntPtr*, int>)GetFunction(interfacePointer, QueryInterfaceIndex);
        var localResult = IntPtr.Zero;
        var status = function(interfacePointer, interfaceId, &localResult);
        result = localResult;
        return status;
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

    public static uint OpenSeize(IntPtr interfacePointer)
    {
        var function = (delegate* unmanaged<IntPtr, uint>)GetFunction(interfacePointer, OpenSeizeIndex);
        return function(interfacePointer);
    }

    public static uint Close(IntPtr interfacePointer)
    {
        var function = (delegate* unmanaged<IntPtr, uint>)GetFunction(interfacePointer, CloseIndex);
        return function(interfacePointer);
    }

    public static uint CreateInterfaceAsyncEventSource(IntPtr interfacePointer, out IntPtr source)
    {
        var function = (delegate* unmanaged<IntPtr, IntPtr*, uint>)GetFunction(interfacePointer, CreateInterfaceAsyncEventSourceIndex);
        var localSource = IntPtr.Zero;
        var result = function(interfacePointer, &localSource);
        source = localSource;
        return result;
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

    public static uint SetAlternateInterface(IntPtr interfacePointer, byte alternateSetting)
    {
        var function = (delegate* unmanaged<IntPtr, byte, uint>)GetFunction(interfacePointer, SetAlternateInterfaceIndex);
        return function(interfacePointer, alternateSetting);
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

    public static uint GetPipeStatus(IntPtr interfacePointer, byte pipeReference)
    {
        var function = (delegate* unmanaged<IntPtr, byte, uint>)GetFunction(interfacePointer, GetPipeStatusIndex);
        return function(interfacePointer, pipeReference);
    }

    public static uint GetPipePropertiesV3(IntPtr interfacePointer, byte pipeReference, ref MacUsbEndpointProperties properties)
    {
        var function = (delegate* unmanaged<IntPtr, byte, MacUsbEndpointProperties*, uint>)GetFunction(interfacePointer, GetPipePropertiesV3Index);
        var localProperties = properties;
        var result = function(interfacePointer, pipeReference, &localProperties);
        properties = localProperties;
        return result;
    }

    public static uint GetEndpointPropertiesV3(IntPtr interfacePointer, ref MacUsbEndpointProperties properties)
    {
        var function = (delegate* unmanaged<IntPtr, MacUsbEndpointProperties*, uint>)GetFunction(interfacePointer, GetEndpointPropertiesV3Index);
        var localProperties = properties;
        var result = function(interfacePointer, &localProperties);
        properties = localProperties;
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

    public static uint ReadPipe(IntPtr interfacePointer, byte pipeReference, IntPtr buffer, uint length, out uint transferred)
    {
        var function = (delegate* unmanaged<IntPtr, byte, void*, uint*, uint>)GetFunction(interfacePointer, ReadPipeIndex);
        var localLength = length;
        var result = function(interfacePointer, pipeReference, (void*)buffer, &localLength);
        transferred = localLength;
        return result;
    }

    public static uint ReadPipeAsync(
        IntPtr interfacePointer,
        byte pipeReference,
        IntPtr buffer,
        uint length,
        delegate* unmanaged<IntPtr, uint, IntPtr, void> callback,
        IntPtr refcon)
    {
        var function = (delegate* unmanaged<IntPtr, byte, void*, uint, delegate* unmanaged<IntPtr, uint, IntPtr, void>, IntPtr, uint>)GetFunction(interfacePointer, ReadPipeAsyncIndex);
        return function(interfacePointer, pipeReference, (void*)buffer, length, callback, refcon);
    }

    public static uint AbortPipe(IntPtr interfacePointer, byte pipeReference)
    {
        var function = (delegate* unmanaged<IntPtr, byte, uint>)GetFunction(interfacePointer, AbortPipeIndex);
        return function(interfacePointer, pipeReference);
    }

    public static uint ResetPipe(IntPtr interfacePointer, byte pipeReference)
    {
        var function = (delegate* unmanaged<IntPtr, byte, uint>)GetFunction(interfacePointer, ResetPipeIndex);
        return function(interfacePointer, pipeReference);
    }

    public static uint ClearPipeStall(IntPtr interfacePointer, byte pipeReference)
    {
        var function = (delegate* unmanaged<IntPtr, byte, uint>)GetFunction(interfacePointer, ClearPipeStallIndex);
        return function(interfacePointer, pipeReference);
    }

    public static uint ClearPipeStallBothEnds(IntPtr interfacePointer, byte pipeReference)
    {
        var function = (delegate* unmanaged<IntPtr, byte, uint>)GetFunction(interfacePointer, ClearPipeStallBothEndsIndex);
        return function(interfacePointer, pipeReference);
    }

    public static uint WritePipe(IntPtr interfacePointer, byte pipeReference, byte[] buffer, uint length)
    {
        var function = (delegate* unmanaged<IntPtr, byte, byte*, uint, uint>)GetFunction(interfacePointer, WritePipeIndex);
        fixed (byte* bufferPointer = buffer)
        {
            return function(interfacePointer, pipeReference, bufferPointer, length);
        }
    }

    public static uint WritePipe(IntPtr interfacePointer, byte pipeReference, IntPtr buffer, uint length)
    {
        var function = (delegate* unmanaged<IntPtr, byte, void*, uint, uint>)GetFunction(interfacePointer, WritePipeIndex);
        return function(interfacePointer, pipeReference, (void*)buffer, length);
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

    public static uint ReadPipeAsyncTo(
        IntPtr interfacePointer,
        byte pipeReference,
        IntPtr buffer,
        uint length,
        uint noDataTimeoutMilliseconds,
        uint completionTimeoutMilliseconds,
        delegate* unmanaged<IntPtr, uint, IntPtr, void> callback,
        IntPtr refcon)
    {
        var function = (delegate* unmanaged<IntPtr, byte, void*, uint, uint, uint, delegate* unmanaged<IntPtr, uint, IntPtr, void>, IntPtr, uint>)GetFunction(interfacePointer, ReadPipeAsyncToIndex);
        return function(interfacePointer, pipeReference, (void*)buffer, length, noDataTimeoutMilliseconds, completionTimeoutMilliseconds, callback, refcon);
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
        if (interfacePointer == IntPtr.Zero)
        {
            throw new UsbTransportException("macOS USB interface pointer is null.");
        }

        var table = Marshal.ReadIntPtr(interfacePointer);
        if (table == IntPtr.Zero)
        {
            throw new UsbTransportException("macOS USB interface function table is null.");
        }

        var function = Marshal.ReadIntPtr(table, index * IntPtr.Size);
        if (function == IntPtr.Zero)
        {
            throw new UsbTransportException($"macOS USB interface function at vtable index {index} is null.");
        }

        return function;
    }
}
