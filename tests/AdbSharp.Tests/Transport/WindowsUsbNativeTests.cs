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
}
