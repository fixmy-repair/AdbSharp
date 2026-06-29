namespace AdbSharp.Platform.Linux.Usb.Locking;

internal readonly record struct LinuxFileIdentity(uint DeviceMajor, uint DeviceMinor, ulong Inode);
