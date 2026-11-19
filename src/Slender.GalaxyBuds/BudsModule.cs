using Slender.Engine;
using Slender.Engine.Devices;

namespace Slender.GalaxyBuds;

/// <summary>
/// The Galaxy Buds4 Pro device module. While stopped it holds nothing: no socket, thread, timer or notification. While started it
/// has one connection attempt or reader thread at a time and, only while the link is down, one slow retry timer (60 s, doubling to
/// 5 min). A Windows push (Wake, or a Bluetooth/audio arrival through the connect hint) tries at once. The link is reported up when
/// the first frame arrives and down when the socket ends. Start, Stop and Wake never block: connecting happens on the worker, and no
/// registry callback is made while the module lock is held. Settings go out only through <see cref="BudsAllowlist"/>.
/// </summary>
public sealed class BudsModule : IDeviceModule, IDisposable
{
    public static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaxRetry = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FindLimit = TimeSpan.FromSeconds(30);

    private readonly IBudsTransport _transport;
    private readonly Func<ulong?> _address;
    private readonly Action<string, bool> _reportLink;
    private readonly Action<string, Exception> _reportFault;
    private readonly Action<string> _log;
    private readonly TimeProvider _time;
    private readonly Func<Action, IDisposable?>? _connectHint;

    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private readonly object _setGate = new();

    private bool _started, _linkUp, _attempt, _findActive;
    private int _gen;
    private IBudsLink? _link;
    private IDisposable? _hint;
    private ITimer? _retry, _findTimer;
    private TimeSpan _backoff = FirstRetry;
    private BudsModel _model = new();
    private string? _writeProblem;
    private int _pendingAck = -1;
    private bool _acked;
    private int _threads;

    public BudsModule(IBudsTransport transport, Func<ulong?> address, Action<string, bool> reportLink, Action<string, Exception> reportFault,
        Action<string>? log = null, TimeProvider? time = null, Func<Action, IDisposable?>? connectHint = null)
    {
        _transport = transport;
        _address = address;
        _reportLink = reportLink;
        _reportFault = reportFault;
        _log = log ?? (static _ => { });
        _time = time ?? TimeProvider.System;
        _connectHint = connectHint;
    }

    public string DeviceId => DeviceCatalog.GalaxyBuds4Pro;

    /// <summary>How long a change waits for the buds' acknowledgement or a status update.</summary>
    public TimeSpan AckTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The state changed (link, status, a change, a refusal). May come from any thread; return quickly.</summary>
    public event Action? StateChanged;

    /// <summary>Threads of ours alive right now (a connection attempt or the reader); 0 once stopped.</summary>
    internal int LiveThreads => Volatile.Read(ref _threads);

    /// <summary>True while a retry or find timer exists.</summary>
    internal bool HasTimers { get { lock (_gate) return _retry is not null || _findTimer is not null; } }

    public bool LinkUp { get { lock (_gate) return _linkUp; } }

