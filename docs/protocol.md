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
| Haptics | 0x19B0 | fn1 getState → p[0] bit0 enabled, p[1] level 0–100; fn2 setState [1, level]; play waveform [id] = **fn4** (confirmed on device; fn3 is silent) | UI level→device {0:25, 1:50, 2:75, 3:100}. Waveforms: 0 default, 1 press, 3 DPI cycle, 7 gesture done |
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

## Capture model (m8)

Hybrid capture. Middle, Back and Forward are read by the low-level mouse hook (`WM_MBUTTONDOWN/UP`,
`WM_XBUTTONDOWN/UP`, XBUTTON1 = Back, XBUTTON2 = Forward) and are never HID++-diverted. A bound press
is swallowed together with its release (paired per button); an unbound press, or one bound to the
button's own native behaviour, is left to Windows. Injected events are ignored, so our own `SendInput`
clicks cannot loop. The thumb wheel stays native (`WM_MOUSEHWHEEL`).

Only Gesture (0x00C3), Mode shift (0x00C4) and Sense Panel (0x01A0) are HID++-diverted, and only when
bound in some profile. Diverts use the temporary flag only (0x03 divert, 0x02 undivert); the
persistent-divert bit (0x04) is never set. Controls Slender does not want are undiverted on connect
(clears Options+ leftovers).

Write allowlist, enforced in `HidppDevice` by feature ID before any index lookup (`HidppAllowlist`,
throws `HidppBlockedRequestException`, nothing is written): 0x0000 fn0-1, 0x0001 fn0-1, 0x0005 fn0-1,
0x1000 fn0, 0x1004 fn0-1, 0x19B0 fn0-4, 0x19C0 fn0-2 (reads), 0x1B04 fn0-3, 0x2110 fn0-1, 0x2111 fn0-2,
0x2121 fn0-2, 0x2150 fn0-2, 0x2201 fn0-3. Never: 0x00D0/0x00D1 (firmware), 0x1814/0x1815 (host
change/pairing), 0x8100 (onboard profiles). Slender.Probe goes through the same layer.

### Haptics play (2026-10-15, Options+ stopped)

`0x19B0` fn4 [waveform] and fn3 [waveform] are both acknowledged (`11FF0B4B…` / `11FF0B3B…`, no error). Only **fn4
plays a pulse** (user felt 3 of 6 when both ran; fn3 alone produced nothing). fn3 is accepted but silent. Slender uses fn4.
With Options+ stopped, every control reads back undiverted and the thumb wheel reports mode 0 (native), invert 0;
Options+ had set invert 1, so the native direction may feel reversed to the user. `Slender.Probe --pulse=<fn>:<waveform>` plays one function three times.

## ERGO K860 (confirmed on device)

Read-only probe 2026-10-15, Options+ running. Raw output: `.agent/probe/k860-readonly-20261015.txt`
(`Slender.Probe --device k860`; state-changing flags are refused for the keyboard).

- BLE PID **0xB359**, name "Wireless Keyboard ERGO K860". HID++ collection UsagePage 0xFF43 / Usage 0x0202,
  20-byte in/out reports (same shape as the MX Master 4). `WindowsHidTransportFactory(ErgoK860ProductIds)` selects it.
- Device index **0x01** answers. 0xFF answered once and timed out on another run, so the keyboard tries 0x01 first, then 0xFF.
- 29 features. Present: 0x0001, 0x0003, 0x0005, 0x1D4B, 0x0020, 0x0007, **0x1000 battery** (idx 7), 0x1814, 0x1815,
  **0x1B04** (idx 10), 0x1C00, **0x40A3 Fn inversion, multi-host** (idx 12), 0x4100, 0x4220, 0x4521, 0x4531, 0x00C2,
  plus 0x1802/1803/1806/1813/1805/1830/18A1/1DF3/1E00/1EB0/1861/18B0. Absent: 0x1004, 0x19B0, 0x2121, 0x2150, 0x40A0, 0x40A2.
