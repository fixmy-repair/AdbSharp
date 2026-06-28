using System.Reflection;
using AdbSharp.Platform.Windows.Usb.Native;

namespace AdbSharp.Tests.Transport;

public sealed class WindowsUsbNativeTests
{
    [Fact]
    public void CreateFileFlagsIncludeOverlappedBit()
    {
        const uint expected = WindowsUsbNative.FileAttributeNormal | WindowsUsbNative.FileFlagOverlapped;

        Assert.Equal(0x40000080u, expected);
    }

    [Fact]
    public void WinUsbTransfersExposeOverlappedParameter()
    {
        var read = typeof(WindowsUsbNative).GetMethod(
            nameof(WindowsUsbNative.WinUsbReadPipe),
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var write = typeof(WindowsUsbNative).GetMethod(
            nameof(WindowsUsbNative.WinUsbWritePipe),
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(read);
        Assert.NotNull(write);
        Assert.Equal(typeof(IntPtr), read.GetParameters()[5].ParameterType);
        Assert.Equal(typeof(IntPtr), write.GetParameters()[5].ParameterType);
    }

    [Fact]
    public void ErrorIoPendingMatchesWindowsOverlappedCompletionCode()
    {
        Assert.Equal(997, WindowsUsbNative.ErrorIoPending);
    }
}
