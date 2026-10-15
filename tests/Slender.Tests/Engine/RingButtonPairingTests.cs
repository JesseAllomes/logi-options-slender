using Slender.Engine;
using Native = Slender.Engine.Native.NativeMethods;

namespace Slender.Tests.Engine;

public class RingButtonPairingTests
{
    [Fact]
    public void A_swallowed_press_swallows_its_release_even_after_capture_ends()
    {
        var p = new RingButtonPairing();
        var down = p.OnMessage(Native.WM_LBUTTONDOWN, 1_000, capture: true);
        Assert.True(down.Swallow);
        Assert.Equal(HookRingKind.LeftDown, down.Kind);

        var up = p.OnMessage(Native.WM_LBUTTONUP, 1_100, capture: false);
        Assert.True(up.Swallow);
        Assert.Null(up.Kind);
    }

    [Fact]
    public void An_unpaired_release_passes_through()
    {
        var p = new RingButtonPairing();
        var up = p.OnMessage(Native.WM_LBUTTONUP, 1_000, capture: false);
        Assert.False(up.Swallow);
        Assert.Null(up.Kind);
    }

    [Fact]
    public void A_release_more_than_5_seconds_after_the_press_passes_through_and_clears_the_flag()
    {
        var p = new RingButtonPairing();
        Assert.True(p.OnMessage(Native.WM_RBUTTONDOWN, 1_000, capture: true).Swallow);
        Assert.False(p.OnMessage(Native.WM_RBUTTONUP, 7_000, capture: false).Swallow);
        Assert.False(p.OnMessage(Native.WM_RBUTTONUP, 7_100, capture: false).Swallow);
    }

    [Fact]
    public void Wheel_is_swallowed_only_while_capture_is_on()
    {
        var p = new RingButtonPairing();
        var on = p.OnMessage(Native.WM_MOUSEWHEEL, 1_000, capture: true);
        Assert.True(on.Swallow);
        Assert.Equal(HookRingKind.Wheel, on.Kind);
        var off = p.OnMessage(Native.WM_MOUSEWHEEL, 1_100, capture: false);
        Assert.False(off.Swallow);
        Assert.Null(off.Kind);
    }

    [Fact]
    public void Other_messages_are_never_swallowed()
    {
        var p = new RingButtonPairing();
        Assert.False(p.OnMessage(Native.WM_MOUSEMOVE, 1_000, capture: true).Swallow);
        Assert.False(p.OnMessage(Native.WM_MOUSEMOVE, 1_100, capture: false).Swallow);
    }
}