- Battery: 0x1000 fn0 works (100 %, status 0); no 0x1004.
- Fn inversion 0x40A3 fn0 getter, request p[0] = host index, reply echoes it: p[1] = inversion state, p[2], p[3] = 1.
  Host 0 and 0xFF read state 0; hosts 1 and 2 read state 1. fn1 setter shape [host, state] is the Logitech-spec
  expectation, **unverified** (the probe never writes). Slender writes host 0xFF (current host); `LogiKeyboard.CurrentHost`.
  State 1 = F-row sends F1..F12 (Fn lock on), also unverified. Only 0x40A3 fn0-1 is on the allowlist.
- 0x1B04: 24 controls. Divertable (flags bit 0x20): CID 209/210/211 Easy-Switch (0xD1-0xD3, flags 0x64), and
  0xC7 brightness down, 0xC8 brightness up, 0xE0 task view, 0x100 switch apps, 0x6E show desktop, 0xD4 search, 0xE4 previous,
  0xE5 play/pause, 0xE6 next, 0xE7 mute, 0xE8 volume down, 0xE9 volume up (all flags 0x7A), 0x101, 0xBF screen capture,
  0x0A calculator, 0xEA look up, 0x6F lock (flags 0x74), 0xEC, 0xEB (0x64). Not divertable: 0xDE (flags 0x02), 0x34 (0x04).
  Decimal Options+ ids match these hex CIDs exactly. At probe time every control reported flags 0 (nothing left diverted).
- Keyboard path: `LogiKeyboard` shares the connect/backoff/divert machinery with `LogiMouse` through `LogiDeviceBase`.
  Temporary divert flags only (0x03/0x02); every known divertable key not wanted is reset to native on connect.

### SmartShift and wheel resolution (read 2026-10-15, Options+ stopped)

- 0x2111 fn1 → `01 16 64`: mode 1 (free-spin at that moment), auto-disengage threshold 22, force 100. Options+'s values
  survive in the mouse; the mode-shift button toggles ratchet/free-spin in firmware (not diverted).
