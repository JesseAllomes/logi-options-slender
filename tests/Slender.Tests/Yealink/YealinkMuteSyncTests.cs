using Slender.Yealink;

namespace Slender.Tests.Yealink;

internal sealed class FakeMic : IMicMute
{
    public bool Muted;
    public int Subscribed, Unsubscribed, Disposals;
    public Action<bool>? Changed;
    public bool Throw;

    public bool GetMute() => Throw ? throw new InvalidOperationException("no endpoint") : Muted;
    public void SetMute(bool muted) { Muted = muted; Changed?.Invoke(muted); }
    public void Subscribe(Action<bool> changed) { Subscribed++; Changed = changed; }
    public void Unsubscribe() { Unsubscribed++; Changed = null; }
    public void Dispose() => Disposals++;

    /// <summary>A change made elsewhere (the Windows sound settings, say).</summary>
    public void External(bool muted) { Muted = muted; Changed?.Invoke(muted); }
}

public class YealinkMuteSyncTests
{
    private readonly FakeTransport _transport = new();
    private readonly FakeMic _mic = new();
    private readonly List<string> _log = [];
    private int _created;

    private YealinkModule Make(bool sync, bool leds)
    {
        var m = new YealinkModule(_transport, _log.Add, () => false, () => { _created++; return _mic; })
        {
            MuteSync = sync,
            LedWritesEnabled = leds,
        };
        return m;
    }

    private static void Press(FakeLink l)
    {
        l.Feed(YealinkCollection.Telephony, 4, 0x04, 0);
        l.Feed(YealinkCollection.Telephony, 4, 0x00, 0);
    }

    [Fact]
    public void Off_by_default_so_the_microphone_is_never_touched()
    {
        var m = Make(sync: false, leds: true);
        m.Start();
        Press(_transport.Link!);
        Assert.Equal(0, _created);
        Assert.False(_mic.Muted);
    }

    [Fact]
    public void Mute_press_toggles_windows_mute_and_the_led_follows()
    {
        var m = Make(sync: true, leds: true);
        m.Start();
        var l = _transport.Link!;
        Assert.Equal(1, _mic.Subscribed);
        Assert.Equal([[4, 0x00]], l.Writes); // initial state pushed to the headset

        Press(l);
        Assert.True(_mic.Muted);
        Assert.Equal([4, 0x02], l.Writes[^1]);

        Press(l);
        Assert.False(_mic.Muted);
        Assert.Equal([4, 0x00], l.Writes[^1]);
        Assert.Equal(3, l.Writes.Count); // initial, muted, unmuted: no duplicate writes
    }

    [Fact]
    public void Led_follows_changes_made_elsewhere()
    {
        var m = Make(sync: true, leds: true);
        m.Start();
        _mic.External(true);
        Assert.Equal([4, 0x02], _transport.Link!.Writes[^1]);
        _mic.External(false);
        Assert.Equal([4, 0x00], _transport.Link!.Writes[^1]);
    }

    [Fact]
    public void Initial_led_reflects_an_already_muted_microphone()
    {
        _mic.Muted = true;
        Make(sync: true, leds: true).Start();
        Assert.Equal([4, 0x02], _transport.Link!.Writes[^1]);
    }

    [Fact]
    public void Sync_without_led_writes_toggles_the_microphone_and_writes_nothing()
    {
        var m = Make(sync: true, leds: false);
        m.Start();
        Press(_transport.Link!);
        Assert.True(_mic.Muted);
        Assert.Empty(_transport.Link!.Writes);
    }

    [Fact]
    public void Stop_unregisters_and_releases_the_microphone_and_later_changes_do_nothing()
    {
        var m = Make(sync: true, leds: true);
        m.Start();
        var l = _transport.Link!;
        m.Stop();

        Assert.Equal(1, _mic.Unsubscribed);
        Assert.Equal(1, _mic.Disposals);
        Assert.Null(_mic.Changed);

        int writes = l.Writes.Count;
        _mic.External(true);
        Assert.Equal(writes, l.Writes.Count);
    }

    [Fact]
    public void Restart_registers_again_and_wake_does_not_double_register()
    {
        var m = Make(sync: true, leds: false);
        m.Start();
        m.Wake();
        Assert.Equal(1, _mic.Subscribed);
        m.Stop();
        m.Start();
        Assert.Equal(2, _mic.Subscribed);
    }

    [Fact]
    public void Missing_microphone_is_logged_and_the_headset_still_works()
    {
        _mic.Throw = true;
        var m = Make(sync: true, leds: true);
        var presses = 0;
        m.MutePressed += () => presses++;
        m.Start();
        Press(_transport.Link!);

        Assert.True(m.IsOpen);
        Assert.Equal(1, presses);
        Assert.Contains(_log, x => x.Contains("unavailable"));
        Assert.Equal(1, _mic.Disposals);
    }

    [Fact]
    public void Standing_down_creates_no_microphone_object()
    {
        var m = new YealinkModule(_transport, _log.Add, () => true, () => { _created++; return _mic; }) { MuteSync = true };
        m.Start();
        Assert.Equal(0, _created);
    }
}
