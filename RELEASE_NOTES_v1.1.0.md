# SyncLightLite 1.1.0

## What's new

- Warm, pixel-inspired native settings UI with readable controls.
- Capture preview that follows unsaved height, top-offset, horizontal-margin and LED-order adjustments.
- Sampling footprints aligned with the real engine, including effective-height limits and LED 1 identification.
- Optional primary-monitor desktop guides: click-through, nonactivating, automatically hidden after 30 seconds and when leaving capture settings.
- Windows capture-exclusion check before showing guides. Older/unsupported configurations use the in-app preview instead.
- Geometry parity and multilingual WinForms regression coverage.

## Install or upgrade

Download `SyncLightLite-1.1.0-windows-x64.zip`, extract it, and run `SyncLightLiteSetup.exe`.
Existing settings, including gamma, are preserved. The Windows startup shortcut points to the updated installed application.

## Validation and limitations

RGB solid-color, per-region strip mapping, and screen-sync color patterns were checked on the supported 71-LED device. Geometry parity tests compare 96 cases against the JavaScript mapper.
The guides show sampling locations, not live LED colors. Windows capture exclusion is verified at the API level; behavior can vary with capture backend. Multi-monitor sampling still uses the primary monitor.
The release retains the .NET 9 Windows desktop runtime requirement and existing native dependencies.
