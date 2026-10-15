using System.Text.Json;
using Slender.Engine;
using Slender.Engine.Config;
using Slender.Engine.Ring;
using Slender.Hidpp;
using Slender.Settings.Logic;

namespace Slender.Tests.Engine;

/// <summary>Hand-fired hold timer: the threshold is reached when the test says so.</summary>
internal sealed class FakeHoldTimer : IHoldTimer
{
    private sealed class Handle(FakeHoldTimer owner, Action fire) : IDisposable
    {
        public Action Fire { get; } = fire;
        public void Dispose() => owner.Pending.Remove(this);
    }

    private List<Handle> Pending { get; } = [];
    public List<int> Delays { get; } = [];

    public IDisposable Schedule(int delayMs, Action fire)
    {
        Delays.Add(delayMs);
        var h = new Handle(this, fire);
        Pending.Add(h);
        return h;
    }

    /// <summary>The threshold passes for every press still waiting (a released press has cancelled its timer).</summary>
    public void Elapse()
    {
        foreach (var h in Pending.ToArray()) h.Fire();
    }
}

public class TapHoldConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new ActionSpecJsonConverter() } };

    [Fact]
    public void Hold_threshold_is_350_ms() => Assert.Equal(350, ActionSpec.TapHold.HoldMs);

    [Fact]
    public void Round_trips_with_the_documented_shape()
    {
        ActionSpec spec = new ActionSpec.TapHold(new ActionSpec.Keys("Ctrl+C"), new ActionSpec.ActionsRing());
        var json = JsonSerializer.Serialize(spec, Options);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("tapHold", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("keys", doc.RootElement.GetProperty("tap").GetProperty("type").GetString());
        Assert.Equal("actionsRing", doc.RootElement.GetProperty("hold").GetProperty("type").GetString());
        Assert.Equal(spec, JsonSerializer.Deserialize<ActionSpec>(json, Options));
    }

    [Fact]
    public void Nested_tapHold_is_rejected_on_read_and_write()
    {
        const string json = """{"type":"tapHold","tap":{"type":"tapHold","tap":{"type":"none"},"hold":{"type":"none"}},"hold":{"type":"none"}}""";
        Assert.Throws<FormatException>(() => JsonSerializer.Deserialize<ActionSpec>(json, Options));
        var nested = new ActionSpec.TapHold(new ActionSpec.None(), new ActionSpec.TapHold(new ActionSpec.None(), new ActionSpec.None()));
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize<ActionSpec>(nested, Options));
    }

    [Fact]
    public void Missing_half_is_rejected() =>
        Assert.Throws<FormatException>(() => JsonSerializer.Deserialize<ActionSpec>("""{"type":"tapHold","tap":{"type":"none"}}""", Options));
}

public class TapHoldEngineTests
{
    private sealed class Rig
    {
        public FakeMouse Mouse = new();
        public FakeHook Hook = new();
        public FakeForeground Fg = new();
        public FakeExecutor Exec = new();
        public FakeHoldTimer Timer = new();
        public SlenderEngine Engine;
        public List<(bool Pressed, bool HoldOnly)> Ring = [];

        public Rig(SlenderConfig cfg)
        {
            Engine = new SlenderEngine(Mouse, Hook, Fg, new FakeExcel(), Exec, cfg, new ListLog(), new InlineQueue(), () => 0, Timer);
            Engine.RingTrigger += (_, e) => Ring.Add((e.Pressed, e.HoldOnly));
            Engine.Start();
        }
    }

    private static readonly ActionSpec Copy = new ActionSpec.Keys("Ctrl+C");
    private static readonly ActionSpec Paste = new ActionSpec.Keys("Ctrl+V");

