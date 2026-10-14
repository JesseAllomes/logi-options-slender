<!-- drainclamp-build: state; schema=1; generation=15 -->
# DrainClamp state

<!-- DC:ARCH -->
Greenfield repo (no code yet). Target: native Windows tray app replacing Logi Options+ for an MX Master 4 (Bluetooth LE).
Reference upstreams (read-only, untrusted, never built/run) in .agent/upstream/:
- MouseControl (Mouser, MIT, Python/PySide6, ~18.7k lines core) -> best Windows reference: core/hid_gesture.py (HID++ over hidapi), core/mouse_hook_windows.py (LL hook + scroll injection), core/actions_ring.py + config.py (MX Master 4 haptics, Actions Ring), core/app_detector.py (per-app profiles), core/logi_devices.py (device catalog incl. MX Master 4)
- MXControl (macOS Swift, README says MIT, no LICENSE file) -> clean HID++ 2.0 layering: Sources/MXControlHIDPP/{Transport,Protocol,Core,Features}, docs/reverse-engineering/
- logi-options-plus-mini (Apache-2.0) -> official Options+ installer feature flags: logi-options-plus-mini.ps1 (analytics, flow, sso, update, dfu, logivoice, smartactions, actions-ring, device-recommendation)
Baseline (2026-10-15, this PC): Options+ 2.8.981479, Logi Plugin Service 6.4.2.3414. RAM ~700MB: LogiPluginService 342, logioptionsplus_agent 262, updater 42, PluginServiceExt 21, appbroker 17, crashpad 19.
Excel findings: two Logi add-ins load each start - LogiOptionsPlusExcelAddin (VSTO, HKCU Office\Excel\Addins, LoadBehavior 2, ~0.3s) and LogiExcelAddIn (PluginService VSTO, ~0.4-1.0s, hard-disabled by Excel Resiliency\DisabledItems after load crash; its dll path no longer exists). PERSONAL.XLSB also in DisabledItems. Excel faults 2026-10-05 (x3), 10-12, 10-14; module attribution not Logi-proven.
Planned layout: src/Slender.Hidpp (transport+protocol+features), src/Slender.Engine (hooks, actions, profiles, Excel bridge), src/Slender.Tray (UI), tests/Slender.Tests, scripts/ (Options+ trim + Excel repair).
Optimisation vectors: single process, no Electron/Qt; engine idle-sleeps on HID read; UI window created on demand and disposed on close; Excel via COM late-binding only while Excel is foreground; no VSTO add-in.
<!-- /DC:ARCH -->

<!-- DC:DECISIONS -->
Direction -> hybrid: trim official Options+ now, build a new native app from the 3 repos, retire Options+ only after feature parity (user)
Excel features to replicate, in priority -> 1 thumb wheel sideways scroll in Excel, 2 Excel-specific custom buttons, 3 haptics/Actions Ring (user)
Stack -> C# / .NET (user); target .NET 10 LTS, not .NET 8 (8 leaves support 2026-11-10); SDK installed per-user via official dotnet-install.ps1, no admin (agent correction)
Target device -> MX Master 4 over Bluetooth only; keyboard/receiver support out of scope unless asked (agent default from observed hardware)
Upstreams -> reference only in .agent/upstream; port logic with attribution (MIT/Apache-2.0), never execute their scripts or binaries (agent default)
Options+ and Slender never own HID++ at the same time; switch-over needs a documented rollback (Mouser README constraint)
Options+ reinstall, Excel registry/Resiliency edits and process kills run only after explicit user approval at run time; scripts back up registry first (security boundary)
Project lives in C:/Users/user/Projects/personal, local only, never OneDrive (user memory)
Build orchestration -> Opus orchestrates on main (scaffold, contracts, review, merge, m6); Sonnet/Haiku subagents build parts in separate git worktrees/branches, merged by Opus (user)
<!-- /DC:DECISIONS -->

<!-- DC:ROADMAP -->
| id | goal | files | status | deps |
|---|---|---|---|---|
| m1 | protocol + baseline notes: MX Master 4 HID++ feature map (thumbwheel 0x2150, reprog 0x1B04, haptics, battery) from Mouser/MXControl, plus Options+ baseline | docs/protocol.md;docs/baseline.md | done | |
| m2 | Options+ trim and Excel repair scripts (dry-run default, registry backup, re-enable PERSONAL.XLSB, drop stale LogiExcelAddIn) | scripts/Trim-OptionsPlus.ps1;scripts/Repair-ExcelAddins.ps1;docs/switch-over.md | done | m1 |
| m3 | .NET 8 solution + HID++ core: BLE HID transport, device discovery, feature lookup, fake-transport tests | Slender.slnx;Directory.Build.props;src/Slender.Hidpp;tests/Slender.Tests | active | m1 |
| m4 | Excel thumb wheel: divert thumbwheel, Excel-foreground detection, sideways scroll via COM, native fallback elsewhere | src/Slender.Engine;tests/Slender.Tests | done | m3 |
| m5 | custom buttons: divert reprog controls, action engine, per-app profiles incl. Excel actions | src/Slender.Engine;tests/Slender.Tests | done | m4 |
| m6 | haptics + Actions Ring | src/Slender.Engine;src/Slender.Tray;tests/Slender.Tests | pending | m5 |
| m7 | tray app, config UI, launch at login, memory budget check, Options+ retirement with rollback | src/Slender.Tray;docs/switch-over.md;tests/Slender.Tests | pending | m6 |
<!-- /DC:ROADMAP -->

<!-- DC:VERIFY -->
```json
[
  {"id": "dotnet-build", "argv": ["dotnet", "build", "Slender.slnx", "-warnaserror"], "cwd": ".", "tier": "fast", "timeout_s": 300},
  {"id": "dotnet-test", "argv": ["dotnet", "test", "Slender.slnx", "--nologo"], "cwd": ".", "tier": "milestone", "timeout_s": 600}
]
```
<!-- /DC:VERIFY -->

<!-- DC:LOG -->
- 2026-10-14T22:21:54+00:00 3cbc90c3 m1 done: docs/baseline.md + docs/protocol.md; .NET 10 scaffold + shared contracts (Hidpp/Engine) committed as worktree base
- 2026-10-14T22:33:15+00:00 dd0e7bc5 m2 done: Trim-OptionsPlus.ps1 + Repair-ExcelAddins.ps1 (report default, -Apply gated, reg backups) + switch-over.md merged; not yet run for real
- 2026-10-14T22:34:01+00:00 627db5c1 m4+m5 merged: LL hook, foreground watcher, out-of-process Excel bridge (COM SmallScroll + WM_HSCROLL fallback), actions, profiles; 54 tests green; Excel/device behaviour unconfirmed
<!-- /DC:LOG -->
