# NOTICE

AdbSharp is an original managed C# implementation. It uses Android Open Source Project materials as protocol references and does not copy significant AOSP implementation code.

Referenced materials include:

- ADB source tree: https://android.googlesource.com/platform/packages/modules/adb/
- AOSP Windows USB host reference: https://android.googlesource.com/platform/development/+/refs/heads/main/host/windows/usb/
- Android WinUSB INF metadata: https://android.googlesource.com/platform/development/+/refs/heads/main/host/windows/usb/android_winusb.inf
- ADB packet constants and structures in AOSP headers
- ADB `STLS`, TLS transport, mDNS service names, and pairing protocol behavior in AOSP `adb.cpp`, `transport.cpp`, `adb_mdns.h`, `adb_mdns.cpp`, `tls/`, `pairing_connection.h`, `pairing_connection.cpp`, `pairing_auth.cpp`, and `pairing.proto`
- ADB service and shell protocol descriptions in AOSP `SERVICES.TXT` and `shell_protocol.h`
- ADB file sync protocol descriptions in AOSP `file_sync_protocol.h`, `file_sync_service.cpp`, and `file_sync_client.cpp`
- Android package-manager install session shell command behavior in AOSP `PackageManagerShellCommand.java`
- Fastboot protocol README: https://android.googlesource.com/platform/system/core/+/refs/heads/main/fastboot/README.md
- Fastboot client command behavior in AOSP `fastboot.cpp` and `fastboot_driver.cpp`
- Android sparse image format in AOSP `libsparse/sparse_format.h`
- Fastbootd documentation: https://source.android.com/docs/core/architecture/bootloader/fastbootd
- Android adb overview: https://developer.android.com/tools/adb
- AOSP licensing guidance: https://source.android.com/license
- Apple macOS SDK IOKit IOUSBLib and IOCFPlugIn headers for native user-client ABI details

AOSP source files inspected for this milestone are licensed under the Apache License, Version 2.0. Preserve AOSP notices if future work incorporates any copyrightable text or code beyond protocol facts.

Third-party managed codec packages used by ADB sync v2 compression:

- `K4os.Compression.LZ4.Streams` is MIT licensed.
- `ZstdSharp.Port` is MIT licensed.
- Brotli support uses `System.IO.Compression.BrotliStream` from the .NET runtime.

Third-party managed TLS package used by Android wireless pairing:

- `BouncyCastle.Cryptography` is MIT licensed and is used for TLS 1.3 keying-material export in the pairing backend.
