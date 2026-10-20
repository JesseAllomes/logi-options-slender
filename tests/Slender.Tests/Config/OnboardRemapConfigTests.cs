using Slender.Engine;
using Slender.Engine.Config;

namespace Slender.Tests.Config;

public sealed class OnboardRemapConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private SlenderConfig LoadText(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), json);
        return new JsonConfigStore(_dir).Load();
    }

    [Fact]
    public void Default_has_no_remaps()
    {
        Assert.Empty(SlenderConfig.Default.OnboardRemaps);
    }

    [Fact]
    public void Remaps_round_trip()
    {
        var remaps = new[] { new OnboardRemap(224, "Ctrl+Esc"), new OnboardRemap(229, "MediaNext") };
        new JsonConfigStore(_dir).Save(SlenderConfig.Default with { OnboardRemaps = remaps });
        Assert.Equal(remaps, new JsonConfigStore(_dir).Load().OnboardRemaps);
    }

    [Fact]
    public void An_older_file_without_remaps_loads_empty()
    {
        Assert.Empty(LoadText("""{"version":2,"profiles":[{"bindings":{}}]}""").OnboardRemaps);
    }

    [Fact]
    public void Garbage_entries_are_dropped_not_the_file()
    {
        var c = LoadText("""
            {"version":2,"profiles":[{"bindings":{}}],"onboardRemaps":[
              {"cid":224,"chord":"Win+Tab"},
              {"cid":99999,"chord":"A"},
              {"cid":-1,"chord":"A"},
              {"cid":225,"chord":"Ctrl+Shift+S"},
              {"cid":226,"chord":"A"},
              {"cid":228,"chord":null},
              {"cid":229,"chord":"Banana"},
              {"cid":230,"chord":"MediaPlayPause"},
              {"cid":230,"chord":"F5"},
              null
            ]}
            """);
        Assert.Equal([new OnboardRemap(224, "Win+Tab"), new OnboardRemap(230, "F5")], c.OnboardRemaps);
    }

    [Fact]
    public void Wrong_shape_does_not_throw()
    {
        var c = LoadText("""{"version":2,"profiles":[{"bindings":{}}],"onboardRemaps":[1,2,3]}""");
        Assert.NotNull(c);
        Assert.Empty(c.OnboardRemaps);
    }
}