- Slender applies SmartShift from `Pointer.SmartShiftEnabled/SmartShiftThreshold` on every connect: read-modify-write,
  keeps mode and force, threshold 0xFF = off, skips the write when unchanged. Default threshold 22 (this user's value).
- 0x2121 fn1 → mode 0x00: native target, **hi-res off**. Under Options+ it was 0x01 (wheel routed to Options+ for its own
  smooth scrolling). `Pointer.SmoothScroll` (default off) will set bit1 for native high-resolution wheel reports.

### Smooth scroll (0x2121) and thumb wheel bindings

- `LogiMouseOptions.SmoothScroll` / `LogiMouse.SetSmoothScrollAsync(bool)`: 0x2121 read-modify-write. Bit1 = hi-res wheel
  (set when on, cleared when off); bit0 (divert to software) is always cleared so the wheel stays native HID; other bits are kept.
  Applied on every connect when non-null; no write when the byte already matches.
- Thumb wheel binding (pseudo id 0xF150, `ActionSpec.Keys` only): `"Right|Left"` sends each chord per notch in its direction;
  a single chord is sent per notch right and the same chord with Shift toggled per notch left. Other actions fall back to the
  Excel column scroll / native pass-through.

### What survives a power cycle (MX Master 4, tested 2026-10-15)

Set before: DPI 1200 (default 1000), SmartShift threshold 21, thumb wheel invert 1, haptics on level 45.
After switching the mouse off and on (`.agent/probe/persist-before.txt` / `persist-after.txt`):

| Setting | Survives | Notes |
|---|---|---|
| SmartShift threshold (0x2111) | **yes** | 21 kept; also why Options+'s 22 was still there |
| Haptic enable/level (0x19B0) | **yes** | level 45 kept |
| DPI (0x2201) | no | back to 1000 |
| Thumb wheel invert (0x2150) | no | back to 0 |
| Hi-res wheel mode (0x2121) | no | 0 |
| Button diverts / remaps (0x1B04) | no | temporary by spec |

So on a new PC without Slender the mouse keeps SmartShift and haptics only. DPI, wheel direction and smooth scroll must be
re-applied by Slender on every connect (already done for invert, SmartShift and smooth scroll; DPI needs wiring).
Restored afterwards: SmartShift 22, thumb invert 1.

## 0x1C00 Persistent remappable action (ERGO K860)

Stores a control's mapping **in the keyboard's own memory**, so it applies on any PC with no software. Sources: Solaar
(github.com/pwr-Solaar/Solaar, master as fetched 2026-10-15), `lib/logitech_receiver/hidpp20.py` (H), `settings_templates.py` (S),
`special_keys.py` (K). Function numbers below are the high nibble of the function byte (Solaar passes them as 0x00..0x50).

| fn | Solaar | Request params | Reply | Solaar source |
|----|--------|----------------|-------|---------------|
| 0 | capabilities | none | `u16 BE` flags | H:789-792 |
| 1 | count | none | `p[0]` = number of controls | H:1951-1953 |
| 2 | getCidInfo | `[index, 0xFF]` | `p[0..1]` = CID (BE) | H:797-799 |
| 3 | getMapping | `[cidHi, cidLo, 0xFF]` | `cid(2), host(1), action(1), param(2 BE), modifiers(1), status(1)` | H:800-811 |
| 4 | setMapping | `[cidHi, cidLo, 0xFF, action, paramHi, paramLo, modifiers]` | ack (not inspected by Solaar) | H:677 |
| 5 | reset | `[cidHi, cidLo, 0xFF]`, then re-read the mapping | ack | H:670-672 |

- **Host index**: Solaar uses 0xFF ("current host") for every call, comment H:780 and S:1518.
- **Action types** (K:1190-1201, `ACTIONID`): 0 Empty, 1 Key, 2 Mouse, 3 X displacement, 4 Y displacement, 5 V scroll, 6 H scroll,
  7 Consumer, 8 Internal, 9 Power. For Key, param = USB HID keyboard usage (page 7); for Consumer, param = HID consumer usage
  (page 0x0C), modifiers 0 (K:1248-1255, H:812-824).
- **Modifier mask** (K:1188 `KEYMOD`): Ctrl 0x01, Shift 0x02, Alt 0x04, Meta/Win 0x08, right-hand 0x10/0x20/0x40/0x80.
  Solaar only offers these combinations (K:1236-1247): none, Ctrl, Shift, Alt, Meta, Ctrl+Shift, Alt+Ctrl, Meta+Ctrl, Alt+Shift,
  Meta+Shift, Meta+Alt. Three or four modifiers are **not exercised by Solaar**; Slender's encoder accepts them but they are unverified.
- **Capabilities** (S:1553-1556): `flags & 0x0041 == 0x0041` = device does Key + Consumer codes (Craft, and the K860, flags 0x0141);
  `flags & 0x0023 == 0x0023` = Key + Mouse + HScroll (M720). Other bits unknown.
- **Restore default**: Solaar maps the pseudo value `KEYS_Default = 0x7FFFFFFF` to fn5 (H:669-672, K:1231). There is no "write the
  default action" path; fn5 is the only way back to factory.
- **Warnings** (S:1525-1527): the setting is persistent in the device ("so no need to persist it here"); Solaar warns that
  remapping important keys can make the system unusable. Craft returns a value Solaar cannot classify, which Solaar treats as Default
  (S:1573-1577, 1575). Solaar's `Empty` reply purges param and modifiers (H:823-824).
- Solaar's `PersistentRemappableAction.action` has a duplicated `Internal` branch where `Power` was meant (H:651-653); harmless.

### K860 read-only dump (2026-10-15)

Raw: `.agent/probe/k860-1c00-20261015.txt` (`Slender.Probe --device k860`, fn0-3 only, Options+ running).
Feature index 11. **Capabilities 0x0141** (Key + Consumer, no Mouse). **17 controls**. The reply layout matches Solaar byte for byte
(`00E0 00 01 002B 08 00`: byte 2 is 0, not 0xFF, i.e. the host is not echoed). All status bytes 0. CIDs are the 0x1B04 CIDs, but the
set differs: 0xD5 (213) and 0xE1 (225) are here only, and the Easy-Switch keys (209-211) are absent.

| CID | dec | action read | meaning |
|-----|-----|-------------|---------|
| 0xC7 | 199 | Empty | brightness down (firmware native, no mapping) |
| 0xC8 | 200 | Empty | brightness up |
| 0xE0 | 224 | Key 0x2B + Meta | Win+Tab (task view) |
| 0xE1 | 225 | Key 0x04 + Meta | Win+A |
| 0x6E | 110 | Key 0x07 + Meta | Win+D (show desktop) |
| 0xD5 | 213 | Consumer 0x0223 | AC Home |
| 0xE4 | 228 | Consumer 0x00B6 | previous track |
| 0xE5 | 229 | Consumer 0x00CD | play/pause |
| 0xE6 | 230 | Consumer 0x00B5 | next track |
| 0xE7 | 231 | Consumer 0x00E2 | mute |
| 0xE8 | 232 | Consumer 0x00EA | volume down |
| 0xE9 | 233 | Consumer 0x00E9 | volume up |
| 0x101 | 257 | Empty | (switch apps, native) |
| 0xBF | 191 | Key 0x46 | Print Screen (screen capture) |
| 0x0A | 10 | Consumer 0x0192 | calculator |
| 0xEA | 234 | Key 0x65 | Menu/Application (look up) |
| 0x6F | 111 | Key 0x0F + Meta | Win+L (lock) |

These look like factory defaults (standard Windows shortcuts and consumer usages), but nothing proves Options+ had not written
any of them; they are stored in `OnboardSync.K860ObservedDefaults` as "observed", not "factory".

### Slender implementation

- Allowlist: `0x1C00` fn0-5 (`HidppAllowlist`). fn4 and fn5 write persistent keyboard memory and are reachable only through
  `LogiKeyboard.SetOnboardActionAsync/SetOnboardChordAsync/ResetOnboardKeyAsync` (Key and Consumer actions only).
- `OnboardChordEncoder`: "Ctrl+Shift+S" gives `01 00 16 03`, "Win+Shift+S" `01 00 16 0A`, media names give Consumer
  (`MediaPlayPause` 0xCD, `MediaNext` 0xB5, `MediaPrevious` 0xB6, `MediaStop` 0xB7, `VolumeMute` 0xE2, `VolumeUp` 0xE9,
  `VolumeDown` 0xEA, `Calculator` 0x192, ...). Rejected: modifiers only, two main keys, modifier + media key, unknown names.
- `OnboardSync.Plan(desired, current, previouslyManaged, factoryDefaults)` is a pure diff; only changed keys produce an op.
- **No write has been sent to a real keyboard.** Unverified on device: fn4 and fn5 reply shapes, whether 0xFF is accepted as host on
  writes, whether a write takes effect immediately, three-modifier masks, and whether a Key remap of a CID that currently reads
  Empty (brightness) is honoured.

### 0x1C00 live write test (ERGO K860 over BLE, 2026-10-15)

- fn4 setMapping with host 0xFF is accepted (`…0B4B` reply, no error) and stored in host slot 0 (this PC, Easy-Switch 1);
  0xFF reads alias host 0. Status byte becomes 1 (remapped). Format matches the factory entries exactly.
- Effect: the key **stops sending its factory action** (Calculator no longer opens) but **no new keystroke is seen by
  Windows**, both for Win+Shift+S (Key 0x16 mods 0x0A) and Ctrl+Shift+Esc (Key 0x29 mods 0x03), before and after an
  Easy-Switch reconnect.
- fn5 reset works: the key reads back Consumer 0x0192 and Calculator opens again. Key left at factory.
- Open question: where the remapped report goes (vendor collection / needs host software?). Not usable until explained.

### 0x1C00 modifier test (ERGO K860, 2026-10-15, input logged with tools/Slender.InputLog)

| Stored action | What Windows received |
|---|---|
| Ctrl+Shift+Esc (Key 0x29 mods 0x03) | Esc only (no Ctrl/Shift events) |
| Win+Shift+S (Key 0x16 mods 0x0A) | (S only, inferred) |
| Win+E (Key 0x08 mods 0x08) | Win+E, Explorer opened |
| Ctrl+Esc (Key 0x29 mods 0x01) | Ctrl+Esc, Start opened |

One modifier works; with two or more the firmware drops all of them. The encoder now refuses multi-modifier chords.
Open: the user reports Options+ offers two-modifier shortcuts for these keys; next step is to set one in Options+ and
read 0x1C00 / 0x1B04 to see whether Options+ stores it onboard (different encoding) or handles it in software (divert).
Key 10 reset to factory after the tests.
