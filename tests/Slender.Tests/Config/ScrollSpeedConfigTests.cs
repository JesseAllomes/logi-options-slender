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
    public void Speeds_round_trip_under_the_old_json_name()
    {
        var store = new JsonConfigStore(_dir);
        var config = SlenderConfig.Default with { Pointer = new PointerSettings(SideScrollSpeed: 60, SecondActionSpeed: 250) };
        store.Save(config);

        Assert.Contains("\"tabScrollSpeed\"", File.ReadAllText(Path.Combine(_dir, "config.json")));
        PointerSettings p = new JsonConfigStore(_dir).Load().Pointer;
        Assert.Equal(60, p.SideScrollSpeed);
        Assert.Equal(250, p.SecondActionSpeed);
    }

    [Fact]
    public void An_older_file_gets_normal_speeds_and_its_chord_switch_is_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), """{"version":2,"profiles":[{"bindings":{}}],"pointer":{"invertThumbWheel":true,"tabScrollChord":true}}""");

        PointerSettings p = new JsonConfigStore(_dir).Load().Pointer;
        Assert.Equal(100, p.SideScrollSpeed);
        Assert.Equal(100, p.SecondActionSpeed);
    }

    [Fact]
    public void Out_of_range_speeds_are_clamped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"),
            """{"version":2,"profiles":[{"bindings":{}}],"pointer":{"sideScrollSpeed":1,"tabScrollSpeed":9000}}""");

        PointerSettings p = new JsonConfigStore(_dir).Load().Pointer;
        Assert.Equal(PointerSettings.SpeedMin, p.SideScrollSpeed);
        Assert.Equal(PointerSettings.SpeedMax, p.SecondActionSpeed);
    }

    [Fact]
    public void Touch_timings_round_trip_default_and_clamp()
    {
        Assert.Equal((250, 300), (new PointerSettings().TouchRestMs, new PointerSettings().DoubleTapMs));
        var store = new JsonConfigStore(_dir);
        store.Save(SlenderConfig.Default with { Pointer = new PointerSettings(TouchRestMs: 400, DoubleTapMs: 200) });
        PointerSettings p = new JsonConfigStore(_dir).Load().Pointer;
        Assert.Equal((400, 200), (p.TouchRestMs, p.DoubleTapMs));

        File.WriteAllText(Path.Combine(_dir, "config.json"), """{"version":2,"profiles":[{"bindings":{}}],"pointer":{"touchRestMs":5,"doubleTapMs":5000}}""");
        p = new JsonConfigStore(_dir).Load().Pointer;
        Assert.Equal((PointerSettings.RestMinMs, PointerSettings.DoubleTapMaxMs), (p.TouchRestMs, p.DoubleTapMs));
    }

    [Fact]
    public void Defaults_are_normal_speed()
    {
        Assert.Equal(100, new PointerSettings().SideScrollSpeed);
        Assert.Equal(100, new PointerSettings().SecondActionSpeed);
    }
}
