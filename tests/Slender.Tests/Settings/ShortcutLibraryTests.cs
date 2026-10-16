using Slender.Engine;
using Slender.Settings.Logic;
using Xunit;

namespace Slender.Tests.Settings;

public class ShortcutLibraryTests
{
    public static TheoryData<string, string> AllChords()
    {
        var data = new TheoryData<string, string>();
        foreach (ShortcutGroup g in ShortcutLibrary.AllGroups)
        {
            foreach (Shortcut s in g.Items)
            {
                data.Add(g.Id, s.Chord);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllChords))]
    public void EveryChord_ParsesForTheEngine(string group, string chord)
    {
        Assert.True(ChordParser.TryParse(chord, out _), $"{group}: '{chord}' does not parse");
    }

    [Theory]
    [MemberData(nameof(AllChords))]
    public void EveryChord_IsInRecorderOrder(string group, string chord)
    {
        // The recorder writes Win, Ctrl, Alt, Shift, key; a preset in the same form matches a recorded chord exactly.
        string[] parts = chord.Split('+');
        string key = parts[^1];
        string[] mods = parts[..^1];
        string[] order = ["Win", "Ctrl", "Alt", "Shift"];
        Assert.Equal(mods.OrderBy(m => Array.IndexOf(order, m)), mods);
        Assert.All(mods, m => Assert.Contains(m, order));
        Assert.DoesNotContain(key, order);
        _ = group;
    }

    [Fact]
    public void NoGroup_RepeatsAChord()
    {
        foreach (ShortcutGroup g in ShortcutLibrary.AllGroups)
        {
            Assert.Equal(g.Items.Count, g.Items.Select(s => s.Chord).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public void AllApps_GetsCommonThenWindows()
    {
        IReadOnlyList<ShortcutGroup> groups = ShortcutLibrary.For(null);

        Assert.Equal([ShortcutLibrary.CommonId, ShortcutLibrary.WindowsId], groups.Select(g => g.Id));
        Assert.Contains(groups[0].Items, s => s.Chord == "Ctrl+Tab" && s.Name == "Next tab");
        Assert.Contains(groups[0].Items, s => s.Chord == "Ctrl+Shift+Tab" && s.Name == "Previous tab");
        Assert.Contains(groups[1].Items, s => s.Chord == "Win+V");
    }

    [Theory]
    [InlineData("msedge")]
    [InlineData("island")]
    [InlineData("CHROME")]
    public void Browsers_ShareTheBrowserList_TitledWithTheAppName(string process)
    {
        IReadOnlyList<ShortcutGroup> groups = ShortcutLibrary.For(process, "My browser");

        Assert.Equal("browser", groups[0].Id);
        Assert.Equal("My browser", groups[0].Title);
        Assert.Equal(ShortcutLibrary.CommonId, groups[1].Id);
    }

    [Fact]
    public void AppList_DropsItsChordsFromCommon()
    {
        IReadOnlyList<ShortcutGroup> groups = ShortcutLibrary.For("msedge");

        Assert.Contains(groups[0].Items, s => s.Chord == "Ctrl+Tab");
        Assert.DoesNotContain(groups[1].Items, s => s.Chord == "Ctrl+Tab");
        Assert.Contains(groups[1].Items, s => s.Chord == "Ctrl+C");
    }

    [Fact]
    public void UnknownApp_GetsCommonAndWindowsOnly()
    {
        Assert.Equal([ShortcutLibrary.CommonId, ShortcutLibrary.WindowsId], ShortcutLibrary.For("notepad").Select(g => g.Id));
    }

    [Fact]
    public void ExcelTitle_DefaultsWhenNoName()
    {
        Assert.Equal("Excel", ShortcutLibrary.For("excel")[0].Title);
    }

    [Fact]
    public void HideKey_IsPerGroup()
    {
        Assert.NotEqual(ShortcutLibrary.HideKey("browser", "Ctrl+Tab"), ShortcutLibrary.HideKey(ShortcutLibrary.CommonId, "Ctrl+Tab"));
    }

    [Fact]
    public void Contains_FindsAppAndWindowsChords()
    {
        Assert.True(ShortcutLibrary.Contains("excel", "Ctrl+PageDown"));
        Assert.True(ShortcutLibrary.Contains(null, "Win+V"));
        Assert.False(ShortcutLibrary.Contains(null, "Ctrl+Alt+Shift+F7"));
    }

    [Fact]
    public void Describe_NamesWindowsChords()
    {
        Assert.Equal("Clipboard history", ActionCatalog.Describe(new ActionSpec.Keys("Win+V")).Text);
        Assert.Equal("Snip & copy", ActionCatalog.Describe(new ActionSpec.Keys("Win+Shift+S")).Text);
    }
}
