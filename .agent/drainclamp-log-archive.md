<!-- drainclamp-build: log archive; schema=1 -->
# DrainClamp log archive

Overflow from DC:LOG, keyed by the 8-character entry id. Logically
append-only. Never auto-loaded into context, and never read during resume —
resume uses `.agent/drainclamp-state.md` alone. This file is for forensics.
- 2026-10-14T22:21:54+00:00 3cbc90c3 m1 done: docs/baseline.md + docs/protocol.md; .NET 10 scaffold + shared contracts (Hidpp/Engine) committed as worktree base
- 2026-10-14T22:33:15+00:00 dd0e7bc5 m2 done: Trim-OptionsPlus.ps1 + Repair-ExcelAddins.ps1 (report default, -Apply gated, reg backups) + switch-over.md merged; not yet run for real
- 2026-10-14T22:34:01+00:00 627db5c1 m4+m5 merged: LL hook, foreground watcher, out-of-process Excel bridge (COM SmallScroll + WM_HSCROLL fallback), actions, profiles; 54 tests green; Excel/device behaviour unconfirmed
- 2026-10-14T22:43:00+00:00 0631fb3c m3 merged + read-only probe confirmed BLE PID B042/idx 0xFF/CIDs; fixed: connect now undiverts Options+ leftovers + thumb wheel native; engine wired into tray; 102 tests
- 2026-10-14T23:32:04+00:00 6fabb77f Settings UI locked (round 4); contracts v2 (hook buttons, config v2, status) at 7187832; m8 + m10 building in worktrees -m8/-m10
- 2026-10-14T23:36:33+00:00 22f79d82 m8 merged: hook captures middle/back/forward, HID++ divert only gesture/modeshift/sense, allowlist enforced in HidppDevice; raw channel sends made internal; 134 tests
- 2026-10-14T23:56:42+00:00 c3ee0158 Live test: Options+ service stopped (until reboot); haptic play = fn4 confirmed; thumb wheel invert applied from config (default on); m9 keyboard merged (151 tests); keyboard router not yet wired into tray
- 2026-10-15T00:24:31+00:00 ac36d067 m10 merged (820c394, 265 tests); keyboard wired into tray; team launched: wt-engine, wt-tray, wt-settings (Sonnet), wt-mem (Opus audit)
- 2026-10-15T01:57:21+00:00 09374121 Merged: Win32 tray (AOT 5.3 MB private), popover mode, setup.exe+sync, onboard (1 modifier max; Options+ uses software divert for multi-mod); 400+ tests
- 2026-10-15T02:04:29+00:00 6a918951 Session wrap: SlenderSetup.exe 1.0.0 built (AOT tray, 71.8 MB); next = user quits old Slender, installs, 5-min live pass (m13)
- 2026-10-15T03:17:31+00:00 55959850 m13/c1 verified install; scope added m17 battery alert, m18 launch/turn-off actions, m19 Options+ import; contracts on main fb2adcb; Sonnet agents on wt-battery, wt-actions, wt-import
- 2026-10-15T03:40:43+00:00 08d82604 m17-m19 merged to main (e71e1e2, 511 tests): battery alert, Launch/Disabled actions, Options+ importer + UI; user asked for all import gaps -> m20 own ring, m21 gestures, m22 brightness/DPI/per-app divert
- 2026-10-15T03:55:44+00:00 75bd84c4 m21 gestures + m22 brightness/DPI/per-app divert merged (601d1d7, 554 tests); m20 ring prototypes round 1 shown, waiting for pick
- 2026-10-15T04:29:13+00:00 72b3ac42 m20 Actions Ring merged (6e486cf, 661 tests): 3 switchable styles, folders, appearance page, import; SlenderSetup 1.1.0 built; next = user installs and runs m13/c2 live pass covering m17-m22 + ring
- 2026-10-15T04:56:47+00:00 030f8f86 In-app updates merged (73bf370, 687 tests); SlenderSetup 1.1.1 built with baked releases source; user installs once, later updates arrive in-app
- 2026-10-15T05:27:40+00:00 401e20f1 Live-test fixes merged (0ec5761, 703 tests): ring preview crash, plugin-service ring import, key recorder; SlenderSetup 1.1.2 built as first in-app update
