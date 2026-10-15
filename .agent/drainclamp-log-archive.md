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
