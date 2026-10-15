# Device inventory: mx-master-4-2b042

Read-only HID++ 2.0 inventory. Only functions in the vetted getter table were sent; features marked listed-only were never written to.

- Generated: 2026-10-15 21:31:40 +11:00
- Device index 0x01, SwId 0x0C, protocol 4.5
- Product id 0xB042
- HID collection: UP FF43 U 0202 in 20 out 20
- Name: MX Master 4 (mouse)
- Friendly name: MX Master 4
- Features reported: 45; requests sent 92; errors 0

## Firmware entities

| Entity | Kind | Name | Version | Build | Active |
|---|---|---|---|---|---|
| 0 | bootloader | LD | 04.00 | 0000 | no |
| 1 | firmware | RBM | 27.02 | 0017 | no |
| 2 | firmware | RBM | 27.03 | 0019 | yes |
| 3 | hardware | HW | 00.00 | 0000 | no |

## Features

| Index | Id | Name | Version | Flags | Disposition | Why |
|---|---|---|---|---|---|---|
| 0 | 0x0000 | IRoot | - |  (0x00) | called | read in the IRoot/IFeatureSet enumeration phase |
| 1 | 0x0001 | IFeatureSet | 2 |  (0x00) | called | read in the IRoot/IFeatureSet enumeration phase |
| 2 | 0x0003 | DeviceFwVersion | 8 |  (0x00) | called | vetted read-only getters |
| 3 | 0x0005 | DeviceName | 5 |  (0x00) | called | vetted read-only getters |
| 4 | 0x1D4B | WirelessDeviceStatus | 0 |  (0x00) | listed-only | no vetted read-only getter in the table |
| 5 | 0x0020 | ConfigChange | 1 |  (0x00) | listed-only | no vetted read-only getter in the table |
| 6 | 0x0021 | CryptoId | 1 |  (0x00) | listed-only | no vetted read-only getter in the table |
| 7 | 0x0007 | DeviceFriendlyName | 0 |  (0x00) | called | vetted read-only getters |
| 8 | 0x0011 | unknown | 0 |  (0x00) | listed-only | no vetted read-only getter in the table |
| 9 | 0x1004 | UnifiedBattery | 5 |  (0x00) | called | vetted read-only getters |
| 10 | 0x1701 | unknown | 0 |  (0x00) | listed-only | no vetted read-only getter in the table |
| 11 | 0x19B0 | Haptic (Options+ slot mx-master-4-2b042_haptic_settings) | 0 |  (0x00) | called | vetted read-only getters |
| 12 | 0x19C0 | ForceSensingButton (Options+ slot mx-master-4-2b042_force_sensing_settings) | 0 |  (0x00) | called | vetted read-only getters |
| 13 | 0x1B04 | ReprogControlsV4 | 6 |  (0x00) | called | vetted read-only getters |
| 14 | 0x1814 | ChangeHost (Options+ slot mx-master-4-2b042_easy_switch_1) | 2 |  (0x00) | listed-only | on the never-call list (firmware/reset/host/profile/engineering class) |
| 15 | 0x1815 | HostsInfo | 2 |  (0x00) | listed-only | on the never-call list (firmware/reset/host/profile/engineering class) |
| 16 | 0x2250 | unknown | 1 |  (0x00) | listed-only | no vetted read-only getter in the table |
| 17 | 0x2111 | SmartShiftEnhanced | 0 |  (0x00) | called | vetted read-only getters |
| 18 | 0x2121 | HiResWheel (Options+ slot mx-master-4-2b042_mouse_scroll_wheel_settings) | 1 |  (0x00) | called | vetted read-only getters |
| 19 | 0x2150 | ThumbWheel (Options+ slot mx-master-4-2b042_thumb_wheel_adapter) | 0 |  (0x00) | called | vetted read-only getters |
| 20 | 0x2201 | AdjustableDpi (Options+ slot mx-master-4-2b042_mouse_settings) | 2 |  (0x00) | called | vetted read-only getters |
| 21 | 0x2251 | unknown | 0 |  (0x00) | listed-only | no vetted read-only getter in the table |
| 22 | 0x00D1 | Dfu2 | 0 |  (0x00) | listed-only | on the never-call list (firmware/reset/host/profile/engineering class) |
| 23 | 0x1802 | DeviceReset | 0 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | on the never-call list (firmware/reset/host/profile/engineering class) |
| 24 | 0x1803 | GpioAccess | 1 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | on the never-call list (firmware/reset/host/profile/engineering class) |
| 25 | 0x1807 | unknown | 4 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |
| 26 | 0x1816 | unknown | 0 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |
| 27 | 0x1805 | OobState | 0 | hidden, engineering (0x60) | listed-only | on the never-call list (firmware/reset/host/profile/engineering class) |
| 28 | 0x1830 | PowerModes | 0 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |
| 29 | 0x1891 | unknown | 9 | hidden, engineering, compliance-deactivatable (0x68) | listed-only | flagged engineering/manufacturing |
| 30 | 0x18A1 | LedSwControl | 0 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | on the never-call list (firmware/reset/host/profile/engineering class) |
| 31 | 0x1E00 | EnableHiddenFeatures | 0 | hidden (0x40) | listed-only | on the never-call list (firmware/reset/host/profile/engineering class) |
| 32 | 0x1E02 | EngineeringTest (0x1E block) | 0 | hidden, engineering (0x60) | listed-only | on the never-call list (firmware/reset/host/profile/engineering class) |
| 33 | 0x1E22 | EngineeringTest (0x1E block) | 1 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | on the never-call list (firmware/reset/host/profile/engineering class) |
| 34 | 0x1E30 | unknown | 0 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |
| 35 | 0x1602 | unknown | 0 |  (0x00) | listed-only | no vetted read-only getter in the table |
| 36 | 0x1EB0 | unknown | 0 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |
| 37 | 0x1861 | unknown | 1 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |
| 38 | 0x9205 | unknown | 1 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |
| 39 | 0x9201 | unknown | 0 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |
| 40 | 0x9300 | unknown | 1 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |
| 41 | 0x9401 | unknown | 0 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |
| 42 | 0x9402 | unknown | 0 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |
| 43 | 0x9001 | unknown | 1 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |
| 44 | 0x18B1 | unknown | 0 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |
| 45 | 0x18C0 | unknown | 0 | hidden, engineering, manufacturing-deactivatable (0x70) | listed-only | flagged engineering/manufacturing |

