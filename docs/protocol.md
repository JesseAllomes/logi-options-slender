# MX Master 4 — HID++ 2.0 over Bluetooth LE on Windows

Sources: Mouser (`MC/` = `.agent/upstream/MouseControl/core/`, MIT) and MXControl
(`MX/` = `.agent/upstream/MXControl/`). **PROBE** marks facts the two disagree on or that
neither confirms. Check these on the device before relying on them (m3 probe tool).

## Transport

| Item | Value | Source |
|---|---|---|
| Vendor ID | 0x046D | MC/hid_gesture.py:816 |
| MX Master 4 PIDs | 0xB042, 0xB048 (which one is BLE: **PROBE**) | MC/logi_device_catalog.py:182 |
| HID++ collection (BLE) | UsagePage 0xFF43, Usage 0x0202 | MX/…/Transport/USBTransport.swift:41,58,156 |
| Fallback filter | any collection with UsagePage ≥ 0xFF00 | MC/hid_gesture.py:1386-1390 |
| Report IDs | 0x10 short (7 bytes), 0x11 long (20 bytes); BLE accepts **long only**, so always send 0x11 | MC/hid_gesture.py:876-879; USBTransport.swift:319-330 |
| Device index | Mouser 0xFF for BT, MXControl 0x01. **PROBE**: try 0xFF, then 0x01 | MC/hid_gesture.py:3024-3030; MX/…/DeviceManager.swift:12 |
| Request timeout | 2000 ms (probe calls 400–500 ms) | MC/hid_gesture.py:1451,3051,1587 |
| Retry | transient errors 3×, 100 ms × attempt | MX/…/Transport/HIDTransport.swift:52-80 |

Windows reads may or may not include the report-ID byte (**PROBE**). The parser must accept both:
if byte 0 is 0x10/0x11, skip it.

## Framing

```
[0]=0x11  [1]=devIdx  [2]=featIdx  [3]=(func<<4)|swId  [4..19]=params
```

- Software ID: use a fixed non-zero nibble (Mouser 0x0A, MXControl 0x01). A frame with our swId is
  a **response**. swId 0 is a device **notification**.
- Response match: same featIdx, swId ours, func equal (Mouser also accepts func+1).
- Error: featIdx 0xFF (HID++ 2.0), error code in params[1]. HID++ 1.0 error 0x8F is handled
  defensively (BLE use: **PROBE**).
- Never block on a request from the listener thread; it deadlocks until timeout
  (MC/engine.py:578-580,1305-1310).

## Features

| Feature | ID | Calls (params) | Notes |
|---|---|---|---|
| IRoot | 0x0000 | fn0 getFeature [idHi, idLo, 0] → p[0]=index (0 = absent); fn1 ping | index 0 always |
| Device name | 0x0005 | fn0 → p[0]=length; fn1 [offset] → chunk | |
| Unified battery | 0x1004 | fn1 → p[0]=level 0–100, p[2]∈{1,2,3,4} charging | fallback 0x1000 fn0. Broadcast: fn0, swId 0 |
| Haptics | 0x19B0 | fn1 getState → p[0] bit0 enabled, p[1] level 0–100; fn2 setState [1, level]; play waveform [id] = **fn4** (comment says fn3: **PROBE**) | UI level→device {0:25, 1:50, 2:75, 3:100}. Waveforms: 0 default, 1 press, 3 DPI cycle, 7 gesture done |
| Force sensing | 0x19C0 | fn2 read u16 BE; fn3 set [0, hi, lo] | Sense Panel force |
| Reprog controls v4 | 0x1B04 | fn0 getCount; fn1 getCidInfo(i) → [0-1] cid, [2-3] task, [4] flags, [5] pos, [6] group, [7] gmask, [8] flags hi; fn2 getCidReporting(cid); fn3 setCidReporting [cidHi, cidLo, flags, 0, 0] | see divert flags below |
| SmartShift enhanced | 0x2111 | fn1 read → [mode, autoDisengage, force]; fn2 write [mode, threshold, force] | mode 1 free-spin, 2 ratchet; disable = ratchet + 0xFF |
| Hi-res wheel | 0x2121 | fn0 caps (p[0] multiplier); fn1 get mode; fn2 set mode | mode bit0 target (0 = native HID), bit1 hi-res, bit2 invert. Always read-modify-write; clear bit0 on start (survives process exit) |
| Thumb wheel | 0x2150 | fn0 getInfo → p[5] caps (bit0 timestamp, bit1 touch, bit2 proximity, bit3 single-tap), p[6-7] time unit; fn1 getConfig → [mode, invert]; fn2 setConfig [mode, invert, 0] (mode 0 native, 1 diverted) | **event layout PROBE**: neither repo parses it. Expected per Logitech spec: fn0 event, p[0-1] int16 BE rotation, p[2-3] timestamp, p[4] rotation status, p[5] flags (touch/proximity/tap) |
| Adjustable DPI | 0x2201 | fn2 getSensorDpi [0]; fn3 setSensorDpi [0, hi, lo] | |

