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
    private readonly Dictionary<ushort, ActionSpec> _active = new(); // action used at press, replayed on release
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

    /// <summary>Union of control IDs with a non-None binding in any profile.</summary>
    public static IReadOnlyList<ushort> ComputeDivertSet(SlenderConfig config) =>
        config.Profiles
            .SelectMany(p => p.Bindings)
            .Where(kv => kv.Value is not null and not ActionSpec.None)
            .Select(kv => kv.Key)
            .Distinct()
            .Order()
            .ToList();

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
        if (Bound(profile, control) is { } a) return a;
        if (Bound(ProfileResolver.Default(cfg), control) is { } d) return d;
        // A diverted control loses its native behaviour, so restore it.
        return control switch
        {
            ControlId.Back => new ActionSpec.MouseButton("Back"),
            ControlId.Forward => new ActionSpec.MouseButton("Forward"),
            ControlId.Middle => new ActionSpec.MouseButton("Middle"),
            _ => new ActionSpec.None(),
        };
    }

    private static ActionSpec? Bound(Profile p, ushort control) =>
        p.Bindings.TryGetValue(control, out var a) && a is not null and not ActionSpec.None ? a : null;

    private void MaybeHaptic(ActionSpec action)
    {
        var h = _config.Haptics;
        if (!h.Enabled) return;
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

    // ---- horizontal wheel (hook thread: O(1), never throws) ----

    private void OnWheel(object? sender, HookWheelEventArgs e)
    {
        try
        {
            var cfg = _config;
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
        _cts.Cancel();
        _queue.Dispose();
        _cts.Dispose();
    }
}
