# Hardware Validation

AdbSharp hardware validation is opt-in and safe by default. It exercises direct
USB communication only; it does not invoke `adb`, `fastboot`, or Android SDK
tools.

## Test Gates

Set `ADBSHARP_HARDWARE_TESTS=1` to enable attached-device tests:

```sh
ADBSHARP_HARDWARE_TESTS=1 \
dotnet test tests/AdbSharp.IntegrationTests/AdbSharp.IntegrationTests.csproj
```

When no matching device is attached, hardware tests return without failing.

## Current Validation Matrix

| Host | Device Mode | Validation |
| --- | --- | --- |
| macOS | ADB | `getprop ro.product.model`, small push/pull roundtrip, open/abort/rediscover/open |
| Windows | ADB | `getprop ro.product.model`, small push/pull roundtrip, open/abort/rediscover/open |
| Linux | ADB | `getprop ro.product.model`, small push/pull roundtrip, open/abort/rediscover/open |
| Any | Fastboot or bootloader | `getvar product` |
| Any | Fastbootd | `getvar is-userspace` when a userspace fastboot device is attached |

Use a filter when validating one scenario:

```sh
ADBSHARP_HARDWARE_TESTS=1 \
dotnet test tests/AdbSharp.IntegrationTests/AdbSharp.IntegrationTests.csproj \
  --filter "FullyQualifiedName~HardwareDiscoveryTests"
```

## Manual Sample Checks

ADB shell:

```sh
dotnet run --project samples/AdbSharp.Console -- shell getprop ro.product.model
```

ADB push/pull can be validated with the integration test roundtrip. Fastboot
product can be checked manually when a device is in bootloader/fastboot mode:

```sh
dotnet run --project samples/AdbSharp.Console -- fb-getvar product
```

USB diagnostics:

```sh
dotnet run --project samples/AdbSharp.Console -- usb-diagnostics
dotnet run --project samples/AdbSharp.Console -- usb-lock-owners
dotnet run --project samples/AdbSharp.Console -- usb-open-diagnostics
```

## Known Host And Vendor Notes

- Samsung devices on macOS may expose multiple vendor-specific interfaces. Use
  `usb-open-diagnostics` to confirm the selected interface has one bulk-in and
  one bulk-out endpoint.
- Android authorization prompts can race the first ADB handshake. Authorize the
  host key on the device, then reconnect or rerun the validation.
- Windows devices must bind the Android interface to a WinUSB-compatible driver
  and expose the Android device-interface GUID. Driver conflicts usually surface
  as `PermissionDenied`, `ExclusiveAccess`, or `DeviceNotFound`.
- Linux requires `libusb-1.0` and udev permissions for the device vendor id.
  Missing rules usually surface as `PermissionDenied`.
- Only one process can own a USB interface at a time. Close other ADB clients,
  IDE device managers, or platform tools before validation.
- If a lock-like open failure occurs, run `usb-lock-owners` to capture process
  name, PID, executable path, confidence, and resolver status.
- Some devices disconnect and reconnect after USB reset or fastboot mode
  transitions. Tests rediscover the device after abort/reset paths instead of
  reusing stale handles.

## Reporting Results

Record:

- Host OS and version.
- Device vendor, model, Android version, and build fingerprint when available.
- Device mode: ADB, recovery, bootloader fastboot, or fastbootd.
- Transport backend from `usb-open-diagnostics`.
- Lock owner resolver status and owner rows from `usb-lock-owners` when a device
  reports `Busy`, `ExclusiveAccess`, or `PermissionDenied`.
- Any `UsbTransportException.Error` value and native diagnostic fields.
