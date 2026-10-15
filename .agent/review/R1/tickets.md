## r1
TICKET r1  (severity medium)
FILES:     tests/Slender.Tests/Engine/LaunchDisabledTests.cs (append only)
DEFECT:    ActionExecutor.Execute's empty-Launch-target branch (src/Slender.Engine/ActionExecutor.cs:78) has no test.
CHANGE:    1. Append one [Fact] Empty_launch_target_is_logged_and_not_opened using the file's existing fakes/ListLog: Execute(new ActionSpec.Launch(""), true) and Execute(new ActionSpec.Launch("   "), true); assert the fake shell was never opened and the log contains "launch target is empty".
TEST:      the new fact; passes with current code (it documents the branch).
DO NOT:    touch other files, change existing tests.
DONE WHEN: the fact exists and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r1: <reason>"

## r2
TICKET r2  (severity medium)
FILES:     src/Slender.Engine/ActionExecutor.cs lines 26-38 (fields, ctor) and 46-53 (SendSteps); tests/Slender.Tests/Engine/LaunchDisabledTests.cs (append only)
DEFECT:    SendSteps calls the static KeyInjector.SendChord (line 51), so its failure branch ("SendInput failed", stop the sequence) cannot be tested.
CHANGE:    1. Add field `private readonly Func<IReadOnlyList<KeyStroke>, bool> _sendChord;` and an optional ctor parameter `Func<IReadOnlyList<KeyStroke>, bool>? sendChord = null` (last), assigned `_sendChord = sendChord ?? KeyInjector.SendChord;`.
           2. Line 51: `KeyInjector.SendChord(steps[i])` -> `_sendChord(steps[i])`. Any other KeyInjector.SendChord call inside ActionExecutor for a single chord: same substitution.
           3. Append a [Fact] Sequence_stops_at_the_first_failed_step: executor with sendChord that records calls and returns false on the 2nd call; Execute(new ActionSpec.Keys("Enter, Ctrl+W, Esc"), true) (check the sequence syntax used by existing tests); assert exactly 2 sends happened and the log has "SendInput failed".
TEST:      the new fact; fails before CHANGE (no seam).
DO NOT:    change public behaviour, rename KeyInjector, touch other files.
DONE WHEN: the fact passes and all existing tests pass.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r2: <reason>"

## r3
TICKET r3  (severity medium)
FILES:     tests/Slender.Tests/Engine/DeviceMilestoneTests.cs (BrightnessTests: FakeDevice around lines 111-117; append a fact)
DEFECT:    BrightnessController.Apply's refused-set branch (src/Slender.Engine/Brightness.cs:94-95) is never exercised; FakeDevice.TrySet always returns true.
CHANGE:    1. Give FakeDevice an optional `bool acceptSet = true` ctor parameter; TrySet returns false (level unchanged) when it is false, still recording the call.
           2. Append [Fact] Refused_set_is_logged_and_not_retried: device with acceptSet=false, Adjust(+10) via the existing synchronous `run` pattern; assert the level is unchanged, the log contains "refused brightness", and a second Adjust still calls TrySet (the monitor is not marked unsupported).
TEST:      the new fact; passes with current code.
DO NOT:    touch src files or other tests.
DONE WHEN: the fact exists and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r3: <reason>"

## r4
TICKET r4  (severity medium)
FILES:     tests/Slender.Tests/Engine/DeviceMilestoneTests.cs (append only)
DEFECT:    ActionSpecJsonConverter.ReadInt's throw (src/Slender.Engine/Config/ActionSpecJsonConverter.cs:98) for a brightness entry without a numeric "step" has no test.
CHANGE:    1. Append [Fact] Brightness_entry_without_step_is_rejected: deserialise {"type":"brightness"} and {"type":"brightness","step":"ten"} through the converter the way the existing round-trip test near line 195 does; Assert.Throws<FormatException> for both, message contains "step".
TEST:      the new fact; passes with current code.
DO NOT:    touch src files or other tests.
DONE WHEN: the fact exists and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r4: <reason>"

