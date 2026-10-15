using Slender.Engine;
using Slender.Engine.Import;
using Slender.Hidpp;

namespace Slender.Tests.Import;

public class OptionsPlusImporterTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static readonly Lazy<ImportResult> Imported = new(() =>
        OptionsPlusImporter.Import(Fixture("optionsplus-settings.json"), Fixture("optionsplus-macros.json"), SlenderConfig.Default));

    private static ImportResult Result => Imported.Value;

    private static Profile App(string process) => Result.Config.Profiles.Single(p => p.AppProcess == process);

    private static Profile Default => Result.Config.Profiles.Single(p => p.AppProcess is null);

    private static List<string> Notes(ImportNoteKind kind) =>
        Result.Notes.Where(n => n.Kind == kind).Select(n => n.Where + ": " + n.Message).ToList();

    [Fact]
    public void ImportedConfigSurvivesSaveAndLoad()
    {
        string dir = Path.Combine(Path.GetTempPath(), "slender-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Slender.Engine.Config.JsonConfigStore(dir);
            store.Save(Result.Config);
            SlenderConfig loaded = store.Load();

            Assert.Equal(Result.Config.Profiles.Count, loaded.Profiles.Count);
            Profile def = loaded.Profiles.Single(p => p.AppProcess is null);
            Assert.Equal(new ActionSpec.Launch("https://chatgpt.com/"), def.Bindings[ControlId.ModeShift]);
            Assert.Equal(new ActionSpec.Keys("Alt+I"), loaded.Profiles.Single(p => p.AppProcess == "excel").Bindings[ControlId.Back]);
            Assert.Equal(Default.KeyBindings.Count, def.KeyBindings.Count);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExcelBackButtonIsAltI()
    {
        Assert.Equal(new ActionSpec.Keys("Alt+I"), App("excel").Bindings[ControlId.Back]);
        Assert.Equal("Excel", App("excel").DisplayName); // existing Slender display name is kept
        Assert.EndsWith("excel.exe", App("excel").ExePath);
    }

    [Fact]
    public void TeamsButtonsAndThumbWheel()
    {
        var teams = App("ms-teams");
        Assert.Equal(new ActionSpec.Keys("Ctrl+Shift+M"), teams.Bindings[ControlId.Back]);
        Assert.Equal(new ActionSpec.Keys("Ctrl+Shift+O"), teams.Bindings[ControlId.Forward]);
        Assert.Equal(new ActionSpec.Keys("VolumeUp|VolumeDown"), teams.Bindings[SlenderEngine.ThumbWheelBindingId]);
    }

    [Fact]
    public void TeamsModeShiftIsExplicitPassThrough()
    {
        // The default profile opens chatgpt.com on mode shift; Teams restores the native ratchet toggle.
        Assert.Equal(new ActionSpec.None(), App("ms-teams").Bindings[ControlId.ModeShift]);
        Assert.DoesNotContain(Notes(ImportNoteKind.Unmapped), n => n.Contains("Teams") && n.Contains("c196"));
    }

    [Fact]
    public void KeyboardKeysMap()
    {
        var k = Default.KeyBindings;
        Assert.Equal(new ActionSpec.Keys("MediaPrevious"), k[228]);
        Assert.Equal(new ActionSpec.Keys("MediaPlayPause"), k[229]);
        Assert.Equal(new ActionSpec.Keys("MediaNext"), k[230]);
        Assert.Equal(new ActionSpec.Keys("VolumeMute"), k[231]);
        Assert.Equal(new ActionSpec.Keys("VolumeDown"), k[232]);
        Assert.Equal(new ActionSpec.Keys("VolumeUp"), k[233]);
        Assert.Equal(new ActionSpec.Launch("calc.exe"), k[10]);
        Assert.Equal(new ActionSpec.Keys("Win+D"), k[110]);
        Assert.Equal(new ActionSpec.Keys("Win+L"), k[111]);
        Assert.Equal(new ActionSpec.Keys("Win+Shift+S"), k[191]);
        Assert.Equal(new ActionSpec.Keys("Win+S"), k[212]);
        Assert.Equal(new ActionSpec.Keys("Win+Tab"), k[224]);
        Assert.Equal(new ActionSpec.Keys("Menu"), k[234]);
        Assert.Equal(new ActionSpec.Keys("Alt+Tab"), k[256]);
        Assert.Equal(new ActionSpec.Brightness(-10), k[199]);
        Assert.Equal(new ActionSpec.Brightness(10), k[200]);
    }

    [Fact]
    public void DefaultMouseButtons()
    {
        var b = Default.Bindings;
        Assert.Equal(new ActionSpec.Launch("https://chatgpt.com/"), b[ControlId.ModeShift]);
        Assert.Equal(new ActionSpec.ActionsRing(), b[ControlId.SensePanel]);
        Assert.Equal(new ActionSpec.Keys("Win+V"), b[ControlId.Back]);
        Assert.Equal(new ActionSpec.Launch("calc.exe"), b[ControlId.Forward]);
        Assert.False(b.ContainsKey(ControlId.Middle)); // native middle click needs no binding
    }

    [Fact]
    public void GestureClickIsMappedAndSwipesAreReported()
    {
        Assert.Equal(new ActionSpec.Launch("snippingtool.exe"), Default.Bindings[ControlId.Gesture]);
        string swipes = Assert.Single(Notes(ImportNoteKind.Unmapped), n => n.Contains("All apps") && n.Contains("c195") && n.Contains("swipe"));
        Assert.Contains("custom_gesture", swipes);
        Assert.Contains("down: launch Explorer.exe", swipes);
        Assert.Contains("left: Ctrl+Win+Left", swipes.Replace("Win+Ctrl", "Ctrl+Win"));
        Assert.Contains("up: Win+Tab", swipes);
        Assert.Contains(Notes(ImportNoteKind.Approximated), n => n.Contains("snippingtool.exe"));
    }

    [Fact]
    public void ExcelProfileOnlyHoldsWhatDiffersFromDefault()
    {
        var excel = App("excel");
        Assert.Equal(new ActionSpec.Keys("Menu"), excel.Bindings[ControlId.Gesture]);
        Assert.Equal(new ActionSpec.Keys("Ctrl+Home"), excel.Bindings[ControlId.ModeShift]);
        Assert.Equal(new ActionSpec.Keys("Ctrl+Shift+Right"), excel.Bindings[ControlId.Middle]);
        Assert.Equal(new ActionSpec.Keys("Ctrl+Shift+Down"), excel.Bindings[ControlId.Forward]);
        Assert.False(excel.Bindings.ContainsKey(ControlId.SensePanel)); // same as default: inherited
        Assert.False(excel.Bindings.ContainsKey(SlenderEngine.ThumbWheelBindingId));
    }

    [Fact]
    public void KeyboardSequenceMacroBecomesChordSequence()
    {
        // Acrobat's forward button points at "Enter and close tab": Num Enter, wait 3000 ms, Ctrl+W.
        Assert.Equal(new ActionSpec.Keys("Enter, Ctrl+W"), App("acrobat").Bindings[ControlId.Forward]);
        Assert.Contains(Notes(ImportNoteKind.Approximated), n => n.Contains("Adobe Acrobat") && n.Contains("c86") && n.Contains("Enter, Ctrl+W") && n.Contains("delays were dropped"));
        Assert.DoesNotContain(Notes(ImportNoteKind.Unmapped), n => n.Contains("Adobe Acrobat") && n.Contains("c86"));
    }

    [Fact]
    public void SequenceWithMouseStepStaysInReport()
    {
        const string settings = """
        {"profile_keys":["profile-d"],"applications":{"applications":[]},
         "profile-d":{"applicationId":"d","name":"PROFILE_NAME_DEFAULT","assignments":[
           {"slotId":"mx-master-4-2b042_c86","card":{"attribute":"MACRO_REF","id":"m1","name":"Click then copy"}}]}}
        """;
        const string macros = """
        {"macro_infos":{"macroInfos":[{"id":"m1","name":"Click then copy","cards":[
          {"macro":{"type":"INPUT_SEQUENCE","inputSequence":{"componentLists":[{"components":[{"mouse":{"hidUsage":1,"isDown":true}}]}]}}},
          {"macro":{"type":"INPUT_SEQUENCE","inputSequence":{"componentLists":[
            {"components":[{"keyboard":{"displayName":"C","hidUsage":6,"isDown":true}}]},
            {"components":[{"keyboard":{"displayName":"C","hidUsage":6}}]}]}}}]}]}}
        """;
        var r = OptionsPlusImporter.Import(settings, macros, SlenderConfig.Default);
        Assert.False(r.Config.Profiles.Single(p => p.AppProcess is null).Bindings.ContainsKey(ControlId.Forward));
        Assert.Contains(r.Notes, n => n.Kind == ImportNoteKind.Unmapped && n.Message.Contains("Click then copy"));
    }

    [Fact]
    public void SingleChordMacroBecomesKeys()
    {
        const string settings = """
        {"profile_keys":["profile-d"],"applications":{"applications":[]},
         "profile-d":{"applicationId":"d","name":"PROFILE_NAME_DEFAULT","assignments":[
           {"slotId":"mx-master-4-2b042_c86","card":{"attribute":"MACRO_REF","id":"m1","name":"Copy"}}]}}
        """;
        const string macros = """
        {"macro_infos":{"macroInfos":[{"id":"m1","name":"Copy","cards":[{"macro":{"type":"INPUT_SEQUENCE","inputSequence":{"componentLists":[
          {"components":[{"keyboard":{"displayName":"Ctrl","hidUsage":224,"isDown":true}}]},
          {"components":[{"keyboard":{"displayName":"C","hidUsage":6,"isDown":true}}]},
          {"components":[{"keyboard":{"displayName":"C","hidUsage":6}}]},
          {"components":[{"keyboard":{"displayName":"Ctrl","hidUsage":224}}]}]}}}]}]}}
        """;
        var r = OptionsPlusImporter.Import(settings, macros, SlenderConfig.Default);
        Assert.Equal(new ActionSpec.Keys("Ctrl+C"), r.Config.Profiles.Single(p => p.AppProcess is null).Bindings[ControlId.Forward]);
    }

    [Fact]
    public void DeviceSettings()
    {
        var c = Result.Config;
        Assert.True(c.Haptics.Enabled);
        Assert.Equal(1, c.Haptics.Level); // 45 of 100 -> notch 1 of 0-3
        Assert.True(c.Haptics.OnButtonPress);
        Assert.True(c.Haptics.OnActionsRing);
        Assert.True(c.Pointer.InvertThumbWheel); // dir NATURAL
        Assert.False(c.Pointer.InvertWheel); // dir STANDARD
        Assert.True(c.Pointer.SmoothScroll);
        Assert.True(c.Pointer.SmartShiftEnabled);
        Assert.Equal(SlenderConfig.Default.Pointer.SmartShiftThreshold, c.Pointer.SmartShiftThreshold);
        Assert.Null(c.Pointer.Dpi);
        var unmapped = Notes(ImportNoteKind.Unmapped);
        Assert.Contains(unmapped, n => n.Contains("mouse_settings") && n.Contains("0.6"));
        Assert.Contains(unmapped, n => n.Contains("SmartShift sensitivity 61"));
        Assert.Contains(unmapped, n => n.Contains("keyboard_settings") && n.Contains("keepKeyboardInOsLayout"));
        Assert.Contains(unmapped, n => n.Contains("'gestures'"));
    }

    [Fact]
    public void RadialMenuItemsAndMechanicalDevicesAreReported()
    {
        Assert.Contains(Notes(ImportNoteKind.Unmapped), n => n.Contains("c408") && n.Contains("Ctrl+X"));
        Assert.Contains(Notes(ImportNoteKind.Unmapped), n => n.Contains("c415") && n.Contains("folder"));
        Assert.Equal(4, Result.Notes.Count(n => n.Kind == ImportNoteKind.Skipped && n.Where.StartsWith("mx-mechanical-")));
        Assert.DoesNotContain(Result.Config.Profiles.SelectMany(p => p.Bindings.Keys), k => k is >= 408 and <= 415);
    }

    [Fact]
    public void SlenderOnlySettingsSurvive()
    {
        var current = SlenderConfig.Default with
        {
            LaunchAtLogin = true,
            Excel = new ExcelSettings(false, 3),
            BatteryAlerts = new BatteryAlertSettings(false, 30, 25),
            Keyboard = new KeyboardSettings(true),
            Profiles = [.. SlenderConfig.Default.Profiles, new Profile("notepad", new Dictionary<ushort, ActionSpec>()) { DisplayName = "Notepad" }],
        };
        var r = OptionsPlusImporter.Import(Fixture("optionsplus-settings.json"), Fixture("optionsplus-macros.json"), current);
        Assert.True(r.Config.LaunchAtLogin);
        Assert.Equal(new ExcelSettings(false, 3), r.Config.Excel);
        Assert.Equal(new BatteryAlertSettings(false, 30, 25), r.Config.BatteryAlerts);
        Assert.True(r.Config.Keyboard.FnLock);
        Assert.Contains(r.Config.Profiles, p => p.AppProcess == "notepad");
        // The built-in Excel sheet buttons were replaced by the imported Excel profile.
        Assert.Single(r.Config.Profiles, p => p.AppProcess == "excel");
        Assert.Equal(new ActionSpec.Keys("Alt+I"), r.Config.Profiles.Single(p => p.AppProcess == "excel").Bindings[ControlId.Back]);
    }

    [Fact]
    public void ReportListsEverythingByKind()
    {
        string report = Result.FormatReport();
        Assert.Contains("Not imported", report);
        Assert.Contains("Imported with changes", report);
        Assert.Contains("Skipped", report);
    }

    [Fact]
    public void MissingDefaultProfileLeavesConfigUntouched()
    {
        var r = OptionsPlusImporter.Import("""{"profile_keys":[]}""", null, SlenderConfig.Default);
        Assert.Same(SlenderConfig.Default, r.Config);
        Assert.Single(r.Notes);
    }
}
