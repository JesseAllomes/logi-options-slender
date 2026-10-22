namespace Slender.Engine.Devices;

/// <summary>
/// Decides which supported devices show in the app and runs each device's module only while the device is around. Inputs are
/// pushed (HID arrival/removal from the presence source, link reports from modules, one grace timer); nothing polls. All state
/// sits behind one lock and module Start/Stop/Wake are called while holding it, so they are serial; modules must not block in
/// them waiting on their own callbacks into the registry from another thread (a synchronous ReportLink from the same thread is
/// fine, the lock is reentrant). Changed is raised outside the lock. The registry does not own the presence source.
/// </summary>
public sealed class DeviceRegistry : IDisposable
{
    /// <summary>How long a known device may be out of reach and still show as Sleeping before it becomes Disconnected.</summary>
    public static readonly TimeSpan SleepGrace = TimeSpan.FromMinutes(30);

    private sealed class Slot(SupportedDevice device)
    {
        public readonly SupportedDevice Device = device;
        public IDeviceModule? Module;
        public bool Running, Faulted, Known, LinkUp, HostSwitched, Listed;
        public DevicePresence Presence = DevicePresence.Hidden;
        public DateTimeOffset First, LastAlive;
    }

    private readonly Slot[] _slots;
    private readonly IDevicePresenceSource _presence;
    private readonly KnownDeviceStore _store;
    private readonly ILog _log;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private IReadOnlySet<HidDeviceKey> _snapshot = new HashSet<HidDeviceKey>();
    private ITimer? _timer;
    private DateTimeOffset? _timerDue;
    private bool _started, _disposed, _dirty;
    private volatile DeviceEntry[] _entries;

    public DeviceRegistry(IReadOnlyList<SupportedDevice> catalog, IDevicePresenceSource presence, KnownDeviceStore store, ILog log,
        TimeProvider? time = null)
    {
        _slots = catalog.Select(d => new Slot(d)).ToArray();
        _presence = presence;
        _store = store;
        _log = log;
        _time = time ?? TimeProvider.System;
        _entries = BuildEntries();
    }

    public event EventHandler? Changed;

    /// <summary>Every catalog device in catalog order, Hidden ones included. LastSeen is when it was last confirmed alive.</summary>
    public IReadOnlyList<DeviceEntry> Devices => _entries;

    public void Register(IDeviceModule module)
    {
        lock (_gate)
        {
            if (_started) throw new InvalidOperationException("Register before Start.");
            var slot = _slots.FirstOrDefault(s => s.Device.Id == module.DeviceId)
                ?? throw new ArgumentException($"Unknown device id '{module.DeviceId}'.", nameof(module));
            if (slot.Module is not null) throw new ArgumentException($"Device '{module.DeviceId}' already has a module.", nameof(module));
            slot.Module = module;
        }
    }

    public void Start() => Run(() =>
    {
        if (_started || _disposed) return;
        _started = true;
        var known = _store.Load();
        foreach (var s in _slots)
            if (known.TryGetValue(s.Device.Id, out var k)) { s.Known = true; s.First = k.First; s.LastAlive = k.Last; }
        _presence.Changed += OnPresenceChanged;
        try { _presence.Start(); }
        catch (Exception e) { _log.Error("device presence start failed", e); }
        Refresh();
        // A known device Windows lists at start gets its sleep grace from now: its module needs a moment to connect, and the last
        // seen time may be from before the computer was off, which would flash it Disconnected on every boot.
        var start = _time.GetUtcNow();
        foreach (var s in _slots)
            if (s.Known && s.LastAlive < start && _snapshot.Any(s.Device.Matches)) s.LastAlive = start;
        Evaluate(presenceChanged: false);
    });

    /// <summary>The module's link to its device went up or down. Up also clears a switched-to-another-host state.</summary>
    public void ReportLink(string deviceId, bool up) => Run(() =>
    {
        var s = Find(deviceId);
        if (s is null) return;
        s.LinkUp = up;
        if (up) s.HostSwitched = false;
        if (_started) Evaluate(false);
    });

    /// <summary>The device switched to another host: Disconnected now, until the next ReportLink(up).</summary>
    public void ReportDisconnected(string deviceId) => Run(() =>
    {
        var s = Find(deviceId);
        if (s is null) return;
        s.LinkUp = false;
        s.HostSwitched = true;
        if (_started) Evaluate(false);
    });

    /// <summary>The module cannot continue: it is stopped and not restarted until the tray restarts.</summary>
    public void ReportFault(string deviceId, Exception ex) => Run(() =>
    {
        var s = Find(deviceId);
        if (s is null) return;
        Fault(s, "reported a fault", ex);
        if (_started) Evaluate(false);
    });

    /// <summary>Removes a Disconnected device from this computer's list. False when it is not Disconnected.</summary>
    public bool Forget(string deviceId)
    {
        bool done = false;
        Run(() =>
        {
            var s = Find(deviceId);
            if (s is null || s.Presence != DevicePresence.Disconnected) return;
            _store.Remove(s.Device.Id);
            s.Known = false;
            s.HostSwitched = false;
            s.LinkUp = false;
            s.LastAlive = default;
            s.First = default;
            done = true;
            Evaluate(false);
        });
        return done;
    }

