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

Use modern .NET.

This project targets **.NET 11 preview and C# 15**. Treat C# 15 as the required coding baseline. Do not write older C# 13 or C# 14-style code when C# 15 provides a newer, clearer, safer, or more expressive feature.

Required:

- Nullable reference types
- File-scoped namespaces
- XML documentation on public APIs
- Warnings as errors
- Roslyn analyzers
- Async-first APIs
- CancellationToken support
- IAsyncDisposable for connections, transports, sessions, and other async-owned resources
- Meaningful exception types with actionable messages
- Thread-safe and safe for multithreaded use by design
- Source-generated P/Invoke with `LibraryImportAttribute` instead of `DllImportAttribute` whenever supported
- Explicit native marshalling for interop
- Platform-specific native code isolated behind platform-specific implementations
- Analyzer-suggested modernizations fixed unless they reduce clarity or alter behavior

Constructor and initialization rules:

- Prefer primary constructors for constructor-only dependency capture when they do not broaden accessibility, obscure validation, or make initialization harder to understand.
- Use object and collection initializers where they improve clarity.
- Avoid unnecessary backing fields when auto-properties are sufficient.

C# 15-first requirements:

- Use C# 15 syntax and language features as the default style for new code.
- Use C# 15 union types for closed result shapes, protocol states, restore phases, message variants, operation outcomes, and other values that are intentionally one of several known cases.
- Prefer union-style modeling over older ad-hoc patterns such as:
  - stringly typed state values
  - loosely related enum/object pairs
  - nullable result bags
  - inheritance hierarchies used only to represent a closed set of cases
  - tuples where named result cases would be clearer
- Use exhaustive pattern matching with union-like result shapes so new cases are handled intentionally.
- Do not introduce older compatibility patterns when a C# 15 feature directly expresses the design better.
- When refactoring existing code, upgrade C# 13/14-era patterns to the equivalent C# 15 style where the resulting code is simpler or safer.

C# 14 usage requirements:

- Use modern extension members where extension properties, static extension members, or grouped extension APIs make the API clearer.
- Prefer C# 14 extension-member syntax over scattered extension-method-only helper classes when the members conceptually belong together.
- Do not use older helper classes or utility wrappers when modern extension members provide a cleaner shape.

C# 13 usage requirements:

- Use `System.Threading.Lock` instead of `object` lock fields for synchronous internal synchronization.
- Use C# 13 lock semantics with `System.Threading.Lock` rather than older `private readonly object gate = new();` patterns.
- Use `SemaphoreSlim` for async coordination or any lock that must be held across `await`.
- Never `await` inside a `lock` block.
- Use `params` collections where they simplify public or internal APIs that accept flexible argument lists.
- Use implicit index access in object initializers where it improves clarity.
- Use modern ref/unsafe support in async and iterator code only when necessary and carefully isolated.
- Use `ref struct` capabilities where they improve allocation-free parsing, but do not leak them into public APIs unless intentional.

Thread-safety requirements:

- Design all transports, sessions, connection managers, and protocol state machines to be safe under concurrent use unless explicitly documented otherwise.
- Protect mutable shared state with clear synchronization boundaries.
- Use `System.Threading.Lock` for short synchronous critical sections.
- Use `SemaphoreSlim` for async coordination, including read/write gates, connect/disconnect gates, lifecycle transitions, and operations that may await.
- Never `await` inside a `lock` block.
- Do not expose unsynchronized mutable collections or shared buffers.
- Avoid static mutable state. If shared state is required, make ownership, synchronization, and lifetime explicit.
- Ensure dispose, disconnect, reconnect, read, and write operations are race-safe and idempotent where practical.
- Prevent concurrent lifecycle transitions such as open, close, dispose, reconnect, and reset from corrupting transport state.
- Make cancellation and timeout behavior safe under concurrent operations.
- Do not hold locks while invoking user callbacks, logging sinks, event handlers, external processes, or native calls that may block.
- Prefer immutable result models and snapshots when returning state to callers.
- Document any API that is intentionally not thread-safe.

Analyzer/style rules to fix, not ignore:

- IDE0004: Remove redundant casts.
- IDE0017: Use object initializer syntax when it is clearer.
- IDE0031: Simplify null checks.
- IDE0032: Use auto-properties when no custom backing-field behavior is needed.
- IDE0046: Simplify `if` statements when the conditional form remains readable.
- IDE0059: Remove unnecessary assignments.
- IDE0074: Use compound assignment.
- IDE0078: Use pattern matching.
- IDE0290: Use primary constructors where appropriate.
- IDE0330: Use `System.Threading.Lock`.
- SYSLIB1054: Use `LibraryImportAttribute` instead of `DllImportAttribute`.

Required examples of preferred modernization:

```csharp
// Prefer this for simple constructor-only dependency capture.
internal sealed class TssClient(HttpClient httpClient)
{
    private readonly HttpClient httpClient = httpClient;
}
```

```csharp
// Prefer this over older object lock fields for synchronous state protection.
private readonly Lock gate = new();

public void UpdateState()
{
    lock (gate)
    {
        // protected synchronous state update
    }
}
```

```csharp
// Prefer SemaphoreSlim for async coordination.
private readonly SemaphoreSlim readGate = new(1, 1);

public async ValueTask<int> ReadAsync(
    Memory<byte> buffer,
    CancellationToken cancellationToken = default)
{
    await readGate.WaitAsync(cancellationToken).ConfigureAwait(false);

    try
    {
        return await ReadCoreAsync(buffer, cancellationToken).ConfigureAwait(false);
    }
    finally
    {
        readGate.Release();
    }
}
```

