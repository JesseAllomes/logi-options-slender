using Slender.Hidpp;

namespace Slender.Engine;

/// <summary>
/// Routes ERGO K860 key presses to actions. Diverts exactly the keys bound (non-None) in some profile, resolves
/// the foreground profile with fallback to the default profile on every press, pairs each release with the action chosen
/// at press time (an app switch while a key is held cannot strand a press). Fn lock belongs to the keyboard (Fn+Esc): it is
/// written only when the user changes <see cref="KeyboardSettings.FnLock"/>, never at start or reconnect, and what the keyboard
/// reports is raised as <see cref="FnLockChangedOnKeyboard"/> so the tray saves it. Per-channel Fn values follow the same rule:
/// only a channel the user changed is written.
/// Nothing throws across the HID thread. The router does not dispose the collaborators it is given.
/// </summary>
public sealed class KeyboardRouter : IDisposable
{
    private readonly ILogiKeyboard _keyboard;
    private readonly IForegroundApp _foreground;
    private readonly IActionExecutor _executor;
    private readonly ILog _log;
    private readonly IWorkQueue _queue;
    private readonly Func<long> _clock;
    private readonly TapHoldTracker _tapHold;
    private readonly object _gate = new();
    private readonly Dictionary<ushort, ActionSpec> _active = new(); // action used at press, replayed on release
    private readonly CancellationTokenSource _cts = new();
    private volatile SlenderConfig _config;
    private IReadOnlyList<ushort> _diverted = [];
    private CancellationTokenSource? _foregroundDebounce;
    private bool? _fnLock;            // what the keyboard has, as far as Slender knows (config until the first read)
    private volatile bool _fnWritePending; // Slender's own Fn write is queued: a read in between is stale
    private byte? _disabledKeys;
    private bool?[]? _channels;     // per-channel Fn as last saved or reported (the keyboard keeps its own)
    private Task _chain = Task.CompletedTask;
    private bool _started, _disposed;

    public KeyboardRouter(ILogiKeyboard keyboard, IForegroundApp foreground, IActionExecutor executor,
        SlenderConfig config, ILog? log = null)
        : this(keyboard, foreground, executor, config, log, null) { }

    internal KeyboardRouter(ILogiKeyboard keyboard, IForegroundApp foreground, IActionExecutor executor,
        SlenderConfig config, ILog? log, IWorkQueue? queue, Func<long>? clock = null, IHoldTimer? holdTimer = null)
    {
        _clock = clock ?? (() => Environment.TickCount64);
        _keyboard = keyboard;
        _foreground = foreground;
        _executor = executor;
        _config = config;
        _log = log ?? NullLog.Instance;
        _queue = queue ?? new BoundedWorker("Slender.Keyboard", 256, sta: false, _log);
        _tapHold = new TapHoldTracker(holdTimer ?? new ThreadingHoldTimer(), _queue, _log);
    }

    public SlenderConfig Config => _config;

    /// <summary>Keys that must be diverted: non-None in any profile's KeyBindings, sorted and de-duplicated.</summary>
    public static IReadOnlyList<ushort> ComputeDivertSet(SlenderConfig config) =>
        config.Profiles
            .SelectMany(p => p.KeyBindings)
            .Where(kv => kv.Value is not null and not ActionSpec.None)
            .Select(kv => kv.Key)
            .Concat(IdentifyKeys(config))
            .Distinct()
            .Order()
            .ToList();

    /// <summary>The spare keys, diverted (temporary divert only) while Settings' Identify panel is open.</summary>
    private static IEnumerable<ushort> IdentifyKeys(SlenderConfig config) =>
        config.Keyboard.IdentifySpareKeys ? SpareKeys.All.Select(k => k.Cid) : [];

    /// <summary>
    /// Keys to divert while <paramref name="process"/> is in front: the app's own entry wins (an explicit None leaves the key
    /// native), else the default profile's binding; diverted only when the effective binding is not None.
    /// </summary>
    public static IReadOnlyList<ushort> ComputeDivertSet(SlenderConfig config, string? process)
    {
        var profile = ProfileResolver.Resolve(config, process);
        var def = ProfileResolver.Default(config);
        return config.Profiles
            .SelectMany(p => p.KeyBindings.Keys)
            .Distinct()
            .Where(key =>
            {
                var effective = profile.KeyBindings.TryGetValue(key, out var own) && own is not null ? own : Bound(def, key);
                return effective is not null and not ActionSpec.None;
            })
            .Concat(IdentifyKeys(config))
            .Distinct()
            .Order()
            .ToList();
    }

