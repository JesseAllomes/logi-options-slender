using Slender.Tray;

namespace Slender.Tests.Tray;

public class LockKeyIndicatorStateTests
{
    [Fact]
    public void First_sample_is_only_a_baseline()
    {
        var s = new LockKeyIndicatorState();
        Assert.Null(s.Update(true, caps: true, num: true));
        Assert.Null(s.Update(true, caps: true, num: true));
    }

    [Fact]
    public void Fires_once_per_change_with_the_new_state()
    {
        var s = new LockKeyIndicatorState();
        s.Update(true, false, true);
        Assert.Equal("Caps Lock on", s.Update(true, true, true));
        Assert.Null(s.Update(true, true, true));
        Assert.Equal("Num Lock off", s.Update(true, true, false));
        Assert.Equal("Caps Lock off", s.Update(true, false, false));
        Assert.Null(s.Update(true, false, false));
    }

    [Fact]
    public void Two_changes_in_one_poll_give_one_label()
    {
        var s = new LockKeyIndicatorState();
        s.Update(true, false, false);
        Assert.Equal("Caps Lock on / Num Lock on", s.Update(true, true, true));
    }

    [Fact]
    public void Nothing_fires_while_off_and_turning_it_on_again_re_baselines()
    {
        var s = new LockKeyIndicatorState();
        s.Update(true, false, false);
        Assert.Null(s.Update(false, true, false)); // option off
        Assert.Null(s.Update(false, false, true));
        Assert.Null(s.Update(true, true, true));   // back on: baseline, not a change
        Assert.Equal("Caps Lock off", s.Update(true, false, true));
    }

    [Fact]
    public void Reset_on_disconnect_re_baselines()
    {
        var s = new LockKeyIndicatorState();
        s.Update(true, false, false);
        s.Reset();
        Assert.Null(s.Update(true, true, false));
    }
}