## r5
TICKET r5  (severity medium)
FILES:     tests/Slender.Tests/Config/ConfigV2Tests.cs (append only)
DEFECT:    JsonConfigStore.ParseSwipes' unknown-direction throw (src/Slender.Engine/Config/JsonConfigStore.cs:163-165) has no test.
CHANGE:    1. Append [Fact] Unknown_gesture_direction_is_rejected_on_load: save SlenderConfig.Default with Store(), rewrite config.json so the default profile has "gestureSwipes": {"diagonal": {"type":"keys","chord":"F5"}}, then Load(). Read JsonConfigStore.Load (lines 34-60) first: if a FormatException on load is quarantined (see Future_version_is_quarantined_and_default_returned near line 108), assert Default returned and a ".bad-" file present; otherwise Assert.Throws<FormatException>.
TEST:      the new fact; passes with current code.
DO NOT:    touch src files or other tests.
DONE WHEN: the fact exists and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r5: <reason>"

## r6
TICKET r6  (severity medium)
FILES:     tests/Slender.Tests/Import/OptionsPlusImporterTests.cs (append only)
DEFECT:    OptionsPlusImporter.Ring.cs:74-78 (a folder with more than 8 children drops the extras and adds an Unmapped note "Slender folders hold ...") is untested; the existing folder test near line 252 uses 2 children.
CHANGE:    1. Append [Fact] Folder_with_more_than_eight_children_keeps_eight_and_reports_the_rest: build a minimal settings JSON in the test (copy the shape of the folder test near line 252) with a FOLDER card at radial slot c415 holding 9 nestedCards (keystroke cards named Item1..Item9, nestedCardsOrder in order); import; assert the folder has 8 children, child 0 is Item1, and Notes has one Unmapped note whose message contains "Slender folders hold" and "Item9".
TEST:      the new fact; passes with current code.
DO NOT:    touch src files, fixtures or other tests.
DONE WHEN: the fact exists and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r6: <reason>"

## r7
TICKET r7  (severity low)
FILES:     tests/Slender.Tests/Import/OptionsPlusImporterTests.cs (append only)
DEFECT:    OptionsPlusImporter.Gestures.cs:53-54 (a continuous "horizontal" gesture item is reported as Unmapped) is not asserted by any test.
CHANGE:    1. Append [Fact] Continuous_horizontal_gesture_item_is_reported: if the fixture has a gesture ONE_OF card whose selected preset's inner "horizontal" carries a macro, assert Result.Notes has an Unmapped note whose Where ends with " horizontal" and whose Message contains "continuous horizontal"; otherwise build a minimal JSON in the test (copy the shape of an existing gesture test in this file).
TEST:      the new fact; passes with current code.
DO NOT:    touch src files, fixtures or other tests.
DONE WHEN: the fact exists and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r7: <reason>"

## r9
TICKET r9  (severity medium)
FILES:     src/Slender.Tray/Ring/RingSlotText.cs lines 101-110 (IconFile); src/Slender.Tray/Ring/RingIcons.cs RingIconCache.Get (near lines 194-202); tests/Slender.Tests/Tray/RingIconFileTests.cs (new)
DEFECT:    Opening the ring extracts file icons synchronously on the UI thread; a UNC or offline-network .exe/.lnk target stalls the tray for the SMB timeout and makes the PC authenticate to that share just by opening the ring.
CHANGE:    1. RingSlotText.IconFile: return null when the resolved path starts with "\\" (UNC) or Path.IsPathFullyQualified is false, for both the "file:" form and the Launch-target form; the slot then shows its fallback glyph. Add one sentence to the XML doc: network paths are never probed.
           2. RingIconCache.Get: before extracting, if !File.Exists(path) cache a 0 result for that path and return 0, so a missing local file is probed once and never again. Keep the existing cache behaviour for hits.
           3. New test file with a [Theory] over IconFile: Icon "file:\\server\share\app.exe" -> null; Launch("\\server\tools\x.exe") -> null; Launch("C:\Tools\x.exe") -> "C:\Tools\x.exe"; Icon "file:C:\img\a.png" -> "C:\img\a.png"; Launch("relative\x.exe") -> null.
TEST:      the new theory; the UNC cases fail before CHANGE.
DO NOT:    add threading, change RingRenderer or RingController, touch other files.
DONE WHEN: the theory passes, the suite passes, and a NativeAOT publish of src/Slender.Tray (into a folder inside the repo that you then delete) shows no new warnings.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r9: <reason>"

