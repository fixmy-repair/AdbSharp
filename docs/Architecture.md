# Architecture

AdbSharp is split by responsibility:

```text
Application
  -> Public API
  -> Protocol Layer
  -> Transport Layer
  -> Operating System
```

Protocol projects do not reference platform projects. USB access is provided through `IUsbDeviceEnumerator`, `IUsbTransportFactory`, and `IUsbTransport`. ADB also has an internal transport-neutral byte-stream boundary so USB and TCP connections share packet serialization, authentication, stream routing, and service code.

## Projects

- `AdbSharp`: facade package, device discovery, default platform provider registration
- `AdbSharp.Common`: device models, progress records, shared exceptions
- `AdbSharp.Transport`: USB contracts, registry, Android USB interface classification
- `AdbSharp.Protocol`: ADB and Fastboot codecs
- `AdbSharp.Authentication`: ADB RSA key handling and token signing
- `AdbSharp.Adb`: ADB connection over USB or managed TCP, stream routing, shell/shell v2/exec/reboot, file sync stat/list/push/pull, logcat, local forwarding, reverse forwarding, package helpers
- `AdbSharp.Fastboot`: Fastboot command and data-phase client, streaming fetch/upload, sparse image validation, logical/super/snapshot commands
- `AdbSharp.Fastbootd`: userspace Fastboot capability probing, logical partition API, transition handling, and unified flash routing
- `AdbSharp.Platform.*`: OS-specific native USB boundaries

## Native USB Backends

- Linux uses libusb for descriptor enumeration, kernel-driver detach, interface claiming, and bulk transfers.
- Windows uses the Android WinUSB device-interface GUID with SetupAPI enumeration, WinUSB bulk pipe I/O, and native pipe transfer timeouts.
- macOS uses IOKit registry discovery for `IOUSBHostInterface` and `IOUSBInterface`, then opens the IOUSBLib interface user client for bulk pipe metadata and timeout-capable synchronous bulk transfers.

Native backend failures are mapped into `UsbTransportError` so protocol and application code can distinguish permission, detach, stall, timeout, busy, exclusive-access, and general I/O failures without inspecting platform-specific error codes.

`UsbTransportValidator` checks opened transport endpoint metadata before ADB or Fastboot protocol clients start handshakes. Invalid endpoint direction, endpoint zero, non-bulk transfers, and zero max-packet sizes fail early with `UsbTransportError.InvalidEndpoint`.

For diagnostics, `UsbTransportRegistry.FindWithDiagnosticsAsync` runs all registered enumerators and returns both successful descriptors and per-enumerator issues. The normal `FindAsync` path remains fail-fast so production discovery does not silently hide host USB setup failures.

## Rediscovery

Fastbootd transition support uses stable device matching after `reboot-fastboot`. The default rediscovery loop polls registered USB enumerators and prefers serial-number matches; when no serial is available it falls back to matching USB identity or a single attached Fastboot candidate. Hosts can override this with `FastbootdClientOptions.RediscoverFastbootdAsync`.

## Unified Flashing

`UnifiedFastbootFlasher` connects to a Fastboot-capable device, probes `is-userspace` and `is-logical:<partition>`, and selects the right command path. Physical partitions flash in bootloader Fastboot. Logical partitions are routed through `FastbootdClient`, which can issue `reboot-fastboot`, rediscover the device, and continue in userspace Fastboot.

## Threading

ADB uses a background reader loop and per-stream channels so multiple logical streams can be routed concurrently. Fastboot remains host-driven and synchronous at the protocol level, so command/data phases are serialized by a connection semaphore.

Fastboot data phases stream through bounded buffers for both host-to-device downloads and device-to-host uploads. Sparse image validation is metadata-only and does not expand image contents in memory.

## Performance

Hot protocol paths avoid avoidable transient arrays. ADB packet writes, ADB packet header reads, ADB stream read-to-end, ADB file sync transfers, Fastboot command writes, Fastboot responses, and Fastboot data phases use caller-provided spans or pooled buffers where the data does not need to survive the operation.

Fastboot command encoding has both allocating and span-based overloads. The connection layer uses the span-based path with `ArrayPool<byte>` so repeated `getvar`, flash, and data-phase commands do not allocate a new command array per request.

Static compliance checks use `GeneratedRegexAttribute` so their regular expression implementations are produced at compile time.

## ADB Service Boundaries

The ADB implementation treats adbd services as logical streams over the packet router. Local port forwarding is implemented inside AdbSharp by accepting host TCP sockets and opening the requested device-side ADB socket for each connection. Reverse forwarding uses the device-side `reverse:` service family because the device owns that listening socket.

File sync operations open `sync:` streams and select v2 records only when the device advertises the corresponding ADB feature. Legacy devices keep using v1 records for compatible operations. Push and pull transfer buffers are pooled across chunks. For `sendrecv_v2`, AdbSharp negotiates advertised compression with the same preference order as AOSP clients: Zstd, then LZ4, then Brotli. Compression and decompression are isolated to stream adapters that emit or consume ordinary sync `DATA` records.

Direct wireless ADB uses `TcpClient` only for the physical byte stream. After the socket is connected, `AdbConnection` runs the same `CNXN`, `AUTH`, `OPEN`, `OKAY`, `WRTE`, and `CLSE` logic used for USB devices. Endpoint validation remains USB-only.
