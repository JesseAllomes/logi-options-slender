using Slender.Engine;
using Slender.Hidpp;
using Slender.Tests.Hidpp;

namespace Slender.Tests.Engine;

internal sealed class FakeKeyboard : ILogiKeyboard
{
    public string Name => "fake-kb";
    public bool IsConnected { get; set; } = true;
    public event EventHandler<bool>? ConnectionChanged;
    public void Connect(bool connected) { IsConnected = connected; ConnectionChanged?.Invoke(this, connected); }
    public event EventHandler<ButtonEvent>? KeyChanged;
    public List<ushort[]> Diverts { get; } = [];
    public List<bool> FnLocks { get; } = [];
    public bool Started;
    public bool ThrowOnDivert;

    public Task StartAsync(CancellationToken ct) { Started = true; return Task.CompletedTask; }
    public Task SetDivertedKeysAsync(IReadOnlyCollection<ushort> ids, CancellationToken ct)
    {
        lock (Diverts) Diverts.Add(ids.ToArray());
        return ThrowOnDivert ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask;
    }
    public Task SetFnLockAsync(bool enabled, CancellationToken ct) { lock (FnLocks) FnLocks.Add(enabled); return Task.CompletedTask; }
    public List<DisabledKeys> Disabled { get; } = [];
    public List<bool?[]> Channels { get; } = [];
    public int? CurrentChannel => null;
    public Task<DisabledKeys?> SetDisabledKeysAsync(DisabledKeys keys, CancellationToken ct)
    {
        lock (Disabled) Disabled.Add(keys);
        return Task.FromResult<DisabledKeys?>(keys);
    }
    public Task SetChannelFnLockAsync(IReadOnlyList<bool?> channels, CancellationToken ct) { lock (Channels) Channels.Add(channels.ToArray()); return Task.CompletedTask; }
    public Task<BatteryStatus?> GetBatteryAsync(CancellationToken ct) => Task.FromResult<BatteryStatus?>(null);
    public void Dispose() { }
    public void Key(ushort id, bool pressed) => KeyChanged?.Invoke(this, new ButtonEvent(id, pressed));
    public int DivertCount { get { lock (Diverts) return Diverts.Count; } }
    public int FnCount { get { lock (FnLocks) return FnLocks.Count; } }

    /// <summary>What the keyboard's own Fn lock is (Fn+Esc); ReadFnLockAsync reports it.</summary>
    public bool? FnNow;
    public int FnReads;
    public bool? FnLockNow => FnNow;
    public event EventHandler<bool>? FnLockRead;
    public Task<bool?> ReadFnLockAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref FnReads);
        if (FnNow is { } v) FnLockRead?.Invoke(this, v);
        return Task.FromResult(FnNow);
    }
    /// <summary>The keyboard was read (as on connect).</summary>
    public void ReadOnConnect(bool on) { FnNow = on; FnLockRead?.Invoke(this, on); }
}

public class KeyboardRouterTests
{
    private static Profile P(string? app, params (ushort Key, ActionSpec Action)[] keys) =>
        new(app, new Dictionary<ushort, ActionSpec>()) { KeyBindings = keys.ToDictionary(k => k.Key, k => k.Action) };

    private static SlenderConfig Cfg(bool fn, params Profile[] profiles) =>
        new(SlenderConfig.CurrentVersion, profiles, new ExcelSettings(), new HapticSettings(), false) { Keyboard = new KeyboardSettings(fn) };

    private static (KeyboardRouter Router, FakeKeyboard Kb, FakeForeground Fg, FakeExecutor Ex, ListLog Log) Make(SlenderConfig cfg)
    {
        var kb = new FakeKeyboard();
        var fg = new FakeForeground();
        var ex = new FakeExecutor();
        var log = new ListLog();
        return (new KeyboardRouter(kb, fg, ex, cfg, log, new InlineQueue()), kb, fg, ex, log);
    }

    [Fact]
    public void Divert_set_is_non_none_keys_across_all_profiles_sorted_and_distinct()
    {
        var cfg = Cfg(false,
            P(null, (KeyboardId.Mute, new ActionSpec.Keys("Ctrl+M")), (KeyboardId.Lock, new ActionSpec.None())),
            P("excel", (KeyboardId.Mute, new ActionSpec.Keys("Ctrl+N")), (KeyboardId.Calculator, new ActionSpec.Excel(ExcelCommand.NextSheet))));
        Assert.Equal(new ushort[] { KeyboardId.Calculator, KeyboardId.Mute }, KeyboardRouter.ComputeDivertSet(cfg));
        Assert.Empty(KeyboardRouter.ComputeDivertSet(SlenderConfig.Default));
    }