## r10
TICKET r10  (severity medium)
FILES:     src/Slender.Engine/Brightness.cs lines 26-46 (doc, fields, ctor) and 81-92 (Apply); tests/Slender.Tests/Engine/DeviceMilestoneTests.cs (append only)
DEFECT:    One failed Open/TryGet puts the monitor in _unsupported for the life of the process (line 89), so a transient DDC/CI failure (monitor waking, input switch) disables brightness keys on it until restart.
CHANGE:    1. Replace HashSet<nint> _unsupported with Dictionary<nint, long> _unsupportedUntil; add public const long UnsupportedRetryMs = 30_000; add an optional ctor parameter Func<long>? clock = null stored as _clock = clock ?? (() => Environment.TickCount64).
           2. Line 85: if (_unsupportedUntil.TryGetValue(monitor, out var until) && _clock() < until) return;
           3. Line 89: _unsupportedUntil[monitor] = _clock() + UnsupportedRetryMs; log text -> "does not support brightness control (or did not answer); retrying in 30 s".
           4. On a successful TryGet: _unsupportedUntil.Remove(monitor);
           5. Class doc: "Unsupported monitors are logged once and ignored." -> "... ignored for 30 s, then tried again."
           6. Append [Fact] Failed_read_is_retried_after_thirty_seconds: fake clock; device whose TryGet fails the first time and succeeds after; Adjust at t=0 (ignored, logged), Adjust at t=10 s (no TryGet call), Adjust at t=31 s (TryGet called, level changed).
TEST:      the new fact; fails before CHANGE.
DO NOT:    change the coalescing logic, touch other files.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r10: <reason>"

## r11
TICKET r11  (severity medium)
FILES:     src/Slender.Tray/UpdateCoordinator.cs lines 256-272 (ApplyOnExitIfReady); tests/Slender.Tests/Tray/UpdateCoordinatorTests.cs (append only)
DEFECT:    ApplyOnExitIfReady schedules the silent apply without asking Slender.Settings to close, unlike ApplyAsync (lines 236-240), so an open Settings window can hold files in the install folder while Velopack replaces them.
CHANGE:    1. Add public async Task ApplyOnExitIfReadyAsync(); keep ApplyOnExitIfReady() as a sync wrapper that calls ApplyOnExitIfReadyAsync().GetAwaiter().GetResult() (the tray is exiting; blocking is acceptable) so Program.cs needs no change.
           2. Inside, before _backend.ApplyOnExit(): if (_settings.IsRunning()) await _settings.CloseAsync(SettingsCloseTimeout).ConfigureAwait(false); proceed regardless, logging Info "Settings still running; the update applies when it closes" when CloseAsync returned false.
           3. Summary: "Asks Settings to close first so the install folder is free."
           4. Append [Fact] Exit_apply_asks_settings_to_close_first using the file's existing fake settings/backend: settings running -> CloseAsync called once, then ApplyOnExit called; settings not running -> CloseAsync not called.
TEST:      the new fact; fails before CHANGE.
DO NOT:    change ApplyAsync, Program.cs, or notification text; touch other files.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r11: <reason>"

## r13
TICKET r13  (severity low)
FILES:     src/Slender.Engine/Import/PluginServiceRingImporter.cs lines 444-453 (LaunchStep) and the caller that turns a Step.Open into a slot; tests/Slender.Tests/Import/PluginServiceRingImporterTests.cs (append only)
DEFECT:    "target||arguments||working folder": the third field is discarded with no note, breaking D27 (nothing silently dropped).
CHANGE:    1. In LaunchStep read string? workDir = withArguments && parts.Length > 2 && parts[2].Trim().Length > 0 ? parts[2].Trim() : null;
           2. Give Step.Open an optional string? Note (fit the existing Step record shape); when workDir is set attach "Working folder '<workDir>' is not supported; the app starts in its own folder."
           3. Where Step.Open becomes a RingSlot, add that note as ImportNoteKind.Approximated with the slot's Where.
           4. Append [Fact] Launch_with_working_folder_imports_and_reports_it: a profile JSON (copy the shape of an existing ExecuteApplication test in this file) whose step is "C:\tools\run.exe||--cfg x||D:\work"; assert Launch("C:\tools\run.exe", "--cfg x") and one Approximated note containing "D:\work".
