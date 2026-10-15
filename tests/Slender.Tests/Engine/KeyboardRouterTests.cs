using Slender.Engine;
using Slender.Hidpp;
using Slender.Tests.Hidpp;

namespace Slender.Tests.Engine;

internal sealed class FakeKeyboard : ILogiKeyboard
{
    public string Name => "fake-kb";
    public bool IsConnected { get; set; } = true;
    public event EventHandler<bool>? ConnectionChanged { add { } remove { } }
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
    public Task<BatteryStatus?> GetBatteryAsync(CancellationToken ct) => Task.FromResult<BatteryStatus?>(null);
    public void Dispose() { }
    public void Key(ushort id, bool pressed) => KeyChanged?.Invoke(this, new ButtonEvent(id, pressed));
    public int DivertCount { get { lock (Diverts) return Diverts.Count; } }
    public int FnCount { get { lock (FnLocks) return FnLocks.Count; } }
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
    public async Task Start_diverts_and_applies_fn_lock()
    {
        var (r, kb, _, _, _) = Make(Cfg(true, P(null, (KeyboardId.Mute, new ActionSpec.Keys("F1")))));
        using (r)
        {
            r.Start();
            await Wait.UntilAsync(() => kb.DivertCount == 1 && kb.FnCount == 1);
            Assert.True(kb.Started);
            Assert.Equal(new[] { KeyboardId.Mute }, kb.Diverts[0]);
            Assert.True(kb.FnLocks[0]);
        }
    }

    [Fact]
    public async Task ApplyConfig_reapplies_fn_lock_only_on_change_and_diverts_only_when_set_changes()
    {
        var a = P(null, (KeyboardId.Mute, new ActionSpec.Keys("F1")));
        var (r, kb, _, _, _) = Make(Cfg(false, a));
        using (r)
        {
            r.Start();
            await Wait.UntilAsync(() => kb.DivertCount == 1 && kb.FnCount == 1);

            r.ApplyConfig(Cfg(false, a)); // identical
            await Task.Delay(50);
            Assert.Equal(1, kb.DivertCount);
            Assert.Equal(1, kb.FnCount);

            r.ApplyConfig(Cfg(true, a)); // fn lock only
            await Wait.UntilAsync(() => kb.FnCount == 2);
            Assert.True(kb.FnLocks[1]);
            Assert.Equal(1, kb.DivertCount);

            r.ApplyConfig(Cfg(true, a, P("excel", (KeyboardId.Lock, new ActionSpec.Keys("F2")))));
            await Wait.UntilAsync(() => kb.DivertCount == 2);
            Assert.Equal(new[] { KeyboardId.Lock, KeyboardId.Mute }, kb.Diverts[1]);
            Assert.Equal(2, kb.FnCount);
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
}
