# Changelog

## Unreleased

- Fixes Windows shutdown/logoff behavior: the tray app now asks the running Node live engine to turn LEDs off gracefully before terminating, avoiding late shutdown `node.exe` errors and leaving the strip on.
- Removes the old shutdown event task on install/startup-script setup because it could launch `node.exe` too late during Windows shutdown.

## 1.0.0 - 2026-07-26

Initial open-source release.

- Adds Node HID live engine for Robobloq SyncLight-style `1A86:FE07` devices.
- Adds screen-edge color sampling with brightness, saturation, gamma, threshold, black floor and smoothing controls.
- Adds Windows tray app with dark settings UI, single-instance behavior and safe off/shutdown handling.
- Adds per-user installer package with Start Menu shortcuts and autostart.
- Documents compatibility, protocol observations, configuration and benchmark results.