TEST:      the new fact; fails before CHANGE.
DO NOT:    change Launch's contract, touch other files.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r13: <reason>"

## r14
TICKET r14  (severity low)
FILES:     src/Slender.Engine/Import/MacroLibrary.cs lines 81-91 (Find); tests/Slender.Tests/Import/OptionsPlusImporterTests.cs (append only)
DEFECT:    The fallback name.StartsWith(m.Name) lets a short or empty macro name match any card name that begins with it, importing the wrong macro silently (breaks D27).
CHANGE:    1. Line 87: var hits = _macros.Where(m => name.Length >= 4 && m.Name.Length >= name.Length && m.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList();  (the card name is the truncated one: the macro's full name must start with it, never the reverse).
           2. Comment: "Card names can be truncated; accept a macro whose full name starts with the card name, never the reverse."
           3. Append [Fact] Macro_name_that_is_only_a_prefix_of_the_card_is_not_matched: MacroLibrary from JSON with one macro named "Enter" and a card named "Enter and close tab (Ado" with an unknown id -> Find returns null; with a macro named "Enter and close tab (Adobe)" instead -> found.
TEST:      the new fact; the first assertion fails before CHANGE.
DO NOT:    touch other files.
DONE WHEN: the fact passes and the suite passes (the existing Acrobat macro test must still pass).
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r14: <reason>"

## r15
TICKET r15  (severity medium)
FILES:     src/Slender.Engine/Ring/RingSession.cs lines 148-157 (OnWheel) plus one new field; tests/Slender.Tests/Ring/RingSessionTests.cs (append only)
DEFECT:    Every wheel event counts as at least one notch (line 154), so hi-res wheel reports (delta ~15 with SmoothScroll on) fire about 8 steps per physical notch on volume/brightness slots.
CHANGE:    1. Add private int _wheelRemainder; reset to 0 where the session opens and whenever the selected slot changes (SetSelected).
           2. OnWheel: _wheelRemainder += delta; var notches = Math.Clamp(Math.Abs(_wheelRemainder) / 120, 0, 10); if (notches == 0) return; var action = _wheelRemainder > 0 ? pair.Up : pair.Down; _wheelRemainder -= Math.Sign(_wheelRemainder) * notches * 120; then the existing loop.
           3. Doc: "Deltas accumulate to whole 120-unit notches so hi-res wheels step once per notch."
           4. Append [Fact] Hires_wheel_deltas_accumulate_to_one_step_per_notch: open on an adjustable slot as the existing wheel test does; eight OnWheel(15) -> exactly one Adjust; then OnWheel(-120) -> one Down adjust; OnWheel(60) -> none.
TEST:      the new fact; fails before CHANGE.
DO NOT:    change RingWheel, touch other files.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r15: <reason>"

## r16
TICKET r16  (severity medium)
FILES:     src/Slender.Engine/Ring/RingSession.cs lines 89-99 (hold release); tests/Slender.Tests/Ring/RingSessionTests.cs (append only)
DEFECT:    The baseline pointer sample sets Selected (OnPointer first=true), so a hold released without any movement activates whatever slot the cursor started over (MonitorCentre position or an edge-clamped ring).
CHANGE:    1. Lines 96-98: comment "// Hold: release on a slot the pointer moved to runs it; release without movement, or in the centre, cancels." then if (_sawSlot && Selected is { } s) Activate(s); else Close(RingCloseReason.Cancelled);
           2. Leave OnPointer unchanged (the first sample still highlights; _sawSlot stays false until a later sample lands on a slot).
           3. Append [Fact] Hold_released_without_movement_cancels_even_over_a_slot: press trigger, baseline OnPointer at a slot position (dx=0, dy=-100), advance past TapMaxMs, release -> no Activate, closed with Cancelled; mirror case where a second OnPointer onto a slot precedes release -> Activate.
TEST:      the new fact; fails before CHANGE.
DO NOT:    change tap behaviour or OnClick, touch other files.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r16: <reason>"

## r17
TICKET r17  (severity medium)
FILES:     src/Slender.Engine/WindowsMonitorBrightness.cs lines 100-131 (LcdBrightness) plus P/Invoke declarations in the same file
DEFECT:    TryGet always reads the AC level (line 120) and TrySet writes both AC and DC (line 127, policy 3); on battery a step starts from the AC value and overwrites the user's separate levels.
CHANGE:    1. Add [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s); with the struct (ACLineStatus byte first, BatteryFlag, BatteryLifePercent, SystemStatusFlag, uint BatteryLifeTime, uint BatteryFullLifeTime) and private static bool OnBattery() => GetSystemPowerStatus(out var s) && s.ACLineStatus == 0;
           2. TryGet: percent = OnBattery() ? current[2] : current[1];  (DISPLAY_BRIGHTNESS: policy, AC, DC)
           3. TrySet: write only the active policy: on battery { 2 /* DISPLAYPOLICY_DC */, 0, level }, otherwise { 1 /* DISPLAYPOLICY_AC */, level, 0 }.
           4. Comment both with the policy values.
TEST:      none possible without a panel (say so in the report); the suite must still pass.
DO NOT:    change the DDC/CI path, touch other files.
DONE WHEN: builds with -warnaserror, suite passes, a NativeAOT publish of the tray shows no new warnings.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r17: <reason>"

## r18
TICKET r18  (severity low)
FILES:     src/Slender.Engine/Import/OptionsPlusImporter.cs lines 153-161; tests/Slender.Tests/Import/OptionsPlusImporterTests.cs (append only)
DEFECT:    A radial-menu slot with a parseable id outside c408-c415 is skipped with no note (line 156 skips ConvertButton when isRadial; ConvertRing skips it too), breaking D27.
CHANGE:    1. Line 156: if (!isRadial) ConvertButton(...); else if (cid is < 408 or > 415) notes.Add(new(ImportNoteKind.Unmapped, where, $"Actions ring entry c{cid} is outside the 8 ring slots; not imported: {Compact(card)}"));
           2. Append [Fact] Radial_slot_outside_the_ring_is_reported: minimal settings JSON with a keystroke card at slotId radial-menu-virtual-device-10000000_c416; assert one Unmapped note containing "c416".
TEST:      the new fact; fails before CHANGE.
DO NOT:    touch OptionsPlusImporter.Ring.cs or other files.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r18: <reason>"

## r19
TICKET r19  (severity low)
FILES:     src/Slender.Settings/Logic/OptionsPlusImportFlow.cs lines 125-142 (CountChanges) plus a small helper; tests/Slender.Tests/Settings/OptionsPlusImportFlowTests.cs (append only)
DEFECT:    The preview change count ignores GestureSwipes, so an import whose only change is swipes reports "0 profiles and 0 bindings".
CHANGE:    1. Add private static bool SameSwipes(IReadOnlyDictionary<GestureDirection, ActionSpec> a, IReadOnlyDictionary<GestureDirection, ActionSpec> b) (same count, every key present, Equals on the records).
           2. Line 132: add && SameSwipes(old.GestureSwipes, p.GestureSwipes).
           3. Line 137: + p.GestureSwipes.Count.
           4. Update the summary comment to mention swipes.
           5. Append [Fact] Swipes_count_as_changes_in_the_preview: before = Default; after = Default with the All-apps profile given one GestureSwipes entry; CountChanges -> (1, 1).
TEST:      the new fact; fails before CHANGE.
DO NOT:    touch other files.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r19: <reason>"

## r20
TICKET r20  (severity low)
FILES:     src/Slender.Tray/UpdateCoordinator.cs lines 171-174 and the text constants near line 57; tests/Slender.Tests/Tray/UpdateCoordinatorTests.cs (append only)
DEFECT:    An on-demand check that arrives while a check or download is running returns silently, so "Check for updates" looks dead.
CHANGE:    1. Add public const string BusyText = "Already checking for updates"; next to the other texts.
           2. Lines 171-174: if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) { if (onDemand) _notify(UpdateNoticeKind.Info, BusyText); return; }
           3. Append [Fact] On_demand_check_while_busy_says_so: start a check whose backend CheckAsync awaits a TaskCompletionSource, call the on-demand check, assert one Info notice equal to BusyText, then complete the TCS.
TEST:      the new fact; fails before CHANGE.
DO NOT:    change scheduling, touch other files.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r20: <reason>"

