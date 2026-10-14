using Slender.Engine;

namespace Slender.Tests.Engine;

public class HookPairingTests
{
    private static bool Run(HookButtonPairing p, HookButton b, bool pressed, Action<HookButtonEventArgs>? handler)
    {
        EventHandler<HookButtonEventArgs>? h = handler is null ? null : (_, e) => handler(e);
        return p.Process(b, pressed, h, new object());
    }

    [Fact]
    public void HandledPressSwallowsReleaseEvenIfReleaseNotHandled()
    {
        var p = new HookButtonPairing();
        Assert.True(Run(p, HookButton.Back, true, e => e.Handled = true));
        Assert.True(Run(p, HookButton.Back, false, _ => { }));
        Assert.False(Run(p, HookButton.Back, false, _ => { })); // pairing consumed
    }

    [Fact]
    public void PassedPressNeverSwallowsRelease()
    {
        var p = new HookButtonPairing();
        Assert.False(Run(p, HookButton.Forward, true, _ => { }));
        Assert.False(Run(p, HookButton.Forward, false, e => e.Handled = true));
    }

    [Fact]
    public void ButtonsAreTrackedIndependently()
    {
        var p = new HookButtonPairing();
        Assert.True(Run(p, HookButton.Middle, true, e => e.Handled = true));
        Assert.False(Run(p, HookButton.Back, true, _ => { }));
        Assert.False(Run(p, HookButton.Back, false, _ => { }));
        Assert.True(Run(p, HookButton.Middle, false, _ => { }));
    }

    [Fact]
    public void ReleaseHandlerStillRuns()
    {
        var p = new HookButtonPairing();
        var seen = new List<bool>();
        Run(p, HookButton.Back, true, e => { seen.Add(e.Pressed); e.Handled = true; });
        Run(p, HookButton.Back, false, e => seen.Add(e.Pressed));
        Assert.Equal([true, false], seen);
    }

    [Fact]
    public void ThrowingHandlerOnPressPassesThrough()
    {
        var p = new HookButtonPairing();
        Assert.False(Run(p, HookButton.Back, true, _ => throw new InvalidOperationException()));
        Assert.False(Run(p, HookButton.Back, false, _ => { }));
    }

    [Fact]
    public void NoHandlersPassesThrough()
    {
        var p = new HookButtonPairing();
        Assert.False(Run(p, HookButton.Middle, true, null));
        Assert.False(Run(p, HookButton.Middle, false, null));
    }

    [Theory]
    [InlineData(0x0207, 0u, HookButton.Middle)]
    [InlineData(0x0208, 0u, HookButton.Middle)]
    [InlineData(0x020B, 1u << 16, HookButton.Back)]
    [InlineData(0x020C, 2u << 16, HookButton.Forward)]
    public void MapsWindowsMessages(int msg, uint data, HookButton expected) =>
        Assert.Equal(expected, LowLevelMouseHook.ToButton(msg, data));

    [Fact]
    public void UnknownXButtonIsIgnored() => Assert.Null(LowLevelMouseHook.ToButton(0x020B, 3u << 16));
}
