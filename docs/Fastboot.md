# Fastboot

Fastboot is implemented as a host-driven command pipeline:

- commands are ASCII packets up to 4096 bytes
- responses are packets up to 256 bytes
- terminal responses are `OKAY` or `FAIL`
- `INFO` and `TEXT` are progress/output packets before a terminal response
- `DATA%08x` starts a data phase

## Implemented

- `getvar`
- parsed integer variables such as `max-download-size`, `max-fetch-size`, and `partition-size:*`
- `download`
- `flash`
- validated sparse image flashing
- `erase`
- `boot`
- `continue`
- `reboot`
- `reboot-bootloader`
- `reboot-fastboot`
- streaming `upload`
- streaming `fetch`
- OEM commands
- flashing lock/unlock commands
- logical partition create/delete/resize commands
- `is-logical:*`, `partition-size:*`, `max-download-size`, and `max-fetch-size` helpers
- `update-super`
- snapshot update cancel/merge commands
- progress reporting and cancellation
- sparse image metadata and chunk validator

## Notes

Large transfers stream through the USB transport in configurable pooled chunks. Commands are serialized because Fastboot is synchronous by design.

`FastbootProtocol.EncodeCommand(string, Span<byte>)` supports allocation-free command encoding for connection code that owns a reusable or pooled buffer. The allocating overload remains available for tests and simple callers.

Sparse image validation checks header sizes, chunk types, encoded chunk lengths, and total expanded block counts before `FlashSparsePartitionAsync` sends the sparse image to the device.