## Getter results

### 0x0003 DeviceFwVersion (index 2, v8)

| Call | Request | Status | Decoded |
|---|---|---|---|
| fn0 getEntityCount |  | ok | entityCount=4; raw=047ACD2C |
| fn1 getFwInfo(entity) #0 | 00 | ok | kind=bootloader; name=LD; version=04.00; build=0000; active=False; extra=0000642BA1A100 |
| fn1 getFwInfo(entity) #1 | 01 | ok | kind=firmware; name=RBM; version=27.02; build=0017; active=False; extra=B0423886094C00 |
| fn1 getFwInfo(entity) #2 | 02 | ok | kind=firmware; name=RBM; version=27.03; build=0019; active=True; extra=B04203CE08D800 |
| fn1 getFwInfo(entity) #3 | 03 | ok | kind=hardware; name=HW; version=00.00; build=0000; active=False; extra=00000000000000 |

### 0x0005 DeviceName (index 3, v5)

| Call | Request | Status | Decoded |
|---|---|---|---|
| fn0 getNameCount |  | ok | length=11 |
| fn1 getName(offset) @0 | 00 | ok | chunk=MX Master 4 |
| fn2 getType |  | ok | type=3; name=mouse |

### 0x0007 DeviceFriendlyName (index 7, v0)

| Call | Request | Status | Decoded |
|---|---|---|---|
| fn0 getFriendlyNameLen |  | ok | length=11; maxLength=14; defaultLength=11 |
| fn1 getFriendlyName(offset) @0 | 00 | ok | chunk=MX Master 4 |
| fn2 getDefaultName(offset) @0 | 00 | ok | chunk=MX Master 4 |

### 0x1004 UnifiedBattery (index 9, v5)

| Call | Request | Status | Decoded |
|---|---|---|---|
| fn0 getCapabilities |  | ok | supportedLevels=[critical, low, good, full]; flagsRaw=0x03; raw=0F0300000000 |
| fn1 getStatus |  | ok | percent=60; level=[full]; charging=discharging; externalPower=0 |

### 0x19B0 Haptic (index 11, v0)

| Call | Request | Status | Decoded |
|---|---|---|---|
| fn1 getState |  | ok | enabled=True; level=45 |

### 0x19C0 ForceSensingButton (index 12, v0)

| Call | Request | Status | Decoded |
|---|---|---|---|
| fn2 readForce |  | ok | value=3887; raw=0F2F0000 |

