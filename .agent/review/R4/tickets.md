## r43
TICKET r43  (severity medium)
FILES:     tests/Slender.Tests/Settings/OptionsPlusImportFlowTests.cs (append only)
DEFECT:    PluginServiceIcons.For's IOException/UnauthorizedAccessException catch (src/Slender.Settings/Logic/PluginServiceIcons.cs:36) has no test.
CHANGE:    1. Append [Fact] Unreadable_action_image_gives_no_icon: construct PluginServiceIcons with a temp directory and a readBytes delegate that throws IOException (the ctor takes `readBytes`, see lines 19-23); For(profile, "x") returns null and HasPending is false. Build the PluginServiceProfile the way the existing icon test near lines 70-90 does.
TEST:      the new fact; passes with current code.
DO NOT:    touch src files or other tests.
DONE WHEN: the fact exists and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r43: <reason>"

## r44
TICKET r44  (severity medium)
FILES:     tests/Slender.Tests/Settings/OptionsPlusImportFlowTests.cs (append only)
DEFECT:    PluginServiceIcons.Save's directory-creation failure (src/Slender.Settings/Logic/PluginServiceIcons.cs:55-62) has no test.
CHANGE:    1. Append [Fact] Icon_save_skips_quietly_when_the_folder_cannot_be_made: in a temp dir create a FILE named "ring"; construct PluginServiceIcons with directory = that file's path and a readBytes delegate returning a few bytes; call For(...) so something is pending, then Save() -> no exception, and the "ring" file is unchanged. Clean up in finally.
TEST:      the new fact; passes with current code.
DO NOT:    touch src files or other tests.
DONE WHEN: the fact exists and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r44: <reason>"

## r45
TICKET r45  (severity low)
FILES:     tests/Slender.Tests/Settings/SettingsLabelTests.cs (new; r46 and r47 append to it)
DEFECT:    ActionCatalog.LaunchName (src/Slender.Settings/Logic/ActionCatalog.cs:143-169) has no direct test; its shell:, URL, www-strip, trailing-slash and empty-name branches are unexercised.
CHANGE:    1. New test class SettingsLabelTests with a [Theory] over LaunchName: read the method first and assert the exact strings it returns today for: an .exe path; "shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"; "https://www.example.com/"; "https://example.com/path/"; a folder path with a trailing backslash; and an input whose derived name is empty.
TEST:      the new theory; passes with current code.
DO NOT:    touch src files.
DONE WHEN: the theory passes and the suite passes.
IF BLOCKED: stop, leave nothing behind, return "BLOCKED r45: <reason>"

## r46
TICKET r46  (severity low)  (after r45)
FILES:     tests/Slender.Tests/Settings/SettingsLabelTests.cs (append to the class r45 created)
DEFECT:    RingGlyphs.AutoGlyph's Launch, Brightness, Disabled and Text arms (src/Slender.Settings/Logic/RingGlyphs.cs:93-96) have no test.
CHANGE:    1. Add a [Theory] or facts asserting AutoGlyph(slot) for: Launch(URL) -> "E774"; Launch(exe) -> "E8A7"; Brightness(+10); Disabled; Text("x") -> the exact glyph codes the code returns today (read RingGlyphs.cs first).
TEST:      the new cases; pass with current code.
DO NOT:    touch src files.
DONE WHEN: the cases pass and the suite passes.
IF BLOCKED: stop, return "BLOCKED r46: <reason>"

## r47
TICKET r47  (severity low)  (after r45)
FILES:     tests/Slender.Tests/Settings/SettingsLabelTests.cs (append to the class r45 created)
DEFECT:    PointerMapping.DefaultText (src/Slender.Settings/Logic/PointerMapping.cs:23) has no test.
CHANGE:    1. Add facts: DefaultText(null) == "Default"; DefaultText(1200) == "Default (1200 DPI)".
TEST:      the new facts; pass with current code.
DO NOT:    touch src files.
DONE WHEN: the facts pass and the suite passes.
IF BLOCKED: stop, return "BLOCKED r47: <reason>"

