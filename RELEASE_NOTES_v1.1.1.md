# SyncLightLite 1.1.1 — Windows power-state fix

## Changes

- Automatically pauses capture and sends lights-off on display-off, session lock and suspension.
- Resumes only when the display/session state allows it and sync was not manually stopped.
- Requires confirmed initial display status, adds bounded resume retries and avoids duplicate engines.
- Fixes false off-success reporting: the tray now reads an explicit HID-write acknowledgement from its engine.
- Sends off before native capture teardown; ignores late capture callbacks and exits failed engines cleanly.
- Adds timestamped power, lock/unlock and off-result diagnostics.

## Validation and limitations

The user confirmed that the physical LEDs turned off during a real Windows S3 suspension on the supported 71-LED Robobloq strip. Automated tests cover display transitions, locks, recovery and engine-stop races. Display timeout without system suspension still needs real-world confirmation.

An off-write acknowledgement confirms that the HID write returned successfully, not a hardware acknowledgement. If Windows disconnects USB before the notification, lights-off remains best effort. Forced shutdown and power loss cannot guarantee an off command.

## Upgrade

Download `SyncLightLite-1.1.1-windows-x64.zip`, extract it and run `SyncLightLiteSetup.exe`. Existing settings, including gamma and color calibration, are preserved.
