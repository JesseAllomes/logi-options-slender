using Slender.Engine;
using Slender.Engine.Ring;

namespace Slender.Tests.Ring;

public class RingSessionTests
{
    private static readonly RingMetrics Metrics = RingLayout.Metrics(RingStyle.Bezel, RingSize.Medium);

    // Pointer offsets (DIP) that select slot i at a comfortable radius.
    private static (double X, double Y) At(int slot) => RingLayout.SlotCenter(RingStyle.Bezel, RingSize.Medium, slot);

    private sealed class Rec
    {
        public RingSession Session;
        public List<RingCloseReason> Closed = [];
        public List<ActionSpec> Ran = [];
        public List<ActionSpec> Adjusted = [];
        public List<RingHaptic> Haptics = [];
        public int Changes;

        public Rec(IReadOnlyList<RingSlot>? slots = null, long now = 0)
        {
            Session = new RingSession(slots ?? RingDefaults.Slots(), Metrics, now);
            Session.Closed += (_, r) => Closed.Add(r);
            Session.ActionRequested += (_, a) => Ran.Add(a);
            Session.Adjust += (_, a) => Adjusted.Add(a);
            Session.Haptic += (_, h) => Haptics.Add(h);
            Session.Changed += (_, _) => Changes++;
        }

        public void Point(int? slot, long now)
        {
            var (x, y) = slot is { } s ? At(s) : (0.0, 0.0);
            Session.OnPointer(x, y, now);
        }
    }

    private static RingSlot Keys(string chord, string? name = null) => new(new ActionSpec.Keys(chord)) { Name = name };

    private static RingSlot[] Eight(Func<int, RingSlot> make) => Enumerable.Range(0, 8).Select(make).ToArray();

    [Fact]
    public void QuickPressReleaseWithoutPointingIsATapAndStaysOpen()
    {
        var r = new Rec();
        r.Point(null, 10); // baseline at the centre
        r.Session.OnTrigger(false, 100);
        Assert.Equal(RingPhase.Open, r.Session.Phase);
        Assert.Empty(r.Closed);
        Assert.Empty(r.Ran);
    }

    [Fact]
    public void TapThenHoverAndLeftClickRunsTheSlot()
    {
        var r = new Rec();
        r.Point(null, 0);
        r.Session.OnTrigger(false, 100);
        r.Point(1, 200);
        var (x, y) = At(1);
        r.Session.OnClick(true, x, y, 300);
        Assert.Equal([new ActionSpec.Keys("Ctrl+C")], r.Ran);
        Assert.Equal([RingCloseReason.Ran], r.Closed);
        Assert.False(r.Session.IsOpen);
    }

    [Fact]
    public void ReleaseAfterTapWindowOnASlotRunsIt()
    {
        var r = new Rec();
        r.Point(null, 0);
        r.Point(2, 100);
        r.Session.OnTrigger(false, 600); // held 600 ms, pointing at Paste
        Assert.Equal([new ActionSpec.Keys("Ctrl+V")], r.Ran);
        Assert.Equal([RingCloseReason.Ran], r.Closed);
    }

    [Fact]
    public void HoldReleaseInTheCentreCancels()
    {
        var r = new Rec();
        r.Point(null, 0);
        r.Session.OnTrigger(false, 600);
        Assert.Equal([RingCloseReason.Cancelled], r.Closed);
        Assert.Empty(r.Ran);
    }

    [Fact]
    public void QuickReleaseAfterMovingOntoASlotCountsAsHold()
    {
        var r = new Rec();
        r.Point(null, 0);
        r.Point(1, 50);
        r.Session.OnTrigger(false, 120); // fast, but the pointer had reached a slot
        Assert.Equal([new ActionSpec.Keys("Ctrl+C")], r.Ran);
    }

    [Fact]
    public void FirstPointerSampleIsABaselineNotAMove()
    {
        // Ring opened at the monitor centre with the cursor elsewhere: a quick tap must still be a tap, and no tick fires.
        var r = new Rec();
        r.Point(3, 0);
        Assert.Equal(3, r.Session.Selected);
        Assert.Empty(r.Haptics);
        r.Session.OnTrigger(false, 100);
        Assert.Equal(RingPhase.Open, r.Session.Phase);
        Assert.Empty(r.Ran);
    }

    [Fact]
    public void PressingTheTriggerAgainWhileOpenCloses()
    {
        var r = new Rec();
        r.Session.OnTrigger(false, 100);
        r.Session.OnTrigger(true, 2000);
        Assert.Equal([RingCloseReason.Toggle], r.Closed);
    }