    /// <summary>Quiet period after a foreground change before the divert set is recomputed; rapid switches collapse into one. Tests set 0.</summary>
    internal int ForegroundDebounceMs { get; set; } = 150;

    /// <summary>Longest a divert change waits for a held key to be released (a lost release must not pin the old set forever).</summary>
    internal const long HeldDivertWaitMs = 10_000;

    private void OnForegroundChanged(object? sender, string? process) => ScheduleForegroundApply(_clock());

    private void ScheduleForegroundApply(long since)
    {
        try
        {
            if (ForegroundDebounceMs <= 0) { ApplyUnlessHeld(since); return; }
            var cts = new CancellationTokenSource();
            lock (_gate)
            {
                if (_disposed) return;
                _foregroundDebounce?.Cancel();
                _foregroundDebounce = cts;
            }
            _ = Task.Delay(ForegroundDebounceMs, cts.Token).ContinueWith(t =>
            {
                try { if (!t.IsCanceled && !_disposed) ApplyUnlessHeld(since); }
                catch (Exception ex) { try { _log.Error("foreground divert failed", ex); } catch { } }
            }, TaskScheduler.Default);
        }
        catch (Exception ex) { try { _log.Error("foreground change handler failed", ex); } catch { } }
    }

    /// <summary>
    /// Changing the divert set while a diverted key is held would lose its release (the device stops reporting it),
    /// so the change waits until nothing is held, up to HeldDivertWaitMs.
    /// </summary>
    private void ApplyUnlessHeld(long since)
    {
        bool held;
        lock (_gate) held = _active.Count > 0;
        if (held && _clock() - since < HeldDivertWaitMs && ForegroundDebounceMs > 0)
        {
            ScheduleForegroundApply(since);
            return;
        }
        Apply(force: false);
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true;
        }
        _keyboard.KeyChanged += OnKey;
        _keyboard.FnLockRead += OnFnLockRead;
        _keyboard.ConnectionChanged += OnConnection;
        _foreground.Changed += OnForegroundChanged;
        try { _ = ObserveAsync(_keyboard.StartAsync(_cts.Token), "keyboard start"); }
        catch (Exception ex) { _log.Error("keyboard start failed", ex); }
        Apply(force: true);
        _ = PollFnLockAsync();
    }

    /// <summary>Swaps the config; re-diverts if the bound key set changed and re-applies Fn lock if it changed.</summary>
    public void ApplyConfig(SlenderConfig config)
    {
        _config = config;
        bool started;
        lock (_gate) started = _started;
        if (started) Apply(force: false);
    }

    private void Apply(bool force)
    {
        var set = ComputeDivertSet(_config, _foreground.ProcessName);
        var fn = _config.Keyboard.FnLock;
        var disabled = _config.Keyboard.DisabledKeys;
        var channels = _config.Keyboard.FnLockChannels.ToArray();
        lock (_gate)
        {
            if (_disposed) return;
            // Serialised so a slow earlier call cannot overwrite a newer one.
            if (force || !_diverted.SequenceEqual(set))
            {
                _diverted = set;
                _chain = _chain.ContinueWith(_ => Safe(() => _keyboard.SetDivertedKeysAsync(set, _cts.Token), "set diverted keys"),
                    TaskScheduler.Default).Unwrap();
            }
            if (force)
            {
                _fnLock ??= fn; // start: never pushed; the keyboard keeps its own, and its first read brings it into the config
            }
            else if (_fnLock != fn)
            {
                _fnLock = fn; // the user changed it in Slender: write it once
                _fnWritePending = true;
                _chain = _chain.ContinueWith(_ => Safe(() => _keyboard.SetFnLockAsync(fn, _cts.Token), "set Fn lock"),
                    TaskScheduler.Default).Unwrap()
                    .ContinueWith(_ => { _fnWritePending = false; }, TaskScheduler.Default);
            }
            // Start never writes them (an update or sign-in must not undo Fn+Esc); a change writes only the channels that changed.
            var changed = new bool?[channels.Length];
            bool any = false;
            for (int i = 0; i < channels.Length && _channels is not null; i++)
            {
                if (channels[i] is { } want && _channels[i] != want) { changed[i] = want; any = true; }
            }
            _channels = channels;
            if (any)
            {
                _chain = _chain.ContinueWith(_ => Safe(() => _keyboard.SetChannelFnLockAsync(changed, _cts.Token), "set Fn lock per channel"),
                    TaskScheduler.Default).Unwrap();
            }
            if (force || _disabledKeys != disabled)
            {
                _disabledKeys = disabled;
                _chain = _chain.ContinueWith(_ => Safe(async () =>
                {
                    var back = await _keyboard.SetDisabledKeysAsync((DisabledKeys)disabled, _cts.Token).ConfigureAwait(false);
                    if (back is { } b && (byte)b != disabled)
                        _log.Info($"disabled keys read back as 0x{(byte)b:X2}, asked for 0x{disabled:X2}");
                }, "set disabled keys"), TaskScheduler.Default).Unwrap();
            }
        }
    }

    private Task Safe(Func<Task> call, string what)
    {
        try { return ObserveAsync(call(), what); }
        catch (Exception ex) { _log.Error(what + " failed", ex); return Task.CompletedTask; }
    }

    private async Task ObserveAsync(Task task, string what)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.Error(what + " failed", ex); }
    }

    // ---- keys (HID thread: O(1), never throws) ----

    private void OnKey(object? sender, ButtonEvent e)
    {
        try { _queue.Post(() => Handle(e)); }
        catch (Exception ex) { try { _log.Error("key dispatch failed", ex); } catch { } }
    }

    private void Handle(ButtonEvent e)
    {
        try
        {
            if (SpareKeys.IsSpare(e.ControlId) && _config.Keyboard.IdentifySpareKeys)
            {
                if (e.Pressed) SpareKeyIdentified?.Invoke(this, e.ControlId); // Identify: report only, run nothing
                return;
            }
            ActionSpec action;
            if (e.Pressed)
            {
                action = Resolve(e.ControlId);
                lock (_gate) _active[e.ControlId] = action;
                if (action is ActionSpec.TapHold th) { _tapHold.Press(e.ControlId, th, StartHold); return; }
                if (action is ActionSpec.ActionsRing) { RaiseRing(true); return; }
                if (action is ActionSpec.None) return;
            }
            else
            {
                lock (_gate)
                {
                    if (!_active.Remove(e.ControlId, out action!)) return;
                }
                if (action is ActionSpec.TapHold) { EndTapHold(e.ControlId); return; }
                if (action is ActionSpec.ActionsRing) { RaiseRing(false); return; }
                if (action is ActionSpec.None) return;
            }
            RunAction(action, e.Pressed);
        }
        catch (Exception ex) { _log.Error("key handling failed", ex); }
    }

    private void RunAction(ActionSpec action, bool pressed) => _executor.Execute(action, pressed);

    /// <summary>HID thread: a disconnected keyboard never reports the release of a held key, so its action is released now.</summary>
    private void OnConnection(object? sender, bool connected)
    {
        if (connected) return;
        try { _queue.Post(ReleaseAllHeld); }
        catch (Exception ex) { try { _log.Error("keyboard disconnect dispatch failed", ex); } catch { } }
    }

    /// <summary>Worker: ends every held key's action (queued after any key events already in flight), like the mouse does.</summary>
    private void ReleaseAllHeld()
    {
        KeyValuePair<ushort, ActionSpec>[] held;
        lock (_gate) { held = [.. _active]; _active.Clear(); }
        foreach (var (key, action) in held)
        {
            try
            {
                if (action is ActionSpec.TapHold)
                {
                    // A started hold is released; a quick press that never reached its hold runs nothing.
                    if (_tapHold.TryRelease(key, out var th, out var started) && started && th.Hold is not ActionSpec.None)
                    {
                        if (th.Hold is ActionSpec.ActionsRing) RaiseRing(false);
                        else RunAction(th.Hold, false);
                    }
                }
                else if (action is ActionSpec.ActionsRing) RaiseRing(false);
                else if (action is not ActionSpec.None) RunAction(action, false);
            }
            catch (Exception ex) { _log.Error("held key release failed", ex); }
        }
    }

    /// <summary>Hold threshold reached while the key is still down: the hold action starts as a press (the keyboard has no haptics).</summary>
    private void StartHold(ActionSpec.TapHold th)
    {
        if (th.Hold is ActionSpec.None) return;
        if (th.Hold is ActionSpec.ActionsRing) RaiseRing(true, holdOnly: true);
        else RunAction(th.Hold, true);
    }

    /// <summary>Physical release: ends a started hold, otherwise it was a quick press and the tap runs once.</summary>
    private void EndTapHold(ushort key)
    {
        if (!_tapHold.TryRelease(key, out var th, out var held)) return;
        var action = held ? th.Hold : th.Tap;
        if (action is ActionSpec.None) return;
        if (action is ActionSpec.ActionsRing)
        {
            if (!held) RaiseRing(true); // a quick press: the ring opens and stays open
            RaiseRing(false);
            return;
        }
        if (!held) RunAction(action, true);
        RunAction(action, false);
    }

    /// <summary>A spare key went down while Identify is on (raised on the router's worker thread).</summary>
    public event EventHandler<ushort>? SpareKeyIdentified;

    /// <summary>
    /// The keyboard's Fn lock differs from the saved one, or from the saved value of the channel it is on (Fn+Esc, or a saved value
    /// from an older version): the tray saves it, with <see cref="KeyboardChannel"/>. Raised on a background thread.
    /// </summary>
    public event EventHandler<bool>? FnLockChangedOnKeyboard;

    /// <summary>How often the keyboard's Fn lock is read to catch Fn+Esc. One small read; nothing is written.</summary>
    internal int FnPollMs { get; set; } = 30_000;

    /// <summary>The Easy-Switch channel (0-2) the keyboard is on, when known.</summary>
    public int? KeyboardChannel => _keyboard.CurrentChannel;

    private void OnFnLockRead(object? sender, bool on)
    {
        try
        {
            if (_fnWritePending) return; // Slender's own write is on its way and wins
            var channel = _keyboard.CurrentChannel;
            lock (_gate)
            {
                if (_disposed) return;
                _fnLock = on;
                // The keyboard already has it: saving it for this channel must not write it back.
                if (channel is int c && _channels is { } known && c < known.Length && known[c] is not null) known[c] = on;
            }
            var k = _config.Keyboard;
            bool channelDiffers = channel is int ch && ch < k.FnLockChannels.Count && k.FnLockChannels[ch] is { } saved && saved != on;
            if (k.FnLock != on || channelDiffers) FnLockChangedOnKeyboard?.Invoke(this, on);
        }
        catch (Exception ex) { try { _log.Error("Fn lock read handler failed", ex); } catch { } }
    }

    private async Task PollFnLockAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(FnPollMs, _cts.Token).ConfigureAwait(false);
                if (!_keyboard.IsConnected || _fnWritePending) continue;
                try { await _keyboard.ReadFnLockAsync(_cts.Token).ConfigureAwait(false); } // raises FnLockRead
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _log.Error("Fn lock read failed", ex); }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary>A key bound to the Actions Ring went down or up (raised on the router's worker thread).</summary>
    public event EventHandler<RingTriggerEventArgs>? RingTrigger;

    private void RaiseRing(bool pressed, bool holdOnly = false)
    {
        try { RingTrigger?.Invoke(this, new RingTriggerEventArgs(pressed, holdOnly)); }
        catch (Exception ex) { _log.Error("ring trigger handler failed", ex); }
    }

    private ActionSpec Resolve(ushort key)
    {
        var cfg = _config;
        var profile = ProfileResolver.Resolve(cfg, _foreground.ProcessName);
        // An explicit None in a per-app profile means "native / pass through": it wins over the default profile's binding.
        if (profile.KeyBindings.TryGetValue(key, out var own) && own is not null) return own;
        if (Bound(ProfileResolver.Default(cfg), key) is { } d) return d;
        return new ActionSpec.None();
    }

    private static ActionSpec? Bound(Profile p, ushort key) =>
        p.KeyBindings.TryGetValue(key, out var a) && a is not null and not ActionSpec.None ? a : null;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _keyboard.KeyChanged -= OnKey;
        _keyboard.FnLockRead -= OnFnLockRead;
        _keyboard.ConnectionChanged -= OnConnection;
        _foreground.Changed -= OnForegroundChanged;
        lock (_gate) _foregroundDebounce?.Cancel();
        _cts.Cancel();
        _queue.Dispose();
        _tapHold.Dispose();
        _cts.Dispose();
    }
}
