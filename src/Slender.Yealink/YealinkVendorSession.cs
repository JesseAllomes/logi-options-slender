using Slender.Engine;
using Slender.Engine.Devices;

namespace Slender.Yealink;

/// <summary>Writes one vendor report to the base; false when it cannot (closed, or the channel is not writable).</summary>
public delegate bool VendorWriter(ReadOnlySpan<byte> report);

/// <summary>What a read pass asks the base for.</summary>
public enum VendorRead { Status, All }

/// <summary>
/// The conversation with the base over the vendor channel: one request in flight at a time, each answered or timed out before
/// the next, so a pass of Hello, version, handset status and every shown setting is about fifty short exchanges. Only
/// allowlisted requests are ever built (<see cref="VendorRequests"/>). A one-shot timer exists only while a pass runs. Between
/// passes it sends nothing and holds no thread: the base pushes battery and signal on its own and the read loop feeds them in.
/// A timeout skips that setting; a silent Hello or three timeouts in a row end the pass.
/// <see cref="Set"/> changes one catalogued setting: EnvSet, then an EnvGet of the same key to read it back, by the same one-at-a-time
/// rule and timer. A write asked for during a pass waits for it; one waiting write per setting is kept (the latest for that setting).
/// </summary>
public sealed class YealinkVendorSession : IDisposable
{
    public const int TimeoutMs = 1500;
    private const int MaxSilent = 3;
    private const int StepHello = 0, StepVersion = 1, StepDect = 2, StepSettings = 3;
    private const int WriteHello = 0, WriteSet = 1, WriteRead = 2;

    private readonly VendorWriter _write;
    private readonly Action<string> _log;
    private readonly VendorReassembler _reassembler = new();
    private readonly VendorDecoder _decoder = new();
    private readonly byte[] _frame = new byte[VendorFrame.Length];
    private readonly string?[] _values = new string?[YealinkSettingsCatalog.All.Count];
    private readonly string?[] _pending = new string?[YealinkSettingsCatalog.All.Count];
    private readonly object _gate = new();

    private Timer? _timer;
    private long _deadline;
    private bool _running, _helloDone, _disposed, _dirty;
    private int _step, _end, _silent;
    private bool _writing, _verifying;
    private int _writeStage, _writeIndex = -1;
    private string? _writeRaw, _writeProblem;
    private HeadsetStatus _status;
    private IReadOnlyList<HeadsetSetting>? _settings;

    public YealinkVendorSession(VendorWriter write, Action<string> log)
    {
        _write = write;
        _log = log;
        _decoder.Reply += OnReply;
        _decoder.StatusChanged += OnStatus;
    }

    /// <summary>Raised (on the read, timer or caller's thread) when the status or a setting changed.</summary>
    public event Action? Changed;

    public bool IsRunning { get { lock (_gate) return _running; } }

    public HeadsetStatus Status { get { lock (_gate) return _status; } }

    /// <summary>Plain words for the last write the base did not take; null once one reads back correctly.</summary>
    public string? WriteProblem { get { lock (_gate) return _writeProblem; } }

    /// <summary>The settings read so far, in catalog order; empty until the first answers.</summary>
    public IReadOnlyList<HeadsetSetting> Settings
    {
        get
        {
            lock (_gate)
            {
                if (_settings != null) return _settings;
                var list = new List<HeadsetSetting>();
                for (int i = 0; i < _values.Length; i++)
                    if (_values[i] is { } raw)
                    {
                        var def = YealinkSettingsCatalog.All[i];
                        list.Add(new HeadsetSetting(def.Group, def.Label, def.Text(raw), def.Key, raw));
                    }
                return _settings = list;
            }
        }
    }

    /// <summary>Feeds one 0xC8 report from the read loop.</summary>
    public void OnReport(ReadOnlySpan<byte> report) => _reassembler.Feed(report, _decoder.Message);

    /// <summary>
    /// Starts a pass. The handshake is sent the first time only. False (nothing sent) while a pass is still running or after Dispose.
    /// </summary>
    public bool Begin(VendorRead what)
    {
        lock (_gate)
        {
            if (_running || _disposed) return false;
            if (what == VendorRead.All) SetProblem(null);
            _running = true;
            _silent = 0;
            _step = _helloDone ? StepDect : StepHello;
            _end = what == VendorRead.All ? StepSettings + _values.Length : StepSettings;
            SendNext();
            return true;
        }
    }

