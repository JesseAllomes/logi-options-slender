# Device feature gaps (2026-10-15)

Read-only HID++ inventory (`Slender.Probe --inventory --device all`, SwId 0x0C, getters from a vetted table only).
Full data: `mx-master-4-2b042.inventory.{json,md}`, `ergo-k860-6b359.inventory.{json,md}`.

## MX Master 4 (firmware RBM 27.03 active, 27.02 in the standby slot; bootloader LD 04.00; HID++ 4.5)

45 features: 21 public, 24 hidden/engineering (listed only, never called).

| Device capability (feature) | What the device reports | Slender today | Possible feature |
|---|---|---|---|
| Scroll wheel resistance (0x2111 SmartShift, tunable torque) | torque 0-100, default 75, current 100; auto-disengage 23 | threshold only | "Wheel resistance" slider (Options+ "scroll force") |
| Sense Panel press force (0x19C0) | reading 3887 (raw force units) | not exposed | "Sense Panel sensitivity" (how hard a press counts) |
| Haptic strength (0x19B0) | 0-100, current 45 | 4 notches | continuous strength slider |
| Pointer speed (0x2201) | 200-8000 step 50, current 2050, default 1000 | 400-8000 | allow 200-400 |
| Thumb wheel (0x2150) | native res 20, diverted res 120, caps: touch + timestamp, inverted | native mode, hook-based | hi-res diverted thumb wheel (6x finer: precise Excel columns, ring steering, tab switching); touch-on-thumb-wheel as a trigger or modifier |
| Main wheel (0x2121) | hi-res x15, ratchet switch status, invert | smooth scroll toggle | show ratchet/free-spin state; react to it |
| Buttons (0x1B04) | middle/back/forward/gesture/mode-shift/Sense Panel divertable with raw XY; virtual gesture button 0x00D7 | hook + divert | raw-XY gestures from the mouse itself (no cursor freeze trick); needs one allowlist addition (temporary divert with raw XY) |
| Firmware (0x0003) | two firmware slots (A/B) | m16 planned | show both slots in Settings |
| Undocumented: 0x0011, 0x1701, 0x1602, 0x2250, 0x2251 | no public function list | none | not usable safely |

## ERGO K860 (firmware MPK 10.00; bootloader BL1 09.00; HID++ 4.5)

29 features: 17 public, 12 hidden/engineering (listed only, never called).

| Device capability (feature) | What the device reports | Slender today | Possible feature |
|---|---|---|---|
| Disable keys (0x4521) | can turn off Caps Lock, Num Lock, Scroll Lock, Insert, Win; none off now | none | on-keyboard "turn off" for those five keys (works even when Slender is closed) |
| Lock key state (0x4220) | present | none | on-screen Caps Lock / Num Lock indicator |
| Multi-platform (0x4531) | present | none | Windows / macOS / iPadOS layout per Easy-Switch channel |
| Fn inversion per host (0x40A3) | channel 1 off, channels 2 and 3 on | current host only | Fn lock per Easy-Switch channel |
| Onboard remap (0x1C00) | 17 keys remappable on the keyboard | m15 planned | store remaps on the keyboard |
| Controls (0x1B04) | 24 controls; 0x00EB, 0x00EC, 0x0101 are divertable but unused and unlabelled | 15 keys bound | identify those keys (input logger) and make them bindable |

## Rules kept

DFU, host change/pairing, reset, GPIO, onboard profiles and every hidden/engineering feature were listed only.
Nothing was written to either device.

## Sense Panel pressure (experiment 2026-10-20)

`Slender.Probe --device mx4 --force-log=45` read 0x19C0 fn2 every 50 ms (591 reads) while the panel was left alone, rested on,
pressed gently and firmly, and held. Every read returned the same value, 4859: fn2 reads back the press threshold Slender
sets with fn3, not a live force. The only events were 0x1B04 diverted-button press/release reports. Conclusion: over this
interface the Sense Panel is a threshold button (press / release); there is no analog pressure stream to build on. The
virtual control 0x00D7 "force raw XY" path remains untested (it needs a temporary divert).
