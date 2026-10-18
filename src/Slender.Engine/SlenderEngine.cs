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
    private readonly TapHoldTracker _tapHold;
    private readonly IThumbWheelSource _hookSource;
    private readonly IThumbWheelSource _hidSource;
    private readonly ThumbTouchTracker _touch = new();
    private ActionSpec? _touchAction; // action used at touch start, released at touch end (under _gate)
    private readonly ColumnScrollAccumulator _accumulator = new();
    private readonly ColumnScrollAccumulator _secondAccumulator = new();
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
    private volatile bool _gestureRaw;        // this press is fed by device raw XY (no hook capture, no swallowing)
    private IReadOnlyList<ushort> _rawXy = [];
    private volatile int _deviceDpi;          // last DPI read from the mouse; 0 = unknown
    private bool _started, _disposed;
    private bool _thumbWanted, _thumbHid; // under _gate: the divert we asked for / the device confirmed it (HID++ path live)
    private volatile bool _ringOpen;
    private long _lastRotation = long.MinValue / 2; // clock ms of the last rotation from either source
    private CancellationTokenSource? _thumbRetry;
    private readonly IHoldTimer _holdTimer;
    private readonly WheelKeyHold _wheelHold = new(); // engine queue only
    private IDisposable? _holdRelease;                 // engine queue only
    private volatile bool _holding;                     // modifiers are held down by the thumb wheel path
    private long _burstStart;                           // engine queue: clock at the first held step of this burst
    private int _burstSteps;                            // engine queue: steps sent in this burst
    private int _burstReports;                          // wheel reports seen during this burst (any thread)
    private volatile bool _unheldLogged;                // one 'not held' line per top button hold
    private volatile bool _chordActive;                 // Left + Right held: the thumb wheel switches tabs
    private volatile ThumbWheelSecond? _topSecond;      // top button held in an app with a second action: the wheel runs it
    private volatile bool _topTurned;                   // the wheel stepped the second action during this hold
    private long _topPressedAt;                         // engine queue: clock when the top button went down
    private long _sideRemainder;                        // engine queue: side scroll speed carry, in delta*percent units

    public SlenderEngine(ILogiMouse mouse, IInputHook hook, IForegroundApp foreground, IExcelBridge excel,
        IActionExecutor executor, SlenderConfig config, ILog? log = null)
        : this(mouse, hook, foreground, excel, executor, config, log, null, null) { }

    internal SlenderEngine(ILogiMouse mouse, IInputHook hook, IForegroundApp foreground, IExcelBridge excel,
        IActionExecutor executor, SlenderConfig config, ILog? log, IWorkQueue? queue, Func<long>? clock, IHoldTimer? holdTimer = null)
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
        _holdTimer = holdTimer ?? new ThreadingHoldTimer();
        _tapHold = new TapHoldTracker(_holdTimer, _queue, _log);
        _hookSource = new HookThumbWheelSource(hook);
        _hidSource = new HidThumbWheelSource(mouse);
    }

    /// <summary>Raised on press of an ActionsRing control (the tray opens the ring). Press and release are both on <see cref="RingTrigger"/>.</summary>
    public event EventHandler? ActionsRingRequested;

    /// <summary>The ring trigger control went down (Pressed) or up. Raised on the engine thread.</summary>
    public event EventHandler<RingTriggerEventArgs>? RingTrigger;
    public event EventHandler<EngineStatus>? StatusChanged;

    /// <summary>Thumb wheel rotation (1/120 notch units, positive = right) for the open Actions Ring while the wheel is diverted. Raised on the HID thread.</summary>
    public event EventHandler<int>? RingThumbWheel;

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
        _hookSource.ThumbWheelRotated += OnRotation;
        _hidSource.ThumbWheelRotated += OnRotation;
        _mouse.ThumbWheelMoved += OnThumbTouch;
        _hook.ButtonChanged += OnHookButton;
        _hook.ButtonChordChanged += OnButtonChord;
        _hook.SetButtonChordEnabled(_config.Pointer.TabScrollChord);
        _hook.MouseMoved += OnHookMove;
        _mouse.RawXyMoved += OnRawXy;
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
        _hook.SetButtonChordEnabled(config.Pointer.TabScrollChord);
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
        // Holding the top button runs the app's thumb wheel second action, so the hold must be seen even when the button is native.
        if (ThumbWheelModes.SecondFor(config, process) is not null) set.Add(ControlId.ModeShift);
        return [.. set];
    }

    /// <summary>
    /// The controls to divert WITH raw XY while <paramref name="process"/> is in front: the gesture button when a swipe is bound
    /// for the effective profile (same rule as the gesture divert); a click-only binding keeps the plain divert.
    /// </summary>
    public static IReadOnlyList<ushort> ComputeRawXySet(SlenderConfig config, string? process)
    {
        var profile = ProfileResolver.Resolve(config, process);
        var def = ProfileResolver.Default(config);
        foreach (var dir in Enum.GetValues<GestureDirection>())
        {
            var effective = profile.GestureSwipes.TryGetValue(dir, out var own) && own is not null ? own
                : def.GestureSwipes.TryGetValue(dir, out var d) ? d : null;
            if (effective is not null and not ActionSpec.None) return [ControlId.Gesture];
        }
        return [];
    }

    private static bool IsHidppControl(ushort id) => id is ControlId.Gesture or ControlId.ModeShift or ControlId.SensePanel;

    /// <summary>Quiet period after a foreground change before the divert set is recomputed; rapid switches collapse into one. Tests set 0.</summary>
    internal int ForegroundDebounceMs { get; set; } = 150;

    /// <summary>Longest a divert change waits for a held control to be released (a lost release must not pin the old set forever).</summary>
    internal const long HeldDivertWaitMs = 10_000;

    private void OnForegroundChanged(object? sender, string? process)
    {
        ReleaseHoldSoon($"app switch to {process}");
        ScheduleForegroundRedivert(_clock());
    }

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
        EvaluateThumb(force: false, _clock());
        var set = ComputeDivertSet(_config, _foreground.ProcessName);
        var raw = ComputeRawXySet(_config, _foreground.ProcessName);
        lock (_gate)
        {
            if (!force && _diverted.SequenceEqual(set) && _rawXy.SequenceEqual(raw)) return;
            _diverted = set;
            _rawXy = raw;
            // Serialise diverts so a slow earlier call cannot overwrite a newer one.
            _divertChain = _divertChain.ContinueWith(_ => SafeDivert(set, raw), TaskScheduler.Default).Unwrap();
        }
    }

    private Task SafeDivert(IReadOnlyList<ushort> set, IReadOnlyList<ushort> raw)
    {
        try { return ObserveAsync(_mouse.SetDivertedControlsAsync(set, raw, _cts.Token), "set diverted controls"); }
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
        ReleaseHoldSoon($"button 0x{e.ControlId:X4} {(e.Pressed ? "down" : "up")}"); // a button must never act with the wheel's Ctrl still held
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
                if (e.ControlId == ControlId.ModeShift && ThumbWheelModes.SecondFor(_config, _foreground.ProcessName) is { } second)
                {
                    StartTopHold(second);
                    return;
                }
                if (action is ActionSpec.None) return;
                if (action is ActionSpec.TapHold th) { _tapHold.Press(e.ControlId, th, StartHold); return; }
                MaybeHaptic(action);
                if (action is ActionSpec.ActionsRing) { RaiseRing(true); return; }
            }
            else
            {
                lock (_gate)
                {
                    if (!_active.Remove(e.ControlId, out action!)) return;
                }
                if (e.ControlId == ControlId.ModeShift && _topSecond is not null) { EndTopHold(action); return; }
                if (action is ActionSpec.TapHold) { EndTapHold(e.ControlId); return; }
                if (action is ActionSpec.ActionsRing) { RaiseRing(false); return; }
                if (action is ActionSpec.None) return;
            }
            Run(action, e.Pressed);
        }
        catch (Exception ex) { _log.Error("button handling failed", ex); }
    }

    private ActionSpec ResolveAction(ushort control) => EffectiveBinding(_config, _foreground.ProcessName, control);

    /// <summary>The binding in effect for <paramref name="control"/> in <paramref name="process"/>: the app's own entry (an explicit None wins), else the default profile's, else None.</summary>
    internal static ActionSpec EffectiveBinding(SlenderConfig cfg, string? process, ushort control)
    {
        var profile = ProfileResolver.Resolve(cfg, process);
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

    // ---- tap / hold (engine queue) ----

    /// <summary>Hold threshold reached while still down: a tick, then the hold action starts as a press (its release is the physical one).</summary>
    private void StartHold(ActionSpec.TapHold th)
    {
        PlayFlipHaptic();
        if (th.Hold is ActionSpec.None) return;
        if (th.Hold is ActionSpec.ActionsRing) RaiseRing(true, holdOnly: true); // the quick press already ran Tap: the ring never sees a tap from this path
        else Run(th.Hold, true);
    }

    private void StopHold(ActionSpec hold)
    {
        if (hold is ActionSpec.None) return;
        if (hold is ActionSpec.ActionsRing) RaiseRing(false);
        else Run(hold, false);
    }

    /// <summary>Physical release: ends a started hold, otherwise it was a quick press and the tap runs once.</summary>
    private void EndTapHold(ushort id)
    {
        try
        {
            if (!_tapHold.TryRelease(id, out var th, out var held)) return;
            if (held) StopHold(th.Hold);
            else if (th.Tap is not ActionSpec.None) RunGestureAction(th.Tap);
        }
        catch (Exception ex) { _log.Error("tap/hold release failed", ex); }
    }

    /// <summary>The control can never report a release (device gone): end a started hold; a pending press does nothing.</summary>
    private void AbortTapHold(ushort id)
    {
        try { if (_tapHold.TryRelease(id, out var th, out var held) && held) StopHold(th.Hold); }
        catch (Exception ex) { _log.Error("tap/hold abort failed", ex); }
    }

    private void RaiseRing(bool pressed, bool holdOnly = false)
    {
        try
        {
            if (pressed) ActionsRingRequested?.Invoke(this, EventArgs.Empty);
            RingTrigger?.Invoke(this, new RingTriggerEventArgs(pressed, holdOnly));
        }
        catch (Exception ex) { _log.Error("ring trigger handler failed", ex); }
    }

    /// <summary>
    /// Runs a ring slot's action on the engine queue (press then release), so it reaches the app that was in front once the
    /// ring window is gone. Also used for the wheel steps of an adjustable slot.
    /// </summary>
    public void RunRingAction(ActionSpec action)
    {
        if (action is ActionSpec.TapHold) return; // not allowed in a ring slot
        try
        {
            _queue.Post(() =>
            {
                try
                {
                    Run(action, true);
                    Run(action, false);
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

    // ---- gesture button (HID thread: press/release and raw XY moves; hook thread: hook moves) ----
    // Two move sources feed the one GestureTracker. Preferred: the device's own raw XY deltas (the gesture button was diverted
    // with raw XY, so the cursor never moves and nothing is swallowed). Fallback: the low-level mouse hook captures and swallows
    // moves while the button is held (device without raw XY, or the raw XY divert was refused).

    /// <summary>Returns true if the gesture path handled the event; false = fall back to the plain press/release binding.</summary>
    private bool OnGestureButton(ButtonEvent e)
    {
        try
        {
            if (e.Pressed)
            {
                _gesturePlainClick = !AnySwipeBound();
                if (_gesturePlainClick) return false;
                _gestureRaw = _mouse.IsRawXyActive(ControlId.Gesture);
                _gesture.Begin(_clock(), _gestureRaw ? GestureTracker.RawThresholdFor(EffectiveDpi()) : GestureTracker.SwipeThresholdPx);
                // Capture moves on both paths: with raw XY the direction comes from the device only, but any cursor movement
                // Windows still receives is swallowed, so the cursor stays put whether or not raw XY stops normal reports.
                _hook.SetMoveCapture(true);
                return true;
            }
            if (_gesturePlainClick) { _gesturePlainClick = false; return false; }
            _hook.SetMoveCapture(false); // always, even if the gesture already timed out
            _gestureRaw = false;
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
            bool live = _gestureRaw ? _gesture.IsLive(_clock()) : _gesture.Add(e.Dx, e.Dy, _clock());
            if (live) e.Handled = true; // raw XY: swallow only, the device's own deltas decide the direction
            else _hook.SetMoveCapture(false); // no live gesture (timed out or cancelled): stop capturing, never swallow
        }
        catch (Exception ex) { SafeLog("hook move handler failed", ex); }
    }

    /// <summary>Raw XY delta from the device while the gesture button is held (HID thread). Ignored unless a raw gesture is live.</summary>
    private void OnRawXy(object? sender, RawXyEvent e)
    {
        try
        {
            if (_gestureRaw) _gesture.Add(e.Dx, e.Dy, _clock()); // a timed-out gesture just stops accumulating; End reports it dead
        }
        catch (Exception ex) { SafeLog("raw XY handler failed", ex); }
    }

    /// <summary>Sensor DPI for scaling the raw threshold: the configured DPI, else the last one read from the device, else the reference.</summary>
    private int EffectiveDpi() => _config.Pointer.Dpi ?? (_deviceDpi > 0 ? _deviceDpi : GestureTracker.ReferenceDpi);

    private async Task FetchDpiAsync()
    {
        try { if (await _mouse.GetDpiAsync(_cts.Token).ConfigureAwait(false) is { } dpi) _deviceDpi = dpi; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.Error("dpi read failed", ex); }
    }

    private void AbortGesture()
    {
        _gesture.Cancel();
        _gestureRaw = false;
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
        if (action is ActionSpec.TapHold th) action = th.Tap; // not offered on swipes; a hand-edited config runs its quick press
        MaybeHaptic(action);
        if (action is ActionSpec.ActionsRing) { RaiseRing(true); RaiseRing(false); return; } // a swipe is a tap: the ring stays open
        Run(action, true);
        Run(action, false);
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
            if (e.Pressed) ReleaseHoldSoon($"mouse {e.Button} pressed");
            if (e.Pressed)
            {
                var action = ResolveAction(id);
                if (action is ActionSpec.None) return;
                if (action is ActionSpec.MouseButton m && string.Equals(m.Button, NativeName(e.Button), StringComparison.OrdinalIgnoreCase))
                    return; // the button's own behaviour: leave it to Windows
                lock (_gate) _hookActive[id] = action;
                e.Handled = true;
                _queue.Post(() => RunHookAction(id, action, true));
            }
            else
            {
                ActionSpec? action;
                lock (_gate) { if (!_hookActive.Remove(id, out action)) return; }
                _queue.Post(() => RunHookAction(id, action, false));
            }
        }
        catch (Exception ex) { SafeLog("hook button handler failed", ex); }
    }

    private void RunHookAction(ushort id, ActionSpec action, bool pressed)
    {
        try
        {
            if (action is ActionSpec.TapHold th)
            {
                if (pressed) _tapHold.Press(id, th, StartHold);
                else EndTapHold(id);
                return;
            }
            if (pressed)
            {
                MaybeHaptic(action);
                if (action is ActionSpec.ActionsRing) { RaiseRing(true); return; }
            }
            else if (action is ActionSpec.ActionsRing) { RaiseRing(false); return; }
            Run(action, pressed);
        }
        catch (Exception ex) { _log.Error("hook button action failed", ex); }
    }

    // ---- horizontal wheel (hook thread: O(1), never throws) ----

    /// <summary>Pseudo control id the settings app stores the thumb wheel binding under.</summary>
    public const ushort ThumbWheelBindingId = 0xF150;
    /// <summary>Pseudo control id for "thumb wheel touch" (held while a finger rests on the wheel). Needs the wheel diverted.</summary>
    public const ushort ThumbWheelTouchBindingId = 0xF151;
    /// <summary>Pseudo control id for "thumb wheel tap" (touch then release within 250 ms and under a quarter notch of rotation).</summary>
    public const ushort ThumbWheelTapBindingId = 0xF152;
    private const int MaxNotchesPerEvent = 10;

    /// <summary>
    /// One entry for every thumb wheel rotation, from the hook (native) or HID++ (diverted). Delta is 1/120 notch units.
    /// Exactly one source is live at a time: a diverted wheel sends no native scroll, and a native hook event seen while the
    /// device is confirmed diverted is left to the OS (never handled twice).
    /// </summary>
    private void OnRotation(object? sender, ThumbWheelRotatedEventArgs e)
    {
        try
        {
            _lastRotation = _clock();
            if (_holding) Interlocked.Increment(ref _burstReports);
            if (e.FromHid)
            {
                if (_ringOpen) { RingThumbWheel?.Invoke(this, e.Delta); return; }
            }
            else
            {
                lock (_gate) if (_thumbHid) return;
            }
            var cfg = _config;
            if (_chordActive)
            {
                // Left + Right held: Tab scroll. The app's tab pair (its second role), else next / previous tab.
                e.Handled = true;
                var own = ThumbWheelModes.SecondFor(cfg, _foreground.ProcessName);
                var tabs = own is null ? ThumbWheelModes.Tabs : own with
                {
                    Forward = own.Forward is ActionSpec.None ? ThumbWheelModes.Tabs.Forward : own.Forward,
                    Back = own.Back is ActionSpec.None ? ThumbWheelModes.Tabs.Back : own.Back,
                };
                var tabSteps = _secondAccumulator.AddScaled(e.Delta, cfg.Pointer.TabScrollSpeed, _clock());
                if (tabSteps != 0) _queue.Post(() => RunSecond(tabs, tabSteps));
                return;
            }
            if (_topSecond is { } second)
            {
                e.Handled = true; // top button held: the second action owns the wheel, the native horizontal scroll never happens
                _topTurned = true;
                var steps = _secondAccumulator.AddScaled(e.Delta, cfg.Pointer.TabScrollSpeed, _clock());
                if (steps != 0) _queue.Post(() => RunSecond(second, steps));
                return;
            }
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
            if (!ExcelColumnScrolling || !cfg.Excel.ThumbWheelScrollsColumns || _foreground.ProcessName != "excel")
            {
                // Nothing wants this turn. From the hook it goes on to Windows by itself. From HID++ the wheel is still diverted
                // (just after leaving tab mode the switch back waits for the wheel to pause, or the app diverts it only for
                // touch/tap), so the turn would be lost: send it on as the normal sideways scroll.
                // A side scroll speed other than 100% swallows the native turn and sends a scaled one.
                var speed = cfg.Pointer.SideScrollSpeed;
                if (e.FromHid || speed != 100)
                {
                    if (!e.FromHid) e.Handled = true;
                    var delta = e.Delta;
                    _queue.Post(() => PassThroughSideScroll(delta, speed));
                }
                return;
            }
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

    // ---- thumb wheel divert on demand ----

    /// <summary>Quiet time after the last rotation before the wheel's reporting mode may change.</summary>
    internal int RotationPauseMs { get; set; } = 150;

    /// <summary>The Actions Ring opened or closed: it needs the wheel diverted while it is open.</summary>
    public void SetRingOpen(bool open)
    {
        if (open) ReleaseHoldSoon("ring opened");
        _ringOpen = open;
        bool started;
        lock (_gate) started = _started;
        if (started) EvaluateThumb(force: false, _clock());
    }

    /// <summary>True while the wheel is confirmed diverted and rotation comes from HID++ (the hook path is the fallback otherwise).</summary>
    internal bool ThumbWheelDiverted { get { lock (_gate) return _thumbHid; } }

    private bool ThumbWheelWanted() =>
        ThumbWheelDivertPolicy.Wanted(_config, _foreground.ProcessName, _ringOpen, _mouse.ThumbWheelTouchSupported, ExcelColumnScrolling);

    /// <summary>
    /// Recomputes whether the wheel should be diverted and applies it on the serialised divert chain. A change never happens
    /// while the wheel is turning: it waits for a pause of <see cref="RotationPauseMs"/>, or the 10 s cap. A forced call always
    /// reaches the device (after a reconnect).
    /// </summary>
    private void EvaluateThumb(bool force, long since)
    {
        try
        {
            var wanted = ThumbWheelWanted();
            lock (_gate)
            {
                if (_disposed) return;
                _thumbRetry?.Cancel();
                _thumbRetry = null;
                if (!force && wanted == _thumbWanted) return;
                if (_clock() - _lastRotation < RotationPauseMs && _clock() - since < HeldDivertWaitMs)
                {
                    var cts = _thumbRetry = new CancellationTokenSource();
                    _ = Task.Delay(Math.Max(10, RotationPauseMs / 3), cts.Token).ContinueWith(t =>
                    {
                        try { if (!t.IsCanceled && !_disposed) EvaluateThumb(force, since); }
                        catch (Exception ex) { SafeLog("thumb wheel divert retry failed", ex); }
                    }, TaskScheduler.Default);
                    return;
                }
                _thumbWanted = wanted;
                _divertChain = _divertChain.ContinueWith(_ => SafeThumbDivert(wanted), TaskScheduler.Default).Unwrap();
            }
        }
        catch (Exception ex) { SafeLog("thumb wheel divert decision failed", ex); }
    }

    private async Task SafeThumbDivert(bool wanted)
    {
        var ok = false;
        try { ok = await _mouse.TrySetThumbWheelDivertedAsync(wanted, _cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.Error("thumb wheel divert failed", ex); }
        lock (_gate)
        {
            _thumbHid = wanted && ok;
            if (wanted && !ok) _thumbWanted = false; // not diverted: the hook keeps the wheel, and the next change retries
        }
        if (wanted && !ok) _log.Info("thumb wheel divert not applied; using the mouse hook");
        else if (ok) _log.Info(wanted ? "thumb wheel diverted to Slender" : "thumb wheel back to Windows");
    }

    // ---- thumb wheel touch and tap (HID thread) ----

    private void OnThumbTouch(object? sender, ThumbWheelEvent e)
    {
        try
        {
            if (_mouse.ThumbWheelTouchSupported == false) return;
            var r = _touch.OnReport(e.Touch, e.Rotation, _clock());
            if (r.Down) _queue.Post(HandleTouchDown);
            // Thumb off the wheel ends a held burst, except while a button holds the second action: there the touch flag can
            // drop mid-turn, which let Ctrl go after the first few tabs, so it lets go when the button does.
            if (r.Up && _topSecond is null && !_chordActive) ReleaseHoldSoon("thumb lifted");
            if (r.Up) _queue.Post(() => HandleTouchUp(r.Tap));
        }
        catch (Exception ex) { SafeLog("thumb wheel touch dispatch failed", ex); }
    }

    private void HandleTouchDown()
    {
        try
        {
            var action = ResolveAction(ThumbWheelTouchBindingId);
            lock (_gate) _touchAction = action is ActionSpec.None or ActionSpec.TapHold ? null : action;
            if (_touchAction is not { } a) return;
            MaybeHaptic(a);
            if (a is ActionSpec.ActionsRing) { RaiseRing(true); return; }
            Run(a, true);
        }
        catch (Exception ex) { _log.Error("thumb wheel touch failed", ex); }
    }

    private void HandleTouchUp(bool tap)
    {
        try
        {
            ActionSpec? held;
            lock (_gate) { held = _touchAction; _touchAction = null; }
            if (held is ActionSpec.ActionsRing) RaiseRing(false);
            else if (held is { } h) Run(h, false);
            if (!tap) return;
            var action = ResolveAction(ThumbWheelTapBindingId);
            if (action is not ActionSpec.None) RunGestureAction(action);
        }
        catch (Exception ex) { _log.Error("thumb wheel tap failed", ex); }
    }

    // ---- thumb wheel second action: hold the top button (engine queue) ----

    /// <summary>True while the top button holds the thumb wheel's second action.</summary>
    internal bool SecondActionHeld => _topSecond is not null;

    /// <summary>Top button down in an app with a second action: the wheel runs it until release. Its own job waits for the release.</summary>
    private void StartTopHold(ThumbWheelSecond second)
    {
        _topPressedAt = _clock();
        _topTurned = false;
        _unheldLogged = false;
        _secondAccumulator.Reset();
        _topSecond = second;
    }

    /// <summary>
    /// Top button up: the second action ends (and lets go of any held Ctrl). A quick press that never turned the wheel runs the
    /// button's own job, as a tap; a longer hold or one that turned the wheel does nothing more. A native top button (None) is
    /// diverted only for the hold, so its own job, ratchet / free-spin, is done here.
    /// </summary>
    private void EndTopHold(ActionSpec action)
    {
        try
        {
            _topSecond = null;
            ReleaseHold("top button released");
            if (_topTurned || _clock() - _topPressedAt >= ActionSpec.TapHold.HoldMs) return;
            var tap = action is ActionSpec.TapHold th ? th.Tap : action;
            if (tap is not ActionSpec.None) RunGestureAction(tap);
            else if (action is ActionSpec.None) _ = ObserveAsync(_mouse.ToggleWheelModeAsync(_cts.Token), "ratchet / free-spin");
        }
        catch (Exception ex) { _log.Error("top button release failed", ex); }
    }

    /// <summary>A mode flip is feedback the user needs, so it follows only the main haptics switch (not "on every button press").</summary>
    private void PlayFlipHaptic()
    {
        if (!_config.Haptics.Enabled) return;
        try { _ = ObserveAsync(_mouse.PlayHapticAsync(1, _cts.Token), "haptic"); }
        catch (Exception ex) { _log.Error("haptic failed", ex); }
    }

    private void RunSecond(ThumbWheelSecond second, int notches)
    {
        try
        {
            var action = notches > 0 ? second.Forward : second.Back;
            if (action is ActionSpec.None or ActionSpec.Disabled or ActionSpec.ActionsRing) return;
            var count = Math.Min(Math.Abs(notches), MaxNotchesPerEvent);
            if (TryHeldSteps(action, notches > 0 ? second.Back : second.Forward, count)) return;
            if (!_unheldLogged && action is ActionSpec.Keys or ActionSpec.Excel)
            {
                _unheldLogged = true;
                _log.Info($"tab step not held: {action} / {(notches > 0 ? second.Back : second.Forward)} (executor raw keys: {_executor.SupportsKeyEvents})");
            }
            ReleaseHold("non-key step"); // anything else runs with no wheel modifier held
            for (var i = count; i > 0; i--)
            {
                _executor.Execute(action, true);
                _executor.Execute(action, false);
            }
        }
        catch (Exception ex) { _log.Error("second thumb wheel action failed", ex); }
    }

    private void Run(ActionSpec action, bool pressed) => _executor.Execute(action, pressed);

    private void RunWheelKeys(string chord, int notches)
    {
        try
        {
            var (right, left) = SplitWheelChord(chord);
            var action = new ActionSpec.Keys(notches > 0 ? right : left);
            var count = Math.Min(Math.Abs(notches), MaxNotchesPerEvent);
            if (TryHeldSteps(action, new ActionSpec.Keys(notches > 0 ? left : right), count)) return;
            ReleaseHold("non-key step");
            for (var i = count; i > 0; i--)
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

    /// <summary>
    /// Excel column scrolling (the thumb wheel moving whole columns in Excel). Off for now (user 2026-10-16): Excel gets the normal
    /// side scroll. The code and its setting stay for later; tests turn it on.
    /// </summary>
    internal bool ExcelColumnScrolling { get; set; }

    /// <summary>Left + Right chord from the hook: Tab scroll while held; Ctrl (if any) is let go when it ends.</summary>
    private void OnButtonChord(object? sender, bool active)
    {
        try
        {
            _chordActive = active;
            _queue.Post(() => _log.Info(active ? "tab scroll: left + right held" : "tab scroll: buttons released")); // never write files on the hook thread
            if (active) _secondAccumulator.Reset();
            else ReleaseHoldSoon("buttons released");
        }
        catch (Exception ex) { SafeLog("button chord handler failed", ex); }
    }

    /// <summary>Engine queue: a diverted turn nothing claimed, sent on as WM_MOUSEHWHEEL (the mouse hook ignores injected input).</summary>
    private void PassThroughSideScroll(int delta, int speed)
    {
        if (_sideRemainder != 0 && Math.Sign(_sideRemainder) != Math.Sign(delta)) _sideRemainder = 0;
        _sideRemainder += (long)delta * Math.Clamp(speed, PointerSettings.SpeedMin, PointerSettings.SpeedMax);
        var send = (int)(_sideRemainder / 100);
        _sideRemainder -= send * 100L;
        if (send == 0) return;
        try { _executor.SendHorizontalWheel(send); }
        catch (Exception ex) { _log.Error("side scroll pass-through failed", ex); }
    }

    // ---- held modifiers across thumb wheel key steps (engine queue) ----

    /// <summary>Quiet time after the last wheel movement before held modifiers are let go.</summary>
    internal int WheelHoldReleaseMs { get; set; } = 400;

    /// <summary>The chord a wheel step sends on the held path; Excel's sheet commands become their keys (faster than COM).</summary>
    private static string? HoldChord(ActionSpec? action) => action switch
    {
        ActionSpec.Keys k => k.Chord,
        ActionSpec.Excel { Command: ExcelCommand.NextSheet } => "Ctrl+PageDown",
        ActionSpec.Excel { Command: ExcelCommand.PreviousSheet } => "Ctrl+PageUp",
        _ => null,
    };

    /// <summary>
    /// Sends <paramref name="count"/> steps with the modifiers this direction shares with <paramref name="other"/> held down
    /// (Ctrl for Ctrl+Tab / Ctrl+Shift+Tab), as one batch. False when the pair has nothing to hold or the executor cannot send
    /// raw keys; the caller then runs whole actions per step.
    /// </summary>
    private bool TryHeldSteps(ActionSpec action, ActionSpec? other, int count)
    {
        if (count <= 0 || !_executor.SupportsKeyEvents) return false;
        var chord = HoldChord(action);
        if (chord is null || !WheelKeyHold.TryPlain(chord, out var keys)) return false;
        var shared = WheelKeyHold.SharedModifiers(chord, HoldChord(other));
        if (shared is null) return false;
        if (!_wheelHold.IsHolding) { _burstStart = _clock(); _burstSteps = 0; Interlocked.Exchange(ref _burstReports, 0); }
        var events = _wheelHold.Step(shared, keys, count);
        _burstSteps += count;
        _holding = _wheelHold.IsHolding;
        if (!_executor.SendKeyEvents(events))
        {
            _log.Error($"SendInput failed for held '{chord}'");
            ReleaseHold("send failed");
            return true;
        }
        ScheduleHoldRelease(WheelHoldReleaseMs);
        return true;
    }

    private void ScheduleHoldRelease(int delayMs)
    {
        _holdRelease?.Dispose();
        _holdRelease = _holdTimer.Schedule(Math.Max(1, delayMs), () =>
        {
            try { _queue.Post(CheckHoldRelease); }
            catch (Exception ex) { SafeLog("held modifier release could not be queued", ex); }
        });
    }

    /// <summary>Lets the modifiers go once the wheel has been still for WheelHoldReleaseMs; otherwise waits the rest.</summary>
    private void CheckHoldRelease()
    {
        try
        {
            if (!_wheelHold.IsHolding) return;
            var quiet = _clock() - _lastRotation;
            if (_chordActive || _topSecond is not null) { ScheduleHoldRelease(WheelHoldReleaseMs); return; } // Ctrl stays down until the button(s) go up
            if (quiet < WheelHoldReleaseMs) { ScheduleHoldRelease((int)(WheelHoldReleaseMs - quiet)); return; }
            ReleaseHold("wheel quiet");
        }
        catch (Exception ex) { SafeLog("held modifier release failed", ex); }
    }

    /// <summary>Engine queue: key-up for every held modifier now. Logs why, how long and how many steps (one line per burst).</summary>
    private void ReleaseHold(string reason)
    {
        _holdRelease?.Dispose();
        _holdRelease = null;
        if (!_wheelHold.IsHolding) { _holding = false; return; }
        var events = _wheelHold.Release();
        _holding = false;
        _log.Info($"wheel hold released ({reason}) after {_clock() - _burstStart} ms, {_burstSteps} steps, {Volatile.Read(ref _burstReports)} wheel reports");
        if (!_executor.SendKeyEvents(events)) _log.Error("releasing held wheel modifiers failed");
    }

    /// <summary>Any thread: queue a release when the wheel holds modifiers (a button, an app switch, the thumb leaving the wheel).</summary>
    private void ReleaseHoldSoon(string reason)
    {
        if (!_holding) return;
        try
        {
            _queue.Post(() =>
            {
                try { ReleaseHold(reason); }
                catch (Exception ex) { SafeLog("held modifier release failed", ex); }
            });
        }
        catch (Exception ex) { SafeLog("held modifier release could not be queued", ex); }
    }

    // ---- status ----

    private void OnConnection(object? sender, bool connected)
    {
        try
        {
            if (connected) EvaluateThumb(force: true, _clock()); // a reconnect re-applies the mouse's own desired wheel state
            else
            {
                lock (_gate) { _thumbHid = false; _thumbWanted = false; } // the hook is the fallback until the device is back
                _topSecond = null; // a held top button can never report its release
                ReleaseHoldSoon("mouse disconnected");
                if (_touch.Reset()) _queue.Post(() => HandleTouchUp(false));
            }
            if (!connected)
            {
                AbortGesture(); // a held gesture button can never be released once the device is gone
                // The device is gone: its held controls will never report a release, so release their actions now.
                KeyValuePair<ushort, ActionSpec>[] held;
                lock (_gate) { held = [.. _active]; _active.Clear(); }
                foreach (var (key, action) in held)
                {
                    if (action is ActionSpec.None) continue;
                    try
                    {
                        if (action is ActionSpec.TapHold) { var id = key; _queue.Post(() => AbortTapHold(id)); }
                        else if (action is ActionSpec.ActionsRing) RaiseRing(false);
                        else _queue.Post(() => ReleaseHeld(action));
                    }
                    catch (Exception ex) { SafeLog("held control release failed", ex); }
                }
            }
            PublishStatus(connected);
            if (connected) { _ = FetchBatteryAsync(); _ = FetchDpiAsync(); }
        }
        catch (Exception ex) { SafeLog("connection handler failed", ex); }
    }

    private void ReleaseHeld(ActionSpec action)
    {
        try { _executor.Execute(action, false); }
        catch (Exception ex) { _log.Error("held control release failed", ex); }
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
        _mouse.RawXyMoved -= OnRawXy;
        _mouse.ConnectionChanged -= OnConnection;
        _hookSource.ThumbWheelRotated -= OnRotation;
        _hidSource.ThumbWheelRotated -= OnRotation;
        _mouse.ThumbWheelMoved -= OnThumbTouch;
        _hookSource.Dispose();
        _hidSource.Dispose();
        _hook.ButtonChanged -= OnHookButton;
        _hook.ButtonChordChanged -= OnButtonChord;
        _hook.MouseMoved -= OnHookMove;
        AbortGesture();
        _foreground.Changed -= OnForegroundChanged;
        lock (_gate) { _foregroundDebounce?.Cancel(); _thumbRetry?.Cancel(); }
        _cts.Cancel();
        try { if (_holding) { _holding = false; _executor.SendKeyEvents(_wheelHold.Release()); } } catch { } // never leave Ctrl down
        _queue.Dispose();
        _tapHold.Dispose();
        _cts.Dispose();
    }
}
