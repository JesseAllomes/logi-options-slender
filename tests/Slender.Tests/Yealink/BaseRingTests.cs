using Slender.Yealink;

namespace Slender.Tests.Yealink;

public class BaseRingDetectorTests
{
    private static readonly byte[] Start = [1, 2], Stop = [1, 1], Release = [1];

    private long _now = 1_000;
    private readonly List<bool> _events = [];

    private BaseRingDetector Make()
    {
        var d = new BaseRingDetector(() => _now);
        d.Changed += _events.Add;
        return d;
    }

    private void Burst(BaseRingDetector d, byte[] code, int presses = 12, int gapMs = 25)
    {
        for (int i = 0; i < presses; i++)
        {
            d.Feed(code);
            d.Feed(Release);
            _now += gapMs;
        }
    }

    [Fact]
    public void A_start_burst_is_one_start()
    {
        var d = Make();
        Burst(d, Start);
        Assert.Equal([true], _events);
    }

    [Fact]
    public void A_stop_burst_is_one_stop()
    {
        var d = Make();
        Burst(d, Stop);
        Assert.Equal([false], _events);
    }

    [Fact]
    public void A_few_presses_are_ignored()
    {
        var d = Make();
        Burst(d, Start, presses: 3);
        Burst(d, Stop, presses: 5);
        Assert.Empty(_events);
    }

    [Fact]
    public void Slow_presses_are_ignored()
    {
        var d = Make();
        Burst(d, Start, presses: 20, gapMs: 300);
        Assert.Empty(_events);
    }

    [Fact]
    public void Releases_and_other_keys_are_ignored()
    {
        var d = Make();
        for (int i = 0; i < 20; i++) { d.Feed(Release); d.Feed([1, 0x10]); d.Feed([]); d.Feed([2, 2]); }
        Assert.Empty(_events);
    }

    [Fact]
    public void A_suppressed_window_is_ignored_and_it_works_again_after()
    {
        var d = Make();
        d.SuppressFor(TimeSpan.FromSeconds(10));
        Burst(d, Start);
        Assert.Empty(_events);

        _now += 10_000;
        Burst(d, Start);
        Assert.Equal([true], _events);
    }

    [Fact]
    public void A_second_burst_after_a_stop_fires_again()
    {
        var d = Make();
        Burst(d, Start);
        _now += 5_000;
        Burst(d, Stop);
        _now += 5_000;
        Burst(d, Start);
        Assert.Equal([true, false, true], _events);
    }
}

public class BaseRingModuleTests
{
    private readonly FakeTransport _transport = new();
    private long _now = 1_000;

    private YealinkModule Make(bool baseRing) =>
        new(_transport, _ => { }, () => false, tick: () => _now) { BaseRingEnabled = baseRing, VendorReadsEnabled = false };

    private void Burst(byte code)
    {
        for (int i = 0; i < 12; i++)
        {
            _transport.Link!.Feed(YealinkCollection.ConsumerKeys, 1, code);
            _transport.Link.Feed(YealinkCollection.ConsumerKeys, 1);
            _now += 20;
        }
    }

    [Fact]
    public void The_consumer_keys_are_opened_only_while_the_base_ring_is_on_and_follow_a_change()
    {
        var m = Make(baseRing: false);
        m.Start();
        Assert.False(_transport.ConsumerRequested);

        m.ApplySettings(false, false, vendorReads: false, baseRing: true);
        Assert.True(_transport.ConsumerRequested);
        Assert.Equal(2, _transport.Opens);

        m.ApplySettings(false, false, vendorReads: false, baseRing: true);
        Assert.Equal(2, _transport.Opens);

        m.ApplySettings(false, false, vendorReads: false, baseRing: false);
        Assert.False(_transport.ConsumerRequested);
        Assert.Equal(3, _transport.Opens);
    }

    [Fact]
    public void Bursts_raise_the_event_and_the_preview_can_silence_them()
    {
        var m = Make(baseRing: true);
        var got = new List<bool>();
        m.BaseRingChanged += got.Add;
        m.Start();
        Assert.True(_transport.ConsumerRequested);

        Burst(2);
        Burst(1);
        Assert.Equal([true, false], got);

        m.SuppressBaseRing(TimeSpan.FromSeconds(5));
        Burst(2);
        Assert.Equal([true, false], got);
        _now += 6_000;
        Burst(2);
        Assert.Equal([true, false, true], got);
    }

    [Fact]
    public void Consumer_reports_do_nothing_when_the_base_ring_is_off()
    {
        var m = Make(baseRing: false);
        var got = new List<bool>();
        m.BaseRingChanged += got.Add;
        m.Start();
        Burst(2);
        Assert.Empty(got);
    }
}