```csharp
// Prefer pattern matching over older null/type checks.
if (message is not RestoreMessage restoreMessage)
{
    return;
}
```

```csharp
// Prefer simplified conditional returns when readable.
return response.IsSuccessStatusCode
    ? RestoreResult.Success(response)
    : RestoreResult.Failed(response.StatusCode);
```

```csharp
// Prefer LibraryImport over DllImport when supported.
[LibraryImport("SomeNativeLibrary", EntryPoint = "SomeMethod", SetLastError = true)]
internal static partial int SomeMethod();
```

```csharp
// Prefer explicit string marshalling for interop.
[LibraryImport("SomeNativeLibrary", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
internal static partial int SomeMethod(string value);
```

Prefer:

- C# 15 language features first
- Union types for closed result shapes
- Exhaustive pattern matching
- Span<T>
- ReadOnlySpan<T>
- Memory<T>
- ReadOnlyMemory<T>
- ArrayPool<T>
- System.IO.Pipelines
- ValueTask where appropriate
- Async streams
- Pattern matching
- Primary constructors
- Collection expressions
- Object and collection initializers
- Source-generated interop
- Explicit native marshalling
- Small immutable result models
- Allocation-conscious protocol parsing
- Clear platform abstractions
- Immutable snapshots for externally visible state
- Explicit synchronization boundaries

Avoid:

- Older C# patterns when a C# 15 feature is available
- Blocking I/O
- Console.WriteLine in libraries
- Static mutable state
- Hidden global services
- Platform checks inside protocol code
- Large allocations in protocol loops
- `DllImportAttribute` when `LibraryImportAttribute` is supported
- `object` lock fields when `System.Threading.Lock` is appropriate
- `lock` for async coordination
- `await` inside `lock`
- Holding locks while calling external code, user callbacks, logging sinks, event handlers, native calls, or external processes
- Unsynchronized mutable collections exposed to callers
- Shared buffer ownership without explicit lifetime rules
- Stringly typed state machines
- Nullable bags used as operation results
- Broad catch blocks that hide protocol, transport, or interop failures
- Reflection or dynamic dispatch in hot protocol paths unless justified
- Unnecessary LINQ allocations in binary parsing or protocol loops

Interop rules:

- Use `LibraryImportAttribute` with `static partial` methods for native calls where supported.
- Set string marshalling explicitly.
- Set `SetLastError = true` only when the native API actually uses last-error semantics.
- Use `Marshal.GetLastPInvokeError()` with source-generated P/Invoke.
- Keep native declarations internal unless intentionally public.
- Isolate platform-specific native code behind platform-specific implementations.
- Do not leak platform checks into protocol-level code.
- Prefer safe handles over raw `IntPtr` ownership.
- Ensure all native handles are released deterministically.

Threading rules:

- Use `System.Threading.Lock` for short synchronous critical sections.
- Use `SemaphoreSlim` for async gates, transport read/write serialization, connect/disconnect coordination, and lifecycle operations that may await.
- Keep lock scopes small.
- Do not hold a synchronous lock while calling external code, user callbacks, blocking I/O, native calls, or external processes.
- Separate read, write, and lifecycle gates when concurrent reads/writes are safe but lifecycle transitions must be exclusive.
- Make dispose, disconnect, reconnect, and reset safe to call while operations are in flight.
- Ensure cancellation does not leave semaphores, locks, handles, sessions, or protocol state in a corrupted state.
- Avoid deadlocks by keeping synchronization ownership simple, explicit, and documented.
- Prefer immutable state transitions or immutable snapshots when practical.
- Document any type or method that is intentionally single-threaded or not safe for concurrent use.

Performance rules:

- Avoid per-message allocations in protocol loops.
- Prefer pooled buffers for repeated binary operations.
- Prefer Span<T>, ReadOnlySpan<T>, Memory<T>, and ReadOnlyMemory<T> for parsing and serialization.
- Avoid copying buffers unless ownership or lifetime requires it.
- Use ValueTask only when the method frequently completes synchronously or is performance-sensitive.
- Keep async paths truly asynchronous.
- Do not wrap blocking work in Task.Run inside library code unless explicitly justified.
- Avoid unnecessary LINQ in hot paths.

Review checklist:

- Is the code using C# 15 where applicable?
- Did the implementation avoid older C# 13/14-era patterns when a C# 15 feature is better?
- Are closed result shapes modeled with union types where appropriate?
- Are analyzer warnings fixed rather than suppressed?
- Does the code compile with warnings as errors?
- Are public APIs documented?
- Are nullable annotations correct?
- Does every async operation accept and honor CancellationToken?
- Are disposable resources disposed correctly?
- Are native calls using LibraryImportAttribute where possible?
- Are platform-specific details isolated?
- Are protocol loops allocation-conscious?
- Are exceptions meaningful and actionable?
- Are synchronous locks using System.Threading.Lock?
- Are async operations using SemaphoreSlim instead of lock?
- Is the implementation safe under concurrent reads, writes, dispose, disconnect, reconnect, and reset?
- Are mutable shared states protected by Lock or SemaphoreSlim as appropriate?
- Are callbacks, logging sinks, external process calls, and native calls made outside lock scopes?
- Are cancellation and timeout paths race-safe?
- Are shared buffers and mutable collections protected from unsafe concurrent access?

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
- `docs/HardwareValidation.md`
- `docs/USBSourceParity.md`
- `docs/DeviceLockOwnerResolver.md`
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