    public void Dispose() => Run(() =>
    {
        if (_disposed) return;
        _disposed = true;
        _presence.Changed -= OnPresenceChanged;
        _timer?.Dispose();
        _timer = null;
        var now = _time.GetUtcNow();
        foreach (var s in _slots)
        {
            if (s.Running) StopModule(s);
            if (!s.Known) continue;
            if (s.Presence == DevicePresence.Active) s.LastAlive = now;
            _store.Upsert(s.Device.Id, s.First, s.LastAlive);
        }
    });

    private Slot? Find(string id) => _slots.FirstOrDefault(s => s.Device.Id == id);

    private void OnPresenceChanged(object? sender, EventArgs e) => Run(() =>
    {
        if (!_started || _disposed) return;
        Refresh();
        Evaluate(presenceChanged: true);
    });

    private void OnTimer(object? state) => Run(() =>
    {
        if (_disposed) return;
        _timerDue = null;
        Evaluate(false);
    });

    private void Refresh()
    {
        try { _snapshot = _presence.Snapshot(); }
        catch (Exception e) { _log.Error("device presence snapshot failed", e); }
    }

    /// <summary>Runs under the lock, then raises Changed once outside it. A nested call (a module reporting from inside Start) leaves that to the outer one.</summary>
    private void Run(Action action)
    {
        lock (_gate) action();
        if (Monitor.IsEntered(_gate)) return;
        bool raise;
        lock (_gate)
        {
            raise = _dirty;
            _dirty = false;
        }

        if (raise) Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Evaluate(bool presenceChanged)
    {
        if (_disposed) return;
        var now = _time.GetUtcNow();
        foreach (var s in _slots)
        {
            bool listed = _snapshot.Any(s.Device.Matches);
            bool arrived = presenceChanged && listed && !s.Listed;
            if (!s.Device.ReportsLink && listed && !s.Listed) s.HostSwitched = false;
            s.Listed = listed;
            bool alive = (s.Device.ReportsLink ? s.LinkUp : listed) && !s.HostSwitched;

            var prev = s.Presence;
            if (!s.Known)
            {
                if (alive)
                {
                    s.Known = true;
                    s.First = s.LastAlive = now;
                    _store.Upsert(s.Device.Id, now, now);
                    s.Presence = DevicePresence.Active;
                }
                else s.Presence = DevicePresence.Hidden;
            }
            else if (alive)
            {
                if (prev != DevicePresence.Active) s.LastAlive = now;
                s.Presence = DevicePresence.Active;
            }
            else
            {
                if (prev == DevicePresence.Active)
                {
                    s.LastAlive = now;
                    _store.Upsert(s.Device.Id, s.First, now);
                }

                s.Presence = !s.HostSwitched && now < s.LastAlive + SleepGrace ? DevicePresence.Sleeping : DevicePresence.Disconnected;
            }

            bool want = !s.Faulted && s.Module is not null
                && (listed || s.Presence is DevicePresence.Active or DevicePresence.Sleeping);
            if (want && !s.Running) StartModule(s);
            else if (!want && s.Running) StopModule(s);
            if (arrived && s.Running) Guard(s, "Wake", () => s.Module!.Wake());
        }

        Rearm();
        var entries = BuildEntries();
        if (!entries.AsSpan().SequenceEqual(_entries)) _dirty = true;
        _entries = entries;
    }

    /// <summary>One one-shot timer, only while something sleeps, due when the earliest sleeper's grace ends.</summary>
    private void Rearm()
    {
        DateTimeOffset? due = null;
        foreach (var s in _slots)
            if (s.Presence == DevicePresence.Sleeping && (due is null || s.LastAlive + SleepGrace < due)) due = s.LastAlive + SleepGrace;

        if (due is null)
        {
            _timer?.Dispose();
            _timer = null;
            _timerDue = null;
            return;
        }

        if (_timer is not null && _timerDue == due) return;
        var span = due.Value - _time.GetUtcNow();
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (_timer is null) _timer = _time.CreateTimer(OnTimer, null, span, Timeout.InfiniteTimeSpan);
        else _timer.Change(span, Timeout.InfiniteTimeSpan);
        _timerDue = due;
    }

    private void StartModule(Slot s)
    {
        s.Running = true;
        Guard(s, "Start", () => s.Module!.Start());
    }

    private void StopModule(Slot s)
    {
        s.Running = false;
        Guard(s, "Stop", () => s.Module!.Stop());
    }

    private void Guard(Slot s, string what, Action call)
    {
        try { call(); }
        catch (Exception e) { Fault(s, $"{what} failed", e); }
    }

    private void Fault(Slot s, string what, Exception ex)
    {
        if (s.Faulted) return;
        s.Faulted = true;
        _dirty = true;
        _log.Error($"device module {s.Device.Id} {what}; disabled until restart", ex);
        if (!s.Running) return;
        s.Running = false;
        try { s.Module?.Stop(); }
        catch { }
    }

    private DeviceEntry[] BuildEntries() =>
        _slots.Select(s => new DeviceEntry(s.Device.Id, s.Device.Name, s.Presence, s.Faulted, s.Known ? s.LastAlive : null)).ToArray();
}