    private static SlenderConfig Cfg(ushort control, ActionSpec action, HapticSettings? h = null) =>
        SlenderConfig.Default with
        {
            Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec> { [control] = action })],
            Haptics = h ?? new HapticSettings(),
        };

    [Fact]
    public void Quick_press_runs_tap_once_and_never_the_hold()
    {
        var r = new Rig(Cfg(ControlId.SensePanel, new ActionSpec.TapHold(Copy, Paste)));
        r.Mouse.Press(ControlId.SensePanel, true);
        Assert.Empty(r.Exec.Calls); // nothing yet: it may still become a hold
        Assert.Equal([ActionSpec.TapHold.HoldMs], r.Timer.Delays);
        r.Mouse.Press(ControlId.SensePanel, false);
        Assert.Equal([(Copy, true), (Copy, false)], r.Exec.Calls);
        r.Timer.Elapse(); // a stale timer does nothing
        Assert.Equal(2, r.Exec.Calls.Count);
        Assert.Empty(r.Mouse.Haptics);
    }

    [Fact]
    public void Hold_plays_a_tick_then_presses_hold_and_releases_with_the_button()
    {
        var r = new Rig(Cfg(ControlId.SensePanel, new ActionSpec.TapHold(Copy, Paste)));
        r.Mouse.Press(ControlId.SensePanel, true);
        r.Timer.Elapse();
        Assert.Equal([(Paste, true)], r.Exec.Calls);
        Assert.Single(r.Mouse.Haptics);
        r.Mouse.Press(ControlId.SensePanel, false);
        Assert.Equal([(Paste, true), (Paste, false)], r.Exec.Calls);
    }

    [Fact]
    public void Hold_tick_respects_haptics_off()
    {
        var r = new Rig(Cfg(ControlId.SensePanel, new ActionSpec.TapHold(Copy, Paste), new HapticSettings(Enabled: false)));
        r.Mouse.Press(ControlId.SensePanel, true);
        r.Timer.Elapse();
        Assert.Empty(r.Mouse.Haptics);
        Assert.Equal([(Paste, true)], r.Exec.Calls);
    }

    [Fact]
    public void Hold_ring_opens_hold_only_at_the_threshold_and_releases_with_the_button()
    {
        var r = new Rig(Cfg(ControlId.SensePanel, new ActionSpec.TapHold(Copy, new ActionSpec.ActionsRing())));
        r.Mouse.Press(ControlId.SensePanel, true);
        Assert.Empty(r.Ring);
        r.Timer.Elapse();
        Assert.Equal([(true, true)], r.Ring);
        r.Mouse.Press(ControlId.SensePanel, false);
        Assert.Equal([(true, true), (false, false)], r.Ring);
        Assert.Empty(r.Exec.Calls);
    }

    [Fact]
    public void Quick_press_with_ring_hold_runs_tap_and_does_not_open_the_ring()
    {
        var r = new Rig(Cfg(ControlId.SensePanel, new ActionSpec.TapHold(Copy, new ActionSpec.ActionsRing())));
        r.Mouse.Press(ControlId.SensePanel, true);
        r.Mouse.Press(ControlId.SensePanel, false);
        Assert.Empty(r.Ring);
        Assert.Equal([(Copy, true), (Copy, false)], r.Exec.Calls);
    }

    [Fact]
    public void Disconnect_mid_hold_releases_the_hold_and_a_pending_press_does_nothing()
    {
        var r = new Rig(Cfg(ControlId.SensePanel, new ActionSpec.TapHold(Copy, Paste)));
        r.Mouse.Press(ControlId.SensePanel, true);
        r.Timer.Elapse();
        r.Mouse.Connect(false);
        Assert.Equal([(Paste, true), (Paste, false)], r.Exec.Calls);
        r.Mouse.Press(ControlId.SensePanel, false); // a late release is ignored
        Assert.Equal(2, r.Exec.Calls.Count);

        var pending = new Rig(Cfg(ControlId.SensePanel, new ActionSpec.TapHold(Copy, Paste)));
        pending.Mouse.Press(ControlId.SensePanel, true);
        pending.Mouse.Connect(false);
        pending.Timer.Elapse();
        Assert.Empty(pending.Exec.Calls);
    }

    [Fact]
    public void Disconnect_mid_ring_hold_releases_the_ring()
    {
        var r = new Rig(Cfg(ControlId.SensePanel, new ActionSpec.TapHold(Copy, new ActionSpec.ActionsRing())));
        r.Mouse.Press(ControlId.SensePanel, true);
        r.Timer.Elapse();
        r.Mouse.Connect(false);
        Assert.Equal([(true, true), (false, false)], r.Ring);
    }

    [Fact]
    public void Foreground_switch_while_held_releases_the_action_chosen_at_press()
    {
        var cfg = SlenderConfig.Default with
        {
            Profiles =
            [
                new Profile(null, new Dictionary<ushort, ActionSpec> { [ControlId.SensePanel] = new ActionSpec.TapHold(Copy, Paste) }),
                new Profile("excel", new Dictionary<ushort, ActionSpec> { [ControlId.SensePanel] = new ActionSpec.TapHold(Copy, new ActionSpec.Keys("F2")) }),
            ],
        };
        var r = new Rig(cfg);
        r.Mouse.Press(ControlId.SensePanel, true);
        r.Fg.Switch("excel");
        r.Timer.Elapse();
        r.Mouse.Press(ControlId.SensePanel, false);
        Assert.Equal([(Paste, true), (Paste, false)], r.Exec.Calls);
    }

    [Fact]
    public void Hook_button_tap_and_hold_are_swallowed_and_paired()
    {
        var r = new Rig(Cfg(ControlId.Middle, new ActionSpec.TapHold(Copy, Paste)));
        Assert.True(r.Hook.Button(HookButton.Middle, true).Handled);
        r.Hook.Button(HookButton.Middle, false);
        Assert.Equal([(Copy, true), (Copy, false)], r.Exec.Calls);

        r.Exec.Calls.Clear();
        Assert.True(r.Hook.Button(HookButton.Middle, true).Handled);
        r.Timer.Elapse();
        Assert.Equal([(Paste, true)], r.Exec.Calls);
        r.Hook.Button(HookButton.Middle, false);
        Assert.Equal([(Paste, true), (Paste, false)], r.Exec.Calls);
    }

    [Fact]
    public void Hook_button_ring_hold_is_hold_only()
    {
        var r = new Rig(Cfg(ControlId.Back, new ActionSpec.TapHold(Copy, new ActionSpec.ActionsRing())));
        r.Hook.Button(HookButton.Back, true);
        r.Timer.Elapse();
        r.Hook.Button(HookButton.Back, false);
        Assert.Equal([(true, true), (false, false)], r.Ring);
    }

    [Fact]
    public void Tap_that_is_the_ring_keeps_the_normal_tap_behaviour()
    {
        var r = new Rig(Cfg(ControlId.SensePanel, new ActionSpec.TapHold(new ActionSpec.ActionsRing(), Paste)));
        r.Mouse.Press(ControlId.SensePanel, true);
        r.Mouse.Press(ControlId.SensePanel, false);
        Assert.Equal([(true, false), (false, false)], r.Ring);
    }

    [Fact]
    public void RunRingAction_ignores_a_tapHold()
    {
        var r = new Rig(SlenderConfig.Default);
        r.Engine.RunRingAction(new ActionSpec.TapHold(Copy, Paste));
        Assert.Empty(r.Exec.Calls);
    }
}

