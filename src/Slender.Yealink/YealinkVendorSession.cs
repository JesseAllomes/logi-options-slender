using Slender.Engine;

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
/// </summary>
public sealed class YealinkVendorSession : IDisposable
{
    public const int TimeoutMs = 1500;
    private const int MaxSilent = 3;
    private const int StepHello = 0, StepVersion = 1, StepDect = 2, StepSettings = 3;

    private readonly VendorWriter _write;
    private readonly Action<string> _log;
    private readonly VendorReassembler _reassembler = new();
    private readonly VendorDecoder _decoder = new();
    private readonly byte[] _frame = new byte[VendorFrame.Length];
    private readonly string?[] _values = new string?[VendorSettingsCatalog.All.Count];
    private readonly object _gate = new();

    private Timer? _timer;
    private long _deadline;
    private bool _running, _helloDone, _disposed;
    private int _step, _end, _silent;
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
                        var def = VendorSettingsCatalog.All[i];
                        list.Add(new HeadsetSetting(def.Group, def.Label, def.Text(raw)));
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
            _running = true;
            _silent = 0;
            _step = _helloDone ? StepDect : StepHello;
            _end = what == VendorRead.All ? StepSettings + _values.Length : StepSettings;
            SendNext();
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _running = false;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void OnStatus(HeadsetStatus s)
    {
        lock (_gate) _status = s;
        Changed?.Invoke();
    }

    private void OnReply(byte command, string? key, string? value)
    {
        bool changed = false;
        lock (_gate)
        {
            if (command == (byte)VendorCommand.Hello) _helloDone = true;
            if (key != null && VendorSettingsCatalog.IndexOf(key) is var i and >= 0 && _values[i] != value)
            {
                _values[i] = value;
                _settings = null;
                changed = true;
            }
            else if (key == null && command is (byte)VendorCommand.EnvGet or (byte)VendorCommand.EnvGetLong && _running && InFlightIs(command) && InFlightSetting() is var s and >= 0 && _values[s] != null)
            {
                // The base answered an empty value: the setting is not set on this firmware.
                _values[s] = null;
                _settings = null;
                changed = true;
            }

            if (_running && Matches(command, key))
            {
                _silent = 0;
                _step++;
                SendNext();
            }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>The request in flight, from the step: its command byte.</summary>
    private byte InFlightCommand() => _step switch
    {
        StepHello => (byte)VendorCommand.Hello,
        StepVersion => (byte)VendorCommand.GetVersion,
        StepDect => (byte)VendorCommand.GetDect,
        _ => VendorSettingsCatalog.All[_step - StepSettings].Key.Length <= VendorFrame.ShortKey ? (byte)VendorCommand.EnvGet : (byte)VendorCommand.EnvGetLong,
    };

    private bool InFlightIs(byte command) => _step < _end && InFlightCommand() == command;

    private int InFlightSetting() => _step >= StepSettings && _step < _end ? _step - StepSettings : -1;

    /// <summary>A reply answers the request in flight when the command matches and, for a settings reply that names its key, the key is the one asked for.</summary>
    private bool Matches(byte command, string? key)
    {
        if (!InFlightIs(command)) return false;
        int s = InFlightSetting();
        return s < 0 || key == null || key == VendorSettingsCatalog.All[s].Key;
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
                    ? VendorRequests.BuildEnvGet(VendorSettingsCatalog.All[s].Key, _frame)
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
            if (_running && Environment.TickCount64 >= _deadline) Expire();
        }
    }

    /// <summary>The request in flight got no answer in time (also called directly by tests).</summary>
    internal void Expire()
    {
        lock (_gate)
        {
            if (!_running) return;
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
    }

    private void Finish()
    {
        _running = false;
        _timer?.Dispose();
        _timer = null;
    }
}
