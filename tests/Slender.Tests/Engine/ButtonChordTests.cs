using Slender.Engine;

namespace Slender.Tests.Engine;

public class ButtonChordTests
{
    private const ChordButton L = ChordButton.Left;
    private const ChordButton R = ChordButton.Right;

    private sealed class Rig
    {
        public readonly FakeHoldTimer Timer = new();
        public readonly List<(ChordButton, bool)> Sent = [];
        public readonly List<bool> Changes = [];
        public readonly ButtonChord Chord;

        public Rig()
        {
            Chord = new ButtonChord((b, d) => Sent.Add((b, d)), Timer);
            Chord.Changed += (_, a) => Changes.Add(a);
        }
    }

    [Fact]
    public void Both_buttons_within_the_window_are_a_chord_and_nothing_reaches_the_app()
    {
        var r = new Rig();
        Assert.True(r.Chord.OnButton(L, true, 10, 10));
        Assert.True(r.Chord.OnButton(R, true, 10, 10));
        Assert.True(r.Chord.IsActive);
        Assert.True(r.Chord.OnButton(L, false, 10, 10));
        Assert.True(r.Chord.IsActive); // still one button held
        Assert.True(r.Chord.OnButton(R, false, 10, 10));

        Assert.False(r.Chord.IsActive);
        Assert.Equal([true, false], r.Changes);
        Assert.Empty(r.Sent);
        r.Timer.Elapse(); // the cancelled window does nothing
        Assert.Empty(r.Sent);
    }

    [Fact]
    public void Right_first_then_left_is_a_chord_too()
    {
        var r = new Rig();
        r.Chord.OnButton(R, true, 0, 0);
        r.Chord.OnButton(L, true, 0, 0);
        Assert.True(r.Chord.IsActive);
    }

    [Fact]
    public void A_quick_click_is_replayed_whole_on_release()
    {
        var r = new Rig();
        Assert.True(r.Chord.OnButton(L, true, 5, 5));
        Assert.Empty(r.Sent);
        Assert.True(r.Chord.OnButton(L, false, 5, 5)); // the real release is swallowed: the replay carries both halves
        Assert.Equal([(L, true), (L, false)], r.Sent);
        Assert.Empty(r.Changes);
    }

    [Fact]
    public void A_held_press_is_replayed_when_the_window_ends_and_its_release_is_injected_after_it()
    {
        var r = new Rig();
        r.Chord.OnButton(R, true, 5, 5);
        r.Timer.Elapse();
        Assert.Equal([(R, true)], r.Sent);
        // The real release is swallowed and re-sent: a real release can overtake the injected press in Windows' queue
        // (a ~50 ms click), leaving the app with a press and no release. Injected input keeps its order.
        Assert.True(r.Chord.OnButton(R, false, 5, 5));
        Assert.Equal([(R, true), (R, false)], r.Sent); // a right-click menu still opens on this release
    }

    [Fact]
    public void A_missed_release_is_sent_before_the_next_press()
    {
        var r = new Rig();
        r.Chord.OnButton(L, true, 0, 0);
        r.Timer.Elapse();                              // replayed press
        Assert.True(r.Chord.OnButton(L, true, 0, 0));  // pressed again: its release never came through
        Assert.Equal([(L, true), (L, false), (L, true)], r.Sent);
        Assert.True(r.Chord.OnButton(L, false, 0, 0));
        Assert.Equal([(L, true), (L, false), (L, true), (L, false)], r.Sent);
        // Back to normal afterwards: the next click is held back as usual.
        Assert.True(r.Chord.OnButton(L, true, 0, 0));
        Assert.True(r.Chord.IsPending);
    }

    [Fact]
    public void Moving_replays_the_press_at_once_so_drags_work()
    {
        var r = new Rig();
        r.Chord.OnButton(L, true, 100, 100);
        r.Chord.OnMove(102, 101); // inside the slop: still waiting
        Assert.Empty(r.Sent);
        r.Chord.OnMove(110, 100);
        Assert.Equal([(L, true)], r.Sent);
        Assert.False(r.Chord.IsPending);
        Assert.True(r.Chord.OnButton(L, false, 120, 100));
        Assert.Equal([(L, true), (L, false)], r.Sent);
    }

    [Fact]
    public void Holding_one_button_then_pressing_the_other_later_is_not_a_chord()
    {
        var r = new Rig();
        r.Chord.OnButton(L, true, 0, 0);
        r.Timer.Elapse(); // left is now held normally
        Assert.False(r.Chord.OnButton(R, true, 0, 0));
        Assert.False(r.Chord.OnButton(R, false, 0, 0));
        Assert.True(r.Chord.OnButton(L, false, 0, 0)); // the replayed press gets an injected release
        Assert.Empty(r.Changes);
        Assert.Equal([(L, true), (L, false)], r.Sent);
    }

    [Fact]
    public void Off_passes_everything_straight_through()
    {
        var r = new Rig();
        r.Chord.Enabled = false;
        Assert.False(r.Chord.OnButton(L, true, 0, 0));
        Assert.False(r.Chord.OnButton(R, true, 0, 0));
        Assert.False(r.Chord.OnButton(R, false, 0, 0));
        Assert.False(r.Chord.OnButton(L, false, 0, 0));
        Assert.Empty(r.Sent);
        Assert.Empty(r.Changes);
    }

    [Fact]
    public void Over_a_window_that_cannot_take_a_replay_presses_pass_straight_through()
    {
        var timer = new FakeHoldTimer();
        var sent = new List<(ChordButton, bool)>();
        var chord = new ButtonChord((b, d) => sent.Add((b, d)), timer, canHoldBack: (x, _) => x < 100);

        Assert.False(chord.OnButton(L, true, 500, 0)); // an elevated window: never held back
        Assert.False(chord.OnButton(L, false, 500, 0));
        Assert.True(chord.OnButton(L, true, 50, 0));   // a normal window: held back as usual
        Assert.Empty(sent);
    }

    [Fact]
    public void Pressing_again_during_a_chord_stays_inside_the_chord()
    {
        var r = new Rig();
        r.Chord.OnButton(L, true, 0, 0);
        r.Chord.OnButton(R, true, 0, 0);
        Assert.True(r.Chord.OnButton(L, false, 0, 0));
        Assert.True(r.Chord.OnButton(L, true, 0, 0));
        Assert.True(r.Chord.OnButton(L, false, 0, 0));
        Assert.True(r.Chord.OnButton(R, false, 0, 0));
        Assert.Equal([true, false], r.Changes);
        Assert.Empty(r.Sent);
        // After the chord, a plain click works normally again.
        Assert.True(r.Chord.OnButton(L, true, 0, 0));
        Assert.True(r.Chord.OnButton(L, false, 0, 0));
        Assert.Equal([(L, true), (L, false)], r.Sent);
    }
}