### Reprog divert flags (0x1B04 fn3, byte 2)

Per MXControl (MX/…/Features/SpecialKeysFeature.swift:219-270): bit0 divert, bit1 divert-valid,
bit2 persist, bit3 persist-valid, bit4 rawXY, bit5 rawXY-valid. Mouser writes 0x03 (divert),
0x33 (divert + rawXY), 0x02 / 0x22 (undivert). Both readings agree on those bytes. **PROBE**:
read back with fn2 after setting.

Diverted button event: fn0 notification, params = CID pairs (u16 BE) ending in 0x0000.
All-zero = everything released. RawXY event: fn1, dx int16 BE p[0-1], dy p[2-3].

Divert is volatile: re-divert on every connect and undivert on clean exit.

## MX Master 4 controls (CIDs)

| Control | CID | Confidence |
|---|---|---|
| Sense Panel (haptic / Actions Ring) | 0x01A0 | Mouser catalog |
| Thumb gesture button | 0x00C3 | Mouser catalog |
| Virtual gesture button | 0x00D7 | Mouser catalog |
| Mode shift (wheel ratchet toggle) | 0x00C4 | Mouser |
| DPI switch | 0x00FD | Mouser (MX4 presence **PROBE**) |
| Middle / Back / Forward | 0x0052 / 0x0053 / 0x0056 | MX Master 3S tables (**PROBE** on MX4) |

The full list comes from enumerating 0x1B04 fn1 on the device.

## Lifecycle

- Connect backoff 1.5 s, doubling to 30 s. A candidate without 0x1B04 cools down for 10 s.
- Three consecutive timeouts mean the device is asleep. Any report wakes it. After 300 s asleep,
  do a full reconnect (re-resolve features, re-divert).
- Options+ and Slender must not both own HID++ diverts. Options+ coexistence is untested.

## Horizontal scroll today

Neither upstream diverts the thumb wheel on Windows. Mouser hooks `WM_MOUSEHWHEEL` (0x020E) in
`WH_MOUSE_LL` and re-injects it with `SendInput` only for inversion. Neither repo has
Excel-specific code. For Excel, Slender's plan:

1. Default path: the LL hook catches `WM_MOUSEHWHEEL` while Excel is in the foreground. Swallow
   it and scroll columns through Excel COM (`ActiveWindow.SmallScroll ToRight/ToLeft`), with the
   delta accumulated to whole columns. Needs no divert, so it works even alongside Options+.
2. Optional path: divert 0x2150 for raw rotation and tap/touch gestures (after the probe).

## Confirmed on device (2026-10-15, read-only probe, Options+ running)

Raw output: `.agent/probe/readonly-20261015.txt`.

- BLE PID **0xB042**. HID++ collection UsagePage 0xFF43 / Usage 0x0202, 20-byte in/out reports.
  Windows reads **include** the 0x11 report-ID byte.
- Device index **0xFF** answers (0x01 answers too). Slender uses 0xFF.
- 45 features. Relevant indices: 0x1004 → 9, 0x19B0 → 11, 0x19C0 → 12, 0x1B04 → 13,
  0x2111 → 17, 0x2121 → 18, 0x2150 → 19, 0x2201 → 20. No 0x2110.
- 0x1B04 controls: 0x0050 left, 0x0051 right (not divertable), 0x0052 middle, 0x0053 back,
  0x0056 forward, 0x00C3 gesture, 0x00C4 mode shift, 0x01A0 Sense Panel (all divertable,
  flags 0x31), 0x00D7 virtual gesture (flags 0xA0). **No 0x00FD DPI switch on MX4.**
- Options+ currently diverts 0x0053, 0x0056, 0x00C3, 0x00C4 and 0x01A0 (getCidReporting byte 2 = 0x01).
- 0x2150 getInfo: native resolution 20, diverted resolution 120, caps 0x03 (timestamp + touch,
  no proximity, no single tap), time unit 1000. getConfig: **mode 1 (diverted by Options+)**,
  invert 1. So with Options+ running, the thumb wheel never reaches Windows as
  `WM_MOUSEHWHEEL`. Options+ reads it over HID++ and drives Excel through its add-in.
- 0x2121 mode 0x01: bit0 set, so Options+ also diverts the main wheel. Slender must clear it.
- 0x19B0: enabled, level 45.
- Battery 0x1004 fn1: 65 %, not charging.

Still to probe (state-changing, run with Options+ quit): haptic play fn3 vs fn4, divert
read-back, 0x2150 notification layout.
