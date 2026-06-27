# AdbSharp Development Guide

## Project Vision

AdbSharp is a fully managed, cross-platform implementation of Google's
Android Platform Tools written in modern C#.

The goal is to provide a native .NET library that communicates directly
with Android devices without relying on Google's command-line tools.

Supported protocols:

- Android Debug Bridge (ADB)
- Fastboot
- Fastbootd

Target:

- .NET 11 Preview
- C# 15 Preview

## Core Principles

-   Never invoke adb.exe or fastboot.exe.
-   Never depend on the Android SDK.
-   Treat AOSP as the protocol specification, not code to copy.
-   Keep platform-specific code isolated.
-   Keep the public API clean and stable.
-   Optimize for performance, correctness, and maintainability.

## Architecture

Application -> Public API -> Protocol Layer -> Transport Layer -> Operating System

Protocol code must never depend on OS-specific APIs.

## Project Layout

- `/src`
    - `AdbSharp`
    - `AdbSharp.Adb`
    - `AdbSharp.Fastboot`
    - `AdbSharp.Fastbootd`
    - `AdbSharp.Protocol`
    - `AdbSharp.Transport`
    - `AdbSharp.Platform.Windows`
    - `AdbSharp.Platform.Linux`
    - `AdbSharp.Platform.Mac`
    - `AdbSharp.Authentication`
    - `AdbSharp.Common`

- `/tests`
    - `AdbSharp.Tests`
    - `AdbSharp.IntegrationTests`

- `/samples`
    - `AdbSharp.Console`

## Coding Standards

-   Nullable enabled
-   Warnings as errors
-   XML documentation on all public APIs
-   Prefer primary constructors for constructor-only dependency capture
    (IDE0290) when doing so does not broaden accessibility or obscure
    validation.
-   Async-first
-   CancellationToken support
-   Prefer `Span<T>`, `Memory<T>`, `ArrayPool<T>`, Pipelines, and
    `ValueTask`
-   Avoid unnecessary allocations
-   Thread-safe by design

## Testing

Every feature requires:

- Unit tests
- Serialization tests
- Mock transport tests
- Integration tests where appropriate

Hardware tests must remain optional.

## Documentation

Maintain:

- `README.md`
- `docs/Architecture.md`
- `docs/ADB.md`
- `docs/Fastboot.md`
- `docs/Fastbootd.md`
- `docs/Authentication.md`
- `docs/Examples.md`
- `docs/PlatformSetup.md`
- `docs/NOTICE.md`

## Roadmap

1. USB abstraction
2. Device discovery
3. Native Windows/Linux/macOS USB transports
4. ADB implementation
5. ADB service and package install-session completion
6. File Sync hardening
7. Fastboot
8. Fastbootd capability detection and unified logical flashing
9. USB transport hardening and hardware integration
10. Performance hardening
11. Wireless ADB direct TCP connect
12. Wireless ADB `STLS`/TLS upgrade with ADB host-key certificates
13. Android 11+ wireless pairing API and packet exchange
14. Built-in wireless pairing crypto backend with TLS exporter, SPAKE2, and AES-GCM peer-info encryption
15. Wireless pairing hardware compatibility validation
16. Wireless debugging mDNS discovery
17. NuGet packaging and release metadata audit
18. Release signing, symbol packages, and CI publishing workflow

## Mission

Build the definitive native Android Platform Tools implementation for
.NET.