    [Fact]
    public void ClickInTheCentreOrOutsideCancelsAndRightClickCancels()
    {
        var r = new Rec();
        r.Session.OnTrigger(false, 100);
        r.Session.OnClick(true, 0, 0, 200);
        Assert.Equal([RingCloseReason.Cancelled], r.Closed);

        r = new Rec();
        r.Session.OnTrigger(false, 100);
        r.Session.OnClick(true, Metrics.Extent + 40, 0, 200);
        Assert.Equal([RingCloseReason.Cancelled], r.Closed);

        r = new Rec();
        r.Session.OnTrigger(false, 100);
        var (x, y) = At(1);
        r.Session.OnClick(false, x, y, 200);
        Assert.Equal([RingCloseReason.Cancelled], r.Closed);
        Assert.Empty(r.Ran);
    }

    [Fact]
    public void EscapeCloses()
    {
        var r = new Rec();
        r.Session.OnTrigger(false, 100);
        r.Session.OnKey(RingKey.Escape, 200);
        Assert.Equal([RingCloseReason.Escape], r.Closed);
    }

    [Fact]
    public void ArrowKeysMoveTheSelectionAndEnterRuns()
    {
        var r = new Rec();
        r.Session.OnTrigger(false, 100);
        r.Session.OnKey(RingKey.Up, 110);
        Assert.Equal(0, r.Session.Selected);
        r.Session.OnKey(RingKey.Right, 120);
        Assert.Equal(1, r.Session.Selected);
        r.Session.OnKey(RingKey.Left, 130);
        r.Session.OnKey(RingKey.Left, 140);
        Assert.Equal(7, r.Session.Selected); // wraps
        r.Session.OnKey(RingKey.Right, 150);
        r.Session.OnKey(RingKey.Right, 160);
        Assert.Equal(1, r.Session.Selected);
        r.Session.OnKey(RingKey.Enter, 170);
        Assert.Equal([new ActionSpec.Keys("Ctrl+C")], r.Ran);
    }

    [Fact]
    public void ArrowKeysTickAndPointerMovesOverrideThem()
    {
        var r = new Rec();
        r.Session.OnTrigger(false, 100);
        r.Point(null, 110);
        r.Session.OnKey(RingKey.Right, 120);
        Assert.Equal(RingHaptic.Tick, Assert.Single(r.Haptics));
        r.Point(null, 130); // same pointer slot as before: the keyboard selection stays
        Assert.Equal(2, r.Session.Selected);
        r.Point(5, 140);
        Assert.Equal(5, r.Session.Selected);
    }

    [Fact]
    public void SelectionChangeTicksAndRunBumps()
    {
        var r = new Rec();
        r.Point(null, 0);
        r.Point(1, 10);
        r.Point(1, 20); // same slot: no second tick
        r.Point(2, 30);
        r.Point(null, 40); // dead zone selects nothing: no tick
        Assert.Equal([RingHaptic.Tick, RingHaptic.Tick], r.Haptics);
        r.Point(0, 50);
        r.Session.OnTrigger(false, 600);
        Assert.Equal(RingHaptic.Bump, r.Haptics[^1]);
    }

    [Fact]
    public void EmptySlotsDoNotTickAndRunNothing()
    {
        var slots = Eight(i => i == 3 ? RingSlot.Empty : Keys("F" + (i + 1)));
        var r = new Rec(slots);
        r.Point(null, 0);
        r.Point(3, 10);
        Assert.Empty(r.Haptics);
        r.Session.OnTrigger(false, 600);
        Assert.Empty(r.Ran);
        Assert.Equal([RingCloseReason.Cancelled], r.Closed);
    }

    [Fact]
    public void FolderOpensItsChildrenAndCentreGoesBack()
    {
        var children = Eight(i => i == 0 ? Keys("Ctrl+1", "One") : RingSlot.Empty);
        var slots = Eight(i => i == 7 ? new RingSlot(new ActionSpec.None()) { Name = "Work", Folder = children } : Keys("F" + (i + 1)));
        var r = new Rec(slots);
        r.Session.OnTrigger(false, 100);
        var (x, y) = At(7);
        r.Session.OnClick(true, x, y, 200);
        Assert.True(r.Session.IsOpen);
        Assert.True(r.Session.InFolder);
        Assert.Equal("Work", r.Session.FolderName);
        Assert.Equal("One", r.Session.Slots[0].Name);
        Assert.Empty(r.Ran);

        r.Session.OnClick(true, 0, 0, 300); // centre: back to the parent
        Assert.False(r.Session.InFolder);
        Assert.Null(r.Session.FolderName);
        Assert.True(r.Session.IsOpen);
    }

    [Fact]
    public void BackspaceGoesBackAndEscapeClosesEverything()
    {
        var folder = new RingSlot(new ActionSpec.None()) { Folder = [Keys("Ctrl+1")] };
        var r = new Rec(Eight(i => i == 0 ? folder : RingSlot.Empty));
        r.Session.OnTrigger(false, 100);
        r.Session.OnKey(RingKey.Up, 110);
        r.Session.OnKey(RingKey.Enter, 120);
        Assert.True(r.Session.InFolder);
        Assert.Equal(RingDefaults.FolderName, r.Session.FolderName); // unnamed folder
        r.Session.OnKey(RingKey.Back, 130);
        Assert.False(r.Session.InFolder);
        r.Session.OnKey(RingKey.Back, 140); // at the root: nothing
        Assert.True(r.Session.IsOpen);
        r.Session.OnKey(RingKey.Enter, 150);
        r.Session.OnKey(RingKey.Escape, 160);
        Assert.Equal([RingCloseReason.Escape], r.Closed);
    }

