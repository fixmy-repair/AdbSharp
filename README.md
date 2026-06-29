# AdbSharp

AdbSharp is a native managed .NET implementation of Android Platform Tools
protocols. It talks directly to Android devices over USB or ADB-over-TCP without
invoking `adb`, `fastboot`, `Process.Start`, or requiring the Android SDK.

The library targets .NET 11 Preview and C# preview language features, with
nullable reference types, XML documentation, latest analyzers, and warnings as
errors enabled.

## Status

AdbSharp is pre-release but buildable, test-covered, and NuGet-package ready.
The current implementation includes native USB transports, ADB, Fastboot,
Fastbootd, wireless ADB discovery/pairing, structured diagnostics, and opt-in
hardware validation.

Production code remains split by responsibility:

- `AdbSharp`: facade package
- `AdbSharp.Adb`: ADB client and services
- `AdbSharp.Fastboot`: bootloader Fastboot client
- `AdbSharp.Fastbootd`: userspace Fastboot and unified flashing
- `AdbSharp.Protocol`: packet and protocol codecs
- `AdbSharp.Transport`: transport abstractions and diagnostics
- `AdbSharp.Platform.Windows`: SetupAPI/WinUSB transport
- `AdbSharp.Platform.Linux`: libusb transport
- `AdbSharp.Platform.Mac`: IOKit/IOUSBLib transport, IOUSBHost experimental path
- `AdbSharp.Authentication`: ADB RSA keys and authentication
- `AdbSharp.Common`: shared device, error, and utility types

## Capabilities

### USB Transport

- Shared `IUsbTransport` abstraction with OS-free protocol layers.
- Public `ResetAsync` and `AbortAsync` lifecycle operations.
- Structured diagnostics through `IUsbTransportDiagnostics`.
- Windows SetupAPI discovery and WinUSB bulk I/O with real overlapped transfers.
- Linux libusb discovery, bulk I/O, reset, and kernel-driver detach/reattach handling.
- macOS IOKit discovery and IOUSBLib bulk I/O; IOUSBHost remains experimental.
- AOSP-style hard failure behavior: non-timeout I/O failures abort/kick the transport.
- Zero-length packet writes for packet-aligned USB transfers.
- Packet-size-aware ADB USB read buffering.
- Injectable native adapters for deterministic WinUSB, libusb, IOUSBLib, and IOUSBHost tests.
- Cross-platform USB lock owner resolution for busy or sharing-denied Android interfaces.

### ADB

- `CNXN`, `AUTH`, `OPEN`, `OKAY`, `WRTE`, `CLSE`, and `STLS` packet support.
- Packet serialization, checksum/magic validation, stream IDs, stream multiplexing.
- RSA key load/create/sign authentication.
- Shell, shell v2, exec, reboot, recovery, bootloader commands.
- File sync stat/list/push/pull with v2 fallback.
- Brotli, LZ4, and Zstd compressed send/recv v2 transfers when advertised.
- Logcat streaming.
- Local TCP port forwarding and reverse-forward service commands.
- Package helpers, split APK install sessions, and staged installs.
- Device properties.
- Direct TCP/wireless ADB connect to already-listening `adbd` endpoints.
- Managed mDNS discovery for `_adb._tcp`, `_adb-tls-pairing._tcp`, and `_adb-tls-connect._tcp`.
- Android 11+ wireless pairing with TLS keying-material export, SPAKE2, and AES-GCM peer-info encryption.

### Fastboot

- Command/response framing for `OKAY`, `FAIL`, `DATA`, `INFO`, and `TEXT`.
- `getvar`, integer variable helpers, `download`, `flash`, `erase`, `boot`,
  `continue`, reboot variants, OEM commands, flashing lock/unlock commands.
- Sparse image validation and flashing.
- Large transfer chunking, pooled buffers, progress, and cancellation.
- Streaming upload/fetch where supported.
- Logical partition, super metadata, and snapshot update commands.

### Fastbootd

- Userspace Fastboot capability probing with `getvar:is-userspace`.
- Dynamic/logical partition operations.
- Super partition metadata operations.
- Snapshot and Virtual A/B capability detection.
- Unified flashing that transitions from bootloader fastboot to fastbootd for
  logical partition operations when needed.

## Requirements

- .NET SDK 11 Preview. This workspace has been validated with
  `11.0.100-preview.5`.
- Windows: WinUSB-compatible Android driver exposing the Android device-interface GUID.
- Linux: `libusb-1.0` and udev permissions for the device vendor id.
- macOS: no Android SDK requirement; exclusive USB interface ownership still applies.

See [docs/PlatformSetup.md](docs/PlatformSetup.md) for host setup details.

## Build

```sh
dotnet restore AdbSharp.sln
dotnet build AdbSharp.sln -warnaserror
dotnet test AdbSharp.sln
dotnet pack AdbSharp.sln
```

Hardware tests are opt-in:

```sh
ADBSHARP_HARDWARE_TESTS=1 \
dotnet test tests/AdbSharp.IntegrationTests/AdbSharp.IntegrationTests.csproj
```

See [docs/HardwareValidation.md](docs/HardwareValidation.md) for host-specific
hardware validation commands and known vendor/driver notes.

## Basic Usage

### Device Discovery

