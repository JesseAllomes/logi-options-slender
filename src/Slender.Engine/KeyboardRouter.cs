using Slender.Hidpp;

namespace Slender.Engine;

/// <summary>
/// Routes ERGO K860 key presses to actions. Diverts exactly the keys bound (non-None) in some profile, resolves
/// the foreground profile with fallback to the default profile on every press, pairs each release with the action chosen
/// at press time (an app switch while a key is held cannot strand a press), and applies <see cref="KeyboardSettings.FnLock"/>.
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
    private bool? _fnLock;
    private byte? _disabledKeys;
    private string? _channelFn;
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
        _foreground.Changed += OnForegroundChanged;
        try { _ = ObserveAsync(_keyboard.StartAsync(_cts.Token), "keyboard start"); }
        catch (Exception ex) { _log.Error("keyboard start failed", ex); }
        Apply(force: true);
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
        var channels = _config.Keyboard.FnLockChannels;
        var channelKey = string.Join(',', channels.Select(c => c is null ? "-" : c.Value ? "1" : "0"));
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
            if (force || _fnLock != fn)
            {
                _fnLock = fn;
                _chain = _chain.ContinueWith(_ => Safe(() => _keyboard.SetFnLockAsync(fn, _cts.Token), "set Fn lock"),
                    TaskScheduler.Default).Unwrap();
            }
            if (force || _channelFn != channelKey)
            {
                _channelFn = channelKey;
                _chain = _chain.ContinueWith(_ => Safe(() => _keyboard.SetChannelFnLockAsync(channels, _cts.Token), "set Fn lock per channel"),
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

    private void RunAction(ActionSpec action, bool pressed)
    {
        if (action is ActionSpec.ToggleThumbWheelMode)
        {
            if (pressed) ThumbWheelToggleRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        _executor.Execute(action, pressed);
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

    /// <summary>A key bound to "Switch thumb wheel mode" went down; the engine owns the mode (raised on the router's worker thread).</summary>
    public event EventHandler? ThumbWheelToggleRequested;

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
        _foreground.Changed -= OnForegroundChanged;
        lock (_gate) _foregroundDebounce?.Cancel();
        _cts.Cancel();
        _queue.Dispose();
        _tapHold.Dispose();
        _cts.Dispose();
    }
}