public class TapHoldKeyboardTests
{
    private const ushort Key = KeyboardId.Mute;

    private static (KeyboardRouter Router, FakeKeyboard Kb, FakeExecutor Ex, FakeHoldTimer Timer, List<(bool, bool)> Ring) Make(ActionSpec action)
    {
        var cfg = SlenderConfig.Default with
        {
            Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()) { KeyBindings = new Dictionary<ushort, ActionSpec> { [Key] = action } }],
        };
        var kb = new FakeKeyboard();
        var ex = new FakeExecutor();
        var timer = new FakeHoldTimer();
        var ring = new List<(bool, bool)>();
        var router = new KeyboardRouter(kb, new FakeForeground(), ex, cfg, new ListLog(), new InlineQueue(), () => 0, timer);
        router.RingTrigger += (_, e) => ring.Add((e.Pressed, e.HoldOnly));
        router.Start();
        return (router, kb, ex, timer, ring);
    }

    [Fact]
    public void Quick_press_runs_tap_once()
    {
        var (r, kb, ex, timer, _) = Make(new ActionSpec.TapHold(new ActionSpec.Keys("Ctrl+C"), new ActionSpec.Keys("Ctrl+V")));
        using (r)
        {
            kb.Key(Key, true);
            Assert.Empty(ex.Calls);
            kb.Key(Key, false);
            timer.Elapse();
            Assert.Equal([((ActionSpec)new ActionSpec.Keys("Ctrl+C"), true), (new ActionSpec.Keys("Ctrl+C"), false)], ex.Calls);
        }
    }

    [Fact]
    public void Hold_holds_the_chord_until_the_key_is_released()
    {
        var hold = new ActionSpec.Keys("Ctrl+V");
        var (r, kb, ex, timer, _) = Make(new ActionSpec.TapHold(new ActionSpec.Keys("Ctrl+C"), hold));
        using (r)
        {
            kb.Key(Key, true);
            timer.Elapse();
            Assert.Equal([((ActionSpec)hold, true)], ex.Calls);
            kb.Key(Key, false);
            Assert.Equal([((ActionSpec)hold, true), (hold, false)], ex.Calls);
        }
    }

    [Fact]
    public void Ring_hold_is_hold_only_and_a_quick_press_runs_the_tap()
    {
        var (r, kb, ex, timer, ring) = Make(new ActionSpec.TapHold(new ActionSpec.Keys("F2"), new ActionSpec.ActionsRing()));
        using (r)
        {
            kb.Key(Key, true);
            kb.Key(Key, false);
            Assert.Empty(ring);
            Assert.Equal(2, ex.Calls.Count);

            kb.Key(Key, true);
            timer.Elapse();
            kb.Key(Key, false);
            Assert.Equal([(true, true), (false, false)], ring);
        }
    }
}

public class TapHoldRingSessionTests
{
    private static readonly RingMetrics Metrics = RingLayout.Metrics(RingStyle.Bezel, RingSize.Medium);