```csharp
var devices = await DeviceManager.FindAsync();
foreach (var device in devices)
{
    Console.WriteLine($"{device.Identity.SerialNumber ?? device.Identity.TransportId}\t{device.Mode}");
}
```

### ADB

```csharp
await using var adb = await AdbClient.ConnectAsync(device);

Console.Write(await adb.ShellAsync("getprop ro.product.model"));

var result = await adb.ShellV2Async("cmd package path android");
Console.Write(result.StandardOutput);

await using var source = File.OpenRead("local.txt");
await adb.PushAsync(source, "/data/local/tmp/local.txt");

await using var destination = File.Create("pulled.txt");
await adb.PullAsync("/data/local/tmp/local.txt", destination);

await adb.RebootBootloaderAsync();
```

### Port Forwarding

```csharp
await using var forward = await adb.StartPortForwardAsync(
    localPort: 0,
    remote: AdbSocketSpec.Tcp(8080));

Console.WriteLine(forward.LocalPort);
```

### Split APK Or Staged Install

```csharp
await using var baseApk = File.OpenRead("base.apk");
await using var splitConfig = File.OpenRead("split_config.arm64_v8a.apk");

var install = await adb.InstallPackagesAsync(
    [
        new AdbPackageFile(baseApk, "base.apk"),
        new AdbPackageFile(splitConfig, "split_config.arm64_v8a.apk")
    ],
    new AdbInstallOptions { Staged = false });

Console.WriteLine(install.Output);
```

### Wireless ADB

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

Pairing-code setup is also native managed code:

```csharp
using var keyPair = await AdbKeyStore.LoadOrCreateAsync(
    Path.Combine(home, ".android", "adbkey"));

var pairingService = (await AdbMdnsDiscovery.FindAsync(new AdbMdnsDiscoveryOptions
{
    ServiceKinds = [AdbMdnsServiceKind.Pairing]
})).First();

var result = await AdbPairingClient.PairAsync(
    pairingService,
    pairingCode,
    keyPair);
```

### Fastboot

```csharp
await using var fastboot = await FastbootClient.ConnectAsync(device);

var product = await fastboot.GetVarAsync("product");
var maxDownload = await fastboot.GetMaxDownloadSizeAsync();

await using var bootImage = File.OpenRead("boot.img");
await fastboot.FlashPartitionAsync("boot", bootImage);

await fastboot.RebootAsync();
```

### Fastbootd And Unified Flashing

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

## Sample Console

```sh
dotnet run --project samples/AdbSharp.Console -- usb-diagnostics
dotnet run --project samples/AdbSharp.Console -- usb-lock-owners
dotnet run --project samples/AdbSharp.Console -- usb-open-diagnostics
dotnet run --project samples/AdbSharp.Console -- shell getprop ro.product.model
dotnet run --project samples/AdbSharp.Console -- mdns
dotnet run --project samples/AdbSharp.Console -- fb-getvar product
```

The sample is intentionally thin; it exercises the same public APIs exposed by
the library.

## Diagnostics

Use structured diagnostics when debugging discovery or native USB behavior:

```csharp
DefaultTransportProvider.RegisterBuiltInTransports();
var result = await UsbTransportRegistry.FindWithDiagnosticsAsync();

foreach (var device in result.Devices)
{
    Console.WriteLine(device.TransportId);
}

foreach (var issue in result.Issues)
{
    Console.WriteLine($"{issue.EnumeratorName}: {issue.Error} {issue.Message}");
}
```

Opened transports that implement `IUsbTransportDiagnostics` expose backend,
transport id, endpoint metadata, native state, and last-error fields without
private reflection.

When a USB interface is busy or sharing is denied, applications can opt into
lock owner resolution without changing the low-level transport factory contract:

```csharp
var resolution = await UsbDeviceLockOwnerResolverRegistry.ResolveAsync(device.Usb);
foreach (var owner in resolution.Owners)
{
    Console.WriteLine($"{owner.ProcessId}\t{owner.ProcessName}\t{owner.Confidence}");
}
```

High-level ADB/Fastboot clients keep the default no-policy open behavior unless
`LockConflictHandling` is set. Graceful ADB server release uses the local ADB
server protocol directly and never launches external tools.

## Documentation

- [Architecture](docs/Architecture.md)
- [ADB](docs/ADB.md)
- [Fastboot](docs/Fastboot.md)
- [Fastbootd](docs/Fastbootd.md)
- [Authentication](docs/Authentication.md)
- [Examples](docs/Examples.md)
- [Platform Setup](docs/PlatformSetup.md)
- [Hardware Validation](docs/HardwareValidation.md)
- [USB Source Parity](docs/USBSourceParity.md)
- [Device Lock Owner Resolver](docs/DeviceLockOwnerResolver.md)
- [NOTICE](docs/NOTICE.md)

## Non-goals And Compliance

- AdbSharp must never invoke `adb`, `fastboot`, `adb.exe`, `fastboot.exe`, or
  Android SDK tools.
- Source and tests include a static guard for `Process.Start`, `adb.exe`, and
  `fastboot.exe`.
- AOSP is treated as protocol and behavior reference material only. Significant
  AOSP implementation code is not copied.

## License

AdbSharp is licensed under the MIT license. See [docs/NOTICE.md](docs/NOTICE.md)
for AOSP reference and licensing notes.
