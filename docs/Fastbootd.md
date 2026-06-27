# Fastbootd

Fastbootd is userspace Fastboot used for dynamic partitions and super partition operations.

## Implemented

- `getvar:is-userspace` detection
- `getvar:super-partition-name` capability probing
- best-effort snapshot update capability detection through `getvar:snapshot-update-status`
- logical partition capability probing through `getvar:is-logical:*`
- optional transition from bootloader Fastboot through `reboot-fastboot`
- rediscovery hook for hosts that can map the rebooted device
- public `FastbootdCapabilities`
- logical partition create/delete/resize
- logical partition flashing
- sparse logical partition flashing
- `update-super`
- snapshot update command wrapper
- unified flash routing through `UnifiedFastbootFlasher`

## Unified Flashing

`UnifiedFastbootFlasher` probes the connected device before flashing. If the target partition is physical, it uses bootloader Fastboot directly. If the target partition is logical or the device is already in userspace Fastboot, it connects through `FastbootdClient` and performs the flash in Fastbootd.

When a bootloader Fastboot device must transition, `FastbootdClient` issues `reboot-fastboot`, waits for rediscovery, confirms `is-userspace=yes`, and then exposes the detected capabilities through `FastbootdClient.Capabilities`.
