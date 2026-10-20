# Handoff: next build (written 2026-10-20, resume in a fresh session)

State before the build: main at the commit that adds this file, Slender 1.1.36 released, 1600 tests, -warnaserror clean.
Roadmap rows m44, m37, m45, m46, m47 are the plan (DC:ROADMAP); the decision line "Next build (user 2026-10-20)" is in DC:DECISIONS.

## How to run it (user's instructions)

- One Sonnet builder per part, each in its OWN worktree (user: "separate worktrees"), made by hand under `.worktrees/<name>`
  on branch `wt-<name>`, removed once merged. Opus orchestrates on main, reviews, merges, resolves conflicts, ships.
- Shared-file conflicts expected and resolved at merge: `src/Slender.Engine/Contracts/EngineContracts.cs`,
  `src/Slender.Engine/Config/JsonConfigStore.cs`, `src/Slender.Settings/MainWindow.Pages.cs`, `src/Slender.Tray/Program.cs`.
  Tell builders to put new types in NEW files and keep edits to shared files minimal.
- Builder rules (same as before): build `dotnet build Slender.slnx -warnaserror`, test `dotnet test Slender.slnx --nologo`
  (per-user SDK: `export PATH="$LOCALAPPDATA/Microsoft/dotnet:$PATH"`); never touch the real devices, never click/type on
  the desktop or launch Slender/Settings/GUIs, never kill processes they did not start; commit on their branch with plain
  messages and NO AI/Co-Authored-By/Claude-Session trailers (public repo), no usernames/hostnames/work paths in tracked
  files; do not push or merge; do not edit AGENTS.md, CLAUDE.md, .gitignore, .agent/, .grok/; they cannot write report
  files, so they reply with everything.
- After merging: wire leftovers, full test run, `scripts/Build-Setup.ps1` publishes the next release (1.1.37).

## The parts

1. **Macros (m44)** - builder `macros`. Research first (AutoHotkey, Logitech G Hub, Razer Synapse, Pulover's Macro
   Creator, TinyTask, PowerToys): steps, timing, run modes, editors, safety. Build: `ActionSpec.Macro(id)` plus a macro
   library in config; steps = key down/up/tap chord, type text, mouse button down/up/click, mouse move (relative and
   absolute), wheel, launch, and ADJUSTABLE PAUSES (user requirement); run modes once / repeat while held / toggle;
   recorder (keys and mouse, with real gaps captured as pauses) and a step editor in the picker (insert, edit, delete,
   reorder, pause length); safety stop (a second press or Esc ends a running macro, a hard time cap). Must work on mouse
   buttons, keys, ring slots and press-and-hold.
2. **Excel add-in (m37)** - builder `exceladdin`. Replaces Logitech's add-in (none is registered now). Opt-in, per user,
   no network port (current-user named pipe), idle unless Slender asks, every call time-boxed, does nothing without Slender.
   Actions: next/previous visible sheet, zoom steps, cycle table style, cycle chart type, context-aware thumb wheel, run a
   named macro. Research the add-in technology first (Excel-DNA .xll on .NET vs others) and say which and why. Settings
   gets an install/uninstall toggle (the user's go-ahead is needed before it is turned on; Excel closed).
   `scripts/Repair-ExcelAddins.ps1` and `docs/switch-over.md` have context.
3. **Notification haptics (m45)** - builder `notifyhaptics`. Buzz on Windows notifications, chosen per app. User: "it
   will need a haptic menu specifically, listing all apps". Research how an UNPACKAGED app can see notifications
   (UserNotificationListener needs package identity; alternatives: sparse package, toast-window detection, per-app hooks)
   and pick the safest working route; the tray is NativeAOT with a tight memory budget, so a WinRT-heavy listener may
   belong in a separate small process. Haptics page: a dedicated app menu listing every app that has notified (with icons
   where possible), each with its own HapticCue (pattern, pulses, advanced), plus a default for new apps and an off switch.
4. **Per-app DPI + battery estimate (m46)** - builder `dpibattery`. DPI: `Profile.Dpi` (null = All apps, then global);
   the engine writes 0x2201 on app switch only when it differs (DPI is volatile on the mouse), restores on leaving; UI in
   the device stage or the Point & scroll page per app. Battery: record each device's battery reading with time to
   `%LOCALAPPDATA%\Slender\battery-history.json` (cap 60 days), estimate days left from the discharge slope (ignore
   charging segments; the K860 only reports 4 levels), show it in Settings and the tray tooltip.
5. **K860 disconnect (m47, to-do t2)** - do it on main yourself: `KeyboardRouter` releases held actions when the
   keyboard disconnects (mirror `SlenderEngine.OnConnection`), with a test.

## Answered / pending with the user

- Onboard remaps (0x1C00) explained: only the K860's 17 remappable keys, each to one key with at most one modifier or a
  media key, stored on the keyboard for every PC and channel; no multi-modifier, text, macros, launch, per-app or mouse.
- Sync sign-in (m14): user logs in on 2026-10-21.
- User tests still open: name haptic waveforms 0-14 with the Test buttons; check Easy-Switch channel names in Settings >
  General; report any double buzz from Windows 11 native haptics; toggle Caps/Num Lock so the log shows the 0x4220 layout;
  check that the picker now fits every tab (1.1.36).
