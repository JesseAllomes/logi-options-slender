using Slender.Settings.Logic;
using Xunit;

namespace Slender.Tests.Settings;

public class WinChordCaptureTests
{
    private const ushort LWin = 0x5B;
    private const ushort RWin = 0x5C;
    private const ushort Ctrl = 0xA2;
    private const ushort Shift = 0xA0;
    private const ushort V = 0x56;
    private const ushort Tab = 0x09;
    private const ushort Esc = 0x1B;
    private const ushort Right = 0x27;
    private const ushort F4 = 0x73;

    [Fact]
    public void WinV_IsSwallowedAndRecorded()
    {
        var c = new WinChordCapture();

        Assert.Equal(new CaptureResult(true), c.OnKey(LWin, true, false, false, false));
        Assert.Equal(new CaptureResult(true, "Win+V"), c.OnKey(V, true, false, false, false));
        Assert.Equal(new CaptureResult(true), c.OnKey(V, false, false, false, false));
        Assert.Equal(new CaptureResult(true), c.OnKey(LWin, false, false, false, false));
        Assert.False(c.WinDown);
    }

    [Fact]
    public void WinAlone_IsSwallowed_SoStartDoesNotOpen()
    {
        var c = new WinChordCapture();

        Assert.True(c.OnKey(RWin, true, false, false, false).Swallow);
        Assert.True(c.WinDown);
        CaptureResult up = c.OnKey(RWin, false, false, false, false);
        Assert.True(up.Swallow);
        Assert.Null(up.Chord);
    }

    [Fact]
    public void ModifiersPassThrough_WhileWinIsDown()
    {
        var c = new WinChordCapture();
        c.OnKey(LWin, true, false, false, false);

        Assert.False(c.OnKey(Ctrl, true, true, false, false).Swallow);
        Assert.Equal(new CaptureResult(true, "Win+Ctrl+Right"), c.OnKey(Right, true, true, false, false));
        Assert.False(c.OnKey(Ctrl, false, false, false, false).Swallow);
    }

    [Fact]
    public void WinShiftS_KeepsRecorderOrder()
    {
        var c = new WinChordCapture();
        c.OnKey(LWin, true, false, false, false);
        c.OnKey(Shift, true, false, false, true);

        Assert.Equal("Win+Shift+S", c.OnKey(0x53, true, false, false, true).Chord);
    }

    [Fact]
    public void PlainAndCtrlKeys_PassThrough_ToTheNormalRecorder()
    {
        var c = new WinChordCapture();

        Assert.Equal(new CaptureResult(false), c.OnKey(V, true, false, false, false));
        Assert.Equal(new CaptureResult(false), c.OnKey(V, false, false, false, false));
        Assert.Equal(new CaptureResult(false), c.OnKey(Tab, true, true, false, false)); // Ctrl+Tab: the window's recorder takes it
    }

    [Theory]
    [InlineData(Tab, false, true, false, "Alt+Tab")]
    [InlineData(F4, false, true, false, "Alt+F4")]
    [InlineData(Esc, true, false, true, "Ctrl+Shift+Esc")]
    [InlineData(Esc, true, false, false, "Ctrl+Esc")]
    public void SystemCombos_AreCaptured(ushort vk, bool ctrl, bool alt, bool shift, string expected)
    {
        var c = new WinChordCapture();

        CaptureResult r = c.OnKey(vk, true, ctrl, alt, shift);
        Assert.True(r.Swallow);
        Assert.Equal(expected, r.Chord);
        Assert.True(c.OnKey(vk, false, ctrl, alt, shift).Swallow);
    }

    [Fact]
    public void UnnamedKey_WithWin_IsSwallowedAndReportedUnsupported()
    {
        var c = new WinChordCapture();
        c.OnKey(LWin, true, false, false, false);

        CaptureResult r = c.OnKey(0xE8, true, false, false, false);
        Assert.True(r.Swallow);
        Assert.True(r.Unsupported);
        Assert.Null(r.Chord);
    }

    [Fact]
    public void Reset_ForgetsWin()
    {
        var c = new WinChordCapture();
        c.OnKey(LWin, true, false, false, false);
        c.Reset();

        Assert.False(c.WinDown);
        Assert.False(c.OnKey(V, true, false, false, false).Swallow);
    }
}
