using Slender.Engine.Ring;
using Slender.Hidpp;

namespace Slender.Engine;

/// <summary>Connection and battery state for the tray.</summary>
public sealed record EngineStatus(bool Connected, int? BatteryPercent, bool Charging);

/// <summary>
/// Wires mouse buttons, the horizontal-wheel hook, foreground tracking and actions together. Nothing here
/// throws across a hook/HID thread: handlers catch and log. Heavy work runs on a bounded worker.
/// The engine does not dispose the collaborators it is given.
/// </summary>
public sealed class SlenderEngine : IDisposable
{
    private readonly ILogiMouse _mouse;
    private readonly IInputHook _hook;
    private readonly IForegroundApp _foreground;
    private readonly IExcelBridge _excel;
    private readonly IActionExecutor _executor;
    private readonly ILog _log;
    private readonly IWorkQueue _queue;
    private readonly Func<long> _clock;
    private readonly ColumnScrollAccumulator _accumulator = new();
    private readonly GestureTracker _gesture = new();
    private readonly object _gate = new();
    private readonly Dictionary<ushort, ActionSpec> _active = new(); // HID++ path: action used at press, replayed on release
    private readonly Dictionary<ushort, ActionSpec> _hookActive = new(); // hook path: same, for swallowed Middle/Back/Forward
    private readonly CancellationTokenSource _cts = new();
    private volatile SlenderConfig _config;
    private IReadOnlyList<ushort> _diverted = [];
    private CancellationTokenSource? _foregroundDebounce;
    private Task _divertChain = Task.CompletedTask;
    private volatile bool _gesturePlainClick; // this press had no swipes configured: the legacy press/release path
    private bool _started, _disposed;

    public SlenderEngine(ILogiMouse mouse, IInputHook hook, IForegroundApp foreground, IExcelBridge excel,
        IActionExecutor executor, SlenderConfig config, ILog? log = null)
        : this(mouse, hook, foreground, excel, executor, config, log, null, null) { }

    internal SlenderEngine(ILogiMouse mouse, IInputHook hook, IForegroundApp foreground, IExcelBridge excel,
        IActionExecutor executor, SlenderConfig config, ILog? log, IWorkQueue? queue, Func<long>? clock)
    {
        _mouse = mouse;
        _hook = hook;
        _foreground = foreground;
        _excel = excel;
        _executor = executor;
        _config = config;
        _log = log ?? NullLog.Instance;
        _queue = queue ?? new BoundedWorker("Slender.Engine", 256, sta: false, _log);
        _clock = clock ?? (() => Environment.TickCount64);
    }

    /// <summary>Raised on press of an ActionsRing control (the tray opens the ring). Press and release are both on <see cref="RingTrigger"/>.</summary>
    public event EventHandler? ActionsRingRequested;

    /// <summary>The ring trigger control went down (Pressed) or up. Raised on the engine thread.</summary>
    public event EventHandler<RingTriggerEventArgs>? RingTrigger;
    public event EventHandler<EngineStatus>? StatusChanged;