### 0x1B04 ReprogControlsV4 (index 13, v6)

| Call | Request | Status | Decoded |
|---|---|---|---|
| fn0 getControlCount |  | ok | count=9 |
| fn1 getCidInfo(index) #0 | 00 | ok | cid=0x0050; task=0x0038; flags=1025; flagNames=[mouse, analytics-key-events]; position=0; group=1; groupMask=0 |
| fn1 getCidInfo(index) #1 | 01 | ok | cid=0x0051; task=0x0039; flags=1025; flagNames=[mouse, analytics-key-events]; position=0; group=1; groupMask=0 |
| fn1 getCidInfo(index) #2 | 02 | ok | cid=0x0052; task=0x003A; flags=1329; flagNames=[mouse, reprogrammable, divertable, raw-xy, analytics-key-events]; position=0; group=2; groupMask=3 |
| fn1 getCidInfo(index) #3 | 03 | ok | cid=0x0053; task=0x003C; flags=1329; flagNames=[mouse, reprogrammable, divertable, raw-xy, analytics-key-events]; position=0; group=2; groupMask=3 |
| fn1 getCidInfo(index) #4 | 04 | ok | cid=0x0056; task=0x003E; flags=1329; flagNames=[mouse, reprogrammable, divertable, raw-xy, analytics-key-events]; position=0; group=2; groupMask=3 |
| fn1 getCidInfo(index) #5 | 05 | ok | cid=0x00C3; task=0x009C; flags=1329; flagNames=[mouse, reprogrammable, divertable, raw-xy, analytics-key-events]; position=0; group=2; groupMask=3 |
| fn1 getCidInfo(index) #6 | 06 | ok | cid=0x00C4; task=0x009D; flags=1329; flagNames=[mouse, reprogrammable, divertable, raw-xy, analytics-key-events]; position=0; group=2; groupMask=3 |
| fn1 getCidInfo(index) #7 | 07 | ok | cid=0x01A0; task=0x0109; flags=1329; flagNames=[mouse, reprogrammable, divertable, raw-xy, analytics-key-events]; position=0; group=2; groupMask=3 |
| fn1 getCidInfo(index) #8 | 08 | ok | cid=0x00D7; task=0x00B4; flags=928; flagNames=[divertable, virtual, raw-xy, force-raw-xy]; position=0; group=3; groupMask=0 |
| fn2 getCidReporting(cid) 0050 | 0050 | ok | cid=0x0050; mappedTo=0x0000; flags=0; flagNames=[] |
| fn2 getCidReporting(cid) 0051 | 0051 | ok | cid=0x0051; mappedTo=0x0000; flags=0; flagNames=[] |
| fn2 getCidReporting(cid) 0052 | 0052 | ok | cid=0x0052; mappedTo=0x0000; flags=0; flagNames=[] |
| fn2 getCidReporting(cid) 0053 | 0053 | ok | cid=0x0053; mappedTo=0x0000; flags=0; flagNames=[] |
| fn2 getCidReporting(cid) 0056 | 0056 | ok | cid=0x0056; mappedTo=0x0000; flags=0; flagNames=[] |
| fn2 getCidReporting(cid) 00C3 | 00C3 | ok | cid=0x00C3; mappedTo=0x0000; flags=1; flagNames=[diverted] |
| fn2 getCidReporting(cid) 00C4 | 00C4 | ok | cid=0x00C4; mappedTo=0x0000; flags=1; flagNames=[diverted] |
| fn2 getCidReporting(cid) 01A0 | 01A0 | ok | cid=0x01A0; mappedTo=0x0000; flags=1; flagNames=[diverted] |
| fn2 getCidReporting(cid) 00D7 | 00D7 | ok | cid=0x00D7; mappedTo=0x0000; flags=0; flagNames=[] |

### 0x2111 SmartShiftEnhanced (index 17, v0)

| Call | Request | Status | Decoded |
|---|---|---|---|
| fn0 getCapabilities |  | ok | flags=0x01; tunableTorque=True; defaultAutoDisengage=10; defaultTunableTorque=75; maxForce=14 |
| fn1 getRatchetControlMode |  | ok | mode=ratchet; autoDisengage=23; force=100 |

### 0x2121 HiResWheel (index 18, v1)

| Call | Request | Status | Decoded |
|---|---|---|---|
| fn0 getWheelCapability |  | ok | multiplier=15; flags=0x1C; hasInvert=True; hasRatchetSwitch=True |
| fn1 getWheelMode |  | ok | flags=0x00; diverted=False; hiRes=False; inverted=False |
| fn2 getRatchetSwitchStatus |  | ok | ratchet=0 |

