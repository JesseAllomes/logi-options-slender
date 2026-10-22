using System.Globalization;
using System.IO.Pipes;
using System.Text;

namespace Slender.Engine;

/// <summary>Which signal told the ringer a call is ringing: the headset's ringer bit, or the 3CX page's notification.</summary>
public enum RingerSignalSource { Headset, Notification }

/// <summary>A call started ringing (Start) or stopped, as reported by the Edge extension through the native host.</summary>
public readonly record struct RingSignal(bool Start, RingerSignalSource Source);

/// <summary>What one line on the buzz pipe asks for: a named alert (null = no name), a test of an explicit cue, or a ring signal.</summary>
public sealed record BuzzRequest(string? Name, HapticCue? TestCue, RingSignal? Ring = null);

/// <summary>
/// The local buzz pipe (<c>\\.\pipe\Slender.Buzz</c>, current user only, never a network port). A client writes one line: an
/// alert name, or nothing. Settings' Test button writes "!test pattern pulses spacingMs strength|- [repeats repeatGapMs]" to hear a cue as shown.
/// The 3CX ringer's native host writes "!ring start|stop hid|notification".
/// </summary>
public static class BuzzProtocol
{
    public const string PipeName = "Slender.Buzz";
    public const int MaxLineChars = 64;
    private const string TestPrefix = "!test ";
    private const string RingPrefix = "!ring ";

    /// <summary>Null when the line must be ignored (too long, or a malformed test line); any other text is an alert name.</summary>
    public static BuzzRequest? Parse(string? line)
    {
        line = (line ?? string.Empty).Trim();
        if (line.Length > MaxLineChars) return null;
        if (line.StartsWith(TestPrefix, StringComparison.Ordinal)) return ParseTest(line[TestPrefix.Length..]);
        if (line.StartsWith(RingPrefix, StringComparison.Ordinal)) return ParseRing(line[RingPrefix.Length..]);
        if (line.StartsWith('!')) return null;
        return new BuzzRequest(line.Length == 0 ? null : line, null);
    }

    public static string TestLine(HapticCue cue)
    {
        cue = cue.Clamped();
        string strength = cue.Strength is { } s ? s.ToString(CultureInfo.InvariantCulture) : "-";
        string line = $"{TestPrefix}{cue.Pattern} {cue.Pulses} {cue.SpacingMs} {strength}";
        return cue.Repeats > 1 ? $"{line} {cue.Repeats} {cue.RepeatGapMs}" : line;
    }

    /// <summary>"!ring start hid", "!ring stop notification": the 3CX ringer (docs/3cx-ringer.md).</summary>
    public static string RingLine(RingSignal signal) =>
        $"{RingPrefix}{(signal.Start ? "start" : "stop")} {(signal.Source == RingerSignalSource.Headset ? "hid" : "notification")}";

    private static BuzzRequest? ParseRing(string rest)
    {
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;
        bool start;
        if (parts[0] == "start") start = true;
        else if (parts[0] == "stop") start = false;
        else return null;
        RingerSignalSource source;
        if (parts[1] == "hid") source = RingerSignalSource.Headset;
        else if (parts[1] == "notification") source = RingerSignalSource.Notification;
        else return null;
        return new BuzzRequest(null, null, new RingSignal(start, source));
    }

    private static BuzzRequest? ParseTest(string rest)
    {
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (4 or 6)) return null;
        var inv = CultureInfo.InvariantCulture;
        if (!int.TryParse(parts[0], NumberStyles.None, inv, out int pattern)
            || !int.TryParse(parts[1], NumberStyles.None, inv, out int pulses)
            || !int.TryParse(parts[2], NumberStyles.None, inv, out int spacing)) return null;
        int? strength = null;
        if (parts[3] != "-")
        {
            if (!int.TryParse(parts[3], NumberStyles.None, inv, out int s)) return null;
            strength = s;
        }
        int repeats = 1, repeatGap = 1000;
        if (parts.Length == 6
            && (!int.TryParse(parts[4], NumberStyles.None, inv, out repeats) || !int.TryParse(parts[5], NumberStyles.None, inv, out repeatGap))) return null;
        return new BuzzRequest(null, new HapticCue(true, pattern, pulses, spacing, strength, repeats, repeatGap).Clamped());
    }

    /// <summary>Sends one line to the running tray. False when Slender is not running (or the pipe did not answer in time).</summary>
    public static bool TrySend(string line, int timeoutMs = 1000)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(timeoutMs);
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { return false; }
    }
}

/// <summary>At most N requests per rolling second; the rest are dropped (a loop must not turn the mouse into a buzzer).</summary>
public sealed class BuzzRateLimiter(int maxPerSecond = 5, Func<long>? clock = null)
{
    private readonly Queue<long> _stamps = new();
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private readonly object _gate = new();

    public bool TryAcquire()
    {
        lock (_gate)
        {
            long now = _clock();
            while (_stamps.Count > 0 && now - _stamps.Peek() >= 1000) _stamps.Dequeue();
            if (_stamps.Count >= maxPerSecond) return false;
            _stamps.Enqueue(now);
            return true;
        }
    }
}
