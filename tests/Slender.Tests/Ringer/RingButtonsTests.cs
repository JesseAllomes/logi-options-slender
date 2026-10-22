using Slender.Engine;
using Slender.Engine.Ringer;
using Slender.Hidpp;
using Slender.Tests.Engine;

namespace Slender.Tests.Ringer;

public class RingButtonsTests
{
    private sealed class FakeChannel : IRingCommandChannel
    {
        public int Arms, Disarms;
        public List<RingCommand> Offers { get; } = [];
        public void Arm() => Arms++;
        public void Disarm() => Disarms++;
        public void Offer(RingCommand command) => Offers.Add(command);
        public void Dispose() { }
    }

    private static readonly HapticCue Cue = new(true, 9, 1, 150, 100, 1, 1500);

    private sealed record Rig(SlenderEngine Engine, FakeHook Hook, FakeExecutor Executor, FakeChannel Channel) : IDisposable
    {
        public void Dispose() => Engine.Dispose();
    }

    /// <summary>An engine whose Back button runs an action; Middle has no binding.</summary>
    private static Rig Make(ushort answer, ushort reject, bool haptics = true)
    {
        var hook = new FakeHook();
        var executor = new FakeExecutor();
        var settings = new HapticSettings(Enabled: haptics) { Ringer = Cue, RingAnswerButton = answer, RingRejectButton = reject };
        var profile = new Profile(null, new Dictionary<ushort, ActionSpec> { [ControlId.Back] = new ActionSpec.Excel(ExcelCommand.PreviousSheet) }) { DisplayName = "All apps" };
        var config = SlenderConfig.Default with { Profiles = [profile], Haptics = settings };
        var engine = new SlenderEngine(new FakeMouse(), hook, new FakeForeground(), new FakeExcel(), executor, config, new ListLog(), new InlineQueue(), () => 0, new FakeHoldTimer());
        var channel = new FakeChannel();
        engine.RingChannel = channel;
        engine.Start();
        return new Rig(engine, hook, executor, channel);
    }

    private static void Ring(Rig r) => r.Engine.SignalRing(new RingSignal(true, RingerSignalSource.Notification));

    [Fact]
    public void Ring_buttons_are_off_by_default()
    {
        Assert.Equal(0, new HapticSettings().RingAnswerButton);
        Assert.Equal(0, new HapticSettings().RingRejectButton);
        using var r = Make(0, 0);
        Ring(r);
        Assert.Equal(0, r.Channel.Arms); // nothing armed, nothing for the extension to poll
        Assert.False(r.Hook.Button(HookButton.Middle, true).Handled);
    }

    [Fact]
    public void While_a_call_rings_the_answer_button_is_consumed_and_raises_answer_and_the_ring_ends()
    {
        using var r = Make(ControlId.Back, ControlId.Middle);
        Ring(r);
        Assert.Equal(1, r.Channel.Arms);

        r.Hook.Click(HookButton.Back); // the click watch sees the press first, and must not end the ring before the button is taken
        Assert.True(r.Hook.ClickWatch);
        Assert.True(r.Hook.Button(HookButton.Back, true).Handled);
        Assert.Equal([RingCommand.Answer], r.Channel.Offers);
        Assert.Empty(r.Executor.Calls); // its normal binding did not run
        Assert.False(r.Hook.ClickWatch); // the ring ended
        r.Hook.Button(HookButton.Back, false); // the release is left to the real hook's pairing, which swallows it with the press
        Assert.Empty(r.Executor.Calls);
    }

    [Fact]
    public void The_reject_button_raises_reject()
    {
        using var r = Make(ControlId.Back, ControlId.Middle);
        Ring(r);
        r.Hook.Click(HookButton.Middle);
        Assert.True(r.Hook.Button(HookButton.Middle, true).Handled);
        Assert.Equal([RingCommand.Reject], r.Channel.Offers);
        Assert.False(r.Hook.ClickWatch);
    }

    [Fact]
    public void A_second_press_after_the_ring_ended_is_normal_again()
    {
        using var r = Make(ControlId.Back, ControlId.Middle);
        Ring(r);
        r.Hook.Click(HookButton.Back);
        r.Hook.Button(HookButton.Back, true);
        r.Hook.Button(HookButton.Back, false);
        Assert.Single(r.Channel.Offers);

        Assert.True(r.Hook.Button(HookButton.Back, true).Handled); // Back is bound: it runs its action as usual
        Assert.Single(r.Executor.Calls);
        Assert.Single(r.Channel.Offers);
    }

