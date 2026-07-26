# 001: HID solid color and sync mapping

## Question

Can SyncLightLite talk directly to the Robobloq/SyncLight monitor backlight over HID, without the heavy Electron SyncLight app?

## Device observed

- VID/PID: 0x1a86 / 0xfe07
- Product: ROBOBLOQ USBHID
- Device id: 000501
- UUID: cdab246a7dbd6ed3
- Firmware: 1.9.4
- LEDs: 71
- Display size config: 34
- HID interface used: interface 0

## Approach

Use the `node-hid` package bundled inside the installed SyncLight app to prototype packets safely before building a native lightweight implementation.

SyncLight was closed before tests to avoid HID contention.

## Commands used

From the project root:

```bash
node spikes/001-hid-solid-color/hid_spike.js info
node spikes/001-hid-solid-color/hid_spike.js color red
node spikes/001-hid-solid-color/hid_spike.js color green
node spikes/001-hid-solid-color/hid_spike.js color blue
node spikes/001-hid-solid-color/hid_spike.js thirds
node spikes/001-hid-solid-color/hid_spike.js sync-thirds
node spikes/001-hid-solid-color/hid_spike.js sync-range 1 5 white
```

## Results

### Validated

- Device discovery works.
- Device info read works.
- RGB channel order is normal RGB:
  - red -> red confirmed visually
  - green -> green confirmed visually
  - blue -> blue confirmed visually
- Solid color control works via `setSectionLED` (`RB`, action `134`).
- Multi-segment sync control works via `setSyncScreen` (`SC`, action `128`).

### Mapping observations

- `sync-thirds` sent:
  - LEDs 1-23: red
  - LEDs 24-47: green
  - LEDs 48-71: blue
- User saw the monitor visually as: blue, green, red.
- Therefore visual left-to-right is reversed relative to logical LED order in that viewing orientation.
- `sync-range 1 5 white` showed LEDs 1-5 at the beginning of the cable that exits USB.

## Important packet notes

### Info request

`RB`, action `130`.

### Solid color

`RB`, action `134`, with section data like:

```text
[1, r, g, b, lampsAmount, lampsAmount + 1, 0, 0, 0, 254]
```

This works for whole-strip static color.

### Multi-range sync

`SC`, action `128`, with 5-byte groups:

```text
[startLed, r, g, b, endLed]
```

Example for thirds on 71 LEDs:

```text
[1,255,0,0,23, 24,0,255,0,47, 48,0,0,255,71, 72,0,0,0,254]
```

## What did not work

Using `setSectionLED` with multiple range groups did not create multiple colors. It made the strip all red, so that command should be treated as static/effect control for this HID device, not as the sync-screen multi-color path.

## Verification

Ad-hoc verification scripts were created under `%TEMP%` with `hermes-verify-` prefixes and removed after execution. They validated packet layout and checksums for:

- solid red packet
- thirds section packet
- sync-thirds `SC` packet
- sync-range LED 1-5 white `SC` packet

These are focused ad-hoc checks, not a canonical test suite.

## Verdict: VALIDATED

Direct HID control is feasible. We can build SyncLightLite without SyncLight/Electron. Next step: implement a lightweight sampler that maps sampled screen edge colors into `SC` sync packets for the 71 logical LEDs, accounting for the observed physical orientation.
