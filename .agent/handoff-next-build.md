# Handoff (updated 2026-10-22, resume in a fresh session)

State 2026-10-22: main 216e066 pushed, 2116 tests, -warnaserror clean, no worktrees open. Latest release v1.1.42 (another
session: device registry m52/m53, history scrub m56). Released from this session: 1.1.39 (Fn lock no longer reset on
start/connect + Fn+Esc live in popover/Settings, window min 1166x770, labels/tooltips, slider wheel m51, haptics redesign m50),
1.1.40 (version labels, full window opens on a screen it fits, exact calibration load), 1.1.41 (strength glitch: real stored
strength tracked; notification haptics on Win11 26300 via EVENT_OBJECT_UNCLOAKED + app from toast's first text).
m49 3CX ringer built and merged (216e066), NOT released: release 1.1.43 waits on the user. Then the user sets it up
(Haptics > 3CX RINGER > Set up, Edge Load unpacked) and one real call decides whether the headset (hid) signal works.
Next queued by the other session: m54 Yealink module (touches the same Devices/Tray/Settings areas; merge carefully).

Older state: Slender 1.1.38 released, 1919 tests.
1.1.37 shipped macros (m44), Excel add-in (m37), notification haptics (m45), per-app DPI + battery estimate (m46),
K860 disconnect fix (m47), K860 onboard remaps in the app (m48) and the gap fixes (Excel macro picker, add-in copy refresh,
macro reference scrub, real DPI steps in Settings, onboard reset from the 0x1C00 status byte, Excel pipe worker lazy).
1.1.38 added HapticCue.Repeats / RepeatGapMs (pulse group repeated with a pause, 15 s cap) for a phone ringer.

## Waiting on the user

- **3CX ringer (m49): built and merged 216e066; release 1.1.43 and live setup + one real call pending.** Plan below (as built).
- Device/UI checks of everything above (nothing new has been seen on screen or on the devices): remaps on one key first,
  per-app pointer speed, macro editor/recorder, battery lines, notification haptics (what name the first toasts show), Excel
  add-in (opt-in, Excel closed; first real run unproven).
- Memory: measure the 1.1.38 tray once installed and idle, against 1.1.36 = 39.7 MB WS / 12.1 MB private.
- Gate 6 review: run only when the user says; priorities are the first line of DC:DECISIONS.
- Sync sign-in (m14) today 2026-10-21. m7 stays open until next week's payrun.
- Older user tests: name haptic waveforms 0-14, Easy-Switch names, Win11 native double buzz, Caps/Num for the 0x4220 log.

## 3CX ringer plan (m49)

User's phone is the 3CX web client installed as an Edge PWA (Default profile); a Yealink USB headset base also rings.
Findings from reading the web client's public bundle (main.*.js; not run):
- The client drives the Yealink over WebHID: `device.sendReport(reportId, data)` with output usages, logged as
  "Send Actions to Headset on report N: <usage> (0x..) : true/false". Ring on/off = the Telephony Ringer usage (0x0B:0x9E).
- Its call notification (`new Notification(title, {requireInteraction: true, tag: localConnectionId})`) is created only
  when the window is NOT focused, closed when the call leaves Ringing (answered anywhere / ended) and auto-closed after 30 s.
  With push enabled a service-worker push notification is used instead (tags "call-<id>").
- So the WinEvent toast route (m45) misses calls while 3CX is focused and stops at 30 s.

Build (one Sonnet builder, own worktree):
1. MV3 Edge extension, unpacked, matches only the user's 3CX origin. Slender writes the manifest locally from a Settings
   field; the PBX hostname never enters the repo. Main-world script wraps `HIDDevice.prototype.sendReport`, decodes the
   Ringer bit from `device.collections` output report items, emits ring-start / ring-stop; fallback wraps `Notification`
   (call notifications only: requireInteraction + numeric tag) and its close(). Logs only which signal fired, never call data.
2. Native messaging host: tiny AOT exe registered per user (HKCU\Software\Microsoft\Edge\NativeMessagingHosts), one-shot
   `sendNativeMessage` per event, forwards to the buzz pipe and exits. No port, nothing idle between calls.
3. Tray: a ringer state: plays the app's ringer cue from ring-start until ring-stop, a mouse click or key (existing hook),
   or a safety cap; ringer cues are not cut by ordinary buzzes. Settings: 3CX section (origin, ringer cue, install folder,
   steps: Edge developer mode -> Load unpacked).
4. First real call with the user decides which signal is reliable. Work browser policy allows unpacked extensions and
   native messaging (checked read-only); a security-monitoring extension means IT can see it (user informed).

## Builder rules (unchanged)

Sonnet builder per part in `.worktrees/<name>` on `wt-<name>`, removed once merged; build `dotnet build Slender.slnx
-warnaserror`, test `dotnet test Slender.slnx --nologo` (per-user SDK on PATH); never touch real devices, desktop, GUIs or
processes they did not start; plain commits with NO AI/Co-Authored-By/Claude-Session trailers; no usernames, hostnames or
work paths in tracked files; never push/merge; never edit AGENTS.md, CLAUDE.md, .gitignore, .agent/, .grok/. Time-box
research and commit increments (a builder once stalled for hours with no edits). Memory to the max and flawless function;
the Excel add-in must never lock up, freeze or crash Excel. Release: `scripts/Build-Setup.ps1 -Version 1.1.N` from a
clean, pushed HEAD.