    [Fact]
    public void A_disconnect_releases_held_keys_and_their_later_release_runs_nothing()
    {
        var (r, kb, _, ex, _) = Make(Cfg(false, P(null, (KeyboardId.Mute, new ActionSpec.Keys("Ctrl+M")), (KeyboardId.Lock, new ActionSpec.ActionsRing()))));
        using (r)
        {
            var ring = new List<bool>();
            r.RingTrigger += (_, e) => ring.Add(e.Pressed);
            r.Start();
            kb.Key(KeyboardId.Mute, true);
            kb.Key(KeyboardId.Lock, true);
            kb.Connect(false);
            Assert.Equal([(new ActionSpec.Keys("Ctrl+M"), true), (new ActionSpec.Keys("Ctrl+M"), false)], ex.Calls);
            Assert.Equal([true, false], ring);

            kb.Connect(true);
            kb.Key(KeyboardId.Mute, false); // a stale release after reconnect: already released
            Assert.Equal(2, ex.Calls.Count);
        }
    }

    [Fact]
    public async Task Start_diverts_but_never_pushes_fn_lock()
    {
        var (r, kb, _, _, _) = Make(Cfg(true, P(null, (KeyboardId.Mute, new ActionSpec.Keys("F1")))));
        using (r)
        {
            r.Start();
            await Wait.UntilAsync(() => kb.DivertCount == 1);
            await Task.Delay(50);
            Assert.True(kb.Started);
            Assert.Equal(new[] { KeyboardId.Mute }, kb.Diverts[0]);
            Assert.Equal(0, kb.FnCount); // the keyboard keeps its own Fn lock (an update restart must not flip it)
        }
    }

    [Fact]
    public async Task ApplyConfig_writes_fn_lock_only_on_change_and_diverts_only_when_set_changes()
    {
        var a = P(null, (KeyboardId.Mute, new ActionSpec.Keys("F1")));
        var (r, kb, fg, _, _) = Make(Cfg(false, a));
        using (r)
        {
            r.Start();
            await Wait.UntilAsync(() => kb.DivertCount == 1);

            r.ApplyConfig(Cfg(false, a)); // identical
            await Task.Delay(50);
            Assert.Equal(1, kb.DivertCount);
            Assert.Equal(0, kb.FnCount);

            r.ApplyConfig(Cfg(true, a)); // the user flipped Fn lock in Slender
            await Wait.UntilAsync(() => kb.FnCount == 1);
            Assert.True(kb.FnLocks[0]);
            Assert.Equal(1, kb.DivertCount);

            r.ApplyConfig(Cfg(true, a, P("excel", (KeyboardId.Lock, new ActionSpec.Keys("F2")))));
            await Task.Delay(50);
            Assert.Equal(1, kb.DivertCount); // Lock is only bound in excel; not in front yet
            fg.Switch("excel");
            await Wait.UntilAsync(() => kb.DivertCount == 2);
            Assert.Equal(new[] { KeyboardId.Lock, KeyboardId.Mute }, kb.Diverts[1]);
            Assert.Equal(1, kb.FnCount);
        }
    }

    [Fact]
    public async Task A_keyboard_read_that_differs_from_the_saved_fn_lock_is_raised_for_saving()
    {
        var (r, kb, _, _, _) = Make(Cfg(true));
        using (r)
        {
            var raised = new List<bool>();
            r.FnLockChangedOnKeyboard += (_, on) => { lock (raised) raised.Add(on); };
            r.Start();

            kb.ReadOnConnect(true); // matches the saved value: nothing to save
            kb.ReadOnConnect(false); // Fn+Esc turned it off
            Assert.Equal([false], raised);

            // The tray saves it; the new config matches what the keyboard has, so nothing is written back.
            r.ApplyConfig(Cfg(false));
            await Task.Delay(50);
            Assert.Equal(0, kb.FnCount);
        }
    }

    [Fact]
    public async Task Flipping_in_slender_after_a_keyboard_read_writes_the_new_value()
    {
        var (r, kb, _, _, _) = Make(Cfg(true));
        using (r)
        {
            r.Start();
            kb.ReadOnConnect(false);
            r.ApplyConfig(Cfg(false)); // the tray saved the keyboard's value
            r.ApplyConfig(Cfg(true));  // then the user turned it on in Slender
            await Wait.UntilAsync(() => kb.FnCount == 1);
            Assert.True(kb.FnLocks[0]);
        }
    }

    [Fact]
    public async Task Fn_lock_is_polled_to_catch_fn_esc()
    {
        var (r, kb, _, _, _) = Make(Cfg(false));
        using (r)
        {
            var raised = new List<bool>();
            r.FnLockChangedOnKeyboard += (_, on) => { lock (raised) raised.Add(on); };
            r.FnPollMs = 10;
            kb.FnNow = false;
            r.Start();
            await Wait.UntilAsync(() => Volatile.Read(ref kb.FnReads) >= 1);

            kb.FnNow = true; // Fn+Esc
            await Wait.UntilAsync(() => { lock (raised) return raised.Contains(true); });
            Assert.Equal(0, kb.FnCount);
        }
    }

