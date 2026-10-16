using Slender.Engine;
using Slender.Engine.Config;

namespace Slender.Tests.Config;

public sealed class ScrollSpeedConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Speeds_and_the_chord_switch_round_trip()
    {
        var store = new JsonConfigStore(_dir);
        var config = SlenderConfig.Default with { Pointer = new PointerSettings(SideScrollSpeed: 60, TabScrollSpeed: 250, TabScrollChord: false) };
        store.Save(config);

        PointerSettings p = new JsonConfigStore(_dir).Load().Pointer;
        Assert.Equal(60, p.SideScrollSpeed);
        Assert.Equal(250, p.TabScrollSpeed);
        Assert.False(p.TabScrollChord);
    }

    [Fact]
    public void An_older_file_gets_normal_speeds_and_the_chord_on()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), """{"version":2,"profiles":[{"bindings":{}}],"pointer":{"invertThumbWheel":true}}""");

        PointerSettings p = new JsonConfigStore(_dir).Load().Pointer;
        Assert.Equal(100, p.SideScrollSpeed);
        Assert.Equal(100, p.TabScrollSpeed);
        Assert.True(p.TabScrollChord);
    }

    [Fact]
    public void Out_of_range_speeds_are_clamped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"),
            """{"version":2,"profiles":[{"bindings":{}}],"pointer":{"sideScrollSpeed":1,"tabScrollSpeed":9000}}""");

        PointerSettings p = new JsonConfigStore(_dir).Load().Pointer;
        Assert.Equal(PointerSettings.SpeedMin, p.SideScrollSpeed);
        Assert.Equal(PointerSettings.SpeedMax, p.TabScrollSpeed);
    }

    [Fact]
    public void Defaults_are_normal_speed_with_the_chord_on()
    {
        Assert.Equal(100, new PointerSettings().SideScrollSpeed);
        Assert.Equal(100, new PointerSettings().TabScrollSpeed);
        Assert.True(new PointerSettings().TabScrollChord);
    }
}
