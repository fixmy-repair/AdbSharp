# AdbSharp

AdbSharp is a native managed .NET implementation of Android Platform Tools protocols. It is designed to communicate directly with Android devices over USB or ADB-over-TCP without invoking `adb`, `fastboot`, or requiring the Android SDK.

## Status

This repository contains the first buildable foundation milestone:

- solution and project layout for ADB, Fastboot, Fastbootd, protocol, transport, platform, authentication, tests, and samples
- OS-isolated USB transport contracts and platform provider boundaries
- Linux libusb discovery/open/read/write for ADB and Fastboot bulk interfaces
- Windows SetupAPI/WinUSB discovery/open/read/write for Google Android WinUSB interfaces
- macOS IOKit/IOUSBLib discovery/open/read/write for Android bulk interfaces
- platform-neutral USB error classification for permission, detach, stall, timeout, busy, and exclusive-access failures
- diagnostic USB discovery through `UsbTransportRegistry.FindWithDiagnosticsAsync`
- endpoint metadata validation before ADB or Fastboot clients use an opened transport
- timeout-aware native transfer loops for cancellation-friendly USB I/O
- ADB packet serialization, checksum/magic validation, authentication hooks, `STLS` upgrade for wireless/TCP transports, managed mDNS discovery for `_adb._tcp`, `_adb-tls-pairing._tcp`, and `_adb-tls-connect._tcp`, Android wireless pairing with managed TLS exporter support, SPAKE2, AES-GCM peer-info encryption, injectable secure backend support, hardware compatibility validation reports for Android 11+ pairing endpoints, stream multiplexing, direct TCP/wireless connect to already-listening `adbd` endpoints, shell/exec/reboot, shell v2 result parsing, logcat streaming, local TCP port forwarding, reverse-forward service commands, package helpers, install sessions for split APKs and staged installs, and pooled-buffer file sync stat/list/push/pull with v2 feature fallback plus advertised Brotli/LZ4/Zstd sendrecv compression
- Fastboot command/response framing, span-based command encoding, `getvar`, integer variables, pooled-buffer `download`, flash, sparse image validation/flash, erase, boot, reboot, streaming upload/fetch, logical partition commands, super metadata commands, snapshot update commands, flashing lock/unlock commands, progress, and cancellation
- Fastbootd userspace wrapper with capability probing, logical partition and super partition operations, and unified flash routing for logical partitions
- generated regular expression implementations for static compliance checks
- mock transport tests, performance-oriented codec tests, and optional hardware integration test gate

The native Windows/Linux/macOS USB backends are intentionally isolated behind platform projects. All three platforms now expose native USB discovery and bulk transport implementations behind the shared transport contracts.

## Target

- .NET 11 Preview
- C# preview language version
- nullable reference types
- implicit usings
- XML documentation for public APIs
- latest .NET analyzers
- warnings as errors

This workspace has been validated with .NET SDK 11.0.100-preview.5.

## Basic Usage

```csharp
var devices = await DeviceManager.FindAsync();
foreach (var device in devices)
{
    Console.WriteLine($"{device.Identity.SerialNumber}: {device.Mode}");
}
```

```csharp
await using var adb = await AdbClient.ConnectAsync(device);
var props = await adb.ShellAsync("getprop");
var result = await adb.ShellV2Async("cmd package path android");
var stat = await adb.StatAsync("/data/local/tmp/file.txt");
await using var forward = await adb.StartPortForwardAsync(0, AdbSocketSpec.Tcp(8080));
await adb.RebootBootloaderAsync();
```

```csharp
var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
using var authenticator = new AdbRsaAuthenticator(
    await AdbKeyStore.LoadOrCreateAsync(Path.Combine(home, ".android", "adbkey")));
var connectService = (await AdbMdnsDiscovery.FindAsync())
    .First(service => service.Kind == AdbMdnsServiceKind.Connect);
await using var wireless = await AdbClient.ConnectWirelessAsync(
    connectService,
    options: new AdbClientOptions { Authenticator = authenticator });
Console.Write(await wireless.ShellAsync("getprop ro.product.model"));
```

```csharp
await using var fastboot = await FastbootClient.ConnectAsync(device);
var product = await fastboot.GetVarAsync("product");
var maxDownload = await fastboot.GetMaxDownloadSizeAsync();
await fastboot.FlashPartitionAsync("boot", imageStream);
await fastboot.RebootAsync();
```

```csharp
await using var fastbootd = await FastbootdClient.ConnectAsync(device);
Console.WriteLine(fastbootd.Capabilities.SuperPartitionName);
await fastbootd.ResizeLogicalPartitionAsync("system_a", 2L * 1024 * 1024 * 1024);
```

```csharp
await using var image = File.OpenRead("system.img");
var result = await UnifiedFastbootFlasher.FlashPartitionAsync(device, "system_a", image);
Console.WriteLine(result.UsedFastbootd ? "fastbootd" : "fastboot");
```

## Platform Notes

- Linux requires `libusb-1.0` at runtime and host permissions to claim the Android USB interface.
- Windows requires a WinUSB-compatible Android driver exposing the Android device-interface GUID used by Google's USB driver.
- macOS uses IOKit registry discovery and the IOUSBLib interface user client for native bulk I/O.
- `UsbTransportValidator` verifies that opened transports expose a bulk IN and bulk OUT endpoint pair before protocol handshakes start.
- Wireless ADB can discover Android debugging endpoints with managed mDNS/DNS-SD and then connect over a managed TCP socket. If the device requests `STLS`, AdbSharp upgrades the socket with managed TLS and presents the configured ADB host-key certificate. Android 11+ pairing-code setup is handled by `AdbPairingClient.PairAsync`, which uses the built-in managed pairing backend by default and never shells out to Android SDK tools.

See [PlatformSetup.md](docs/PlatformSetup.md) for host USB permissions, driver setup, and opt-in hardware test details.

## Non-goals

AdbSharp must never call external Android tools. Source and tests include a static guard for `Process.Start`, `adb.exe`, and `fastboot.exe`.
