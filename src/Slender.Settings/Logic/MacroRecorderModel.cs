using Slender.Engine;
using Slender.Engine.Macros;

namespace Slender.Settings.Logic;

/// <summary>
/// Turns what the user does on the editor's recording pad (keys and mouse, with the time of each) into macro steps, with the
/// real gaps as pauses. Pure: it only receives events, it never reads or sends input. The UI feeds it while recording.
/// Mouse moves are relative (the pad is inside Settings, so screen positions mean nothing at replay).
/// </summary>
public sealed class MacroRecorderModel(bool recordMoves = true)
{
    /// <summary>Gaps shorter than this are not worth a pause step.</summary>
    public const int MinGapMs = 25;

    /// <summary>A key or button released this soon after it went down, with nothing in between, becomes one Tap or Click.</summary>
    public const int TapMaxMs = 400;

    /// <summary>Pointer moves are sampled at most every <see cref="MoveSampleMs"/>, and only past <see cref="MoveMinPx"/>.</summary>
    public const int MoveSampleMs = 40, MoveMinPx = 2;

    private enum Raw { KeyDown, KeyUp, MouseDown, MouseUp, Move, Wheel }

    private sealed record Event(Raw Kind, long At, string? Name = null, int X = 0, int Y = 0);

    private readonly List<Event> _events = [];
    private readonly HashSet<string> _keysDown = [];
    private readonly HashSet<string> _buttonsDown = [];
    private (int X, int Y)? _lastPointer, _lastEmitted;
    private long _lastMoveAt = long.MinValue / 2;

    /// <summary>Key presses dropped because the engine has no name for the key.</summary>
    public int Skipped { get; private set; }

    public bool IsRunning { get; private set; }

    /// <summary>Events recorded so far (before pauses are worked out).</summary>
    public int EventCount => _events.Count;

    /// <summary>True once recording is at <see cref="MacroDef.MaxSteps"/> events; the UI stops there.</summary>
    public bool Full => _events.Count >= MacroDef.MaxSteps;

    public void Start()
    {
        _events.Clear();
        _keysDown.Clear();
        _buttonsDown.Clear();
        _lastPointer = _lastEmitted = null;
        _lastMoveAt = long.MinValue / 2;
        Skipped = 0;
        IsRunning = true;
    }

    /// <summary>The engine's name for a virtual key, modifiers included; null for keys it cannot send.</summary>
    public static string? KeyNameFor(ushort vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => "Shift",
        0x11 or 0xA2 or 0xA3 => "Ctrl",
        0x12 or 0xA4 or 0xA5 => "Alt",
        0x5B or 0x5C => "Win",
        _ => ChordRecorder.KeyName(vk),
    };

    /// <summary>A key went down or up. Auto-repeat (a key already down) is ignored.</summary>
    public void Key(long nowMs, ushort vk, bool down)
    {
        if (!IsRunning || Full)
        {
            return;
        }

        if (KeyNameFor(vk) is not { } name)
        {
            if (down)
            {
                Skipped++;
            }

            return;
        }

        if (down ? !_keysDown.Add(name) : !_keysDown.Remove(name))
        {
            return;
        }

        _events.Add(new Event(down ? Raw.KeyDown : Raw.KeyUp, nowMs, name));
    }

    /// <summary>A mouse button ("Left", "Right", "Middle", "Back", "Forward") went down or up with the pointer at (x, y).</summary>
    public void Button(long nowMs, string button, bool down, int x, int y)
    {
        if (!IsRunning || Full || MacroStep.CanonicalButton(button) is not { } name)
        {
            return;
        }

        Pointer(nowMs, x, y, force: true);
        if (down ? !_buttonsDown.Add(name) : !_buttonsDown.Remove(name))
        {
            return;
        }

        _events.Add(new Event(down ? Raw.MouseDown : Raw.MouseUp, nowMs, name));
    }

    /// <summary>The wheel turned; notches positive = up / right.</summary>
    public void Wheel(long nowMs, int vertical, int horizontal)
    {
        if (!IsRunning || Full || (vertical == 0 && horizontal == 0))
        {
            return;
        }

        _events.Add(new Event(Raw.Wheel, nowMs, X: horizontal, Y: vertical));
    }

    /// <summary>The pointer is at (x, y) on the pad. Recorded as a relative move from the last recorded position.</summary>
    public void Pointer(long nowMs, int x, int y) => Pointer(nowMs, x, y, force: false);

    private void Pointer(long nowMs, int x, int y, bool force)
    {
        if (!IsRunning || !recordMoves || Full)
        {
            return;
        }

        _lastPointer = (x, y);
        if (_lastEmitted is not { } from)
        {
            _lastEmitted = (x, y); // the first position is only the starting point
            return;
        }

        if (!force && nowMs - _lastMoveAt < MoveSampleMs)
        {
            return;
        }

        int dx = x - from.X, dy = y - from.Y;
        if (Math.Abs(dx) < MoveMinPx && Math.Abs(dy) < MoveMinPx)
        {
            return;
        }

        _lastEmitted = (x, y);
        _lastMoveAt = nowMs;
        _events.Add(new Event(Raw.Move, nowMs, X: dx, Y: dy));
    }

    /// <summary>Ends recording and returns the steps: pauses for the real gaps, quick down/up pairs folded into taps and clicks.</summary>
    public IReadOnlyList<MacroStep> Stop(long nowMs)
    {
        if (!IsRunning)
        {
            return [];
        }

        if (_lastPointer is { } p)
        {
            Pointer(nowMs, p.X, p.Y, force: true); // the last bit of travel
        }

        IsRunning = false;
        var steps = new List<MacroStep>();
        long prev = 0;
        for (int i = 0; i < _events.Count; i++)
        {
            Event e = _events[i];
            if (i > 0 && e.At - prev >= MinGapMs)
            {
                steps.Add(MacroStep.Pause((int)Math.Min(e.At - prev, MacroStep.PauseMaxMs)));
            }

            int? up = e.Kind switch
            {
                Raw.KeyDown when !IsModifier(e.Name!) => PairedUp(i, Raw.KeyUp),
                Raw.MouseDown => PairedUp(i, Raw.MouseUp),
                _ => null,
            };
            if (up is { } u)
            {
                steps.Add(e.Kind == Raw.KeyDown ? MacroStep.Tap(e.Name!) : MacroStep.Click(e.Name!));
                prev = _events[u].At;
                i = u;
                continue;
            }

            steps.Add(e.Kind switch
            {
                Raw.KeyDown => MacroStep.KeyDown(e.Name!),
                Raw.KeyUp => MacroStep.KeyUp(e.Name!),
                Raw.MouseDown => MacroStep.MouseDown(e.Name!),
                Raw.MouseUp => MacroStep.MouseUp(e.Name!),
                Raw.Move => MacroStep.MoveBy(e.X, e.Y),
                _ => MacroStep.Wheel(e.Y, e.X),
            });
            prev = e.At;
        }

        return steps.Take(MacroDef.MaxSteps).ToList();
    }

    /// <summary>The index of the matching up event when it comes straight after event <paramref name="i"/> and soon enough; else null.</summary>
    private int? PairedUp(int i, Raw upKind)
    {
        if (i + 1 >= _events.Count)
        {
            return null;
        }

        Event next = _events[i + 1];
        return next.Kind == upKind && next.Name == _events[i].Name && next.At - _events[i].At <= TapMaxMs ? i + 1 : null;
    }

    private static bool IsModifier(string name) => name is "Shift" or "Ctrl" or "Alt" or "Win";
}
