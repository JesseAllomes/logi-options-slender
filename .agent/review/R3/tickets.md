## r32
TICKET r32  (severity medium)
FILES:     tests/Slender.Tests/Sync/SyncTests.cs (append only)
DEFECT:    SyncManifest.Save's retry-on-locked-file branch and its give-up path (src/Slender.Sync/SyncModels.cs ~lines 130-146) have no test.
CHANGE:    1. Append [Fact] Manifest_save_retries_while_the_file_is_briefly_locked: temp dir; save once; open the target with FileShare.None, release it after ~40 ms from a Task.Run (Task.Delay then Dispose); Save again -> no exception and the file content is the new manifest.
           2. Append [Fact] Manifest_save_gives_up_when_the_file_stays_locked: hold the target with FileShare.None for the whole call; Assert.ThrowsAny<IOException> or UnauthorizedAccessException (use Record.Exception and assert it is one of the two); dispose the handle in finally; temp dir deleted in finally.
TEST:      the new facts; pass with current code.
DO NOT:    touch src files or other tests.
DONE WHEN: the facts pass reliably (run them 3 times) and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r32: <reason>"

## r33
TICKET r33  (severity medium)
FILES:     tests/Slender.Tests/Hidpp/DpiTests.cs (append only)
DEFECT:    LogiMouse.GetDpiAsync (src/Slender.Hidpp/LogiMouse.cs ~lines 89-101) has no test: the connected read, the disconnected null, the dpi > 0 check and the HidppException catch.
CHANGE:    1. Using the file's existing fake transport and 0x2201 fn2 handler (see the test near line 13), append facts: (a) connected and the device reports 1600 -> GetDpiAsync returns 1600; (b) not connected -> null; (c) device reports 0 -> null; (d) the fn2 request faults with a HID++ error -> null (no exception).
TEST:      the new facts; pass with current code.
DO NOT:    touch src files or other tests.
DONE WHEN: the facts pass and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r33: <reason>"

## r34
TICKET r34  (severity medium)  (apply after r38)
FILES:     tests/Slender.Tests/Engine/RingButtonPairingTests.cs (new)
DEFECT:    The ring-capture swallow logic (a swallowed press always swallows its release, even after capture ends; an unpaired release passes through) has no test; it is the logic that causes stuck mouse buttons if it regresses.
CHANGE:    1. New tests against RingButtonPairing (created by r38): (a) capture on: LBUTTONDOWN swallowed with LeftDown; capture off; LBUTTONUP 100 ms later swallowed. (b) capture off, no prior press: LBUTTONUP passes through. (c) capture on: RBUTTONDOWN swallowed; RBUTTONUP 6 s later passes through and the flag is cleared (a second RBUTTONUP also passes). (d) wheel swallowed only while capture is on. (e) other messages (WM_MOUSEMOVE) never swallowed.
TEST:      the new facts.
DO NOT:    touch src files.
DONE WHEN: the facts pass and the suite passes.
IF BLOCKED: stop, leave nothing behind, return "BLOCKED r34: <reason>"