## r48
TICKET r48  (severity medium)
FILES:     src/Slender.Settings/MainWindow.Pages.cs lines 150-187 (pointer speed row in the Point & scroll page builder)
DEFECT:    deviceDpi is captured once when the page is built and never refreshed, so after the user drags the speed and then clicks Default (which leaves the mouse at the dragged value) the readout says "Default (old DPI)" and the slider jumps to the old value. The tray now re-reads the DPI after every change and publishes it in status.json.
CHANGE:    1. Replace the captured `deviceDpi`/`restDpi` locals with a local function `int? DeviceDpi() => _status.Device("mx-master-4")?.Dpi;` and `int RestDpi() => DeviceDpi() is { } dd ? PointerMapping.SnapDpi(dd) : PointerMapping.DpiShown;`; use RestDpi() for the slider's initial value, ShowDefault uses DefaultText(DeviceDpi()), the reset handler uses RestDpi().
           2. While the speed is Default, follow status changes: add a handler `void OnDpiStatus() { if (_config.Config.Pointer.Dpi is null) { setting = true; dpi.Value = RestDpi(); setting = false; ShowDefault(true); } }`, subscribe it to `_status.Changed` when the row is built, and unsubscribe in `dpiRow.Unloaded` (so a rebuilt page does not leak handlers). If _status.Changed is raised off the UI thread, marshal with DispatcherQueue.TryEnqueue (check how OnStatusChanged in MainWindow.xaml.cs is invoked and match it).
TEST:      none (WinUI view code); the suite must pass.
DO NOT:    change PointerMapping, other rows or other files.
DONE WHEN: builds with -warnaserror and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r48: <reason>"

## r49
TICKET r49  (severity medium)
FILES:     src/Slender.Settings/MainWindow.Picker.cs lines 217-241 (MoreTab: box, Save, Enter handler)
DEFECT:    Enter in the URL/folder/app box calls Save() even when Save is disabled (text unchanged), and Save always commits Launch(text) with no Arguments, so an imported Launch with arguments silently loses them.
CHANGE:    1. Enter handler: `if (e.Key == VirtualKey.Enter) { e.Handled = true; if (save.IsEnabled) Save(); }`
           2. Save: keep the arguments when the target is unchanged: `string? args = own is ActionSpec.Launch l && string.Equals(l.Target, text, StringComparison.Ordinal) ? l.Arguments : null; commit(new ActionSpec.Launch(text, args));`
TEST:      none (WinUI view code); the suite must pass.
DO NOT:    change the browse handler or other tabs.
DONE WHEN: builds with -warnaserror and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r49: <reason>"

## r50
TICKET r50  (severity low)
FILES:     src/Slender.Settings/MainWindow.Picker.cs lines 55-91 (ShowPicker) and the "default" tab builder in BuildTab; src/Slender.Settings/Logic/ProfileOps.cs (only if a helper is needed); tests/Slender.Tests/Settings/ProfileOpsPassThroughTests.cs (new)
DEFECT:    For an app-profile control that holds an explicit None (pass-through, e.g. imported from Options+), the picker pre-selects "Same as All apps (...)"; the stage label correctly shows the native function. Clicking that option commits null, silently deleting the pass-through so the control follows All apps.
CHANGE:    1. Read how the "default" tab is built in BuildTab and how PickerSpec carries defaultText. For app profiles (target != null) the Default tab offers two options: "Same as All apps (<inherited>)" which commits null (unchanged), and "Normal <NativeText> in <app>" which commits `new ActionSpec.None()`. The selected one is the pass-through option when `ProfileOps.IsPassThrough(_config.Config, target, _kind, info.Id)` is true, otherwise the Same-as option (as today). Add the minimal field to PickerSpec (e.g. `string? PassThroughText`, `bool IsPassThrough`) to carry this; for All apps (target null) nothing changes.
           2. Confirm ProfileOps.SetBinding with ActionSpec.None stores the explicit None for an app profile (pass-through) and with null removes it; if SetBinding treats None as removal, add a parameter or a separate ProfileOps.SetPassThrough so the new option really stores None.
           3. New tests: SetBinding/SetPassThrough(app, id, None) -> IsPassThrough true and the config keeps the None entry; SetBinding(app, id, null) -> IsPassThrough false and the entry is gone.
TEST:      the new tests; the second step's behaviour is pinned.
DO NOT:    change the stage label, other tabs, or other files.
DONE WHEN: builds with -warnaserror and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r50: <reason>"

## r51
TICKET r51  (severity low)
FILES:     src/Slender.Settings/MainWindow.Stage.cs lines 173-186 (swipe strip)
DEFECT:    The four swipe StageItems never get a Position (default 0,0), but arrow-key navigation (OnStageKeyDown, ~line 684) ranks candidates by Position, so arrows cannot move between swipe labels and can jump to them wrongly.
CHANGE:    1. When creating each swipe item set `swipeItem.Position = new Point(stripLeft + i * (cell + 8) + cell / 2, h - LabelHeight / 2 - 6);` (the centre of its label, same coordinate space the render hotspots use for Position; check how hotspot items set Position and match it).
TEST:      none (WinUI view code); the suite must pass.
DO NOT:    change navigation logic or other files.
DONE WHEN: builds with -warnaserror and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r51: <reason>"