    /// <summary>
    /// Sets one catalogued setting to raw, then reads it back. Starts at once when idle (handshake first if never done); during a
    /// pass or another write it waits; one waiting write per setting is kept (the latest for that setting). False (nothing sent) for a bad index or after Dispose;
    /// a value the catalog refuses, or a base that does not take it, shows in <see cref="WriteProblem"/>.
    /// </summary>
    public bool Set(int catalogIndex, string raw)
    {
        lock (_gate)
        {
            if (_disposed || catalogIndex < 0 || catalogIndex >= _values.Length) return false;
            if (_running)
            {
                _pending[catalogIndex] = raw;
            }
            else StartWrite(catalogIndex, raw);
        }
        Notify();
        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _running = false;
            Array.Clear(_pending);
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Notify()
    {
        bool raise;
        lock (_gate)
        {
            raise = _dirty;
            _dirty = false;
        }
        if (raise) Changed?.Invoke();
    }

    private void StartWrite(int index, string raw)
    {
        SetProblem(null);
        _running = true;
        _writing = true;
        _verifying = false;
        _writeIndex = index;
        _writeRaw = raw;
        _writeStage = _helloDone ? WriteSet : WriteHello;
        SendWrite();
    }

    private void SetProblem(string? problem)
    {
        if (_writeProblem == problem) return;
        _writeProblem = problem;
        _dirty = true;
    }

    private void FailWrite(string why, bool unanswered = false)
    {
        var def = YealinkSettingsCatalog.All[_writeIndex];
        _log($"Yealink: write of {def.Key}={_writeRaw} failed: {why}");
        SetProblem($"The base did not take the change to {def.Label}.");
        _dirty = true;
        if (unanswered && !_verifying && _writeStage != WriteHello)
        {
            // The base went quiet: it may have taken the change, so read the setting once more.
            _verifying = true;
            _writeStage = WriteRead;
            SendWrite();
            return;
        }
        Finish();
    }

    private void SendWrite()
    {
        var def = YealinkSettingsCatalog.All[_writeIndex];
        int stage = _writeStage;
        try
        {
            int length = stage switch
            {
                WriteHello => VendorRequests.Build(VendorCommand.Hello, _frame),
                WriteSet => VendorRequests.BuildEnvSet(def, _writeRaw!, _frame),
                _ => VendorRequests.BuildEnvGet(def.Key, _frame),
            };
            if (!_write(_frame.AsSpan(0, length)))
            {
                FailWrite("vendor channel is not writable");
                return;
            }
        }
        catch (Exception ex)
        {
            FailWrite(ex.Message);
            return;
        }
        if (_running && _writeStage == stage) Arm();
    }

    private void OnWriteReply(byte command, string? key, string? value)
    {
        var def = YealinkSettingsCatalog.All[_writeIndex];
        bool mine = key == null || key == def.Key;
        switch (_writeStage)
        {
            case WriteHello when command == (byte)VendorCommand.Hello:
                _writeStage = WriteSet;
                SendWrite();
                break;
            case WriteSet when command is (byte)VendorCommand.EnvSet or (byte)VendorCommand.EnvSetLong && mine:
                _log($"Yealink: set {def.Key}={_writeRaw}");
                _writeStage = WriteRead;
                SendWrite();
                break;
            case WriteRead when command is (byte)VendorCommand.EnvGet or (byte)VendorCommand.EnvGetLong && mine:
                if (key != null && value == _writeRaw) { SetProblem(null); Finish(); }
                else if (_verifying) Finish();
                else FailWrite($"read back {value ?? "nothing"}");
                break;
        }
    }

    private void OnStatus(HeadsetStatus s)
    {
        lock (_gate) _status = s;
        Changed?.Invoke();
    }

    private void OnReply(byte command, string? key, string? value)
    {
        lock (_gate)
        {
            if (command == (byte)VendorCommand.Hello) _helloDone = true;
            if (command is (byte)VendorCommand.EnvSet or (byte)VendorCommand.EnvSetLong)
            {
                // An echo of the write, not a reading: the read-back sets the value.
            }
            else if (key != null && YealinkSettingsCatalog.IndexOf(key) is var i and >= 0 && _values[i] != value)
            {
                _values[i] = value;
                _settings = null;
                _dirty = true;
            }
            else if (key == null && !_writing && command is (byte)VendorCommand.EnvGet or (byte)VendorCommand.EnvGetLong && _running && InFlightIs(command) && InFlightSetting() is var s and >= 0 && _values[s] != null)
            {
                // The base answered an empty value: the setting is not set on this firmware.
                _values[s] = null;
                _settings = null;
                _dirty = true;
            }

            if (_running && _writing) OnWriteReply(command, key, value);
            else if (_running && Matches(command, key))
            {
                _silent = 0;
                _step++;
                SendNext();
            }
        }
        Notify();
    }

    /// <summary>The request in flight, from the step: its command byte.</summary>
    private byte InFlightCommand() => _step switch
    {
        StepHello => (byte)VendorCommand.Hello,
        StepVersion => (byte)VendorCommand.GetVersion,
        StepDect => (byte)VendorCommand.GetDect,
        _ => YealinkSettingsCatalog.All[_step - StepSettings].Key.Length <= VendorFrame.ShortKey ? (byte)VendorCommand.EnvGet : (byte)VendorCommand.EnvGetLong,
    };

    private bool InFlightIs(byte command) => _step < _end && InFlightCommand() == command;

    private int InFlightSetting() => _step >= StepSettings && _step < _end ? _step - StepSettings : -1;

    /// <summary>A reply answers the request in flight when the command matches and, for a settings reply that names its key, the key is the one asked for.</summary>
    private bool Matches(byte command, string? key)
    {
        if (!InFlightIs(command)) return false;
        int s = InFlightSetting();
        return s < 0 || key == null || key == YealinkSettingsCatalog.All[s].Key;
    }

    private void SendNext()
    {
        while (_running)
        {
            if (_step >= _end) { Finish(); return; }
            int length, step = _step;
            try
            {
                int s = InFlightSetting();
                length = s >= 0
                    ? VendorRequests.BuildEnvGet(YealinkSettingsCatalog.All[s].Key, _frame)
                    : VendorRequests.Build((VendorCommand)InFlightCommand(), _frame);
                if (!_write(_frame.AsSpan(0, length)))
                {
                    _log("Yealink: vendor channel is not writable; read stopped");
                    Finish();
                    return;
                }
            }
            catch (Exception ex)
            {
                _log($"Yealink: vendor request failed: {ex.Message}");
                Finish();
                return;
            }
            // A reply that arrived during the write has already moved on (or finished); only the request still waiting needs a deadline.
            if (_running && _step == step) Arm();
            return;
        }
    }

    private void Arm()
    {
        _deadline = Environment.TickCount64 + TimeoutMs;
        _timer ??= new Timer(static o => ((YealinkVendorSession)o!).OnTimer(), this, Timeout.Infinite, Timeout.Infinite);
        _timer.Change(TimeoutMs, Timeout.Infinite);
    }

    private void OnTimer()
    {
        lock (_gate)
        {
            if (_running && Environment.TickCount64 >= _deadline) ExpireLocked();
        }
        Notify();
    }

    /// <summary>The request in flight got no answer in time (also called directly by tests).</summary>
    internal void Expire()
    {
        lock (_gate) ExpireLocked();
        Notify();
    }

    private void ExpireLocked()
    {
        if (!_running) return;
        if (_writing)
        {
            FailWrite("no answer", unanswered: true);
            return;
        }
        _silent++;
        if (_step == StepHello || _silent >= MaxSilent)
        {
            _log(_step == StepHello ? "Yealink: the base did not answer; vendor read stopped" : "Yealink: the base stopped answering; vendor read stopped");
            Finish();
            return;
        }
        _step++;
        SendNext();
    }

    /// <summary>Ends the pass or write, and starts the write that waited, if any.</summary>
    private void Finish()
    {
        _running = false;
        _writing = false;
        _verifying = false;
        _timer?.Dispose();
        _timer = null;
        if (_disposed) return;
        for (int i = 0; i < _pending.Length; i++)
        {
            if (_pending[i] is not { } raw) continue;
            _pending[i] = null;
            StartWrite(i, raw);
            return;
        }
    }
}
