# SyncLightLite 1.0.2

Critical shutdown/off hotfix.

## Fixed

- Fixes the tray-to-Node graceful off command so `off`, `shutdown` and `stop-off` now stop the live engine with the explicit lights-off flag.
- Fixes cases where the log showed `live stopped; ... off=false` after requesting LEDs off, meaning the engine stopped without sending the black/off packet.
- Changes the tray "Turn off now" action to stop the active live engine with `turnOff: true` instead of spawning a separate off process while the live process may still own the HID handle.
- Reduces HID contention during shutdown/logoff and manual tray shutdown paths, helping avoid LEDs staying on and late `node.exe` shutdown errors.

## Install

Download `SyncLightLite-1.0.2-windows-x64.zip`, extract it and run `SyncLightLiteSetup.exe`.

If you already installed 1.0.0 or 1.0.1, installing 1.0.2 over it preserves your config and updates the tray/off behavior.

## Compatibility

Same compatibility as 1.0.x:

- Windows 10/11 x64.
- Robobloq SyncLight-style USB HID devices exposing VID/PID `1A86:FE07`, interface `0`.
- Verified on a 71-LED strip.
