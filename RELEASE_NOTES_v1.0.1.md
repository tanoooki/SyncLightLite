# SyncLightLite 1.0.1

Critical hotfix release for Windows shutdown/logoff behavior.

## Critical fix

- Fixes a shutdown bug where Windows could show a `node.exe` error while powering off.
- Fixes LEDs staying on after computer shutdown/logoff.
- The tray app now asks the already-running Node live engine to turn the LEDs off gracefully before terminating it.
- The old shutdown event task is removed during install/setup because launching a new `node.exe` late in Windows shutdown was unreliable.

## Install

Download `SyncLightLite-1.0.1-windows-x64.zip`, extract it and run `SyncLightLiteSetup.exe`.

If you already installed 1.0.0, installing 1.0.1 over it preserves your config and removes the old shutdown task automatically.

Close the original vendor SyncLight app before starting SyncLightLite to avoid HID contention.

## Compatibility

Same compatibility as 1.0.0:

- Robobloq/SyncLight HID strip
- VID/PID: `1A86:FE07`, interface `0`
- 71 LEDs
- Windows 10/11 x64 target
