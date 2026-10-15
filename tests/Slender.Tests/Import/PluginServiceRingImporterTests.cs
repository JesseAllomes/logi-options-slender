using Slender.Engine;
using Slender.Engine.Import;

namespace Slender.Tests.Import;

public class PluginServiceRingImporterTests
{
    private static readonly string Store = Path.Combine(AppContext.BaseDirectory, "Fixtures", "pluginservice");

    private static readonly Lazy<IReadOnlyList<PluginServiceProfile>> Profiles = new(() => PluginServiceStore.Read(Store));

    private static (SlenderConfig Config, List<ImportNote> Notes) Run(SlenderConfig? current = null, Func<PluginServiceProfile, string, string?>? icons = null)
    {
        var notes = new List<ImportNote>();
        return (PluginServiceRingImporter.Apply(Profiles.Value, current ?? SlenderConfig.Default, notes, icons), notes);
    }

    private static readonly Lazy<(SlenderConfig Config, List<ImportNote> Notes)> Imported = new(() => Run());

    private static IReadOnlyList<RingSlot> DefaultRing => Imported.Value.Config.Profiles.Single(p => p.AppProcess is null).RingSlots!;

    private static IReadOnlyList<RingSlot> AppRing(string process) => Imported.Value.Config.Profiles.Single(p => p.AppProcess == process).RingSlots!;

    private static List<string> Notes(ImportNoteKind kind) =>
        Imported.Value.Notes.Where(n => n.Kind == kind).Select(n => n.Where + ": " + n.Message).ToList();

    [Fact]
    public void ReadsTheRingDeviceAppsWithDefaultFirst()
    {
        Assert.Equal(["@_defaultwin", "@_excel", "@_powerpoint", "@_word-1daa6242ea4e470f"], Profiles.Value.Select(p => p.AppName).ToArray());
        Assert.Equal("Excel", Profiles.Value[1].DisplayName);
    }

    [Fact]
    public void ControlsBecomeSlotsInOrder()
    {
        Assert.Equal(Profile.RingSlotCount, DefaultRing.Count);
        string[] names = DefaultRing.Select(s => s.Name ?? "").ToArray();
        Assert.Equal(["Explore AI", "Switch Device", "New Note", "Lock", "Logi Options+", "Play / pause", "Folders and Apps", "WebLinks"], names);
    }

    [Fact]
    public void FoldersKeepTheirChildrenInOrder()
    {
        RingSlot ai = DefaultRing[0];
        Assert.True(ai.IsFolder);
        Assert.Equal(Profile.RingSlotCount, ai.Folder!.Count);
        Assert.Equal(["Claude", "ChatGPT", "Microsoft 365 Copilot"], ai.Folder.Take(3).Select(c => c.Name ?? "").ToArray());
        Assert.Equal(new ActionSpec.Launch(@"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude"), ai.Folder[0].Action);
        Assert.StartsWith("https://example-", ((ActionSpec.Launch)ai.Folder[3].Action).Target);
        Assert.All(ai.Folder.Skip(4), c => Assert.True(c.IsEmpty));

        RingSlot links = DefaultRing[7];
        Assert.Equal(8, links.Folder!.Count(c => !c.IsEmpty));
        Assert.All(links.Folder!.Where(c => c.Action is ActionSpec.Launch l && l.Target.StartsWith("http", StringComparison.Ordinal)),
            c => Assert.Matches(@"^https://example-\d+\.test", ((ActionSpec.Launch)c.Action).Target));
    }

    [Fact]
    public void AppsAndFilesLaunch()
    {
        Assert.Equal(new ActionSpec.Launch(@"C:\Program Files\LogiOptionsPlus\logioptionsplus.exe"), DefaultRing[4].Action);
        RingSlot apps = DefaultRing[6];
        Assert.Equal(new ActionSpec.Launch(@"C:\Program Files\Google\Chrome\Application\chrome.exe"), apps.Folder![0].Action);
        Assert.Equal("Excel", apps.Folder[3].Name);
        Assert.IsType<ActionSpec.Launch>(apps.Folder[7].Action); // a folder path
    }

    [Fact]
    public void NativeActionsMapToSlenderActions()
    {
        Assert.Equal(new ActionSpec.Keys("Win+L"), DefaultRing[3].Action);
        Assert.Equal(new ActionSpec.Keys("MediaPlayPause"), DefaultRing[5].Action);
    }

    [Fact]
    public void KeystrokeProfileActionsBecomeChords()
    {
        IReadOnlyList<RingSlot> excel = AppRing("excel");
        Assert.Equal(new ActionSpec.Keys("Ctrl+C"), excel[0].Action);
        Assert.Equal(new ActionSpec.Keys("Ctrl+Minus"), excel[2].Action);
        Assert.Contains(Notes(ImportNoteKind.Approximated), n => n.Contains("numpad minus"));
        Assert.Equal(new ActionSpec.Keys("Ctrl+Alt+V"), excel[7].Action);
    }