    [Fact]
    public async Task No_poll_while_disconnected()
    {
        var (r, kb, _, _, _) = Make(Cfg(false));
        using (r)
        {
            r.FnPollMs = 5;
            kb.IsConnected = false;
            r.Start();
            await Task.Delay(80);
            Assert.Equal(0, Volatile.Read(ref kb.FnReads));
        }
    }

    [Fact]
    public void Press_uses_app_profile_then_falls_back_to_default()
    {
        var def = new ActionSpec.Keys("Ctrl+D");
        var xl = new ActionSpec.Keys("Ctrl+X");
        var (r, kb, fg, ex, _) = Make(Cfg(false,
            P(null, (KeyboardId.Mute, def), (KeyboardId.Lock, def)),
            P("excel", (KeyboardId.Mute, xl), (KeyboardId.Lock, new ActionSpec.None()))));
        using (r)
        {
            r.Start();
            fg.ProcessName = "excel";
            kb.Key(KeyboardId.Mute, true); kb.Key(KeyboardId.Mute, false);
            kb.Key(KeyboardId.Lock, true); kb.Key(KeyboardId.Lock, false); // explicit None in excel -> pass through, nothing runs
            fg.ProcessName = "notepad";
            kb.Key(KeyboardId.Mute, true); kb.Key(KeyboardId.Mute, false);
            kb.Key(KeyboardId.Lock, true); kb.Key(KeyboardId.Lock, false); // absent in notepad -> default's action
            Assert.Equal(new (ActionSpec, bool)[]
            {
                (xl, true), (xl, false), (def, true), (def, false), (def, true), (def, false),
            }, ex.Calls);
        }
    }

    [Fact]
    public void Release_replays_press_time_action_across_app_switch()
    {
        var def = new ActionSpec.Keys("Ctrl+D");
        var xl = new ActionSpec.Keys("Ctrl+X");
        var (r, kb, fg, ex, _) = Make(Cfg(false, P(null, (KeyboardId.Mute, def)), P("excel", (KeyboardId.Mute, xl))));
        using (r)
        {
            r.Start();
            fg.ProcessName = "excel";
            kb.Key(KeyboardId.Mute, true);
            fg.ProcessName = "notepad";
            kb.Key(KeyboardId.Mute, false);
            Assert.Equal(new (ActionSpec, bool)[] { (xl, true), (xl, false) }, ex.Calls);

            kb.Key(KeyboardId.Mute, false); // stray release: nothing
            Assert.Equal(2, ex.Calls.Count);
        }
    }

    [Fact]
    public void Unbound_key_and_none_do_nothing_and_never_pair_a_release()
    {
        var (r, kb, _, ex, _) = Make(Cfg(false, P(null, (KeyboardId.Mute, new ActionSpec.None()))));
        using (r)
        {
            r.Start();
            kb.Key(KeyboardId.Mute, true); kb.Key(KeyboardId.Mute, false);
            kb.Key(KeyboardId.Lock, true); kb.Key(KeyboardId.Lock, false);
            Assert.Empty(ex.Calls);
        }
    }

    [Fact]
    public async Task Failures_are_logged_and_never_thrown()
    {
        var (r, kb, _, ex, log) = Make(Cfg(false, P(null, (KeyboardId.Mute, new ActionSpec.Keys("F1")))));
        using (r)
        {
            kb.ThrowOnDivert = true;
            ex.Throw = true;
            r.Start();
            await Wait.UntilAsync(() => { lock (log.Errors) return log.Errors.Count >= 1; });
            kb.Key(KeyboardId.Mute, true);
            kb.Key(KeyboardId.Mute, false);
            lock (log.Errors) Assert.Contains("key handling failed", log.Errors);
        }
    }

    [Fact]
    public void Dispose_stops_listening()
    {
        var (r, kb, _, ex, _) = Make(Cfg(false, P(null, (KeyboardId.Mute, new ActionSpec.Keys("F1")))));
        r.Start();
        r.Dispose();
        kb.Key(KeyboardId.Mute, true);
        Assert.Empty(ex.Calls);
        r.Dispose(); // idempotent
    }

    [Fact]
    public void Keyboard_divert_change_waits_while_a_key_is_held()
    {
        var cfg = Cfg(false,
            P(null, (KeyboardId.Mute, new ActionSpec.Keys("Ctrl+M"))),
            P("teams", (KeyboardId.Mute, new ActionSpec.None())));
        var (r, kb, fg, _, _) = Make(cfg);
        using var router = r;
        router.ForegroundDebounceMs = 20;
        router.Start();
        Assert.True(SpinWait.SpinUntil(() => kb.DivertCount == 1, 5000));

        kb.Key(KeyboardId.Mute, true);
        fg.Switch("teams"); // would un-divert the key and lose its release
        Thread.Sleep(200);
        Assert.Equal(1, kb.DivertCount);

        kb.Key(KeyboardId.Mute, false);
        Assert.True(SpinWait.SpinUntil(() => kb.DivertCount == 2, 5000));
        Assert.Empty(kb.Diverts[1]);
    }
}
