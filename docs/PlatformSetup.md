# Platform Setup

AdbSharp talks directly to Android USB interfaces. It does not invoke Android SDK tools, so host USB permissions and drivers must be configured for the current platform.

## Linux

- Install `libusb-1.0`.
- Grant the current user access to Android vendor-specific USB interfaces with udev rules.
- Reload udev rules and reconnect the device after changing permissions.
- If another process has claimed the interface, AdbSharp may report `UsbTransportError.Busy` or `UsbTransportError.ExclusiveAccess`.

Example udev rule shape:

```text
SUBSYSTEM=="usb", ATTR{idVendor}=="18d1", MODE="0660", GROUP="plugdev", TAG+="uaccess"
```

Use the real vendor id for non-Google devices.

## Windows

- Install a WinUSB-compatible Android driver for the device interface.
- The interface must expose the Android device-interface GUID used by Google's USB driver.
- Driver or sharing failures are reported through `UsbTransportException.Error`, commonly `PermissionDenied`, `DeviceNotFound`, `DeviceDisconnected`, `Timeout`, `Busy`, or `ExclusiveAccess`.

## macOS

- AdbSharp uses IOKit discovery and IOUSBLib user clients directly.
- Only one process can hold exclusive access to a USB interface at a time.
- Disconnect tools that may already own the interface before opening the device with AdbSharp.
- Permission, exclusive-access, timeout, and detach failures are reported through `UsbTransportException.Error`.

## Hardware Tests

Hardware integration tests are opt-in:

```sh
ADBSHARP_HARDWARE_TESTS=1 dotnet test tests/AdbSharp.IntegrationTests/AdbSharp.IntegrationTests.csproj
```

The hardware tests cover safe paths only: discovery, opened-transport endpoint validation, ADB `getprop`, a small `/data/local/tmp` push/pull roundtrip, Fastboot `getvar:product`, and Fastbootd `getvar:is-userspace` when matching devices are attached.

See `docs/HardwareValidation.md` for host-specific validation commands and
known vendor/driver notes.

For host setup troubleshooting, use diagnostic discovery instead of the fail-fast device manager:

```csharp
DefaultTransportProvider.RegisterBuiltInTransports();
var result = await UsbTransportRegistry.FindWithDiagnosticsAsync();
```

This returns descriptors from successful platform enumerators and `UsbDiscoveryIssue` records for failed enumerators.

## Wireless ADB

Wireless ADB uses a managed TCP socket and does not need USB permissions once `adbd` is already listening on the network. The default port is 5555:

```csharp
var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
using var authenticator = new AdbRsaAuthenticator(
    await AdbKeyStore.LoadOrCreateAsync(Path.Combine(home, ".android", "adbkey")));
await using var adb = await AdbClient.ConnectTcpAsync(
    "192.168.1.42",
    options: new AdbClientOptions { Authenticator = authenticator });
Console.Write(await adb.ShellAsync("getprop"));
```

`ConnectTcpAsync` and `ConnectWirelessAsync` run the ADB `CNXN`/`AUTH` handshake directly over the socket. If the device requests `STLS`, the socket is upgraded to managed TLS and the configured ADB host-key certificate is presented.

AdbSharp can discover wireless debugging endpoints through mDNS without invoking the ADB server:

```csharp
var services = await AdbMdnsDiscovery.FindAsync();
foreach (var service in services)
{
    Console.WriteLine($"{service.Kind}\t{service.InstanceName}\t{service.Host}:{service.Port}");
}
```

The Android service kinds are `_adb._tcp`, `_adb-tls-pairing._tcp`, and `_adb-tls-connect._tcp`. The sample console exposes the same discovery path:

```sh
dotnet run --project samples/AdbSharp.Console -- mdns
dotnet run --project samples/AdbSharp.Console -- mdns pairing
dotnet run --project samples/AdbSharp.Console -- mdns connect
```

`AdbPairingClient.PairAsync` provides the public pairing entry point and implements the AOSP pairing packet exchange through the built-in managed pairing backend:

```csharp
using var keyPair = await AdbKeyStore.LoadOrCreateAsync(Path.Combine(home, ".android", "adbkey"));
var pairingService = (await AdbMdnsDiscovery.FindAsync(new AdbMdnsDiscoveryOptions
{
    ServiceKinds = [AdbMdnsServiceKind.Pairing]
})).First();

var result = await AdbPairingClient.PairAsync(
    pairingService,
    pairingCode,
    keyPair);
```

The backend performs the TLS keying-material export, SPAKE2 exchange, and AES-GCM peer-info encryption required by Android 11+ wireless debugging. After pairing succeeds, applications can call `ConnectTcpAsync` or `ConnectWirelessAsync` against the ADB TCP endpoint shown by the device.

## Wireless Pairing Compatibility Validation

Android pairing codes are single-use and expire quickly. For hardware compatibility validation, put each device on the Android 11+ wireless debugging "Pair with code" screen, collect the fresh pairing host, pairing port, and code, then run the opt-in matrix before the codes expire. The ADB TCP port is the separate port shown on the main wireless debugging screen.

```json
{
  "keyPath": "~/.android/adbkey",
  "publicKeyComment": "AdbSharp lab",
  "verifyAdbConnection": true,
  "defaultAdbPort": 5555,
  "minimumVendorCount": 2,
  "endpoints": [
    {
      "vendor": "Google",
      "model": "Pixel 8",
      "host": "192.168.1.42",
      "pairingPort": 37123,
      "pairingCode": "123456",
      "adbPort": 39211,
      "expectedManufacturer": "Google",
      "expectedModel": "Pixel 8"
    },
    {
      "vendor": "Samsung",
      "model": "Galaxy S24",
      "host": "192.168.1.43",
      "pairingPort": 41234,
      "pairingCode": "654321",
      "adbPort": 38999,
      "expectedManufacturer": "samsung"
    }
  ]
}
```

Run the hardware integration matrix:

```sh
ADBSHARP_HARDWARE_TESTS=1 \
ADBSHARP_PAIRING_MATRIX=pairing-matrix.json \
ADBSHARP_PAIRING_MIN_VENDORS=2 \
dotnet test tests/AdbSharp.IntegrationTests/AdbSharp.IntegrationTests.csproj
```

For a single endpoint, the test also accepts `ADBSHARP_PAIRING_HOST`, `ADBSHARP_PAIRING_PORT`, `ADBSHARP_PAIRING_CODE`, `ADBSHARP_PAIRING_VENDOR`, `ADBSHARP_PAIRING_MODEL`, `ADBSHARP_PAIRING_ADB_PORT`, `ADBSHARP_PAIRING_EXPECTED_MANUFACTURER`, and `ADBSHARP_PAIRING_EXPECTED_MODEL`.

The sample console can produce a JSON compatibility report without printing pairing codes:

```sh
dotnet run --project samples/AdbSharp.Console -- pairing-compat pairing-matrix.json
```
