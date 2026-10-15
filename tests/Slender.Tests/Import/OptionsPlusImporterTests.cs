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
    public void GestureClickIsMapped()
    {
        Assert.Equal(new ActionSpec.Launch("snippingtool.exe"), Default.Bindings[ControlId.Gesture]);
        Assert.Contains(Notes(ImportNoteKind.Approximated), n => n.Contains("snippingtool.exe"));
        Assert.DoesNotContain(Notes(ImportNoteKind.Unmapped), n => n.Contains("swipe actions were not imported"));
    }

    [Fact]
    public void DefaultGestureSwipesAreImported()
    {
        var s = Default.GestureSwipes;
        Assert.Equal(4, s.Count);
        Assert.Equal(new ActionSpec.Keys("Win+Tab"), s[GestureDirection.Up]);
        Assert.Equal(new ActionSpec.Launch("Explorer.exe"), s[GestureDirection.Down]);
        Assert.IsType<ActionSpec.Keys>(s[GestureDirection.Left]);
        Assert.IsType<ActionSpec.Keys>(s[GestureDirection.Right]);
        Assert.Contains("Left", ((ActionSpec.Keys)s[GestureDirection.Left]).Chord);
        Assert.Contains("Right", ((ActionSpec.Keys)s[GestureDirection.Right]).Chord);
    }

    [Fact]
    public void TeamsGesturePresetOnlyHoldsWhatDiffersFromDefault()
    {
        var teams = Result.Config.Profiles.Single(p => p.AppProcess is not null && p.AppProcess.Contains("teams", StringComparison.OrdinalIgnoreCase));
        // The Start menu (a bare Win key) cannot be sent as a chord: reported, and the default swipe is switched off here.
        Assert.Equal(new ActionSpec.None(), teams.GestureSwipes[GestureDirection.Up]);
        Assert.Contains(Notes(ImportNoteKind.Unmapped), n => n.Contains("Teams") && n.Contains("swipe up"));
        Assert.Equal(new ActionSpec.Keys("Win+D"), teams.GestureSwipes[GestureDirection.Down]);
        Assert.False(teams.GestureSwipes.ContainsKey(GestureDirection.Left)); // same as default: inherited
        Assert.False(teams.GestureSwipes.ContainsKey(GestureDirection.Right));
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
        Assert.Equal(45, c.Haptics.Strength); // the exact Options+ strength carries over
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
    public void RadialMenuItemsBecomeTheDefaultRingAndMechanicalDevicesAreReported()
    {
        Assert.DoesNotContain(Notes(ImportNoteKind.Unmapped), n => n.Contains("c408") || n.Contains("c415"));
        Assert.Equal(4, Result.Notes.Count(n => n.Kind == ImportNoteKind.Skipped && n.Where.StartsWith("mx-mechanical-")));
        Assert.DoesNotContain(Result.Config.Profiles.SelectMany(p => p.Bindings.Keys), k => k is >= 408 and <= 415);

        IReadOnlyList<RingSlot> ring = Default.RingSlots!;
        Assert.Equal(8, ring.Count);
        Assert.Equal(new ActionSpec.Keys("Ctrl+X"), ring[0].Action);
        Assert.Equal(new ActionSpec.Keys("Ctrl+C"), ring[1].Action);
        Assert.Equal(new ActionSpec.Keys("Ctrl+V"), ring[2].Action);
        Assert.Equal(new ActionSpec.MouseButton("Forward"), ring[3].Action);
        Assert.Equal(new ActionSpec.MouseButton("Back"), ring[4].Action);
        Assert.Equal(new ActionSpec.Keys("Ctrl+Z"), ring[5].Action);
        Assert.Equal(new ActionSpec.Keys("Ctrl+Y"), ring[6].Action);
        Assert.True(ring[7].IsFolder);
        Assert.Equal("Folder", ring[7].Name);
        Assert.All(ring[7].Folder!, s => Assert.True(s.IsEmpty));
        Assert.All(Result.Config.Profiles.Where(p => p.AppProcess is not null), p => Assert.Null(p.RingSlots));
    }

    private static string RingJson(string folderCard) => """
        {"profile_keys":["p"],"p":{"applicationId":"420fd454-0c36-499d-bde4-146823b16147","name":"PROFILE_NAME_DEFAULT","assignments":[
          {"slotId":"radial-menu-virtual-device-10000000_c409","card":{"attribute":"MACRO_PLAYBACK","id":"x","name":"ASSIGNMENT_NAME_PASTE","macro":{"type":"KEYSTROKE","keystroke":{"code":25,"modifiers":[224]}}}},
          {"slotId":"radial-menu-virtual-device-10000000_c415","card":
        """ + folderCard + "}]}}";

    private static ImportResult ImportRing(string folderCard) =>
        OptionsPlusImporter.Import(RingJson(folderCard), null, SlenderConfig.Default);

    [Fact]
    public void FolderChildrenAndCustomNameAreImportedInOrder()
    {
        ImportResult r = ImportRing("""
            {"attribute":"FOLDER","id":"card_global_presets_folder","name":"AI Actions","nestedCardsOrder":["b","a"],"nestedCards":{
              "a":{"attribute":"MACRO_PLAYBACK","id":"a","macro":{"type":"OPEN_WEB_PAGE","openWebPage":{"url":"https://a.example/"}}},
              "b":{"attribute":"MACRO_PLAYBACK","id":"b","macro":{"type":"MEDIA","media":{"usage":"MUTE"}}}}}
            """);

        RingSlot[] ring = [.. r.Config.Profiles.Single(p => p.AppProcess is null).RingSlots!];
        Assert.Equal(new ActionSpec.Keys("Ctrl+V"), ring[1].Action);
        Assert.True(ring[0].IsEmpty);
        RingSlot folder = ring[7];
        Assert.Equal("AI Actions", folder.Name);
        Assert.Equal(8, folder.Folder!.Count);
        Assert.Equal(new ActionSpec.Keys("VolumeMute"), folder.Folder[0].Action);
        Assert.Equal(new ActionSpec.Launch("https://a.example/"), folder.Folder[1].Action);
        Assert.True(folder.Folder[2].IsEmpty);
    }

    [Fact]
    public void AnUnmappableRingChildIsReportedAndLeftEmpty()
    {
        ImportResult r = ImportRing("""
            {"attribute":"FOLDER","name":"ASSIGNMENT_NAME_FOLDER","nestedCards":{
              "a":{"attribute":"MACRO_PLAYBACK","id":"a","macro":{"type":"PLUGIN_THING"}}}}
            """);

        RingSlot folder = r.Config.Profiles.Single(p => p.AppProcess is null).RingSlots![7];
        Assert.Equal("Folder", folder.Name);
        Assert.True(folder.Folder![0].IsEmpty);
        Assert.Contains(r.Notes, n => n.Kind == ImportNoteKind.Unmapped && n.Where.Contains("c415") && n.Message.Contains("left empty"));
    }

    [Fact]
    public void ProfilesWithoutRingAssignmentsKeepTheirRing()
    {
        var current = SlenderConfig.Default with
        {
            Profiles = [.. SlenderConfig.Default.Profiles, new Profile("notepad", new Dictionary<ushort, ActionSpec>()) { RingSlots = [.. Enumerable.Repeat(RingSlot.Empty, 8)] }],
        };
        ImportResult r = OptionsPlusImporter.Import(RingJson("""{"attribute":"FOLDER","name":"ASSIGNMENT_NAME_FOLDER"}"""), null, current);

        Assert.NotNull(r.Config.Profiles.Single(p => p.AppProcess == "notepad").RingSlots);
        Assert.Null(r.Config.Profiles.Single(p => p.AppProcess == "excel").RingSlots);
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
    public void Macro_name_that_is_only_a_prefix_of_the_card_is_not_matched()
    {
        static MacroLibrary Library(string name) => MacroLibrary.Parse(
            """{"macro_infos":{"macroInfos":[{"id":"m1","name":"NAME","cards":[]}]}}""".Replace("NAME", name), []);

        Assert.Null(Library("Enter").Find("unknown", "Enter and close tab (Ado"));
        Assert.NotNull(Library("Enter and close tab (Adobe)").Find("unknown", "Enter and close tab (Ado"));
    }

    [Fact]
    public void Radial_slot_outside_the_ring_is_reported()
    {
        const string settings = """
        {"profile_keys":["profile-d"],"applications":{"applications":[]},
         "profile-d":{"applicationId":"d","name":"PROFILE_NAME_DEFAULT","assignments":[
           {"slotId":"radial-menu-virtual-device-10000000_c416","card":{"attribute":"MACRO_PLAYBACK","id":"x","name":"X","macro":{"type":"KEYSTROKE","keystroke":{"code":25,"modifiers":[224]}}}}]}}
        """;
        var r = OptionsPlusImporter.Import(settings, null, SlenderConfig.Default);
        Assert.Single(r.Notes, n => n.Kind == ImportNoteKind.Unmapped && n.Message.Contains("c416"));
    }

    [Fact]
    public void Folder_with_more_than_eight_children_keeps_eight_and_reports_the_rest()
    {
        const string template = """{"attribute":"MACRO_PLAYBACK","id":"iN","name":"ItemN","macro":{"type":"KEYSTROKE","keystroke":{"code":CODE,"modifiers":[224]}}}""";
        string nested = string.Join(",", Enumerable.Range(1, 9).Select(i =>
            "\"i" + i + "\":" + template.Replace("iN", "i" + i).Replace("ItemN", "Item" + i).Replace("CODE", (3 + i).ToString())));
        string order = string.Join(",", Enumerable.Range(1, 9).Select(i => "\"i" + i + "\""));
        ImportResult r = ImportRing("{\"attribute\":\"FOLDER\",\"name\":\"Big\",\"nestedCardsOrder\":[" + order + "],\"nestedCards\":{" + nested + "}}");

        RingSlot folder = r.Config.Profiles.Single(p => p.AppProcess is null).RingSlots![7];
        Assert.Equal(8, folder.Folder!.Count);
        Assert.Equal(new ActionSpec.Keys("Ctrl+A"), folder.Folder[0].Action);
        ImportNote note = Assert.Single(r.Notes, n => n.Kind == ImportNoteKind.Unmapped && n.Message.Contains("Slender folders hold"));
        Assert.Contains("item9", note.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Continuous_horizontal_gesture_item_is_reported()
    {
        const string settings = """
        {"profile_keys":["profile-d"],"applications":{"applications":[]},
         "profile-d":{"applicationId":"d","name":"PROFILE_NAME_DEFAULT","assignments":[
           {"slotId":"mx-master-4-2b042_c195","card":{"attribute":"ONE_OF","id":"g","selectedNestedCard":"custom_gesture","nestedCards":{
             "custom_gesture":{"attribute":"MACRO_PLAYBACK","id":"custom_gesture","nestedCards":{
               "up":{"attribute":"MACRO_PLAYBACK","id":"u","macro":{"type":"KEYSTROKE","keystroke":{"code":25,"modifiers":[224]}}},
               "horizontal":{"attribute":"MACRO_PLAYBACK","id":"h","macro":{"type":"KEYSTROKE","keystroke":{"code":25,"modifiers":[224]}}}}}}}}]}}
        """;
        var r = OptionsPlusImporter.Import(settings, null, SlenderConfig.Default);
        Assert.Contains(r.Notes, n => n.Kind == ImportNoteKind.Unmapped && n.Where.EndsWith(" horizontal", StringComparison.Ordinal) && n.Message.Contains("continuous horizontal"));
    }

    [Fact]
    public void MissingDefaultProfileLeavesConfigUntouched()
    {
        var r = OptionsPlusImporter.Import("""{"profile_keys":[]}""", null, SlenderConfig.Default);
        Assert.Same(SlenderConfig.Default, r.Config);
        Assert.Single(r.Notes);
    }
}
