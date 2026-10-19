using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public class MyShortcutOpsTests
{
    private static SlenderConfig With(params CustomShortcut[] mine) => SlenderConfig.Default with { MyShortcuts = mine };

    [Fact]
    public void Everywhere_entries_show_for_every_app_and_app_entries_only_for_theirs()
    {
        var c = With(new("Paste values", "Ctrl+Alt+V", "excel"), new("Snip", "Win+Shift+S"), new("Mute", "Ctrl+Shift+M", "ms-teams"));
        Assert.Equal(["Paste values", "Snip"], MyShortcutOps.For(c, "EXCEL.exe").Select(e => e.Shortcut.Name));
        Assert.Equal(["Snip"], MyShortcutOps.For(c, null).Select(e => e.Shortcut.Name));
        Assert.Equal([1, 2], MyShortcutOps.For(c, "ms-teams").Select(e => e.Index));
    }

    [Fact]
    public void Add_update_and_remove_keep_order_and_trim()
    {
        var c = MyShortcutOps.Add(With(), "  Format painter ", " Ctrl+Shift+C ", "WINWORD.EXE");
        Assert.Equal(new CustomShortcut("Format painter", "Ctrl+Shift+C", "WINWORD"), c.MyShortcuts.Single());

        c = MyShortcutOps.Add(c, "Snip", "Win+Shift+S", null);
        c = MyShortcutOps.Update(c, 0, "Painter", "Ctrl+Shift+C", null);
        Assert.Equal(["Painter", "Snip"], c.MyShortcuts.Select(s => s.Name));
        Assert.Null(c.MyShortcuts[0].App);

        c = MyShortcutOps.Remove(c, 0);
        Assert.Equal(["Snip"], c.MyShortcuts.Select(s => s.Name));
        Assert.Same(c, MyShortcutOps.Remove(c, 5));
        Assert.Same(c, MyShortcutOps.Update(c, -1, "x", "Ctrl+X", null));
    }

    [Theory]
    [InlineData("", "Ctrl+C", "Give it a name.")]
    [InlineData("   ", "Ctrl+C", "Give it a name.")]
    [InlineData("Copy", null, "Press the keys for it.")]
    [InlineData("Copy", "Ctrl+Nonsense", "Press the keys for it.")]
    [InlineData("Copy", "Ctrl+C", null)]
    public void Problems_are_plain(string name, string? chord, string? problem) => Assert.Equal(problem, MyShortcutOps.Problem(name, chord));

    [Fact]
    public void A_long_name_is_refused() => Assert.NotNull(MyShortcutOps.Problem(new string('a', MyShortcutOps.MaxNameLength + 1), "Ctrl+C"));

    [Fact]
    public void My_shortcuts_round_trip_and_bad_entries_are_dropped()
    {
        string dir = Path.Combine(Path.GetTempPath(), "slender-mine-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Slender.Engine.Config.JsonConfigStore(dir);
            store.Save(With(new("Paste values", "Ctrl+Alt+V", "excel"), new("Snip", "Win+Shift+S")));
            Assert.Equal(2, store.Load().MyShortcuts.Count);
            File.WriteAllText(Path.Combine(dir, "config.json"),
                """{"version":2,"profiles":[{"bindings":{}}],"myShortcuts":[{"name":"Paste values","chord":"Ctrl+Alt+V","app":"excel"},{"name":"Bad","chord":"Win+Bogus"},{"name":" ","chord":"Ctrl+C"}]}""");
            Assert.Equal(["Paste values"], store.Load().MyShortcuts.Select(s => s.Name));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
