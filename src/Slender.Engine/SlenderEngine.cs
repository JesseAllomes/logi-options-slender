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
    private readonly object _gate = new();
    private readonly Dictionary<ushort, ActionSpec> _active = new(); // HID++ path: action used at press, replayed on release
    private readonly Dictionary<ushort, ActionSpec> _hookActive = new(); // hook path: same, for swallowed Middle/Back/Forward
    private readonly CancellationTokenSource _cts = new();
    private volatile SlenderConfig _config;
    private IReadOnlyList<ushort> _diverted = [];
    private Task _divertChain = Task.CompletedTask;
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

    public event EventHandler? ActionsRingRequested;
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
    /// HID++-diverted controls: only Gesture, Mode shift and Sense Panel, and only when bound (non-None) in some profile.
    /// Middle/Back/Forward are captured by the mouse hook and never diverted.
    /// </summary>
    public static IReadOnlyList<ushort> ComputeDivertSet(SlenderConfig config) =>
        config.Profiles
            .SelectMany(p => p.Bindings)
            .Where(kv => kv.Value is not null and not ActionSpec.None && IsHidppControl(kv.Key))
            .Select(kv => kv.Key)
            .Distinct()
            .Order()
            .ToList();

    private static bool IsHidppControl(ushort id) => id is ControlId.Gesture or ControlId.ModeShift or ControlId.SensePanel;

    private void Rediverts(bool force)
    {
        var set = ComputeDivertSet(_config);
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
                if (action is ActionSpec.ActionsRing) { RaiseRing(); return; }
            }
            else
            {
                lock (_gate)
                {
                    if (!_active.Remove(e.ControlId, out action!)) return;
                }
                if (action is ActionSpec.None or ActionSpec.ActionsRing) return;
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

    private void RaiseRing()
    {
        try { ActionsRingRequested?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { _log.Error("ActionsRingRequested handler failed", ex); }
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
                if (action is ActionSpec.ActionsRing) { RaiseRing(); return; }
            }
            else if (action is ActionSpec.ActionsRing) return;
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
        _cts.Cancel();
        _queue.Dispose();
        _cts.Dispose();
    }
}