    public SlenderConfig Config => _config;

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true;
        }
        _mouse.ButtonChanged += OnButton;
        _mouse.ConnectionChanged += OnConnection;
        _hook.HorizontalWheel += OnWheel;
        _hook.ButtonChanged += OnHookButton;
        _hook.MouseMoved += OnHookMove;
        _foreground.Changed += OnForegroundChanged;
        try { _hook.Start(); }
        catch (Exception ex) { _log.Error("mouse hook failed to start; thumb wheel will not scroll Excel", ex); }
        try { _ = ObserveAsync(_mouse.StartAsync(_cts.Token), "mouse start"); }
        catch (Exception ex) { _log.Error("mouse start failed", ex); }
        Rediverts(force: true);
        if (_mouse.IsConnected) PublishStatus(true);
    }

    /// <summary>Swaps the config and re-diverts if the bound control set changed.</summary>
    public void ApplyConfig(SlenderConfig config)
    {
        _config = config;
        bool started;
        lock (_gate) started = _started;
        if (started) Rediverts(force: false);
    }

    /// <summary>
    /// HID++-diverted controls over every profile (the union): only Gesture, Mode shift and Sense Panel, and only when bound
    /// (non-None) in some profile, or Gesture when a swipe is bound. Middle/Back/Forward are captured by the mouse hook and
    /// never diverted. The live set is per foreground app, see the overload with a process name.
    /// </summary>
    public static IReadOnlyList<ushort> ComputeDivertSet(SlenderConfig config)
    {
        var set = config.Profiles
            .SelectMany(p => p.Bindings)
            .Where(kv => kv.Value is not null and not ActionSpec.None && IsHidppControl(kv.Key))
            .Select(kv => kv.Key)
            .ToHashSet();
        if (config.Profiles.Any(p => p.GestureSwipes.Values.Any(a => a is not null and not ActionSpec.None))) set.Add(ControlId.Gesture);
        return [.. set.Order()];
    }

    /// <summary>
    /// The controls to divert while <paramref name="process"/> is in front. A control is diverted only when its effective
    /// binding is not None: the app's own entry wins (an explicit None leaves the control native, e.g. mode shift keeps toggling
    /// ratchet/free-spin), else the default profile's. The gesture button is also diverted when any swipe is bound for the effective profile.
    /// </summary>
    public static IReadOnlyList<ushort> ComputeDivertSet(SlenderConfig config, string? process)
    {
        var profile = ProfileResolver.Resolve(config, process);
        var def = ProfileResolver.Default(config);
        var set = new SortedSet<ushort>();
        foreach (var id in new[] { ControlId.Gesture, ControlId.ModeShift, ControlId.SensePanel })
        {
            var effective = profile.Bindings.TryGetValue(id, out var own) && own is not null ? own : Bound(def, id);
            if (effective is not null and not ActionSpec.None) set.Add(id);
        }
        foreach (var dir in Enum.GetValues<GestureDirection>())
        {
            var effective = profile.GestureSwipes.TryGetValue(dir, out var own) && own is not null ? own
                : def.GestureSwipes.TryGetValue(dir, out var d) ? d : null;
            if (effective is not null and not ActionSpec.None) { set.Add(ControlId.Gesture); break; }
        }
        return [.. set];
    }

    private static bool IsHidppControl(ushort id) => id is ControlId.Gesture or ControlId.ModeShift or ControlId.SensePanel;

    /// <summary>Quiet period after a foreground change before the divert set is recomputed; rapid switches collapse into one. Tests set 0.</summary>
    internal int ForegroundDebounceMs { get; set; } = 150;

    /// <summary>Longest a divert change waits for a held control to be released (a lost release must not pin the old set forever).</summary>
    internal const long HeldDivertWaitMs = 10_000;

    private void OnForegroundChanged(object? sender, string? process) => ScheduleForegroundRedivert(_clock());

    private void ScheduleForegroundRedivert(long since)
    {
        try
        {
            if (ForegroundDebounceMs <= 0) { RedivertUnlessHeld(since); return; }
            var cts = new CancellationTokenSource();
            lock (_gate)
            {
                if (_disposed) return;
                _foregroundDebounce?.Cancel();
                _foregroundDebounce = cts;
            }
            _ = Task.Delay(ForegroundDebounceMs, cts.Token).ContinueWith(t =>
            {
                try { if (!t.IsCanceled && !_disposed) RedivertUnlessHeld(since); }
                catch (Exception ex) { SafeLog("foreground divert failed", ex); }
            }, TaskScheduler.Default);
        }
        catch (Exception ex) { SafeLog("foreground change handler failed", ex); }
    }

    /// <summary>
    /// Changing the divert set while a diverted control is held would lose its release (the device stops reporting it),
    /// so the change waits until nothing is held, up to HeldDivertWaitMs.
    /// </summary>
    private void RedivertUnlessHeld(long since)
    {
        bool held;
        lock (_gate) held = _active.Count > 0 || _gesture.Active;
        if (held && _clock() - since < HeldDivertWaitMs && ForegroundDebounceMs > 0)
        {
            ScheduleForegroundRedivert(since);
            return;
        }
        Rediverts(force: false);
    }

    private void Rediverts(bool force)
    {
        var set = ComputeDivertSet(_config, _foreground.ProcessName);
        lock (_gate)
        {
            if (!force && _diverted.SequenceEqual(set)) return;
            _diverted = set;
            // Serialise diverts so a slow earlier call cannot overwrite a newer one.
            _divertChain = _divertChain.ContinueWith(_ => SafeDivert(set), TaskScheduler.Default).Unwrap();
        }
    }

    private Task SafeDivert(IReadOnlyList<ushort> set)
    {
        try { return ObserveAsync(_mouse.SetDivertedControlsAsync(set, _cts.Token), "set diverted controls"); }
        catch (Exception ex) { _log.Error("set diverted controls failed", ex); return Task.CompletedTask; }
    }

    private async Task ObserveAsync(Task task, string what)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.Error(what + " failed", ex); }
    }

    // ---- buttons (HID thread) ----

    private void OnButton(object? sender, ButtonEvent e)
    {
        if (!IsHidppControl(e.ControlId)) return; // Middle/Back/Forward come from the hook; ignore stale diverts
        if (e.ControlId == ControlId.Gesture && OnGestureButton(e)) return;
        try { _queue.Post(() => HandleButton(e)); }
        catch (Exception ex) { SafeLog("button dispatch failed", ex); }
    }

    private void HandleButton(ButtonEvent e)
    {
        try
        {
            ActionSpec action;
            if (e.Pressed)
            {
                action = ResolveAction(e.ControlId);
                lock (_gate) _active[e.ControlId] = action;
                if (action is ActionSpec.None) return;
                MaybeHaptic(action);
                if (action is ActionSpec.ActionsRing) { RaiseRing(true); return; }
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
        catch (Exception ex) { _log.Error("button handling failed", ex); }
    }

    private ActionSpec ResolveAction(ushort control)
    {
        var cfg = _config;
        var profile = ProfileResolver.Resolve(cfg, _foreground.ProcessName);
        // An explicit None in a per-app profile means "native / pass through": it wins over the default profile's binding.
        if (profile.Bindings.TryGetValue(control, out var own) && own is not null) return own;
        if (Bound(ProfileResolver.Default(cfg), control) is { } d) return d;
        return new ActionSpec.None();
    }

    private static ActionSpec? Bound(Profile p, ushort control) =>
        p.Bindings.TryGetValue(control, out var a) && a is not null and not ActionSpec.None ? a : null;

    private void MaybeHaptic(ActionSpec action)
    {
        var h = _config.Haptics;
        if (!h.Enabled || action is ActionSpec.Disabled) return;
        var want = action is ActionSpec.ActionsRing ? h.OnActionsRing : h.OnButtonPress;
        if (!want) return;
        try { _ = ObserveAsync(_mouse.PlayHapticAsync(1, _cts.Token), "haptic"); }
        catch (Exception ex) { _log.Error("haptic failed", ex); }
    }

    private void RaiseRing(bool pressed)
    {
        try
        {
            if (pressed) ActionsRingRequested?.Invoke(this, EventArgs.Empty);
            RingTrigger?.Invoke(this, new RingTriggerEventArgs(pressed));
        }
        catch (Exception ex) { _log.Error("ring trigger handler failed", ex); }
    }

    /// <summary>
    /// Runs a ring slot's action on the engine queue (press then release), so it reaches the app that was in front once the
    /// ring window is gone. Also used for the wheel steps of an adjustable slot.
    /// </summary>
    public void RunRingAction(ActionSpec action)
    {
        try
        {
            _queue.Post(() =>
            {
                try
                {
                    _executor.Execute(action, true);
                    _executor.Execute(action, false);
                }
                catch (Exception ex) { _log.Error("ring action failed", ex); }
            });
        }
        catch (Exception ex) { SafeLog("ring action dispatch failed", ex); }
    }

    /// <summary>A haptic pulse for the ring (tick = selection changed, bump = a slot ran), if haptics and the ring option are on.</summary>
    public void PlayRingHaptic(RingHaptic kind)
    {
        var h = _config.Haptics;
        if (!h.Enabled || !h.OnActionsRing) return;
        try { _ = ObserveAsync(_mouse.PlayHapticAsync(kind == RingHaptic.Tick ? RingHaptics.HoverWaveform : RingHaptics.RunWaveform, _cts.Token), "ring haptic"); }
        catch (Exception ex) { _log.Error("ring haptic failed", ex); }
    }

    // ---- gesture button (HID thread: press/release; hook thread: moves) ----

    /// <summary>Returns true if the gesture path handled the event; false = fall back to the plain press/release binding.</summary>
    private bool OnGestureButton(ButtonEvent e)
    {
        try
        {
            if (e.Pressed)
            {
                _gesturePlainClick = !AnySwipeBound();
                if (_gesturePlainClick) return false;
                _gesture.Begin(_clock());
                _hook.SetMoveCapture(true);
                return true;
            }
            if (_gesturePlainClick) { _gesturePlainClick = false; return false; }
            _hook.SetMoveCapture(false); // always, even if the gesture already timed out
            var (live, direction) = _gesture.End(_clock());
            if (live) _queue.Post(() => RunGesture(direction));
            return true;
        }
        catch (Exception ex)
        {
            AbortGesture();
            SafeLog("gesture button handling failed", ex);
            return true;
        }
    }

    private void OnHookMove(object? sender, HookMoveEventArgs e)
    {
        try
        {
            if (_gesture.Add(e.Dx, e.Dy, _clock())) e.Handled = true;
            else _hook.SetMoveCapture(false); // no live gesture (timed out or cancelled): stop capturing, never swallow
        }
        catch (Exception ex) { SafeLog("hook move handler failed", ex); }
    }

    private void AbortGesture()
    {
        _gesture.Cancel();
        try { _hook.SetMoveCapture(false); } catch { }
    }

    private bool AnySwipeBound()
    {
        foreach (var d in Enum.GetValues<GestureDirection>())
            if (ResolveSwipe(d) is not ActionSpec.None) return true;
        return false;
    }

    private ActionSpec ResolveSwipe(GestureDirection direction)
    {
        var cfg = _config;
        var profile = ProfileResolver.Resolve(cfg, _foreground.ProcessName);
        // Like Bindings: an explicit None in an app profile means no action and wins over the default profile.
        if (profile.GestureSwipes.TryGetValue(direction, out var own) && own is not null) return own;
        if (ProfileResolver.Default(cfg).GestureSwipes.TryGetValue(direction, out var d) && d is not null) return d;
        return new ActionSpec.None();
    }

    private void RunGesture(GestureDirection? direction)
    {
        try
        {
            if (direction is { } dir)
            {
                var swipe = ResolveSwipe(dir);
                if (swipe is ActionSpec.None) return;
                RunGestureAction(swipe);
            }
            else
            {
                var click = ResolveAction(ControlId.Gesture);
                if (click is ActionSpec.None) return;
                RunGestureAction(click);
            }
        }
        catch (Exception ex) { _log.Error("gesture action failed", ex); }
    }

    private void RunGestureAction(ActionSpec action)
    {
        MaybeHaptic(action);
        if (action is ActionSpec.ActionsRing) { RaiseRing(true); RaiseRing(false); return; } // a swipe is a tap: the ring stays open
        _executor.Execute(action, true);
        _executor.Execute(action, false);
    }

    // ---- hook buttons (hook thread: O(1), never throws) ----

    private static string NativeName(HookButton b) => b switch
    {
        HookButton.Middle => "Middle",
        HookButton.Back => "Back",
        _ => "Forward",
    };

    private void OnHookButton(object? sender, HookButtonEventArgs e)
    {
        try
        {
            var id = (ushort)e.Button;
            if (e.Pressed)
            {
                var action = ResolveAction(id);
                if (action is ActionSpec.None) return;
                if (action is ActionSpec.MouseButton m && string.Equals(m.Button, NativeName(e.Button), StringComparison.OrdinalIgnoreCase))
                    return; // the button's own behaviour: leave it to Windows
                lock (_gate) _hookActive[id] = action;
                e.Handled = true;
                _queue.Post(() => RunHookAction(action, true));
            }
            else
            {
                ActionSpec? action;
                lock (_gate) { if (!_hookActive.Remove(id, out action)) return; }
                _queue.Post(() => RunHookAction(action, false));
            }
        }
        catch (Exception ex) { SafeLog("hook button handler failed", ex); }
    }

    private void RunHookAction(ActionSpec action, bool pressed)
    {
        try
        {
            if (pressed)
            {
                MaybeHaptic(action);
                if (action is ActionSpec.ActionsRing) { RaiseRing(true); return; }
            }
            else if (action is ActionSpec.ActionsRing) { RaiseRing(false); return; }
            _executor.Execute(action, pressed);
        }
        catch (Exception ex) { _log.Error("hook button action failed", ex); }
    }

    // ---- horizontal wheel (hook thread: O(1), never throws) ----

    /// <summary>Pseudo control id the settings app stores the thumb wheel binding under.</summary>
    public const ushort ThumbWheelBindingId = 0xF150;
    private const int MaxNotchesPerEvent = 10;

    private void OnWheel(object? sender, HookWheelEventArgs e)
    {
        try
        {
            var cfg = _config;
            var bound = ResolveAction(ThumbWheelBindingId);
            if (bound is ActionSpec.Disabled)
            {
                e.Handled = true; // turned off: swallow the horizontal scroll
                return;
            }

            if (bound is ActionSpec.Keys keys)
            {
                e.Handled = true;
                var notches = _accumulator.Add(e.Delta, 1, _clock());
                if (notches == 0) return;
                _queue.Post(() => RunWheelKeys(keys.Chord, notches));
                return;
            }
            if (!cfg.Excel.ThumbWheelScrollsColumns) return;
            if (_foreground.ProcessName != "excel") return;
            var window = _foreground.WindowHandle;
            e.Handled = true;
            var cols = _accumulator.Add(e.Delta, cfg.Excel.ColumnsPerNotch, _clock());
            if (cols == 0) return;
            _queue.Post(() =>
            {
                try { _excel.TryScrollColumns(window, cols); }
                catch (Exception ex) { _log.Error("excel scroll failed", ex); }
            });
        }
        catch (Exception ex) { SafeLog("wheel handler failed", ex); }
    }

    private void RunWheelKeys(string chord, int notches)
    {
        try
        {
            var (right, left) = SplitWheelChord(chord);
            var action = new ActionSpec.Keys(notches > 0 ? right : left);
            for (var i = Math.Min(Math.Abs(notches), MaxNotchesPerEvent); i > 0; i--)
                _executor.Execute(action, true);
        }
        catch (Exception ex) { _log.Error("thumb wheel action failed", ex); }
    }

    /// <summary>
    /// Thumb wheel Keys encoding: "Right|Left" gives both directions explicitly (e.g. "VolumeUp|VolumeDown");
    /// a single chord is sent per notch right, and the same chord with Shift toggled per notch left
    /// ("Ctrl+Tab" / "Ctrl+Shift+Tab").
    /// </summary>
    public static (string Right, string Left) SplitWheelChord(string chord)
    {
        var bar = chord.IndexOf('|');
        if (bar >= 0) return (chord[..bar].Trim(), chord[(bar + 1)..].Trim());
        var parts = chord.Split('+', StringSplitOptions.TrimEntries).ToList();
        var i = parts.FindIndex(p => p.Equals("Shift", StringComparison.OrdinalIgnoreCase));
        if (i >= 0) parts.RemoveAt(i); else parts.Insert(Math.Max(0, parts.Count - 1), "Shift");
        return (chord.Trim(), string.Join('+', parts));
    }

    // ---- status ----

    private void OnConnection(object? sender, bool connected)
    {
        try
        {
            if (!connected) AbortGesture(); // a held gesture button can never be released once the device is gone
            PublishStatus(connected);
            if (connected) _ = FetchBatteryAsync();
        }
        catch (Exception ex) { SafeLog("connection handler failed", ex); }
    }

    private async Task FetchBatteryAsync()
    {
        try
        {
            var b = await _mouse.GetBatteryAsync(_cts.Token).ConfigureAwait(false);
            if (b is { } bat) PublishStatus(_mouse.IsConnected, bat.Percent, bat.Charging);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.Error("battery read failed", ex); }
    }

    private void PublishStatus(bool connected, int? percent = null, bool charging = false)
    {
        try { StatusChanged?.Invoke(this, new EngineStatus(connected, percent, charging)); }
        catch (Exception ex) { _log.Error("StatusChanged handler failed", ex); }
    }

    private void SafeLog(string msg, Exception ex)
    {
        try { _log.Error(msg, ex); } catch { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _mouse.ButtonChanged -= OnButton;
        _mouse.ConnectionChanged -= OnConnection;
        _hook.HorizontalWheel -= OnWheel;
        _hook.ButtonChanged -= OnHookButton;
        _hook.MouseMoved -= OnHookMove;
        AbortGesture();
        _foreground.Changed -= OnForegroundChanged;
        lock (_gate) _foregroundDebounce?.Cancel();
        _cts.Cancel();
        _queue.Dispose();
        _cts.Dispose();
    }
}
