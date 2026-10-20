using Slender.Engine;
using Slender.Hidpp;
using Slender.Tests.Hidpp;

namespace Slender.Tests.Engine;

public class KeyboardExtrasRouterTests
{
    private static SlenderConfig Cfg(KeyboardSettings kb, params Profile[] profiles) =>
        new(SlenderConfig.CurrentVersion, profiles, new ExcelSettings(), new HapticSettings(), false) { Keyboard = kb };

    private static Profile P(params (ushort Key, ActionSpec Action)[] keys) =>
        new(null, new Dictionary<ushort, ActionSpec>()) { KeyBindings = keys.ToDictionary(k => k.Key, k => k.Action) };

    private static (KeyboardRouter R, FakeKeyboard Kb, FakeExecutor Ex) Make(SlenderConfig cfg)
    {
        var kb = new FakeKeyboard();
        var ex = new FakeExecutor();
        return (new KeyboardRouter(kb, new FakeForeground(), ex, cfg, new ListLog(), new InlineQueue()), kb, ex);
    }

    [Fact]
    public void Spare_keys_are_diverted_only_when_bound()
    {
        Assert.Empty(KeyboardRouter.ComputeDivertSet(Cfg(new KeyboardSettings(), P())));
        var cfg = Cfg(new KeyboardSettings(), P((KeyboardId.Spare0101, new ActionSpec.Keys("Ctrl+K")), (KeyboardId.Spare00EB, new ActionSpec.None())));
        Assert.Equal(new[] { KeyboardId.Spare0101 }, KeyboardRouter.ComputeDivertSet(cfg));
        Assert.Equal(new[] { KeyboardId.Spare0101 }, KeyboardRouter.ComputeDivertSet(cfg, null));
    }

    [Fact]
    public async Task Bound_spare_key_runs_its_action_on_press_and_release()
    {
        var (r, kb, ex) = Make(Cfg(new KeyboardSettings(), P((KeyboardId.Spare00EC, new ActionSpec.Keys("F13")))));
        using (r)
        {
            r.Start();
            await Wait.UntilAsync(() => kb.DivertCount >= 1);
            Assert.Equal(new[] { KeyboardId.Spare00EC }, kb.Diverts[0]);
            kb.Key(KeyboardId.Spare00EC, true);
            kb.Key(KeyboardId.Spare00EC, false);
            Assert.Equal(2, ex.Calls.Count);
        }
    }

    [Fact]
    public async Task Identify_diverts_all_three_reports_the_press_and_runs_nothing()
    {
        var cfg = Cfg(new KeyboardSettings(IdentifySpareKeys: true), P((KeyboardId.Spare00EC, new ActionSpec.Keys("F13"))));
        Assert.Equal(new ushort[] { KeyboardId.Spare00EB, KeyboardId.Spare00EC, KeyboardId.Spare0101 }, KeyboardRouter.ComputeDivertSet(cfg));
        var (r, kb, ex) = Make(cfg);
        using (r)
        {
            var seen = new List<ushort>();
            r.SpareKeyIdentified += (_, cid) => seen.Add(cid);
            r.Start();
            await Wait.UntilAsync(() => kb.DivertCount >= 1);
            kb.Key(KeyboardId.Spare00EC, true);
            kb.Key(KeyboardId.Spare00EC, false);
            kb.Key(KeyboardId.Spare0101, true);
            Assert.Equal(new[] { KeyboardId.Spare00EC, KeyboardId.Spare0101 }, seen);
            Assert.Empty(ex.Calls);

            r.ApplyConfig(Cfg(new KeyboardSettings(), P())); // Identify closed: spare keys go back to native
            await Wait.UntilAsync(() => kb.DivertCount >= 2);
            Assert.Empty(kb.Diverts[1]);
            kb.Key(KeyboardId.Spare0101, true);
            Assert.Equal(2, seen.Count);
        }
    }

    [Fact]
    public async Task Disabled_keys_are_applied_on_start_and_channel_fn_only_when_a_channel_changes()
    {
        var first = new KeyboardSettings(DisabledKeys: (byte)(DisabledKeys.CapsLock | DisabledKeys.Windows), FnLockChannel2: true);
        var (r, kb, _) = Make(Cfg(first));
        using (r)
        {
            r.Start();
            await Wait.UntilAsync(() => kb.Disabled.Count >= 1);
            Assert.Equal(DisabledKeys.CapsLock | DisabledKeys.Windows, kb.Disabled[0]);
            Assert.Empty(kb.Channels); // start (update, sign-in) never undoes Fn+Esc

            r.ApplyConfig(Cfg(first with { ShowLockKeyChanges = false })); // unrelated change: nothing re-sent
            r.ApplyConfig(Cfg(first with { DisabledKeys = 0, FnLockChannel1 = false }));
            await Wait.UntilAsync(() => kb.Disabled.Count >= 2 && kb.Channels.Count >= 1);
            Assert.Equal(DisabledKeys.None, kb.Disabled[1]);
            Assert.Equal(new bool?[] { false, null, null }, kb.Channels[0]); // only the channel that changed
            Assert.Equal(2, kb.Disabled.Count);
            Assert.Single(kb.Channels);
        }
    }

    [Fact]
    public async Task Fn_esc_on_a_channel_with_a_saved_value_is_reported_and_not_written_back()
    {
        var cfg = new KeyboardSettings(FnLock: false, FnLockChannel1: true, FnLockChannel2: true);
        var (r, kb, _) = Make(Cfg(cfg));
        using (r)
        {
            var raised = new List<bool>();
            r.FnLockChangedOnKeyboard += (_, on) => { lock (raised) raised.Add(on); };
            kb.Channel = 0;
            r.Start();
            kb.ReadOnConnect(false); // matches FnLock but not channel 1's saved value
            lock (raised) Assert.Equal([false], raised);
            Assert.Equal(0, r.KeyboardChannel);

            r.ApplyConfig(Cfg(cfg with { FnLockChannel1 = false })); // what the tray saves for it
            await Task.Delay(50);
            Assert.Empty(kb.Channels);
        }
    }
}
