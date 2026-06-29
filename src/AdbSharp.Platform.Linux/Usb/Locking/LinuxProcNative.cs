using System.Runtime.InteropServices;

namespace AdbSharp.Platform.Linux.Usb.Locking;

internal static partial class LinuxProcNative
{
    public const int AtFdcwd = -100;
    public const uint StatxBasicStats = 0x000007ff;

    [StructLayout(LayoutKind.Sequential)]
    public struct StatxTimestamp
    {
        public long Seconds;
        public uint Nanoseconds;
        public int Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Statx
    {
        public uint Mask;
        public uint BlockSize;
        public ulong Attributes;
        public uint LinkCount;
        public uint UserId;
        public uint GroupId;
        public ushort Mode;
        public ushort Spare0;
        public ulong Inode;
        public ulong Size;
        public ulong Blocks;
        public ulong AttributesMask;
        public StatxTimestamp AccessTime;
        public StatxTimestamp BirthTime;
        public StatxTimestamp ChangeTime;
        public StatxTimestamp ModifyTime;
        public uint DeviceIdMajor;
        public uint DeviceIdMinor;
        public uint RDeviceIdMajor;
        public uint RDeviceIdMinor;
        public ulong Spare0Value;
        public ulong Spare1Value;
        public ulong Spare2Value;
        public ulong Spare3Value;
        public ulong Spare4Value;
        public ulong Spare5Value;
        public ulong Spare6Value;
        public ulong Spare7Value;
        public ulong Spare8Value;
        public ulong Spare9Value;
        public ulong Spare10Value;
        public ulong Spare11Value;
        public ulong Spare12Value;
        public ulong Spare13Value;
    }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial int StatxFile(int dirfd, string pathname, int flags, uint mask, out Statx statx);
}
