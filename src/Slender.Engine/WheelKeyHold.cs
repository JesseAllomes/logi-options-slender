namespace Slender.Engine;

/// <summary>One key going down or up.</summary>
public readonly record struct KeyEvent(KeyStroke Key, bool Up);

/// <summary>
/// Thumb wheel key steps with the shared modifiers held. For a pair like Ctrl+Tab / Ctrl+Shift+Tab, Ctrl goes down at the
/// first step and stays down while the wheel keeps turning; each step then sends only the rest (Tab, or Shift+Tab), so a
/// burst reads to the app like a person holding Ctrl and tapping Tab. <see cref="Release"/> lets the modifiers go; the
/// engine calls it once the wheel is quiet, or at once on anything that must not see a held modifier (a button, a mode
/// flip, an app switch). Builds events only; the caller sends them. Used on the engine queue only.
/// </summary>
public sealed class WheelKeyHold
{
    private readonly List<KeyStroke> _held = [];

    /// <summary>Modifiers down now, in press order.</summary>
    public IReadOnlyList<KeyStroke> Held => _held;

    public bool IsHolding => _held.Count > 0;

    /// <summary>
    /// The modifiers to hold for a wheel pair: those both chords share (just the forward chord's when there is no back).
    /// Null when a chord is not a single plain chord (a sequence, unknown key) or nothing is shared; the caller then sends
    /// whole chords per step as before.
    /// </summary>
    public static IReadOnlyList<KeyStroke>? SharedModifiers(string forward, string? back)
    {
        if (!TryPlain(forward, out var f)) return null;
        IReadOnlyList<KeyStroke> shared = Modifiers(f);
        if (back is not null)
        {
            if (!TryPlain(back, out var b)) return null;
            var other = Modifiers(b);
            shared = [.. shared.Where(other.Contains)];
        }
        return shared.Count == 0 ? null : shared;
    }

    /// <summary>A chord the hold path can send: one step (no ',' sequence, no '|' pair) that ChordParser accepts.</summary>
    public static bool TryPlain(string chord, out IReadOnlyList<KeyStroke> keys)
    {
        keys = [];
        return chord.IndexOf(',') < 0 && chord.IndexOf('|') < 0 && ChordParser.TryParse(chord, out keys);
    }

    /// <summary>
    /// Events for <paramref name="count"/> presses of <paramref name="chord"/> with <paramref name="shared"/> held: modifiers no
    /// longer shared go up, newly shared ones go down, then per press the chord's other modifiers and its keys go down and up.
    /// </summary>
    public IReadOnlyList<KeyEvent> Step(IReadOnlyList<KeyStroke> shared, IReadOnlyList<KeyStroke> chord, int count)
    {
        var events = new List<KeyEvent>();
        for (var i = _held.Count - 1; i >= 0; i--)
        {
            if (shared.Contains(_held[i])) continue;
            events.Add(new KeyEvent(_held[i], true));
            _held.RemoveAt(i);
        }
        foreach (var m in shared)
        {
            if (_held.Contains(m)) continue;
            events.Add(new KeyEvent(m, false));
            _held.Add(m);
        }

        var rest = chord.Where(k => !_held.Contains(k)).ToList(); // modifiers first (ChordParser order), then the keys
        for (var n = Math.Max(0, count); n > 0; n--)
        {
            foreach (var k in rest) events.Add(new KeyEvent(k, false));
            for (var i = rest.Count - 1; i >= 0; i--) events.Add(new KeyEvent(rest[i], true));
        }
        return events;
    }

    /// <summary>Lets every held modifier go (reverse press order). Empty when nothing is held.</summary>
    public IReadOnlyList<KeyEvent> Release()
    {
        var events = new List<KeyEvent>(_held.Count);
        for (var i = _held.Count - 1; i >= 0; i--) events.Add(new KeyEvent(_held[i], true));
        _held.Clear();
        return events;
    }

    private static List<KeyStroke> Modifiers(IReadOnlyList<KeyStroke> keys) => [.. keys.Where(k => ChordParser.IsModifierKey(k.Vk))];
}
