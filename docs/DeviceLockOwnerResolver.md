# Device Lock Owner Resolver

AdbSharp can optionally identify host processes that may be holding an Android
USB interface when opening fails with `Busy`, `ExclusiveAccess`, or
`PermissionDenied`.

This feature is intentionally separate from the core transport factories. Normal
USB opens still fail fast. Applications decide whether to prompt the user,
retry, gracefully release an ADB server, terminate a process, or ignore the
conflict.

## Public API

- `IUsbDeviceLockOwnerResolver` resolves owners for a `UsbDeviceDescriptor`.
- `UsbDeviceLockOwnerResolverRegistry` stores platform resolvers.
- `UsbDeviceLockOwnerResolution` reports status, platform path, message, and owners.
- `UsbDeviceLockOwner` reports process name, PID, executable path, owner kind,
  confidence, and evidence.
- `IUsbDeviceLockOwnerReleaser` releases a selected owner.
- `UsbDeviceLockOwnerReleaser.Default` supports graceful ADB server release and
  explicit PID termination.
- `UsbDeviceLockConflictOptions` enables opt-in conflict handling for
  `AdbClientOptions` and `FastbootClientOptions`.

Example owner reporting:

```csharp
DefaultTransportProvider.RegisterBuiltInTransports();

var resolution = await UsbDeviceLockOwnerResolverRegistry.ResolveAsync(device.Usb);
foreach (var owner in resolution.Owners)
{
    Console.WriteLine(
        $"{owner.ProcessId}\t{owner.ProcessName}\t{owner.Kind}\t{owner.Confidence}\t{owner.ExecutablePath}");
}
```

Example opt-in ADB open retry after graceful ADB server release:

```csharp
await using var adb = await AdbClient.ConnectAsync(
    device,
    new AdbClientOptions
    {
        LockConflictHandling = new UsbDeviceLockConflictOptions
        {
            ResolveOwners = true,
            ReleaseAdbServer = true,
            RetryAfterRelease = true
        }
    });
```

Forced process termination is never used by automatic open retry. Callers that
want it must call `IUsbDeviceLockOwnerReleaser.ReleaseAsync` with
`AllowProcessTermination = true` for a specific owner.

## Platform Behavior

Windows uses system handle enumeration for the decoded WinUSB device interface
path. Exact path matches return `Confidence.Exact`. Processes that cannot be
inspected due to host permissions produce partial results instead of failing the
whole resolution.

Linux resolves the libusb transport id to `/dev/bus/usb/<bus>/<device>` and
scans `/proc/<pid>/fd`. It matches fd symlink targets directly and uses `statx`
device/inode identity when available.

macOS uses best-effort process detection through `libproc`. It focuses on known
Android tooling processes such as ADB, Fastboot, Android Studio, and scrcpy.
macOS results are intentionally not marked exact because the platform does not
provide the same stable public interface-owner mapping.

## Release Behavior

Graceful ADB release talks to the local ADB server protocol on `127.0.0.1:5037`
and sends `host:kill`. It does not launch external tools and does not require
the Android SDK.

Process termination is explicit and PID-based. The library exposes it so UI and
CLI applications can build a confirmation flow, but AdbSharp does not terminate
processes as part of default connection behavior.

## Sample Commands

```sh
dotnet run --project samples/AdbSharp.Console -- usb-lock-owners
dotnet run --project samples/AdbSharp.Console -- usb-open-diagnostics
```

`usb-open-diagnostics` prints lock owner details only after lock-like open
failures. `usb-lock-owners` reports likely owners for discovered devices without
opening, releasing, or terminating anything.
