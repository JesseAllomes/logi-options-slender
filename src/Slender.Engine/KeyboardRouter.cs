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
    private readonly object _gate = new();
    private readonly Dictionary<ushort, ActionSpec> _active = new(); // action used at press, replayed on release
    private readonly CancellationTokenSource _cts = new();
    private volatile SlenderConfig _config;
    private IReadOnlyList<ushort> _diverted = [];
    private CancellationTokenSource? _foregroundDebounce;
    private bool? _fnLock;
    private Task _chain = Task.CompletedTask;
    private bool _started, _disposed;

    public KeyboardRouter(ILogiKeyboard keyboard, IForegroundApp foreground, IActionExecutor executor,
        SlenderConfig config, ILog? log = null)
        : this(keyboard, foreground, executor, config, log, null) { }

    internal KeyboardRouter(ILogiKeyboard keyboard, IForegroundApp foreground, IActionExecutor executor,
        SlenderConfig config, ILog? log, IWorkQueue? queue)
    {
        _keyboard = keyboard;
        _foreground = foreground;
        _executor = executor;
        _config = config;
        _log = log ?? NullLog.Instance;
        _queue = queue ?? new BoundedWorker("Slender.Keyboard", 256, sta: false, _log);
    }

    public SlenderConfig Config => _config;

    /// <summary>Keys that must be diverted: non-None in any profile's KeyBindings, sorted and de-duplicated.</summary>
    public static IReadOnlyList<ushort> ComputeDivertSet(SlenderConfig config) =>
        config.Profiles
            .SelectMany(p => p.KeyBindings)
            .Where(kv => kv.Value is not null and not ActionSpec.None)
            .Select(kv => kv.Key)
            .Distinct()
            .Order()
            .ToList();

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
            .Order()
            .ToList();
    }

    /// <summary>Quiet period after a foreground change before the divert set is recomputed; rapid switches collapse into one. Tests set 0.</summary>
    internal int ForegroundDebounceMs { get; set; } = 150;

    private void OnForegroundChanged(object? sender, string? process)
    {
        try
        {
            if (ForegroundDebounceMs <= 0) { Apply(force: false); return; }
            var cts = new CancellationTokenSource();
            lock (_gate)
            {
                if (_disposed) return;
                _foregroundDebounce?.Cancel();
                _foregroundDebounce = cts;
            }
            _ = Task.Delay(ForegroundDebounceMs, cts.Token).ContinueWith(t =>
            {
                try { if (!t.IsCanceled && !_disposed) Apply(force: false); }
                catch (Exception ex) { try { _log.Error("foreground divert failed", ex); } catch { } }
            }, TaskScheduler.Default);
        }
        catch (Exception ex) { try { _log.Error("foreground change handler failed", ex); } catch { } }
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
            ActionSpec action;
            if (e.Pressed)
            {
                action = Resolve(e.ControlId);
                lock (_gate) _active[e.ControlId] = action;
                if (action is ActionSpec.ActionsRing) { RaiseRing(true); return; }
                if (action is ActionSpec.None) return;
            }
            else
            {
                lock (_gate)
                {
                    if (!_active.Remove(e.ControlId, out action!)) return;
                }
                if (action is ActionSpec.ActionsRing) { RaiseRing(false); return; }
                if (action is ActionSpec.None) return;
            }
            _executor.Execute(action, e.Pressed);
        }
        catch (Exception ex) { _log.Error("key handling failed", ex); }
    }

    /// <summary>A key bound to the Actions Ring went down or up (raised on the router's worker thread).</summary>
    public event EventHandler<RingTriggerEventArgs>? RingTrigger;

    private void RaiseRing(bool pressed)
    {
        try { RingTrigger?.Invoke(this, new RingTriggerEventArgs(pressed)); }
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
        _cts.Dispose();
    }
}
