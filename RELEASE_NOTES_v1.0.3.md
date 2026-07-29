# SyncLightLite 1.0.3

Shutdown/logoff transparency hotfix.

## Fixed

- Fixes shutdown/logoff crashes when Windows removes the Robobloq HID device before SyncLightLite finishes stopping.
- The tray no longer launches a fresh `node.exe` lights-off process while Windows is ending the session. During session ending it asks the already-running live engine to stop, waits briefly, and logs when fallback off is intentionally skipped.
- Live screen sync and built-in effects now treat late HID write/disconnect failures as clean stop conditions during shutdown instead of surfacing late Node stack traces.

## Log transparency

This release intentionally keeps shutdown evidence visible in `synclightlite.log`:

- Tray shutdown/logoff detection is logged.
- If Windows removes the HID device before the final off packet can be written, SyncLightLite logs the skipped write/disconnect reason instead of hiding it.
- The goal is to avoid alarming crash dialogs while preserving enough detail to diagnose future device/Windows shutdown timing issues.

## Install

Download `SyncLightLite-1.0.3-windows-x64.zip`, extract it and run `SyncLightLiteSetup.exe`.

If you already installed 1.0.0, 1.0.1 or 1.0.2, installing 1.0.3 over it preserves your config and updates the shutdown/logoff behavior.

## Compatibility

Same compatibility as 1.0.x:

- Windows 10/11 x64.
- Robobloq SyncLight-style USB HID devices exposing VID/PID `1A86:FE07`, interface `0`.
- Verified on a 71-LED strip.