    [Fact]
    public void FolderOpenedByHoldReleaseStaysOpen()
    {
        var folder = new RingSlot(new ActionSpec.None()) { Name = "F", Folder = [Keys("Ctrl+1")] };
        var r = new Rec(Eight(i => i == 4 ? folder : RingSlot.Empty));
        r.Point(null, 0);
        r.Point(4, 100);
        r.Session.OnTrigger(false, 700);
        Assert.True(r.Session.IsOpen);
        Assert.True(r.Session.InFolder);
        Assert.Equal(RingPhase.Open, r.Session.Phase);
    }

    [Fact]
    public void FolderChildrenRunLikeSlotsAndRunsCloseFirst()
    {
        var folder = new RingSlot(new ActionSpec.None()) { Folder = [Keys("Ctrl+1")] };
        var r = new Rec(Eight(i => i == 0 ? folder : RingSlot.Empty));
        var order = new List<string>();
        r.Session.Closed += (_, _) => order.Add("closed");
        r.Session.ActionRequested += (_, _) => order.Add("run");
        r.Session.OnTrigger(false, 100);
        var (x, y) = At(0);
        r.Session.OnClick(true, x, y, 200); // open folder
        r.Session.OnClick(true, x, y, 300); // run child 0
        Assert.Equal([new ActionSpec.Keys("Ctrl+1")], r.Ran);
        Assert.Equal(["closed", "run"], order);
    }

    [Fact]
    public void IdleTimeoutClosesAfterThirtySeconds()
    {
        var r = new Rec(now: 1000);
        r.Session.OnTrigger(false, 1100);
        r.Session.Tick(1100 + RingSession.IdleTimeoutMs - 1);
        Assert.True(r.Session.IsOpen);
        r.Session.Tick(1100 + RingSession.IdleTimeoutMs);
        Assert.Equal([RingCloseReason.Idle], r.Closed);
    }

    [Fact]
    public void InputResetsTheIdleTimer()
    {
        var r = new Rec(now: 0);
        r.Session.OnTrigger(false, 100);
        r.Point(1, 20_000);
        r.Session.Tick(40_000);
        Assert.True(r.Session.IsOpen);
        r.Session.Tick(50_001);
        Assert.Equal([RingCloseReason.Idle], r.Closed);
    }

    [Fact]
    public void ExternalCloseReportsReasonOnceAndLaterInputIsIgnored()
    {
        var r = new Rec();
        r.Session.Close(RingCloseReason.FocusLost);
        r.Session.Close(RingCloseReason.DeviceLost);
        r.Session.OnClick(true, 0, 0, 10);
        r.Session.OnKey(RingKey.Enter, 20);
        Assert.Equal([RingCloseReason.FocusLost], r.Closed);
        Assert.Empty(r.Ran);
    }

    [Fact]
    public void WheelOverAnAdjustableSlotRepeatsItAndKeepsTheRingOpen()
    {
        var slots = Eight(i => i == 0 ? Keys("VolumeUp|VolumeDown") : i == 1 ? new RingSlot(new ActionSpec.Brightness(10)) : Keys("F" + i));
        var r = new Rec(slots);
        r.Session.OnTrigger(false, 100);
        r.Point(0, 110);
        r.Session.OnWheel(120, 120);
        r.Session.OnWheel(-120, 130);
        r.Session.OnWheel(240, 140); // two notches
        Assert.Equal([new ActionSpec.Keys("VolumeUp"), new ActionSpec.Keys("VolumeDown"), new ActionSpec.Keys("VolumeUp"), new ActionSpec.Keys("VolumeUp")], r.Adjusted);
        r.Point(1, 150);
        r.Session.OnWheel(120, 160);
        r.Session.OnWheel(-120, 170);
        Assert.Equal(new ActionSpec.Brightness(10), r.Adjusted[4]);
        Assert.Equal(new ActionSpec.Brightness(-10), r.Adjusted[5]);
        Assert.True(r.Session.IsOpen);
        Assert.Empty(r.Closed);
    }

    [Fact]
    public void WheelOverANormalOrEmptySlotDoesNothing()
    {
        var r = new Rec(Eight(i => i == 3 ? RingSlot.Empty : Keys("Ctrl+C")));
        r.Session.OnTrigger(false, 100);
        r.Point(0, 110);
        r.Session.OnWheel(120, 120);
        r.Point(3, 130);
        r.Session.OnWheel(-120, 140);
        r.Point(null, 150);
        r.Session.OnWheel(120, 160);
        Assert.Empty(r.Adjusted);
        Assert.True(r.Session.IsOpen);
    }
}
