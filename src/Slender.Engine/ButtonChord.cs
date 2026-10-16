namespace Slender.Engine;

public enum ChordButton { Left, Right }

/// <summary>
/// Left + Right pressed together is a chord (Tab scroll: the thumb wheel switches tabs while both are held). Same idea as X11's
/// Emulate3Buttons: a left or right press is held back for up to <see cref="WindowMs"/> to see whether the other button follows.
/// <list type="bullet">
/// <item>The other button comes within the window: chord. Neither press nor either release ever reaches an app.</item>
/// <item>The button is released first (a quick click): the whole click is replayed then, down and up in order.</item>
/// <item>The window ends, or the pointer moves (a drag starting): the press is replayed and the button behaves normally.</item>
/// </list>
/// A press made while the other button is already held normally is never a chord (holding one, then pressing the other later).
/// Only real (non-injected) input should be fed in; the replays are injected, so the hook ignores them. Thread-safe.
/// </summary>
public sealed class ButtonChord
{
    public const int WindowMs = 50;
    public const int MoveSlop = 4;

    private readonly object _gate = new();
    private readonly IHoldTimer _timer;
    private readonly Action<ChordButton, bool> _send;
    private ChordButton? _pending;
    private int _px, _py;
    private IDisposable? _flush;
    private bool _active;
    private bool _chordLeft, _chordRight;   // while active: which chord buttons are still held
    private bool _passLeft, _passRight;     // pressed normally: their release must reach the app
    private volatile bool _enabled = true;

    private readonly Func<int, int, bool>? _canHoldBack;

    /// <param name="send">Injects a left/right press or release (the replay).</param>
    /// <param name="canHoldBack">
    /// Whether a press at (x, y) may be held back: false where a replay could not be delivered (an elevated window blocks injected
    /// input), so the press goes straight through there and that click is never a chord.
    /// </param>
    public ButtonChord(Action<ChordButton, bool> send, Func<int, int, bool>? canHoldBack = null)
        : this(send, new ThreadingHoldTimer(), canHoldBack) { }

    internal ButtonChord(Action<ChordButton, bool> send, IHoldTimer timer, Func<int, int, bool>? canHoldBack = null)
    {
        _send = send;
        _timer = timer;
        _canHoldBack = canHoldBack;
    }

    /// <summary>Off: every press passes straight through (a chord or wait already under way still finishes cleanly).</summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>Both buttons are held as a chord.</summary>
    public bool IsActive { get { lock (_gate) return _active; } }

    /// <summary>A press is being held back, waiting for the other button.</summary>
    public bool IsPending { get { lock (_gate) return _pending is not null; } }

    /// <summary>The chord started (true) or ended (false). Raised outside the lock, on the caller's thread.</summary>
    public event EventHandler<bool>? Changed;

    /// <summary>A real left/right press or release at screen point (x, y). Returns true to swallow it.</summary>
    public bool OnButton(ChordButton button, bool down, int x, int y)
    {
        var replay = new List<(ChordButton, bool)>(2);
        bool? changed = null;
        bool swallow;
        lock (_gate)
        {
            if (_active)
            {
                if (button == ChordButton.Left) _chordLeft = down; else _chordRight = down;
                swallow = true;
                if (!_chordLeft && !_chordRight)
                {
                    _active = false;
                    changed = false;
                }
            }
            else if (_pending is { } p)
            {
                if (down && button != p)
                {
                    CancelFlush();
                    _pending = null;
                    _active = true;
                    _chordLeft = _chordRight = true;
                    changed = true;
                    swallow = true;
                }
                else if (!down && button == p)
                {
                    // A quick click: it never reached the app, so replay all of it now, in order.
                    CancelFlush();
                    _pending = null;
                    replay.Add((p, true));
                    replay.Add((p, false));
                    swallow = true;
                }
                else
                {
                    swallow = false;
                }
            }
            else if (down && _enabled && !_passLeft && !_passRight && (_canHoldBack?.Invoke(x, y) ?? true))
            {
                _pending = button;
                _px = x;
                _py = y;
                _flush = _timer.Schedule(WindowMs, Flush);
                swallow = true;
            }
            else
            {
                if (button == ChordButton.Left) _passLeft = down; else _passRight = down;
                swallow = false;
            }
        }

        Send(replay);
        if (changed is { } c) Raise(c);
        return swallow;
    }

    /// <summary>The pointer moved. A held-back press is replayed at once (a drag is starting). Never swallows.</summary>
    public void OnMove(int x, int y)
    {
        bool flush;
        lock (_gate) flush = _pending is not null && (Math.Abs(x - _px) > MoveSlop || Math.Abs(y - _py) > MoveSlop);
        if (flush) Flush();
    }

    /// <summary>The window ended (or a drag began): replay the held-back press; its release will pass normally.</summary>
    private void Flush()
    {
        ChordButton b;
        lock (_gate)
        {
            if (_pending is not { } p) return;
            CancelFlush();
            _pending = null;
            if (p == ChordButton.Left) _passLeft = true; else _passRight = true;
            b = p;
        }
        Send([(b, true)]);
    }

    private void CancelFlush()
    {
        _flush?.Dispose();
        _flush = null;
    }

    private void Send(List<(ChordButton Button, bool Down)> events)
    {
        foreach (var (button, down) in events)
        {
            try { _send(button, down); }
            catch { } // never throw into the hook
        }
    }

    private void Raise(bool active)
    {
        try { Changed?.Invoke(this, active); }
        catch { }
    }
}
