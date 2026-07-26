# SyncLightLite 1.0.0

First open-source release of SyncLightLite, a lightweight Windows HID ambient-light controller for Robobloq SyncLight-style monitor backlight strips.

## Confirmed compatibility

- Robobloq/SyncLight HID strip
- VID/PID: `1A86:FE07`, interface `0`
- 71 LEDs
- Windows 10/11 x64 target

## What's included

- Node.js live screen-sync engine
- Native Windows tray/settings app
- Per-user installer/uninstaller
- Friendly `SyncLightLiteSetup.exe` installer at the release root, replacing script-first `.cmd`/`.ps1` install flow
- Start Menu shortcuts and autostart setup
- Safe off/shutdown paths
- Focused test suite and packaging script
- Configurable `reverseLedOrder` option for strips installed in the opposite physical direction
- Windows Installed Apps registration with icon/version/uninstall command
- Startup entry now uses a real `.lnk` to `SyncLightLite.exe` instead of a `.cmd` launcher, so Task Manager shows the app name/icon
- Multilingual UI with language detection and flag selector: English, Brazilian Portuguese, Spanish and Chinese
- Live language refresh for visible settings plus localized tray menu after saving
- Tabbed settings UI: General, Effects, Capture and Colors, avoiding one long scrolling form
- Lighting/effect modes inspired by vendor presets: screen sync, fixed color, breathing, rainbow, color cycle and wave
- Non-blocking save confirmation: green pulsing save button instead of a popup on every change

## Install

Download `SyncLightLite-1.0.0-windows-x64.zip`, extract it and run `SyncLightLiteSetup.exe`.

Close the original vendor SyncLight app before starting SyncLightLite to avoid HID contention.

## Notes

The release zip bundles runtime dependencies needed by the app. Source checkouts may need a local/vendored `@warren-robobloq/quiklight` module because it is not currently published on the public npm registry.