### 0x2150 ThumbWheel (index 19, v0)

| Call | Request | Status | Decoded |
|---|---|---|---|
| fn0 getInfo |  | ok | nativeResolution=20; divertedResolution=120; defaultDirection=0; caps=[timestamp, touch]; timeUnit=1000 |
| fn1 getConfig |  | ok | mode=native; inverted=True |

### 0x2201 AdjustableDpi (index 20, v2)

| Call | Request | Status | Decoded |
|---|---|---|---|
| fn0 getSensorCount |  | ok | count=1 |
| fn1 getSensorDpiList(sensor) #0 | 00 | ok | sensor=0; discrete=[]; ranges=[200-8000 step 50]; raw=0000C8E0321F40000000000000000000 |
| fn2 getSensorDpi(sensor) #0 | 00 | ok | sensor=0; dpi=2050; defaultDpi=1000; tail=000000 |

## Controls (0x1B04)

| CID | Name | Options+ label | Task | Flags | Pos | Group | GMask | Reporting |
|---|---|---|---|---|---|---|---|---|
| 0x0050 | Left Click |  | 0x0038 | mouse, analytics-key-events (0x401) | 0 | 1 | 0x00 | [] -> 0x0000 |
| 0x0051 | Right Click |  | 0x0039 | mouse, analytics-key-events (0x401) | 0 | 1 | 0x00 | [] -> 0x0000 |
| 0x0052 | Middle Button | Middle Button | 0x003A | mouse, reprogrammable, divertable, raw-xy, analytics-key-events (0x531) | 0 | 2 | 0x03 | [] -> 0x0000 |
| 0x0053 | Back Button | Back Button | 0x003C | mouse, reprogrammable, divertable, raw-xy, analytics-key-events (0x531) | 0 | 2 | 0x03 | [] -> 0x0000 |
| 0x0056 | Forward Button | Forward Button | 0x003E | mouse, reprogrammable, divertable, raw-xy, analytics-key-events (0x531) | 0 | 2 | 0x03 | [] -> 0x0000 |
| 0x00C3 | Mouse Gesture Button | Gesture Button | 0x009C | mouse, reprogrammable, divertable, raw-xy, analytics-key-events (0x531) | 0 | 2 | 0x03 | [diverted] -> 0x0000 |
| 0x00C4 | Smart Shift | Modeshift Button | 0x009D | mouse, reprogrammable, divertable, raw-xy, analytics-key-events (0x531) | 0 | 2 | 0x03 | [diverted] -> 0x0000 |
| 0x01A0 | Sense Panel | Assignment Name Show Radial Menu | 0x0109 | mouse, reprogrammable, divertable, raw-xy, analytics-key-events (0x531) | 0 | 2 | 0x03 | [diverted] -> 0x0000 |
| 0x00D7 | Virtual Gesture Button |  | 0x00B4 | divertable, virtual, raw-xy, force-raw-xy (0x3A0) | 0 | 3 | 0x00 | [] -> 0x0000 |

## Options+ non-control slots (read-only metadata)

- mx-master-4-2b042-click (Gesture Click Button)
- mx-master-4-2b042-down (Gesture Down Button)
- mx-master-4-2b042-left (Gesture Left Button)
- mx-master-4-2b042-right (Gesture Right Button)
- mx-master-4-2b042-up (Gesture Up Button)
- mx-master-4-2b042_easy_switch_1 (Easyswitch 1)
- mx-master-4-2b042_easy_switch_2 (Easyswitch 2)
- mx-master-4-2b042_easy_switch_3 (Easyswitch 3)
- mx-master-4-2b042_force_sensing_settings (Button Sensitivity)
- mx-master-4-2b042_haptic_settings
- mx-master-4-2b042_haptic_settings (Haptic Feedback)
- mx-master-4-2b042_host_change (Easyswitch Host Change Button)
- mx-master-4-2b042_mouse_scroll_wheel_settings (Scroll Wheel Settings)
- mx-master-4-2b042_mouse_settings
- mx-master-4-2b042_mouse_settings (Pointer Settings)
- mx-master-4-2b042_mouse_thumb_wheel_settings (Thumbwheel Settings)
- mx-master-4-2b042_mouse_wheel_settings
- mx-master-4-2b042_thumb_wheel_adapter (Thumbwheel)