    [Fact]
    public void Outside_a_ringing_call_the_buttons_keep_their_normal_action()
    {
        using var r = Make(ControlId.Back, ControlId.Middle);
        Assert.True(r.Hook.Button(HookButton.Back, true).Handled);
        Assert.Single(r.Executor.Calls);
        Assert.False(r.Hook.Button(HookButton.Middle, true).Handled); // unbound: left to Windows
        Assert.Empty(r.Channel.Offers);

        Ring(r);
        r.Engine.SignalRing(new RingSignal(false, RingerSignalSource.Notification)); // the call stopped
        Assert.Equal(1, r.Channel.Disarms);
        Assert.False(r.Hook.Button(HookButton.Middle, true).Handled);
        Assert.Empty(r.Channel.Offers);
    }

    [Fact]
    public void Other_buttons_during_a_ring_end_it_as_before_and_do_not_raise_commands()
    {
        using var r = Make(ControlId.Middle, 0);
        Ring(r);
        r.Hook.Click(HookButton.Back);
        Assert.False(r.Hook.ClickWatch);
        Assert.Empty(r.Channel.Offers);
    }

    [Fact]
    public void Nothing_is_armed_or_consumed_when_the_ringer_cannot_ring()
    {
        using var r = Make(ControlId.Middle, 0, haptics: false);
        Ring(r);
        Assert.Equal(0, r.Channel.Arms);
        Assert.False(r.Hook.Button(HookButton.Middle, true).Handled);
    }

    [Fact]
    public void A_ring_button_press_is_taken_once_per_ring()
    {
        int ended = 0;
        var session = new RingerSession(new FakeHoldTimer(), new InlineQueue(), () => Cue, _ => { }, () => ended++, _ => { }, new ListLog());
        Assert.False(session.TryConsume());
        session.Signal(true);
        Assert.True(session.TryConsume());
        Assert.False(session.TryConsume());
        Assert.False(session.IsRinging);
        Assert.Equal(1, ended);
        session.Signal(true); // the next ring can be answered again
        Assert.True(session.TryConsume());
    }

    [Fact]
    public void Only_hook_buttons_can_be_chosen_and_the_match_is_exact()
    {
        Assert.Equal<ushort>(0, RingButtons.Normalize(ControlId.Gesture));
        Assert.Equal<ushort>(0, RingButtons.Normalize(-1));
        Assert.Equal<ushort>(0, RingButtons.Normalize(70000));
        Assert.Equal(ControlId.Forward, RingButtons.Normalize(ControlId.Forward));
        var h = new HapticSettings { RingAnswerButton = ControlId.Back, RingRejectButton = ControlId.Back };
        Assert.Equal(RingCommand.Answer, RingButtons.Match(h, ControlId.Back));
        Assert.Null(RingButtons.Match(h, ControlId.Middle));
        Assert.Null(RingButtons.Match(new HapticSettings(), 0));
    }

    [Fact]
    public void The_command_pipe_exists_only_while_armed_and_hands_over_one_command()
    {
        string name = "Slender.Test." + Guid.NewGuid().ToString("N");
        using var server = new RingCommandServer(name);
        Assert.Equal(RingCommandPipe.Off, RingCommandPipe.Poll(name, 50)); // nothing armed, no pipe

        server.Arm();
        Assert.Equal(RingCommandPipe.None, RingCommandPipe.Poll(name, 2000));
        server.Offer(RingCommand.Reject);
        Assert.Equal(RingCommandPipe.Reject, RingCommandPipe.Poll(name, 2000));
        Assert.True(SpinWait.SpinUntil(() => !server.IsArmed, 2000));
        Assert.Equal(RingCommandPipe.Off, RingCommandPipe.Poll(name, 200)); // delivered: gone
    }

    [Fact]
    public void A_stop_closes_the_pipe_unless_a_command_is_waiting()
    {
        string name = "Slender.Test." + Guid.NewGuid().ToString("N");
        using var server = new RingCommandServer(name);
        server.Arm();
        Assert.Equal(RingCommandPipe.None, RingCommandPipe.Poll(name, 2000));
        server.Disarm();
        Assert.False(server.IsArmed);
        Assert.Equal(RingCommandPipe.Off, RingCommandPipe.Poll(name, 100));

        server.Arm();
        server.Offer(RingCommand.Answer);
        server.Disarm(); // the call stopped but the command was not fetched yet
        Assert.True(server.IsArmed);
        Assert.Equal(RingCommandPipe.Answer, RingCommandPipe.Poll(name, 2000));
    }
}
