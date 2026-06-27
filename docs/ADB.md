# ADB

The ADB implementation is built around the current AOSP packet shape:

- 24-byte little-endian packet header
- command, two arguments, payload length, payload checksum, and command magic
- command constants for `CNXN`, `AUTH`, `OPEN`, `OKAY`, `WRTE`, `CLSE`, and `STLS`
- negotiated version and max payload during `CNXN`

## Implemented

- packet serialization/deserialization
- checksum and magic validation
- RSA `AUTH` token signing and ADB public key payload export
- `STLS` negotiation and managed TLS socket upgrade for TCP/wireless transports
- managed mDNS/DNS-SD discovery for `_adb._tcp`, `_adb-tls-pairing._tcp`, and `_adb-tls-connect._tcp`
- Android 11+ wireless pairing public API, peer-info records, pairing packet framing, built-in TLS keying-material export, SPAKE2, AES-GCM peer-info encryption, and hardware compatibility validation reports
- async stream multiplexing
- direct TCP/wireless connect to an already-listening `adbd` endpoint
- legacy shell, shell v2 stdout/stderr/exit parsing, and exec services
- reboot services
- local TCP forwarding to device-side ADB sockets
- device-side reverse-forward service commands
- logcat line streaming
- package list, uninstall, clear, install session, split APK install, and staged install helpers
- file sync stat, lstat, directory listing, push, and pull through `sync:`
- sync v2 feature detection for `stat_v2`, `ls_v2`, `sendrecv_v2`, and send/recv compression codecs
- compressed send/recv v2 transfers for advertised Brotli, LZ4, and Zstd support
- unauthorized-device diagnostics that identify missing or rejected host keys

## Service API Notes

- `ShellAsync` uses the legacy `shell:` service and returns combined output.
- `ShellV2Async` opens `shell,v2,raw:` and parses shell protocol frames into stdout, stderr, and an exit code.
- `ConnectTcpAsync` and `ConnectWirelessAsync` open a managed TCP socket and then run the same `CNXN`/`AUTH` handshake, packet router, and service stream code as USB-backed ADB. When a peer requests `STLS`, the connection replies with `STLS`, upgrades the socket to TLS, presents the configured ADB host certificate, and then waits for the encrypted `CNXN`.
- `AdbMdnsDiscovery.FindAsync` sends managed mDNS PTR queries for Android ADB service types, resolves SRV/TXT/A/AAAA records, and returns direct TCP endpoints for pairing or connection flows.
- `AdbPairingClient.PairAsync` opens the Android wireless debugging pairing endpoint, sends the host ADB public key as peer info, and runs the AOSP pairing packet exchange through the built-in managed pairing backend by default. Applications can still provide `IAdbPairingBackend` for specialized TLS stacks.
- `AdbPairingCompatibilityValidator.ValidateAsync` runs the same pairing path against real Android 11+ hardware and optionally verifies the post-pair ADB TCP endpoint by reading device properties.
- `StartPortForwardAsync` owns the host-side TCP listener directly and opens the requested device socket for each accepted connection. This avoids depending on an external ADB server.
- `ReverseForwardAsync`, `RemoveReverseForwardAsync`, `RemoveAllReverseForwardsAsync`, and `ListReverseForwardsAsync` use the device-side `reverse:` service family.
- `LogcatAsync` streams UTF-8 lines from `shell:logcat`.
- `CreateInstallSessionAsync`, `AdbPackageInstallSession.WriteAsync`, and `CommitAsync` map to `pm install-create`, `pm install-write`, and `pm install-commit`. `InstallPackagesAsync` is the high-level split APK path, and `InstallStagedPackagesAsync` sets `--staged` for reboot-applied installs.
- `StatAsync`, `LstatAsync`, and `ListDirectoryAsync` use sync v2 records when the device advertises the matching feature, and fall back to legacy records where possible.
- `PushAsync` and `PullAsync` use `SND2` and `RCV2` when `sendrecv_v2` is advertised, otherwise they use legacy `SEND` and `RECV`. When the peer also advertises `sendrecv_v2_zstd`, `sendrecv_v2_lz4`, or `sendrecv_v2_brotli`, transfers use the v2 compression flag in that priority order.
- File sync transfer buffers are pooled across chunks to avoid per-record allocations during large pushes and pulls.

## Next Work

- NuGet packaging and release metadata audit
