using Slender.Engine;
using Slender.Engine.Config;
using Slender.Tests.Engine;

namespace Slender.Tests.Ringer;

public sealed class RingSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void The_default_is_either_and_every_choice_round_trips()
    {
        Assert.Equal(CallRingSource.Either, new HapticSettings().RingSource);
        var store = new JsonConfigStore(_dir);
        foreach (var source in Enum.GetValues<CallRingSource>())
        {
            store.Save(SlenderConfig.Default with { Haptics = new HapticSettings { RingSource = source } });
            Assert.Equal(source, store.Load().Haptics.RingSource);
        }
    }

    [Fact]
    public void An_old_file_or_an_unknown_value_loads_as_either()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "config.json");
        File.WriteAllText(file, """{"version":2,"haptics":{"level":2}}""");
        Assert.Equal(CallRingSource.Either, new JsonConfigStore(_dir).Load().Haptics.RingSource);
        File.WriteAllText(file, """{"version":2,"haptics":{"ringSource":"carrier-pigeon"}}""");
        Assert.Equal(CallRingSource.Either, new JsonConfigStore(_dir).Load().Haptics.RingSource);
    }

    private static (List<byte> Buzzes, int Reported) Ring(CallRingSource allowed, RingerSignalSource from)
    {
        var mouse = new FakeMouse();
        var haptics = new HapticSettings { Ringer = new HapticCue(true, 9, 1, 150, 100, 1, 1500), RingSource = allowed };
        using var engine = new SlenderEngine(mouse, new FakeHook(), new FakeForeground(), new FakeExcel(), new FakeExecutor(),
            SlenderConfig.Default with { Haptics = haptics }, new ListLog(), new InlineQueue(), () => 0, new FakeHoldTimer());
        int seen = 0;
        engine.RingSignalReceived += (_, _) => seen++;
        engine.SignalRing(new RingSignal(true, from));
        engine.SignalRing(new RingSignal(true, from)); // already ringing: a no-op
        return ([.. mouse.Haptics], seen);
    }

    [Theory]
    [InlineData(CallRingSource.Base, RingerSignalSource.Base, true)]
    [InlineData(CallRingSource.Base, RingerSignalSource.Notification, false)]
    [InlineData(CallRingSource.Base, RingerSignalSource.Headset, false)]
    [InlineData(CallRingSource.Extension, RingerSignalSource.Base, false)]
    [InlineData(CallRingSource.Extension, RingerSignalSource.Notification, true)]
    [InlineData(CallRingSource.Extension, RingerSignalSource.Headset, true)]
    [InlineData(CallRingSource.Either, RingerSignalSource.Base, true)]
    [InlineData(CallRingSource.Either, RingerSignalSource.Notification, true)]
    public void Each_source_starts_the_ringer_only_when_it_is_allowed(CallRingSource allowed, RingerSignalSource from, bool rings)
    {
        var (buzzes, reported) = Ring(allowed, from);
        Assert.Equal(rings ? [(byte)9] : [], buzzes); // a second start while ringing adds nothing
        Assert.Equal(rings, reported > 0);
    }
}
