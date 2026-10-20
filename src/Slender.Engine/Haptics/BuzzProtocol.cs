using System.Globalization;
using System.IO.Pipes;
using System.Text;

namespace Slender.Engine;

/// <summary>What one line on the buzz pipe asks for: a named alert (null = no name) or a test of an explicit cue.</summary>
public sealed record BuzzRequest(string? Name, HapticCue? TestCue);

/// <summary>
/// The local buzz pipe (<c>\\.\pipe\Slender.Buzz</c>, current user only, never a network port). A client writes one line: an
/// alert name, or nothing. Settings' Test button writes "!test pattern pulses spacingMs strength|-" to hear a cue as shown.
/// </summary>
public static class BuzzProtocol
{
    public const string PipeName = "Slender.Buzz";
    public const int MaxLineChars = 64;
    private const string TestPrefix = "!test ";

    /// <summary>Null when the line must be ignored (too long, or a malformed test line); any other text is an alert name.</summary>
    public static BuzzRequest? Parse(string? line)
    {
        line = (line ?? string.Empty).Trim();
        if (line.Length > MaxLineChars) return null;
        if (line.StartsWith(TestPrefix, StringComparison.Ordinal)) return ParseTest(line[TestPrefix.Length..]);
        if (line.StartsWith('!')) return null;
        return new BuzzRequest(line.Length == 0 ? null : line, null);
    }

    public static string TestLine(HapticCue cue)
    {
        cue = cue.Clamped();
        string strength = cue.Strength is { } s ? s.ToString(CultureInfo.InvariantCulture) : "-";
        return $"{TestPrefix}{cue.Pattern} {cue.Pulses} {cue.SpacingMs} {strength}";
    }

    private static BuzzRequest? ParseTest(string rest)
    {
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return null;
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
        return new BuzzRequest(null, new HapticCue(true, pattern, pulses, spacing, strength).Clamped());
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
