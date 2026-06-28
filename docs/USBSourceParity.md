# USB Source-Parity Hardening

This checklist tracks AdbSharp USB behavior against AOSP ADB client behavior and
the legacy Windows USB API reference. AOSP is used as protocol and behavior
reference only; AdbSharp does not copy AOSP implementation code.

## References

- ADB USB client sources: https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/client/
- ADB USB packet framing: https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/client/transport_usb.cpp
- Windows ADB USB client: https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/client/usb_windows.cpp
- libusb ADB client: https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/client/usb_libusb_device.cpp
- macOS ADB client: https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/client/usb_osx.cpp
- Linux usbfs ADB client: https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/client/usb_linux.cpp
- Windows USB API reference: https://android.googlesource.com/platform/development/+/refs/heads/main/host/windows/usb/api

## Behavior Checklist

| Behavior | AOSP Reference | AdbSharp Implementation | Tests |
| --- | --- | --- | --- |
| ADB header and payload are written as separate USB transfers. | `transport_usb.cpp`, `usb_libusb_device.cpp` | `UsbAdbTransport.WriteAsync` splits header and payload at `AdbConstants.HeaderLength`. | `UsbAdbTransportTests.WriteAsync_preserves_separate_adb_header_and_payload_usb_writes` |
| Non-Apple ADB USB reads use packet-size-aware buffers and preserve overflow bytes. | `transport_usb.cpp` | `UsbAdbTransport.ReadAsync` rounds non-Apple read requests to endpoint packet size and stores extra bytes for the next read. | `UsbAdbTransportTests.ReadAsync_requests_packet_aligned_buffer_and_preserves_extra_bytes` |
| Packet-aligned nonzero writes are followed by a zero-length packet. | `usb_windows.cpp`, `usb_libusb_device.cpp`, `usb_linux.cpp` | Windows, Linux, IOUSBLib, and IOUSBHost transports send ZLP after packet-aligned nonzero writes. | `WindowsUsbTransportNativeSeamTests`, `LinuxUsbTransportNativeSeamTests`, `MacUsbTransportNativeSeamTests` |
| Any non-timeout, non-user-cancel I/O failure kicks the transport. | `usb_windows.cpp`, `usb_linux.cpp` | Platform transports call `AbortAsync(CancellationToken.None)` before throwing mapped `UsbTransportException` for hard read/write failures. | Native fake tests cover WinUSB, libusb, IOUSBLib, and IOUSBHost failure paths. |
| User cancellation interrupts only the pending operation. | Windows and macOS cancellation paths | Windows uses `CancelIoEx` for the current overlapped operation; macOS aborts the current pipe for the pending operation; cancellation throws `OperationCanceledException`. | `WindowsUsbTransportNativeSeamTests.ReadAsync_cancellation_calls_cancel_io_ex_without_poisoning_transport` |
| Timeout remains retryable. | ADB USB client read/write loops | Windows and Linux timeout failures loop without poisoning the transport; macOS keeps existing stall/timeout recovery. | `WindowsUsbTransportNativeSeamTests`, `LinuxUsbTransportNativeSeamTests` |
| Public kick/reset lifecycle exists. | AOSP kick/cleanup/reset behavior | `IUsbTransport.AbortAsync` invalidates native handles and unblocks I/O. `ResetAsync` requests reset where available and invalidates the transport. | `UsbTransportLifecycleTests` and hardware open/abort/rediscover/open test. |
| Windows opens and transfers with overlapped I/O. | Windows USB API `FILE_FLAG_OVERLAPPED`, overlapped read/write helpers | Windows transport keeps `FILE_FLAG_OVERLAPPED`, passes non-null `OVERLAPPED` to WinUSB, waits for completion, and cancels with `CancelIoEx`. | `WindowsUsbNativeTests` and `WindowsUsbTransportNativeSeamTests` |
| Linux reset invalidates the opened handle. | `usb_libusb_device.cpp`, `usb_linux.cpp` | Linux transport calls `libusb_reset_device`, then aborts and closes the handle so callers rediscover/reopen. | Hardware open/abort/rediscover/open test; reset hardware test pending. |
| macOS stalls are cleared before hard abort. | `usb_osx.cpp` | IOUSBLib keeps recoverable stall clearing and interface reopen path, then aborts on unrecoverable failures. IOUSBHost remains experimental. | Existing macOS interface tests plus hardware validation. |
| Diagnostics avoid private reflection. | N/A | `IUsbTransportDiagnostics.GetDiagnosticSnapshot()` exposes backend, endpoints, state, and selected native status fields. The sample prints this model. | Build and sample compile; dedicated CLI snapshot test pending. |

## Notes

- Windows remains direct WinUSB P/Invoke. AdbSharp does not depend on
  `AdbWinApi.dll`.
- IOUSBHost remains experimental; IOUSBLib is still the primary macOS backend.
- Native failure and completion paths are covered through injectable native
  adapters and fake OS handles.
