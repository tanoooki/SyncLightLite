# Changelog

## 1.0.3 - 2026-07-28

- Fixes shutdown/logoff crashes when Windows removes the Robobloq HID device before SyncLightLite finishes stopping.
- Stops the tray from launching a fresh `node.exe` off process during Windows session ending; it now asks the already-running live engine to stop and logs that fallback behavior was intentionally skipped.
- Keeps shutdown logs transparent: HID write/disconnect errors are logged as clean stop conditions instead of surfacing late Node stack traces.

## 1.0.2 - 2026-07-27

- Fixes the tray-to-Node graceful off command so shutdown/off requests pass the explicit lights-off flag instead of stopping with `off=false`.
- Changes the tray "Turn off now" action to stop the active live engine with `turnOff: true`, avoiding HID contention from a second off process while live sync owns the device.
- Improves shutdown/logoff reliability for LEDs staying on and late `node.exe` teardown errors.

## 1.0.1 - 2026-07-26

- Fixes Windows shutdown/logoff behavior: the tray app now asks the running Node live engine to turn LEDs off gracefully before terminating, avoiding late shutdown `node.exe` errors and leaving the strip on.
- Removes the old shutdown event task on install/startup-script setup because it could launch `node.exe` too late during Windows shutdown.

## 1.0.0 - 2026-07-26

Initial open-source release.

- Adds Node HID live engine for Robobloq SyncLight-style `1A86:FE07` devices.
- Adds screen-edge color sampling with brightness, saturation, gamma, threshold, black floor and smoothing controls.
- Adds Windows tray app with dark settings UI, single-instance behavior and safe off/shutdown handling.
- Adds per-user installer package with Start Menu shortcuts and autostart.
- Documents compatibility, protocol observations, configuration and benchmark results.
