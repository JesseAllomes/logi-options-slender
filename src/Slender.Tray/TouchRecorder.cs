using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Slender.Engine;
using Slender.Hidpp;

namespace Slender.Tray;

/// <summary>
/// Touch calibration recorder (user 2026-10-20: tell grabbing the mouse apart from a deliberate rest or tap). Off unless a
/// trigger file appears: %LOCALAPPDATA%\Slender\touch-record.txt with lines "label=grab", "seconds=120" and optionally
/// "quiet=1". While recording the thumb wheel is diverted (so touch is reported), and every thumb wheel report, plus the pointer
/// position and the system idle time every 20 ms, goes to calibration\&lt;label&gt;-&lt;time&gt;.csv in the same folder. Quiet
/// recordings run nothing (preview mode, like the card's tester); others keep every action working. The trigger file is
/// deleted when a recording starts. Nothing is sent anywhere.
/// </summary>
public sealed class TouchRecorder : IDisposable
{
    private const string TriggerName = "touch-record.txt";
    private readonly SlenderEngine _engine;
    private readonly ILogiMouse _mouse;
    private readonly ILog _log;
    private readonly string _dir;
    private readonly object _gate = new();
    private Timer? _poll;
    private Session? _session;

    public TouchRecorder(SlenderEngine engine, ILogiMouse mouse, string stateDirectory, ILog? log = null)
    {
        _engine = engine;
        _mouse = mouse;
        _dir = stateDirectory;
        _log = log ?? NullLog.Instance;
    }

    public void Start() => _poll ??= new Timer(_ => Poll(), null, 1000, 1000);

    private void Poll()
    {
        try
        {
            lock (_gate)
            {
                if (_session is { } s)
                {
                    if (Environment.TickCount64 >= s.EndsAt) Stop("time up");
                    return;
                }
            }

            string trigger = Path.Combine(_dir, TriggerName);
            if (!File.Exists(trigger)) return;
            var settings = File.ReadAllLines(trigger)
                .Select(l => l.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());
            File.Delete(trigger);
            string label = settings.TryGetValue("label", out var l) && l.Length > 0 && l.All(char.IsLetterOrDigit) ? l : "touch";
            int seconds = settings.TryGetValue("seconds", out var sec) && int.TryParse(sec, out var n) ? Math.Clamp(n, 10, 1800) : 120;
            bool quiet = settings.TryGetValue("quiet", out var q) && q == "1";
            Begin(label, seconds, quiet);
        }
        catch (Exception ex) { _log.Error("touch recorder failed", ex); }
    }

    private void Begin(string label, int seconds, bool quiet)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "calibration"));
        string path = Path.Combine(_dir, "calibration", $"{label}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var session = new Session(path, Environment.TickCount64 + seconds * 1000L, quiet);
        lock (_gate) _session = session;
        session.Write("ms,kind,a,b,c,d");
        _mouse.ThumbWheelMoved += OnWheel;
        if (quiet) _engine.SetPreview(session);
        else _engine.SetThumbDivertForced(true);
        session.Sampler = new Timer(_ => Sample(session), null, 0, 20);
        _log.Info($"touch recording '{label}' started for {seconds} s{(quiet ? " (nothing runs)" : string.Empty)}: {path}");
    }

    private void OnWheel(object? sender, ThumbWheelEvent e)
    {
        Session? s;
        lock (_gate) s = _session;
        s?.Write($"{s.Ms},wheel,{e.Rotation},{(e.Touch ? 1 : 0)},{e.Status},");
    }

    private static void Sample(Session s)
    {
        GetCursorPos(out var pt);
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        long idle = GetLastInputInfo(ref info) ? (uint)Environment.TickCount - info.Time : -1;
        s.Write($"{s.Ms},cur,{pt.X},{pt.Y},{idle},");
    }

    private void Stop(string why)
    {
        Session? s;
        lock (_gate) { s = _session; _session = null; }
        if (s is null) return;
        s.Sampler?.Dispose();
        _mouse.ThumbWheelMoved -= OnWheel;
        if (s.Quiet) _engine.SetPreview(null);
        else _engine.SetThumbDivertForced(false);
        s.Write($"{s.Ms},end,{why},,,");
        s.Close();
        _log.Info($"touch recording ended ({why}): {s.Path}");
    }

    public void Dispose()
    {
        _poll?.Dispose();
        Stop("Slender exiting");
    }

    /// <summary>One recording: lines go through a channel to a single writer so the HID thread never touches the disk.</summary>
    private sealed class Session : IThumbPreviewSink
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        private readonly long _start = Environment.TickCount64;
        private readonly Task _writer;

        public Session(string path, long endsAt, bool quiet)
        {
            Path = path;
            EndsAt = endsAt;
            Quiet = quiet;
            _writer = Task.Run(async () =>
            {
                await using var file = new StreamWriter(path, append: false, Encoding.ASCII);
                await foreach (var line in _lines.Reader.ReadAllAsync().ConfigureAwait(false)) await file.WriteLineAsync(line).ConfigureAwait(false);
            });
        }

        public string Path { get; }
        public long EndsAt { get; }
        public bool Quiet { get; }
        public Timer? Sampler { get; set; }
        public string Ms => (Environment.TickCount64 - _start).ToString(CultureInfo.InvariantCulture);

        public void Write(string line) => _lines.Writer.TryWrite(line);

        public void Close()
        {
            _lines.Writer.TryComplete();
            try { _writer.Wait(2000); } catch { }
        }

        // Quiet recordings are preview mode: the engine reports here instead of running anything.
        public void Rotated(int delta) { }
        public void TopButton(bool down) => Write($"{Ms},top,{(down ? 1 : 0)},,,");
        public void Gesture(TouchGesture gesture) => Write($"{Ms},gesture,{gesture},,,");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