    [Fact]
    public void LaunchPlusKeysKeepsTheLaunchAndKeyNamesParse()
    {
        // the "New Note" macro is: open OneNote, wait, Ctrl+N
        Assert.Equal(new ActionSpec.Launch(@"C:\Program Files\Microsoft Office\root\Office16\ONENOTE.EXE"), DefaultRing[2].Action);
        Assert.Contains(Notes(ImportNoteKind.Approximated), n => n.Contains("New Note") && n.Contains("dropped"));

        Assert.True(PluginServiceRingImporter.TryChord("ControlOrCommand+KeyN___67699721___Ctrl+N___", out string chord, out _));
        Assert.Equal("Ctrl+N", chord);
        Assert.True(PluginServiceRingImporter.TryChord("ControlOrCommand+Shift+Key5___1___x___mac", out chord, out _));
        Assert.Equal("Ctrl+Shift+5", chord);
        Assert.False(PluginServiceRingImporter.TryChord("ControlOrCommand+Control___1___x", out _, out _));
    }

    [Fact]
    public void UnmappableItemsAreReportedAndLeftEmpty()
    {
        // Easy-Switch devices in the "Switch Device" folder, Excel table gallery adjustments and unsupported commands
        Assert.True(DefaultRing[1].IsFolder);
        Assert.All(DefaultRing[1].Folder!, c => Assert.True(c.IsEmpty));
        Assert.True(AppRing("excel")[3].IsEmpty);
        Assert.True(AppRing("excel")[5].IsEmpty);

        List<string> unmapped = Notes(ImportNoteKind.Unmapped);
        Assert.Contains(unmapped, n => n.Contains("Switch Device") && n.Contains("Easy"));
        Assert.Contains(unmapped, n => n.Contains("PasteValuesOnly"));
        Assert.Contains(unmapped, n => n.Contains("Word ring") && n.Contains("left as it was"));
    }

    [Fact]
    public void AppProfilesAreMatchedOrCreatedByProcess()
    {
        Profile excel = Imported.Value.Config.Profiles.Single(p => p.AppProcess == "excel");
        Assert.Equal(SlenderConfig.Default.Profiles.Single(p => p.AppProcess == "excel").Bindings, excel.Bindings); // bindings untouched

        Profile ppt = Imported.Value.Config.Profiles.Single(p => p.AppProcess == "powerpnt");
        Assert.Equal("PowerPoint", ppt.DisplayName);
        Assert.Equal(new ActionSpec.Keys("Ctrl+Z"), ppt.RingSlots![0].Action);
        Assert.True(ppt.RingSlots[2].IsEmpty); // Bring to front has no shortcut

        // Word's ring is only plug-in virtual commands: no profile is made for it
        Assert.DoesNotContain(Imported.Value.Config.Profiles, p => p.AppProcess == "winword");
    }

    [Fact]
    public void ImportingAgainReplacesTheRingAndIsStable()
    {
        RingSlot[] stale = Enumerable.Repeat(new RingSlot(new ActionSpec.Keys("Ctrl+Q")) { Name = "Old" }, Profile.RingSlotCount).ToArray();
        SlenderConfig before = SlenderConfig.Default with
        {
            Profiles = SlenderConfig.Default.Profiles.Select(p => p with { RingSlots = stale }).ToList(),
        };

        (SlenderConfig first, _) = Run(before);
        (SlenderConfig second, _) = Run(first);

        Profile def = first.Profiles.Single(p => p.AppProcess is null);
        Assert.Equal("Explore AI", def.RingSlots![0].Name);
        Assert.Equal(first.Profiles.Count, second.Profiles.Count);
        Assert.Equal(def.RingSlots.Select(s => s.Name), second.Profiles.Single(p => p.AppProcess is null).RingSlots!.Select(s => s.Name));
    }

    [Fact]
    public void IconsComeFromTheSuppliedCallback()
    {
        var asked = new List<string>();
        (SlenderConfig config, _) = Run(icons: (_, action) =>
        {
            asked.Add(action);
            return action.EndsWith("77B246FCB5104A83B009581493755F13", StringComparison.Ordinal) ? @"file:C:\assets\ring\a.png" : null;
        });

        Assert.Equal(@"file:C:\assets\ring\a.png", config.Profiles.Single(p => p.AppProcess is null).RingSlots![4].Icon);
        Assert.Null(config.Profiles.Single(p => p.AppProcess is null).RingSlots![3].Icon);
        Assert.Contains(asked, a => a.StartsWith("$@Generic___@ProfileAction___", StringComparison.Ordinal)); // folders ask too
    }

    [Fact]
    public void TheStoreIsOnlyReadThroughTheInjectedCopy()
    {
        // an "original" store whose files are garbage: reading it directly would fail to parse
        string fake = Path.Combine(Path.GetTempPath(), "slender-fake-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string real in Directory.EnumerateFiles(Store, "*.json", SearchOption.AllDirectories))
            {
                string target = Path.Combine(fake, Path.GetRelativePath(Store, real));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, "this is not json");
            }

            var copied = new List<string>();
            IReadOnlyList<PluginServiceProfile> read = PluginServiceStore.Read(fake, (source, destination) =>
            {
                copied.Add(source);
                File.Copy(Path.Combine(Store, Path.GetRelativePath(fake, source)), destination);
            });

            Assert.Equal(4, read.Count);
            Assert.Equal(8, copied.Count); // ApplicationInfo.json and ProfileInfo.json of each app
            Assert.All(copied, c => Assert.StartsWith(fake, c));
            Assert.All(read, p => Assert.StartsWith("{", p.ProfileJson.TrimStart()));
        }
        finally
        {
            Directory.Delete(fake, recursive: true);
        }
    }

    [Fact]
    public void MissingStoreReadsAsNothing()
    {
        Assert.Empty(PluginServiceStore.Read(Path.Combine(Path.GetTempPath(), "slender-no-store-" + Guid.NewGuid().ToString("N"))));
    }
}