## r36
TICKET r36  (severity medium)  (also covers r39: same defect, same lines)
FILES:     src/Slender.Tray/UpdateCoordinator.cs (ctor ~line 79-95, ApplyAsync lines 236-256); src/Slender.Tray/TrayApplication.cs (near line 172-173, PrepareForUpdateRestart); src/Slender.Tray/Program.cs line 129-131 (UpdateCoordinator construction); tests/Slender.Tests/Tray/UpdateCoordinatorTests.cs (Harness Create and append)
DEFECT:    ApplyAsync runs _applying (TrayApplication.PrepareForUpdateRestart -> RemoveIcon) before _backend.ApplyAndRestart(); if that throws, the icon stays removed and the failure balloon is dropped (ShowBalloon returns when !_iconAdded), leaving Slender running with no icon, menu or message.
CHANGE:    1. UpdateCoordinator: add an optional ctor parameter `Action? applyFailed = null` (after `applying`), stored in a field; in ApplyAsync's catch, call `_applyFailed?.Invoke();` BEFORE `_notify(UpdateNoticeKind.Info, ApplyFailedText);` (inside its own try/catch that logs).
           2. TrayApplication: add `public void RestoreAfterFailedRestart() => Post(AddIcon);` with doc "Puts the tray icon back when Restart to update failed after PrepareForUpdateRestart removed it." (use the class's existing Post-to-UI helper; AddIcon must run on the UI thread). Because NotifyUpdate also posts its balloon, the icon is re-added before the balloon is shown.
           3. Program.cs: pass `applyFailed: app.RestoreAfterFailedRestart` to the UpdateCoordinator constructor.
           4. Tests: let the Harness pass an applyFailed callback that records calls and the order relative to Notices; append [Fact] Failed_restart_restores_the_icon_before_saying_so: backend whose ApplyAndRestart throws; after ApplyAsync, applying was called once, applyFailed was called once, and it was called before the ApplyFailedText notice was recorded.
TEST:      the new fact; fails before CHANGE.
DO NOT:    change ApplyOnExitIfReady, other notices, or other files.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r36: <reason>"

## r37
TICKET r37  (severity medium)
FILES:     src/Slender.Engine/KeyboardRouter.cs lines 75-97 (debounce + OnForegroundChanged) and a clock field/ctor parameter; tests/Slender.Tests/Engine/KeyboardRouterTests.cs (append only)
DEFECT:    The keyboard router recomputes its divert set on a foreground change with no held-key guard; un-diverting a key that is held loses its release, leaving a key-down or mouse-button-down action stuck. SlenderEngine already guards this (RedivertUnlessHeld, HeldDivertWaitMs = 10_000).
CHANGE:    1. Mirror SlenderEngine's pattern exactly (read SlenderEngine.cs lines 130-180 first): add `internal const long HeldDivertWaitMs = 10_000;`, an optional `Func<long>? clock = null` ctor parameter (last; default Environment.TickCount64), and replace the body so OnForegroundChanged calls a ScheduleForegroundApply(since: _clock()) that debounces as today and then calls ApplyUnlessHeld(since): `bool held; lock (_gate) held = _active.Count > 0; if (held && _clock() - since < HeldDivertWaitMs && ForegroundDebounceMs > 0) { ScheduleForegroundApply(since); return; } Apply(force: false);`
           2. Doc comment as in SlenderEngine.
           3. Append [Fact] Keyboard_divert_change_waits_while_a_key_is_held, copying the shape of SlenderEngine's Divert_change_waits_while_a_diverted_control_is_held (tests/Slender.Tests/Engine/DeviceMilestoneTests.cs): ForegroundDebounceMs = 20; a key bound in the default profile and explicit None in a "teams" profile; press it, switch foreground to teams, wait 200 ms -> divert set unchanged; release -> divert set changes.
TEST:      the new fact; fails before CHANGE.
DO NOT:    change Apply, Handle or other files.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r37: <reason>"

## r38
TICKET r38  (severity low)  (apply before r34)
FILES:     src/Slender.Engine/LowLevelMouseHook.cs lines 38-44 (SetRingCapture doc) and 164-189 (RingMouse) plus the _leftSwallowed/_rightSwallowed fields; src/Slender.Engine/RingButtonPairing.cs (new)
DEFECT:    _leftSwallowed/_rightSwallowed persist forever when a swallowed press's release never reaches the hook (secure-desktop switch, hook timeout), so the next unrelated real release is swallowed and that click stays pressed in the app. The logic is also untestable inside the hook callback.
CHANGE:    1. New `internal sealed class RingButtonPairing` (pure, no Win32 calls; follow the style of src/Slender.Engine/HookButtonPairing.cs): method `public RingDecision OnMessage(int msg, uint timeMs, bool capture)` returning a small `internal readonly record struct RingDecision(bool Swallow, HookRingKind? Kind)`. Rules: LBUTTONUP/RBUTTONUP when the matching press was swallowed -> clear the flag and Swallow = true only if `unchecked(timeMs - pressTime) < StaleMs` (StaleMs = 5_000; a release later than that is treated as unrelated and passes through); otherwise, when !capture -> (false, null); LBUTTONDOWN/RBUTTONDOWN -> record the press time, set the flag, (true, LeftDown/RightDown); MOUSEWHEEL -> (true, Wheel); anything else -> (false, null). Use the NativeMethods WM_* constants.
           2. LowLevelMouseHook.RingMouse: delegate to a `private readonly RingButtonPairing _ringPairing = new();` using data.Time as timeMs; keep the wheel delta extraction and the RingInput invoke in RingMouse; remove the two bool fields.
           3. Update the SetRingCapture doc: "... A swallowed press swallows its release, even after capture ended, unless the release comes more than 5 s later (then it belongs to another click)."
TEST:      covered by ticket r34.
DO NOT:    change other hook paths (buttons, moves, horizontal wheel) or other files.
DONE WHEN: builds with -warnaserror and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r38: <reason>"

## r39
TICKET r39  (severity medium)
FILES:     none beyond ticket r36
DEFECT:    Duplicate of r36 (TrayApplication.PrepareForUpdateRestart removes the icon before a restart that can fail).
CHANGE:    1. No edit of its own: ticket r36 fixes it. Apply r36 first, then confirm RestoreAfterFailedRestart exists next to PrepareForUpdateRestart and report APPLIED r39 (covered by r36).
TEST:      r36's fact.
DO NOT:    make any other change.
DONE WHEN: r36 is applied.
IF BLOCKED: return "BLOCKED r39: <reason>"

## r40
TICKET r40  (severity medium)
FILES:     src/Slender.Tray/TrayApplication.cs (near RefreshDpiAsync, lines ~717-740: add one public method); src/Slender.Tray/Program.cs line ~153 (SetDpiAsync on config change)
DEFECT:    The device DPI in status.json is read only on connect; after Settings changes or resets pointer speed it goes stale, so Settings shows the wrong current DPI.
CHANGE:    1. TrayApplication: `public void RefreshDpi() => Post(() => _ = RefreshDpiAsync());` with doc "Re-reads the device DPI after Slender changed it, so status.json stays current."
           2. Program.cs line ~153: chain the refresh after the set: `_ = mouse.SetDpiAsync(changed.Pointer.Dpi, CancellationToken.None).ContinueWith(_ => app.RefreshDpi(), TaskScheduler.Default);` (keep any existing condition around the call; if `app` is not in scope at that point, use whatever reference to the TrayApplication instance is in scope there).
TEST:      none practical (needs the device); suite must pass.
DO NOT:    change RefreshDpiAsync, timers or other files.
DONE WHEN: builds with -warnaserror, suite passes, a NativeAOT publish of src/Slender.Tray (output inside the repo, deleted after) has no new warnings.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r40: <reason>"

## r41
TICKET r41  (severity low)
FILES:     src/Slender.Engine/SlenderEngine.cs lines 516-525 (OnConnection); tests/Slender.Tests/Engine/DeviceMilestoneTests.cs (append only)
DEFECT:    _active (held HID++ controls) is never cleared when the mouse disconnects, so a press whose release is lost keeps the held-check true and delays every per-app divert change by 10 s; any held action (e.g. a mouse-button or key-down) also never gets its release.
CHANGE:    1. In OnConnection, when !connected: take and clear the held entries under _gate (`KeyValuePair<ushort, ActionSpec>[] held; lock (_gate) { held = [.. _active]; _active.Clear(); }`), then for each entry whose action is not None and not ActionsRing, post its release through the same path HandleButton uses for a release (`_queue.Post(() => _executor.Execute(action, false))`, each inside the existing try/catch style). For an ActionsRing entry call RaiseRing(false) the same way the release path does.
           2. Comment: "The device is gone: its held controls will never report a release, so release their actions now."
           3. Append [Fact] Disconnect_releases_held_controls_and_unblocks_divert_changes: engine with ModeShift bound to Keys("F2") in the default profile (reuse TeamsNone() from this file) and a recording FakeExecutor; press ModeShift; mouse.Connect(false) -> the executor saw Execute(F2, pressed: false); then with ForegroundDebounceMs = 20, switch foreground to "teams" -> the divert set changes within 500 ms (not after 10 s).
TEST:      the new fact; fails before CHANGE.
DO NOT:    change RedivertUnlessHeld or other files.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r41: <reason>"

