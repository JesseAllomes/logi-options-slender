<!-- drainclamp-build: state; schema=1; generation=86 -->
# DrainClamp state

<!-- DC:ARCH -->
Slender = lean Windows replacement for Logi Options+ (MX Master 4 BLE PID B042 + ERGO K860 BLE PID B359). main @ d28ce04, 977 tests; shipped SlenderSetup 1.1.5 (in-app update).
Modules -> responsibility -> entry:
- src/Slender.Hidpp -> HID++ 2.0 over Windows HID (shared open, SwId 0x0B tray / 0x0C probe), HidppChannel, HidppDevice + HidppAllowlist (payload-aware; only listed setters; DFU/host-change/pairing/reset/onboard profiles blocked; raw XY only for CID 0x00C3/0x00D7), LogiMouse (diverts gesture/modeshift/sense, raw-XY gesture divert 0x33, thumb wheel 0x2150 hi-res divert on demand + invert, SmartShift threshold + torque, Sense Panel force 0x19C0 fn3 within 50-200% of first reading, haptic strength 0x19B0, DPI 200-8000, all re-applied on connect, null = leave device alone), LogiKeyboard (diverts, Fn lock per host 0x40A3, disable keys 0x4521, 0x1C00 onboard remap read/set/reset 1 modifier max), Inventory/ (read-only probe: vetted getters, NeverCall guard on send path)
- src/Slender.Engine -> SlenderEngine (hook middle/back/forward; HID++ buttons; TapHold 350 ms; gesture swipes from raw XY or hook, cursor held either way; thumb wheel via IThumbWheelSource hook|HID with per-app divert policy; Excel columns; dual thumb wheel per-app mode; per-app divert sets with held-control wait; brightness; Text/Launch/Disabled actions), KeyboardRouter (same per-app/held rules, TapHold, spare keys, toggle), Ring/ (RingSession tap/hold/hold-only, folders, wheel adjust, thumb-wheel steering; RingLayout geometry; RingDefaults; RingSource), Import/ (Options+ settings.db + Logi Plugin Service ring importer, read-only copies), ThumbWheelModes, RingButtonPairing, GestureTracker, BrightnessController + WindowsMonitorBrightness (DDC/CI + LCD IOCTL, active power policy); Config/ JsonConfigStore v2 (source-gen, quarantine bad files); Contracts/EngineContracts.cs
- src/Slender.Tray -> raw Win32 tray, NativeAOT (~13.3 MB exe), Ring/ Direct2D layered overlay (Bezel/Compass/Keypad, appearance from config) + ThumbWheelToast cursor label, UpdateCoordinator (GitHub Releases source baked in, 30 s/6 h/on-demand, balloon + Restart to update, closes Settings first), battery alerts, lock-key indicator, OptionsPlusGuard, StatusPublisher (status.json incl. versions, DPI, feel values, keyboard channel), MemoryWatchdog, SyncService; entry Program.cs
- src/Slender.Settings -> WinUI 3 (locked design .grok/product-ui), pages: device stage, Point & scroll (feel sliders, dual thumb wheel), Haptics (0-100), Actions Ring (appearance + slot editor), General (import, keyboard disable keys/Fn per channel, version/updates); picker tabs incl. More (Launch/Text/Off/Press-Hold)
- src/Slender.Sync -> MSAL personal MS account, OneDrive approot config sync (needs slender.sync.json client id; m14)
- tools/Slender.Probe (read-only by default; --inventory), tools/Slender.InputLog
- scripts/Build-Setup.ps1 -> artifacts/SlenderSetup.exe (Velopack, downloads latest GitHub release as delta base, next patch, publishes v<version> to GitHub Releases; needs clean pushed HEAD + gh auth; -LocalOnly = old folder flow)
- docs/: protocol.md, devices/ (inventory reports + feature-gaps.md), actions-ring-optionsplus.md, ring-prototypes-round1.html, memory.md, install.md, sync.md, switch-over.md
Assets: device renders and imported ring icons under %LOCALAPPDATA%/Slender/assets; never committed.
Device facts: survive power cycle = SmartShift threshold, haptics; NOT = DPI, thumb invert, hi-res, diverts. Options+ is installed but disabled (kept for firmware updates); Logi Plugin Service holds the real Actions Ring config (Loupedeck72).
Machine/tooling: per-user .NET SDK at %LOCALAPPDATA%/Microsoft/dotnet (PATH dotnet has none); AOT needs VS Installer on PATH for vswhere. Worktrees only under .worktrees/ (git-excluded); one empty locked folder .worktrees/probe clears on reboot. DrainClamp plugin patched locally for .NET bin/obj caches (upstream PR #1). Branch wt-mem kept (audit experiments).
Repo: public https://github.com/JesseAllomes/logi-options-slender (main pushed; wt-mem local only). History rewritten 2026-10-16 before first push: Windows username, work hostname and AI co-author trailers scrubbed; pre-rewrite backup bundle in the session scratchpad. Releases: v1.1.7 first GitHub release.
<!-- /DC:ARCH -->

<!-- DC:DECISIONS -->
Direction -> hybrid: trim official Options+ now, build a new native app from the 3 repos, retire Options+ only after feature parity (user)
Excel features to replicate, in priority -> 1 thumb wheel sideways scroll in Excel, 2 Excel-specific custom buttons, 3 haptics/Actions Ring (user)
Stack -> C# / .NET (user); target .NET 10 LTS, not .NET 8 (8 leaves support 2026-11-10); SDK installed per-user via official dotnet-install.ps1, no admin (agent correction)
Devices -> MX Master 4 (BLE B042) and ERGO K860 keyboard (BLE B359) both in scope; keyboard = top-row key customisation, Fn lock, Easy-Switch (user)
Upstreams -> reference only in .agent/upstream; port logic with attribution (MIT/Apache-2.0), never execute their scripts or binaries (agent default)
Options+ and Slender never own HID++ at the same time; switch-over needs a documented rollback (Mouser README constraint)
Options+ reinstall, Excel registry/Resiliency edits and process kills run only after explicit user approval at run time; scripts back up registry first (security boundary)
Project lives in %USERPROFILE%/Projects/personal, local only, never OneDrive (user memory)
Build orchestration -> Opus orchestrates on main (scaffold, contracts, review, merge, m6); Sonnet/Haiku subagents build parts in separate git worktrees/branches, merged by Opus (user)
Button capture -> hybrid: LL hook for middle/back/forward + thumb wheel; temporary HID++ divert (0x03, never persist bit 0x04) only for gesture/Sense Panel/mode shift when bound; firmware remap deferred (spec: temporary, resets on config reset) (user + docs)
HID++ safety -> allowlist of writable features/functions in code; never touch DFU 0x00D1, host/pairing 0x1814/0x1815, onboard profiles (agent, user asked about device risk)
Settings UI -> WinUI 3 separate process launched from tray, exits on close; design via product-ui prototype loop, user prefers mix of round-1 directions 1+2+3 with real device renders (user)
Device renders -> copied at runtime from local Options+ cache (C:/ProgramData/LogiOptionsPlus/depots) into %LOCALAPPDATA%/Slender; never committed (agent, licensing)
Distribution -> one all-in-one SlenderSetup.exe (Velopack, per-user, no admin, self-update) replacing the PowerShell installer (user)
Sync -> sign-in with personal Microsoft account; settings stored in the user's own OneDrive app folder (Files.ReadWrite.AppFolder), no server; public-client app registration on the user's personal account; no secrets in repo (user)
Onboard -> K860 global key remaps stored on the keyboard via 0x1C00 (allowlist writes keyboard-only); mouse power-cycle test decides which device settings are trusted to persist (user)
Firmware updates -> no own DFU (brick risk, allowlist keeps 0x00D1 blocked); Slender reads firmware versions, and "Update firmware..." launches Options+ temporarily; Options+ service set to Manual otherwise (user asked for updates via Slender)
Tray -> Win32 rewrite (no WinForms) built NativeAOT-ready, then NativeAOT publish; VS 2022 C++ Build Tools installed for it (user)
Popover -> WinUI popover kept for looks; runs only while open (process exits when dismissed); unfolds into the full window (user)
Leak guard -> tray memory watchdog: log + clean self-restart above a threshold (Options+ reached 14 GB; user)
Onboard K860 -> dig deeper with an input logger before deciding (user)
Subagents must never click/type on the desktop or launch UI windows without the user's go-ahead (Outlook clicks + pop-ups happened); never kill processes they did not start (python kill happened) (user)
Onboard K860 -> one modifier max or media key (tested); multi-modifier per app via software divert, same as Options+ (tested)
Live device writes and Options+ stops only with explicit user go-ahead in the current conversation; user authorised stopping Options+ processes/service only
Options+ is disabled, not uninstalled; keep it installed for firmware updates (user 2026-10-15)
New scope (user 2026-10-15): 15% mouse charge alert, open custom apps from a key, turn a key off, migrate all Options+ profile settings
Options+ import -> one-time, read-only (copy settings.db first, never write Options+ files); unmapped items listed in a report, never silently dropped (agent default)
Actions Ring -> own ring drawn by the AOT tray; styles Bezel, Compass, Keypad (round-1 prototypes 1-3) switchable in Settings with colour, opacity and layout options; folder slots open nested apps/web pages/actions like Options+; feature parity researched before build (user 2026-10-15)
Builder worktrees -> only under logi-options-slender/.worktrees/<name> (ignored via .git/info/exclude); remove each once merged; nothing else in the personal folder (user 2026-10-15)
Gate 6 decisions (rounds R2-R6 of m13) -> orchestrator records fix for every finding the refuter confirms and the Opus adjudicator upholds; asks the user only when recommending waive/dismiss or when a finding argues against a recorded decision (user 2026-10-15)
Thumb wheel modes -> per-app mode remembered until flipped back, for the life of the tray process (not persisted); browsers preset to tabs (Ctrl+Tab/Ctrl+Shift+Tab); toggle is an action bound to the top (mode shift) button per app (user 2026-10-15)
Ring + thumb wheel -> while the ring is open the thumb wheel moves the highlight one slot per notch; main wheel keeps adjusting volume/brightness slots (user 2026-10-15)
Custom firmware -> not pursued (signed-only updates; hardware route irreversible; Slender already covers the gains) (user 2026-10-15)
Device probe -> read-only inventory: enumerate every feature (incl. hidden/engineering), call only documented getters from a vetted table, list-only for DFU/host/pairing/reset/onboard-profile and unknown features; own SwId so it runs beside the tray (user 2026-10-15)
Device feature round (user 2026-10-15): build mouse feel, precise thumb wheel + touch, K860 extras, raw-XY gestures, and press-vs-hold (Sense Panel: quick press = chosen task, long press = ring). Each new device write gets its own HidppAllowlist entry for the documented setter only; DFU/host/pairing/reset/onboard profiles stay blocked
Hosting + OTA (user 2026-10-16) -> public GitHub repo JesseAllomes/logi-options-slender; updates ship as GitHub Releases (anonymous read, no token in the app); release only from a committed, pushed HEAD
Public repo hygiene (user 2026-10-16) -> no AI co-author or Claude-Session trailers in this repo's commits; no Windows username, work hostname or SRS paths in tracked files (use placeholders in fixtures)
<!-- /DC:DECISIONS -->

<!-- DC:ROADMAP -->
| id | goal | files | status | deps |
|---|---|---|---|---|
| m1 | protocol + baseline notes | docs/protocol.md;docs/baseline.md | done | |
| m2 | Options+ trim and Excel repair scripts | scripts/Trim-OptionsPlus.ps1;scripts/Repair-ExcelAddins.ps1;docs/switch-over.md | done | m1 |
| m3 | HID++ core + probe | src/Slender.Hidpp;tools/Slender.Probe | done | m1 |
| m4 | Excel thumb wheel | src/Slender.Engine | done | m3 |
| m5 | custom buttons + per-app profiles | src/Slender.Engine | done | m4 |
| m8 | hybrid button capture + HID++ allowlist | src/Slender.Engine;src/Slender.Hidpp | done | m3 |
| m9 | ERGO K860 support | src/Slender.Hidpp;src/Slender.Engine | done | m8 |
| m10 | WinUI 3 settings app (locked design) | src/Slender.Settings | done | m8 |
| m11 | team round: engine gaps, tray keyboard status, settings polish | src/Slender.Engine;src/Slender.Tray;src/Slender.Settings | done | m10 |
| m12 | memory: Win32 AOT tray, gen0 cap, watchdog, popover, shared runtime | src/Slender.Tray;src/Slender.Settings;docs/memory.md | done | m10 |
| m13 | install SlenderSetup.exe and live verification pass (popover, Excel IDispatch path, devices, keyboard line, watchdog) | scripts/Build-Setup.ps1;docs/install.md | done | m12 |
| m14 | sign-in: user creates personal MS app registration, slender.sync.json, live sync test | src/Slender.Sync;docs/sync.md | pending | m13 |
| m15 | onboard keys UI: contract KeyboardSettings.OnboardKeys + OnboardManaged, "Store on keyboard" in picker (1 modifier max), wire OnboardSync on connect/config change | src/Slender.Engine/Contracts/EngineContracts.cs;src/Slender.Engine/OnboardSync.cs;src/Slender.Settings;src/Slender.Tray | pending | m13 |
| m16 | firmware: tray publishes device firmware versions (0x0003 read, allowlist), "Update firmware..." launches Options+ temporarily; set Options+ service to Manual after switch-over | src/Slender.Hidpp;src/Slender.Tray;src/Slender.Settings | pending | m13 |
| m17 | low-battery alert: tray notification "plug in to charge" when MX Master 4 <= 15% and not charging; once per discharge, re-arm after charging or >= 20%; threshold in config | src/Slender.Tray;src/Slender.Engine/Contracts/EngineContracts.cs;src/Slender.Engine/Config | done | m13 |
| m18 | actions: "Open app/file/URL" (ActionSpec.Launch: exe incl. Store apps, file/folder, URL; browse picker) and "Turn off" (ActionSpec.Disabled swallows the button/key on hook, HID++ divert and K860 paths) | src/Slender.Engine;src/Slender.Settings | done | m13 |
| m19 | Options+ import: read-only copy of settings.db (+macros.db) -> Slender config: default + per-app profiles (Island, Acrobat, Edge, Excel, Teams), mouse buttons, K860 keys, DPI/SmartShift/thumb wheel/haptics; backup current config; preview + report of anything unmapped; "Import from Options+" in Settings | src/Slender.Engine/Import;src/Slender.Settings;docs/switch-over.md | done | m18 |
| m20 | own Actions Ring: Win32 overlay in the AOT tray at the cursor on Sense Panel press, 8 slots per profile (any ActionSpec), haptic tick, slot editor in Settings, importer maps radial menu c408-415 | src/Slender.Engine/Contracts/EngineContracts.cs;src/Slender.Tray;src/Slender.Settings;src/Slender.Engine/Import | done | m19 |
| m21 | gesture swipes: hold gesture button + move (LL hook deltas, cursor frozen) -> up/down/left/right/click actions per profile; importer maps Options+ gesture nested cards | src/Slender.Engine;src/Slender.Settings;src/Slender.Engine/Import | done | m19 |
| m22 | brightness keys (monitor under cursor: WMI internal panel, DDC/CI externals), pointer speed (tray reads DPI via 0x2201, Settings DPI control, importer), per-app divert so explicit None restores native mode shift | src/Slender.Engine;src/Slender.Hidpp;src/Slender.Tray;src/Slender.Settings | done | m19 |
| m23 | thumb wheel steers the Actions Ring: while open, thumb wheel moves the highlight one slot per notch (haptic tick), swallowed so the app below never scrolls | src/Slender.Engine/Ring;src/Slender.Engine/LowLevelMouseHook.cs;src/Slender.Engine/RingButtonPairing.cs;src/Slender.Tray/Ring | done | m20 |
| m24 | thumb wheel modes: per-app second thumb-wheel function (browsers preset to tabs), "Switch thumb wheel mode" action for the top button, per-app mode kept until flipped back, haptic bump + short label at the cursor, Settings rows | src/Slender.Engine;src/Slender.Tray;src/Slender.Settings | done | m22 |
| m25 | device inventory: read-only probe of every HID++ feature on MX Master 4 and K860 (getters only), reports in docs/devices/, gap analysis of features Slender could add | tools/Slender.Probe;docs/devices | done | m22 |
| m26 | mouse feel: wheel resistance (0x2111 torque), Sense Panel press force (0x19C0), haptic strength 0-100 (0x19B0), DPI 200-8000; Settings rows; allowlist adds only those setters | src/Slender.Hidpp;src/Slender.Engine/Contracts;src/Slender.Engine/Config;src/Slender.Tray;src/Slender.Settings | done | m25 |
| m27 | K860 extras: on-keyboard disable keys (0x4521), Caps/Num Lock on-screen indicator (0x4220), Fn lock and platform per Easy-Switch channel (0x40A3, 0x4531), identify and bind spare keys 0x00EB/0x00EC/0x0101 | src/Slender.Hidpp;src/Slender.Engine;src/Slender.Tray;src/Slender.Settings | done | m25 |
| m28 | press vs hold: ActionSpec.TapHold(Tap, Hold) for any button; Sense Panel default stays ring; quick press runs Tap, hold past threshold runs Hold (ring opens in hold mode), haptic tick at the threshold; picker support | src/Slender.Engine;src/Slender.Settings | done | m20 |
| m29 | precise thumb wheel: divert 0x2150 to hi-res (120) only while an app needs non-native behaviour (Excel columns, dual mode, ring), synthesize nothing otherwise; thumb-wheel touch/tap as a bindable control | src/Slender.Hidpp;src/Slender.Engine;src/Slender.Settings | done | m26 |
| m30 | mouse-tracked gestures: temporary divert with raw XY on the gesture button (0x1B04), gestures from device deltas, hook path kept as fallback; allowlist adds the raw-XY divert flag only | src/Slender.Hidpp;src/Slender.Engine | done | m26 |
| m31 | thumb wheel card on the device stage: clicking the thumb wheel label/hotspot opens a card (Scroll sideways action -> picker, Second action on/off, Scroll right / Scroll left actions, Switch with = which mouse button holds "Switch thumb wheel mode" in this app, inherited line + Customise), follows the app strip; Point & scroll section kept with the same labels | src/Slender.Settings/MainWindow.ThumbCard.cs;src/Slender.Settings/MainWindow.Stage.cs;src/Slender.Settings/MainWindow.DualThumb.cs;src/Slender.Settings/Logic/ThumbWheelOps.cs;tests/Slender.Tests/Settings/ThumbWheelOpsTests.cs | active | m24 |
| m7 | switch-over complete: Options+ service Manual or uninstalled, Logi Plugin Service off, Excel add-ins disabled, one-week no-crash check | docs/switch-over.md;scripts/Repair-ExcelAddins.ps1 | pending | m16;m24;m30 |
| m32 | shortcut picker: recorder takes Ctrl+Tab etc. (window accelerators off while recording) and Win combos (low-level hook while recording + Settings foreground); Shortcuts tab with per-app, common and Windows presets, hide per entry, show hidden, General toggle to hide the tab | src/Slender.Settings/Logic/ShortcutLibrary.cs;src/Slender.Settings/Logic/WinChordCapture.cs;src/Slender.Settings/MainWindow.Shortcuts.cs;src/Slender.Settings/MainWindow.Picker.cs;src/Slender.Settings/Logic/UiState.cs;tests/Slender.Tests/Settings | active | m31 |
| m33 | thumb wheel tab mode: hold shared modifiers (Ctrl) across a burst, send only the key per step in one SendInput batch, release after 400 ms quiet or on button/touch-up/app switch/mode flip/ring/disconnect/dispose; Excel sheet steps as Ctrl+PageDown/PageUp keys instead of COM | src/Slender.Engine/WheelKeyHold.cs;src/Slender.Engine/SlenderEngine.cs;src/Slender.Engine/KeyInjector.cs;src/Slender.Engine/ActionExecutor.cs;tests/Slender.Tests/Engine/WheelKeyHoldTests.cs | done | m32 |
| m34 | Fn lock belongs to the keyboard: never pushed at start/reconnect; written once only when the user changes it in Slender; read on connect and every 30 s (Fn+Esc) and mirrored into config by the tray; default stays off | src/Slender.Hidpp/LogiKeyboard.cs;src/Slender.Hidpp/Contracts/KeyboardContracts.cs;src/Slender.Engine/KeyboardRouter.cs;src/Slender.Tray/TrayApplication.cs;src/Slender.Tray/Program.cs | done | m33 |
| m35 | no dropped side scroll: a diverted thumb wheel turn nothing claims (just after leaving tab mode, or touch/tap-only divert) is re-sent as WM_MOUSEHWHEEL; claimed turns (columns, keys, second role, ring, off) and hook turns untouched | src/Slender.Engine/SlenderEngine.cs;src/Slender.Engine/KeyInjector.cs;src/Slender.Engine/ActionExecutor.cs;tests/Slender.Tests/Engine/ThumbWheelDivertTests.cs | done | m33 |
<!-- /DC:ROADMAP -->

<!-- DC:VERIFY -->
[
  {"id": "dotnet-build", "argv": ["dotnet", "build", "Slender.slnx", "-warnaserror"], "cwd": ".", "tier": "fast", "timeout_s": 300},
  {"id": "dotnet-test", "argv": ["dotnet", "test", "Slender.slnx", "--nologo"], "cwd": ".", "tier": "milestone", "timeout_s": 600}
]
<!-- /DC:VERIFY -->

<!-- DC:LOG -->
- 2026-10-15T04:56:47+00:00 030f8f86 In-app updates merged (73bf370, 687 tests); SlenderSetup 1.1.1 built with baked releases source; user installs once, later updates arrive in-app
- 2026-10-15T05:27:40+00:00 401e20f1 Live-test fixes merged (0ec5761, 703 tests): ring preview crash, plugin-service ring import, key recorder; SlenderSetup 1.1.2 built as first in-app update
- 2026-10-15T06:05:08+00:00 7a156dbb Live pass on 1.1.2: in-app update worked end to end, real ring imported, no new errors; 1 h memory check pending
- 2026-10-15T06:45:16+00:00 9b38a586 Gate 6 R1 (final, base 8aefde1): 20 candidates, 18 fixed (da0bf66, 723 tests), 2 refuted; verdict NOT-RUN because the packet cap left 57/105 files unreviewed
- 2026-10-15T07:56:53+00:00 d88f8c74 Gate 6 m13 REVIEW-PASS: 5 rounds (R1 final + R2-R5 --only batches around the 1500-line cap), 105 files, 46 fixed, 9 refuted, 0 waived; 763 tests; DrainClamp bin/obj cache fix -> PR #1
- 2026-10-15T09:23:52+00:00 65bc4be7 m23 thumb wheel steers ring + m24 dual thumb wheel merged (795 tests, AOT clean); not yet shipped as an update
- 2026-10-15T10:33:40+00:00 bf4a3720 m25 device inventory done: gaps = wheel resistance, Sense Panel force, haptic 0-100, DPI 200, hi-res/touch thumb wheel, raw-XY gestures; K860 disable keys, lock-key OSD, multi-platform, per-host Fn lock, 3 unused keys
- 2026-10-15T11:10:03+00:00 82a52e7a Device feature round m26-m30 merged (977 tests, AOT clean): mouse feel, K860 extras, press/hold, precise thumb wheel + touch/tap, raw-XY gestures with cursor hold
- 2026-10-15T11:12:32+00:00 72922479 State refresh: ARCH rewritten for 1.1.5 (m17-m30 done); open: m13 c4 live memory/Excel check, m14 sync, m15 onboard K860 UI, m16 firmware, m7 switch-over; to-dos t1 (forward-slash UNC, fixed in R2), t2 keyboard disconnect release
- 2026-10-15T21:51:04+00:00 8c83d2cd m13 c4 live check: Excel thumb wheel + sheets pass (user); tray 8.8 MB commit at 8 min, 1 h sampler running (.agent/memory-samples-2026-10-16.txt); Settings 219 MB commit while open (note). m31 added: thumb wheel card on the stage (user chose card on click, labels Scroll right / Scroll left)
- 2026-10-15T21:56:50+00:00 ee4bd2da m31 built and committed 7050cdc (985 tests, -warnaserror clean): thumb wheel card on the stage. Not yet seen live: needs Build-Setup.ps1 + in-app update, then click the thumb wheel on the mouse stage. m13 c4 memory sample still running
- 2026-10-15T22:04:11+00:00 981b2667 m31 shipped: SlenderSetup 1.1.6 built 09:04 Melbourne (delta 1.1.5->1.1.6 in artifacts/releases); tray picks it up within 30 s / on Check for updates; awaiting user's live look at the card
- 2026-10-15T22:28:27+00:00 e1338ec4 m13 c4 done: 1 h memory sample flat at ~11 MB commit (.agent/memory-samples-2026-10-16.txt); m13 all chunks done, close after the user's live look at 1.1.6
- 2026-10-15T23:51:19+00:00 4b8962d4 Repo public on GitHub (history scrubbed: username, work hostname, AI trailers); OTA moved to GitHub Releases: Build-Setup.ps1 publishes v<version> via vpk upload github, 218118c; v1.1.7 published 10:50 Melbourne, installed 1.1.6 takes it from the local folder then follows GitHub. m31 still awaits the user's live look
- 2026-10-16T00:15:43+00:00 62015572 Bug fixed (user report: K860 Lock key set to Turn off kept Win+L): WinUI ItemClick gives the row's Content, so every picker option-list and ring icon click was dropped; ClickedItems.Find maps it back, 990 tests, ee9713a; v1.1.8 published to GitHub 11:15 Melbourne
- 2026-10-16T00:43:51+00:00 a153c171 m32 built 7cdd1ff (1387 tests, -warnaserror clean): recorder takes Ctrl+Tab/Ctrl+Shift+Tab (window accelerators off while open) and Win/Alt system combos (RecorderKeyHook + WinChordCapture, only while Settings is foreground; Win+L impossible); Shortcuts tab with app/common/Windows presets, per-row hide, Show hidden, General toggle (settings-ui.json). Not yet seen live; shipping as 1.1.9
- 2026-10-16T01:34:33+00:00 2ebfc1d9 m33 built (user idea: hold Ctrl while the wheel turns, send only Tab): WheelKeyHold + engine release triggers, 1402 tests. Known gap left open: switching back from tab mode drops side-scroll turns until the wheel pauses 150 ms (undivert waits for quiet); not fixed yet
- 2026-10-16T01:45:14+00:00 3369d1a2 m34 built (user: every update reset Fn lock): config fnLock was true and was pushed on every start, overriding Fn+Esc. Now the keyboard owns it; 1410 tests. Decision: keyboard state wins, Slender mirrors it
- 2026-10-16T01:52:32+00:00 c233d22d m35 built: unclaimed diverted turns pass through as native side scroll, 1413 tests. Assumption to check live: diverted 0x2150 rotation already follows the invert bit (user has invert on); if the side scroll right after leaving tab mode runs the wrong way, negate in PassThroughSideScroll
- 2026-10-16T02:16:48+00:00 d066df98 m33 follow-up (user: Edge tab scroll fast at first, then old behaviour): likely the unconfirmed 0x2150 touch flag dropping mid-turn released Ctrl. Tab mode now ignores touch-up; quiet timer/buttons/app switch/flip still release. Wheel shortcuts outside tab mode keep touch-up release (user scoped it). 1415 tests
<!-- /DC:LOG -->