    public BudsState GetState()
    {
        lock (_gate) return _model.ToState(_linkUp, _findActive, _writeProblem);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            _gen++;
            _backoff = FirstRetry;
            _writeProblem = null;
            try { _hint = _connectHint?.Invoke(OnHint); }
            catch (Exception e) { _log("buds: connect hint unavailable: " + e.Message); }
            BeginAttempt();
        }
    }

    public void Stop()
    {
        bool wasUp;
        lock (_gate)
        {
            if (!_started) return;
            _started = false;
            _gen++;
            if (_findActive && _linkUp && _link is { } l) TrySend(l, BudsIds.FindStop, []);
            _findActive = false;
            _link?.Dispose();
            _link = null;
            _attempt = false;
            DisposeTimers();
            _hint?.Dispose();
            _hint = null;
            wasUp = _linkUp;
            _linkUp = false;
            _model = KeepIdentity(_model);
            _pendingAck = -1;
            Monitor.PulseAll(_gate);
        }

        if (wasUp) _reportLink(DeviceId, false);
        StateChanged?.Invoke();
    }

    /// <summary>The device's interfaces arrived: try now if the link is down.</summary>
    public void Wake() => TryNow();

    public void Dispose() => Stop();

    private void OnHint() => TryNow();

    private void TryNow()
    {
        lock (_gate)
        {
            if (!_started || _linkUp || _attempt) return;
            _backoff = FirstRetry;
            _retry?.Dispose();
            _retry = null;
            BeginAttempt();
        }
    }

    private static BudsModel KeepIdentity(BudsModel old) => new() { Firmware = old.Firmware, Model = old.Model };

    private void DisposeTimers()
    {
        _retry?.Dispose();
        _retry = null;
        _findTimer?.Dispose();
        _findTimer = null;
    }

    /// <summary>Caller holds the lock.</summary>
    private void BeginAttempt()
    {
        if (!_started || _attempt) return;
        _attempt = true;
        int gen = _gen;
        Interlocked.Increment(ref _threads);
        var t = new Thread(() => Run(gen)) { IsBackground = true, Name = "Slender.GalaxyBuds" };
        try { t.Start(); }
        catch
        {
            _attempt = false;
            Interlocked.Decrement(ref _threads);
            throw;
        }
    }

    private void Run(int gen)
    {
        IBudsLink? link = null;
        Exception? fault = null;
        try
        {
            ulong? address = _address();
            if (address is null) return;
            link = _transport.Create(address.Value);
            lock (_gate)
            {
                if (gen != _gen) return;
                _link = link;
            }

            link.Connect();
            var reader = new BudsFrameReader();
            var frames = new List<BudsFrame>(8);
            var buf = new byte[1024];
            while (true)
            {
                int n = link.Read(buf);
                if (n <= 0) break;
                frames.Clear();
                reader.Feed(buf.AsSpan(0, n), frames);
                foreach (var f in frames)
                    if (!HandleFrame(gen, link, f)) return;
            }
        }
        catch (IOException e)
        {
            _log("buds: " + e.Message);
        }
        catch (Exception e)
        {
            fault = e;
        }
        finally
        {
            try { link?.Dispose(); }
            catch { }
            try { Ended(gen, link, fault); }
            finally { Interlocked.Decrement(ref _threads); }
        }
    }

    /// <summary>False when this connection was stopped meanwhile.</summary>
    private bool HandleFrame(int gen, IBudsLink link, BudsFrame f)
    {
        bool up = false, changed = false, stopFind = false;
        lock (_gate)
        {
            if (gen != _gen) return false;
            if (!_linkUp)
            {
                _linkUp = true;
                _backoff = FirstRetry;
                up = changed = true;
            }

            if (f.Id == BudsIds.Ack)
            {
                if (f.Payload.Length >= 1 && f.Payload[0] == _pendingAck) _acked = true;
                Monitor.PulseAll(_gate);
            }
            else if (BudsStatusParser.Apply(_model, f))
            {
                changed = true;
                if (f.Id is BudsIds.StatusUpdated or BudsIds.ExtendedStatusUpdated or BudsIds.NoiseControlsUpdate && _pendingAck >= 0)
                {
                    _acked = true;
                    Monitor.PulseAll(_gate);
                }

                if (_findActive && _model.AnyWorn) stopFind = true;
            }
        }

        if (up)
        {
            // Ask for the model name once per link; everything else the buds push on their own.
            TrySend(link, BudsIds.DebugSku, []);
            _reportLink(DeviceId, true);
        }

        if (stopFind)
        {
            lock (_gate) _writeProblem = "Find stopped: an earbud was put in.";
            StopFind();
        }

        if (changed) StateChanged?.Invoke();
        return true;
    }

    private void Ended(int gen, IBudsLink? link, Exception? fault)
    {
        bool wasUp;
        lock (_gate)
        {
            if (gen != _gen) return;
            _attempt = false;
            if (ReferenceEquals(_link, link)) _link = null;
            wasUp = _linkUp;
            _linkUp = false;
            _findActive = false;
            _findTimer?.Dispose();
            _findTimer = null;
            _model = KeepIdentity(_model);
            _pendingAck = -1;
            Monitor.PulseAll(_gate);
            if (_started && fault is null)
            {
                var due = _backoff;
                _backoff = TimeSpan.FromTicks(Math.Min(_backoff.Ticks * 2, MaxRetry.Ticks));
                _retry?.Dispose();
                _retry = _time.CreateTimer(_ => OnRetry(gen), null, due, Timeout.InfiniteTimeSpan);
            }
        }

        if (wasUp) _reportLink(DeviceId, false);
        StateChanged?.Invoke();
        if (fault is not null)
        {
            _log("buds: fault: " + fault.Message);
            _reportFault(DeviceId, fault);
        }
    }

    private void OnRetry(int gen)
    {
        lock (_gate)
        {
            if (gen != _gen || !_started || _attempt || _linkUp) return;
            _retry?.Dispose();
            _retry = null;
            BeginAttempt();
        }
    }

    // ---- sending ----

    /// <summary>Sends one allowlisted message. Returns false (never throws) when it is refused or the write failed.</summary>
    private bool TrySend(IBudsLink link, byte id, byte[] payload)
    {
        if (!BudsAllowlist.IsAllowed(id, payload.Length))
        {
            _log($"buds: refused to send message {id}");
            return false;
        }

        try
        {
            var frame = BudsCodec.Encode(id, payload);
            lock (_writeGate) link.Write(frame);
            return true;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Sends and waits for the ack of this id or a status update. Caller must hold _setGate.</summary>
    private bool SendAndWait(byte id, byte[] payload)
    {
        IBudsLink link;
        lock (_gate)
        {
            if (!_linkUp || _link is null) return false;
            link = _link;
            _pendingAck = id;
            _acked = false;
        }

        if (!TrySend(link, id, payload))
        {
            lock (_gate) _pendingAck = -1;
            return false;
        }

        lock (_gate)
        {
            long timeoutMs = (long)AckTimeout.TotalMilliseconds;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!_acked && _linkUp && sw.ElapsedMilliseconds < timeoutMs)
                Monitor.Wait(_gate, (int)Math.Max(1, timeoutMs - sw.ElapsedMilliseconds));
            bool ok = _acked;
            _pendingAck = -1;
            return ok;
        }
    }

    private void Problem(string? text)
    {
        lock (_gate) _writeProblem = text;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Changes the setting at <paramref name="index"/> in GalaxyBudsSettingsCatalog.All to the logical <paramref name="raw"/> value.
    /// Blocks up to <see cref="AckTimeout"/> for the buds' answer; call it off the UI thread. A refusal or failure is in the state's WriteProblem.
    /// </summary>
    public void Set(int index, long raw)
    {
        if (index < 0 || index >= GalaxyBudsSettingsCatalog.All.Count || raw < int.MinValue || raw > int.MaxValue)
        {
            Problem("That setting or value is not allowed.");
            return;
        }

        var def = GalaxyBudsSettingsCatalog.All[index];
        if (!def.Accepts((int)raw))
        {
            Problem("That setting or value is not allowed.");
            return;
        }

        lock (_setGate)
        {
            byte id;
            byte[] payload;
            string? problem;
            lock (_gate)
            {
                if (!_linkUp)
                {
                    _writeProblem = "The earbuds are not connected.";
                    problem = _writeProblem;
                    id = 0;
                    payload = [];
                }
                else if (!BudsCommands.TryBuild(def.Key, (int)raw, _model, out id, out payload, out problem))
                {
                    _writeProblem = problem;
                }
                else problem = null;
            }

            if (problem is not null)
            {
                StateChanged?.Invoke();
                return;
            }

            if (!SendAndWait(id, payload))
            {
                Problem("The earbuds did not take that change.");
                return;
            }

            lock (_gate)
            {
                _model.Set(def.Key, (int)raw);
                _writeProblem = null;
            }

            StateChanged?.Invoke();
        }
    }

    /// <summary>Starts or stops the find-my-earbuds beep. Start is refused while either bud is worn; it stops by itself after 30 s.</summary>
    public void Find(bool start)
    {
        if (!start)
        {
            StopFind();
            return;
        }

        lock (_setGate)
        {
            string? refusal = null;
            lock (_gate)
            {
                if (!_linkUp) refusal = "The earbuds are not connected.";
                else if (_model.AnyWorn) refusal = "Take the earbuds out first; the beep is loud.";
                else if (!_model.PlacementKnown) refusal = "The earbuds have not said where they are yet.";
                else if (_findActive) return;
            }

            if (refusal is not null)
            {
                Problem(refusal);
                return;
            }

            if (!SendAndWait(BudsIds.FindStart, []))
            {
                Problem("The earbuds did not start the beep.");
                return;
            }

            lock (_gate)
            {
                _findActive = true;
                _writeProblem = null;
                _findTimer?.Dispose();
                int gen = _gen;
                _findTimer = _time.CreateTimer(_ => OnFindLimit(gen), null, FindLimit, Timeout.InfiniteTimeSpan);
            }

            StateChanged?.Invoke();
        }
    }

    /// <summary>Sends the stop without waiting for an answer (it can run on the reader thread); safe to repeat.</summary>
    private void StopFind()
    {
        IBudsLink? link = null;
        lock (_gate)
        {
            bool wasActive = _findActive;
            _findActive = false;
            _findTimer?.Dispose();
            _findTimer = null;
            if (wasActive && _linkUp) link = _link;
        }

        if (link is not null) TrySend(link, BudsIds.FindStop, []);
        StateChanged?.Invoke();
    }

    private void OnFindLimit(int gen)
    {
        lock (_gate)
            if (gen != _gen || !_findActive) return;
        StopFind();
    }
}