    [Fact]
    public void Hold_only_session_cancels_on_a_quick_release_instead_of_staying_open()
    {
        var plain = new RingSession(RingDefaults.Slots(), Metrics, 0);
        plain.OnTrigger(false, 100);
        Assert.Equal(RingPhase.Open, plain.Phase); // a plain binding: tap leaves the ring open

        var closed = new List<RingCloseReason>();
        var hold = new RingSession(RingDefaults.Slots(), Metrics, 0, holdOnly: true);
        hold.Closed += (_, r) => closed.Add(r);
        hold.OnTrigger(false, 100);
        Assert.False(hold.IsOpen);
        Assert.Equal([RingCloseReason.Cancelled], closed);
    }

    [Fact]
    public void Hold_only_session_runs_the_slot_released_on_after_movement()
    {
        var ran = new List<ActionSpec>();
        var hold = new RingSession(RingDefaults.Slots(), Metrics, 0, holdOnly: true);
        hold.ActionRequested += (_, a) => ran.Add(a);
        hold.OnPointer(0, 0, 10);
        var (x, y) = RingLayout.SlotCenter(RingStyle.Bezel, RingSize.Medium, 0);
        hold.OnPointer(x, y, 50);
        hold.OnTrigger(false, 100);
        Assert.Single(ran);
    }
}

public class TapHoldPickerLogicTests
{
    [Fact]
    public void Label_reads_press_and_hold()
    {
        var th = new ActionSpec.TapHold(new ActionSpec.MouseButton("Back"), new ActionSpec.ActionsRing());
        Assert.Equal("Press: Back · Hold: Actions Ring", ActionCatalog.Describe(th).Text);
        Assert.Equal("Press: Nothing · Hold: Actions Ring", ActionCatalog.Plain(new ActionSpec.TapHold(new ActionSpec.None(), new ActionSpec.ActionsRing())));
    }

    [Fact]
    public void Sense_panel_preset_holds_the_ring()
    {
        var preset = ActionCatalog.HoldOpensRing(new ActionSpec.Keys("Ctrl+C"));
        Assert.Equal(new ActionSpec.Keys("Ctrl+C"), preset.Tap);
        Assert.IsType<ActionSpec.ActionsRing>(preset.Hold);
    }

    [Fact]
    public void Nested_tapHold_cannot_be_built_and_choices_never_offer_one()
    {
        var inner = new ActionSpec.TapHold(new ActionSpec.None(), new ActionSpec.None());
        Assert.False(ActionCatalog.TryBuildTapHold(inner, new ActionSpec.None(), out _));
        Assert.False(ActionCatalog.TryBuildTapHold(new ActionSpec.None(), inner, out _));
        Assert.True(ActionCatalog.TryBuildTapHold(new ActionSpec.None(), new ActionSpec.ActionsRing(), out _));
        foreach (var kind in Enum.GetValues<DeviceKind>())
            Assert.DoesNotContain(ActionCatalog.TapHoldChoices(kind), o => o.Action is ActionSpec.TapHold);
    }

    [Fact]
    public void Not_offered_on_gesture_swipes_or_thumb_wheel()
    {
        Assert.False(ActionCatalog.CanTapHold(DeviceKind.Mouse, ControlId.Gesture));
        Assert.False(ActionCatalog.CanTapHold(DeviceKind.Mouse, DeviceIds.ThumbWheelPseudoId));
        Assert.False(ActionCatalog.CanTapHold(DeviceKind.Mouse, DeviceIds.SwipeId(GestureDirection.Up)));
        Assert.True(ActionCatalog.CanTapHold(DeviceKind.Mouse, ControlId.SensePanel));
        Assert.True(ActionCatalog.CanTapHold(DeviceKind.Keyboard, KeyboardId.Mute));

        var th = new ActionSpec.TapHold(new ActionSpec.None(), new ActionSpec.ActionsRing());
        var cfg = SlenderConfig.Default;
        Assert.Same(cfg, ProfileOps.SetBinding(cfg, null, DeviceKind.Mouse, ControlId.Gesture, th));
        Assert.NotSame(cfg, ProfileOps.SetBinding(cfg, null, DeviceKind.Mouse, ControlId.SensePanel, th));
    }

    [Fact]
    public void Ring_slots_reject_tapHold()
    {
        var cfg = SlenderConfig.Default;
        var th = new ActionSpec.TapHold(new ActionSpec.None(), new ActionSpec.ActionsRing());
        Assert.Same(cfg, RingOps.SetSlot(cfg, null, new RingPath(0), new RingSlot(th)));
    }

    [Fact]
    public void Typed_shortcut_must_parse()
    {
        Assert.True(ActionCatalog.TryParseShortcut(" Ctrl+Shift+P ", out var keys));
        Assert.Equal("Ctrl+Shift+P", keys.Chord);
        Assert.False(ActionCatalog.TryParseShortcut("Ctrl+Banana", out _));
        Assert.False(ActionCatalog.TryParseShortcut("  ", out _));
    }
}
