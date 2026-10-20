# DrainClamp decisions archive

The 46 decisions as they stood on 2026-10-20, before they were merged into 21 lines in DC:DECISIONS.

 1. Gate 6 review priorities (user 2026-10-20; instruction for every review run, not run yet) -> 1 minimise memory spend to the max (tray idle working set, private bytes, threads, timers, allocations; nothing loaded or running for a feature that is off) while keeping functionality flawless; 2 the Excel add-in must never lock up, freeze or crash Excel (nothing on Excel's main thread beyond a short time box, no blocking waits, every COM call guarded, no work when Slender is absent). Findings against either are high severity
 2. Direction -> hybrid: trim official Options+ now, build a new native app from the 3 repos, retire Options+ only after feature parity (user)
 3. Excel features to replicate, in priority -> 1 thumb wheel sideways scroll in Excel, 2 Excel-specific custom buttons, 3 haptics/Actions Ring (user)
 4. Stack -> C# / .NET (user); target .NET 10 LTS, not .NET 8 (8 leaves support 2026-11-10); SDK installed per-user via official dotnet-install.ps1, no admin (agent correction)
 5. Devices -> MX Master 4 (BLE B042) and ERGO K860 keyboard (BLE B359) both in scope; keyboard = top-row key customisation, Fn lock, Easy-Switch (user)
 6. Upstreams -> reference only in .agent/upstream; port logic with attribution (MIT/Apache-2.0), never execute their scripts or binaries (agent default)
 7. Options+ and Slender never own HID++ at the same time; switch-over needs a documented rollback (Mouser README constraint)
 8. Options+ reinstall, Excel registry/Resiliency edits and process kills run only after explicit user approval at run time; scripts back up registry first (security boundary)
 9. Project lives in %USERPROFILE%/Projects/personal, local only, never OneDrive (user memory)
10. Build orchestration -> Opus orchestrates on main (scaffold, contracts, review, merge, m6); Sonnet/Haiku subagents build parts in separate git worktrees/branches, merged by Opus (user)
11. Button capture -> hybrid: LL hook for middle/back/forward + thumb wheel; temporary HID++ divert (0x03, never persist bit 0x04) only for gesture/Sense Panel/mode shift when bound; firmware remap deferred (spec: temporary, resets on config reset) (user + docs)
12. HID++ safety -> allowlist of writable features/functions in code; never touch DFU 0x00D1, host/pairing 0x1814/0x1815, onboard profiles (agent, user asked about device risk)
13. Settings UI -> WinUI 3 separate process launched from tray, exits on close; design via product-ui prototype loop, user prefers mix of round-1 directions 1+2+3 with real device renders (user)
14. Device renders -> copied at runtime from local Options+ cache (C:/ProgramData/LogiOptionsPlus/depots) into %LOCALAPPDATA%/Slender; never committed (agent, licensing)
15. Distribution -> one all-in-one SlenderSetup.exe (Velopack, per-user, no admin, self-update) replacing the PowerShell installer (user)
16. Sync -> sign-in with personal Microsoft account; settings stored in the user's own OneDrive app folder (Files.ReadWrite.AppFolder), no server; public-client app registration on the user's personal account; no secrets in repo (user)
17. Onboard -> K860 global key remaps stored on the keyboard via 0x1C00 (allowlist writes keyboard-only); mouse power-cycle test decides which device settings are trusted to persist (user)
18. Firmware updates -> no own DFU (brick risk, allowlist keeps 0x00D1 blocked); Slender reads firmware versions, and "Update firmware..." launches Options+ temporarily; Options+ service set to Manual otherwise (user asked for updates via Slender)
19. Tray -> Win32 rewrite (no WinForms) built NativeAOT-ready, then NativeAOT publish; VS 2022 C++ Build Tools installed for it (user)
20. Popover -> WinUI popover kept for looks; runs only while open (process exits when dismissed); unfolds into the full window (user)
21. Leak guard -> tray memory watchdog: log + clean self-restart above a threshold (Options+ reached 14 GB; user)
22. Onboard K860 -> dig deeper with an input logger before deciding (user)
23. Subagents must never click/type on the desktop or launch UI windows without the user's go-ahead (Outlook clicks + pop-ups happened); never kill processes they did not start (python kill happened) (user)
24. Onboard K860 -> one modifier max or media key (tested); multi-modifier per app via software divert, same as Options+ (tested)
25. Live device writes and Options+ stops only with explicit user go-ahead in the current conversation; user authorised stopping Options+ processes/service only
26. Options+ uninstalled on purpose (user 2026-10-20: "I wanted it gone"); supersedes keeping it for firmware updates (2026-10-15). Slender never depends on Options+: its device pictures are Slender's own copies with a backup; "Update firmware..." only explains that Options+ is needed; no own DFU (still blocked)
27. New scope (user 2026-10-15): 15% mouse charge alert, open custom apps from a key, turn a key off, migrate all Options+ profile settings
28. Options+ import -> one-time, read-only (copy settings.db first, never write Options+ files); unmapped items listed in a report, never silently dropped (agent default)
29. Actions Ring -> own ring drawn by the AOT tray; styles Bezel, Compass, Keypad (round-1 prototypes 1-3) switchable in Settings with colour, opacity and layout options; folder slots open nested apps/web pages/actions like Options+; feature parity researched before build (user 2026-10-15)
30. Builder worktrees -> only under logi-options-slender/.worktrees/<name> (ignored via .git/info/exclude); remove each once merged; nothing else in the personal folder (user 2026-10-15)
31. Gate 6 decisions (rounds R2-R6 of m13) -> orchestrator records fix for every finding the refuter confirms and the Opus adjudicator upholds; asks the user only when recommending waive/dismiss or when a finding argues against a recorded decision (user 2026-10-15)
32. Thumb wheel modes -> per-app mode remembered until flipped back, for the life of the tray process (not persisted); browsers preset to tabs (Ctrl+Tab/Ctrl+Shift+Tab); toggle is an action bound to the top (mode shift) button per app (user 2026-10-15)
33. Ring + thumb wheel -> while the ring is open the thumb wheel moves the highlight one slot per notch; main wheel keeps adjusting volume/brightness slots (user 2026-10-15)
34. Custom firmware -> not pursued (signed-only updates; hardware route irreversible; Slender already covers the gains) (user 2026-10-15)
35. Device probe -> read-only inventory: enumerate every feature (incl. hidden/engineering), call only documented getters from a vetted table, list-only for DFU/host/pairing/reset/onboard-profile and unknown features; own SwId so it runs beside the tray (user 2026-10-15)
36. Device feature round (user 2026-10-15): build mouse feel, precise thumb wheel + touch, K860 extras, raw-XY gestures, and press-vs-hold (Sense Panel: quick press = chosen task, long press = ring). Each new device write gets its own HidppAllowlist entry for the documented setter only; DFU/host/pairing/reset/onboard profiles stay blocked
37. Hosting + OTA (user 2026-10-16) -> public GitHub repo JesseAllomes/logi-options-slender; updates ship as GitHub Releases (anonymous read, no token in the app); release only from a committed, pushed HEAD
38. Public repo hygiene (user 2026-10-16) -> no AI co-author or Claude-Session trailers in this repo's commits; no Windows username, work hostname or SRS paths in tracked files (use placeholders in fixtures)
39. Tab switching (user 2026-10-16) -> new tack: hold Left+Right and turn the thumb wheel; separate Tab scroll and side scroll speed sliders; Excel per-column scroll removed for now; the mode-button toggle stays
40. Excel add-in (user 2026-10-16) -> wanted if it genuinely ends the hangs/crashes: first disable the Logitech add-in and measure Excel stability against the baseline, then build a minimal Slender add-in (no network port, idle unless asked, time-boxed calls). Removing Logitech's add-in is what can stop its faults; ours must never add any
41. Thumb wheel second action (user 2026-10-19) -> press and hold the top button engages the app's second action, release disengages; a quick press does the top button's own job; default is nothing; no switch/toggle function and no Switch with setting (supersedes the per-app latching mode and t3)
42. Thumb wheel card (user 2026-10-19, round 3) -> card 3 variant 2: drawn wheel per mode, per-app side scroll and second action speeds inside the card (7 notches 25-400%), live result preview from the real wheel while the card is open (nothing runs); per-app vertical scroll speed dropped; Point & scroll speed rows removed
43. Ring motion (user 2026-10-19, round 3) -> Off / Pop / Clockwise bloom / Fly in, speed Slowest..Quickest where Quickest = old Normal and each step +0.4x (Normal 1.8x default), close = 3/4 of open; sweep rejected
44. Lost top button release (user 2026-10-19) -> keep the 1.5 s still-wheel Ctrl release; release-on-click alone would make that click a Ctrl+click
45. Research build (user 2026-10-20): build every researched idea except K860 Mac layout (0x4531); full haptic customisation per task/alert/function: pattern + pulses by default; Advanced (hidden by default) has buzz length (pulses close together), spacing, and opt-in exact strength (sets the mouse's stored strength for that buzz and restores it: two writes per buzz)
46. Next build (user 2026-10-20): macros with adjustable pauses (research first), Excel add-in, notification haptics with a Haptics menu listing all apps, per-app DPI, battery estimate, K860 disconnect fix; one Sonnet builder per part in worktrees; sync sign-in tomorrow (user logs in 2026-10-21)
