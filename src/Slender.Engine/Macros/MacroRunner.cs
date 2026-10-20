namespace Slender.Engine.Macros;

/// <summary>What a running macro sends. Abstracted so tests never inject input. Every method runs on the macro's own thread.</summary>
public interface IMacroInput
{
    bool KeyDown(KeyStroke key);
    bool KeyUp(KeyStroke key);
    /// <summary>Presses the keys in order and releases them in reverse (one chord).</summary>
    bool Chord(IReadOnlyList<KeyStroke> keys);
    bool Type(IReadOnlyList<TextKey> keys);
    bool MouseButton(string button, bool down);
    bool MoveBy(int dx, int dy);
    bool MoveTo(int x, int y);
    /// <summary>WHEEL_DELTA units (120 per notch); vertical positive = away from the user, horizontal positive = right.</summary>
    bool Wheel(int vertical, int horizontal);
    void Launch(string target, string? arguments);
    /// <summary>True while the user's Esc key is physically down (the macro's own Esc presses are filtered out by the runner).</summary>
    bool EscapeHeld();
}

/// <summary>Time for a macro: tests drive a virtual one.</summary>
public interface IMacroClock
{
    long NowMs { get; }
    void Sleep(int ms);
}

public sealed class SystemMacroClock : IMacroClock
{
    public long NowMs => Environment.TickCount64;
    public void Sleep(int ms) => Thread.Sleep(ms);
}

/// <summary>What the action executor talks to: a press and a release of a control bound to a macro.</summary>
public interface IMacroHost
{
    /// <summary>
    /// Pressed starts the macro, or ends it when it is already running. Released ends a WhileHeld macro once its current pass is
    /// done (so a click or a ring slot, which release at once, still run one full pass). Never blocks.
    /// </summary>
    void Handle(string macroId, bool pressed);

    /// <summary>Ends every running macro (they release what they hold).</summary>
    void StopAll();
}

public enum MacroEnd { Finished, Pressed, Escape, TimeCap, Failed, Shutdown }

/// <summary>
/// Runs macros, each on its own thread so the engine queue and the hooks are never held up by a pause. A macro ends when it is
/// finished, when its control is pressed again, when the user presses Esc, when its time cap passes (the safety stop), on a failed
/// send, or when the runner is disposed. However it ends, every key and mouse button it pressed is released.
/// </summary>
public sealed class MacroRunner : IMacroHost, IDisposable
{
    /// <summary>Most macros running at once.</summary>
    public const int MaxRunning = 4;
    /// <summary>Longest a single sleep lasts before the stop conditions are checked again.</summary>
    public const int SliceMs = 20;
    /// <summary>Gap between the chords of a Tap sequence, so the target app sees separate presses.</summary>
    public const int SequenceGapMs = 30;

    private const ushort VkEscape = 0x1B;

    private sealed class Session(MacroDef macro)
    {
        public MacroDef Macro { get; } = macro;
        public volatile bool StopRequested;
        public volatile bool Released;
    }

    private readonly IMacroInput _input;
    private readonly IMacroClock _clock;
    private readonly ILog _log;
    private readonly Action<Action> _spawn;
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _running = new();
    private bool _disposed;

    /// <summary>Finds a macro by id (the engine's current config). Set once the engine exists.</summary>
    public Func<string, MacroDef?>? Lookup { get; set; }

    /// <summary>Raised on the macro's thread when it ends, with why. For logs and tests.</summary>
    public event Action<string, MacroEnd>? Ended;

    public MacroRunner(IMacroInput input, IMacroClock? clock = null, ILog? log = null, Action<Action>? spawn = null)
    {
        _input = input;
        _clock = clock ?? new SystemMacroClock();
        _log = log ?? NullLog.Instance;
        _spawn = spawn ?? (work => new Thread(() => work()) { IsBackground = true, Name = "Slender.Macro" }.Start());
    }

    public int RunningCount { get { lock (_gate) return _running.Count; } }

    public bool IsRunning(string macroId) { lock (_gate) return _running.ContainsKey(macroId); }

    public void Handle(string macroId, bool pressed)
    {
        Session session;
        lock (_gate)
        {
            if (_disposed) return;
            _running.TryGetValue(macroId, out var existing);
            if (!pressed) { if (existing is not null) existing.Released = true; return; }
            if (existing is not null) { existing.StopRequested = true; return; } // a second press ends it
            MacroDef? macro = null;
            try { macro = Lookup?.Invoke(macroId); } catch (Exception ex) { _log.Error("macro lookup failed", ex); }
            if (macro is null) { _log.Error($"macro '{macroId}' not found"); return; }
            if (_running.Count >= MaxRunning) { _log.Info($"macro '{macro.Name}' not started: {MaxRunning} already running"); return; }
            session = new Session(macro);
            _running[macroId] = session;
        }
        try { _spawn(() => RunSession(macroId, session)); }
        catch (Exception ex)
        {
            _log.Error("macro thread failed to start", ex);
            lock (_gate) { _running.Remove(macroId); Monitor.PulseAll(_gate); }
        }
    }

    public void StopAll()
    {
        lock (_gate) foreach (var s in _running.Values) s.StopRequested = true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var s in _running.Values) s.StopRequested = true;
            var until = Environment.TickCount64 + 2000;
            while (_running.Count > 0 && Environment.TickCount64 < until) Monitor.Wait(_gate, 50); // let them release their keys
        }
    }

    private bool Disposed { get { lock (_gate) return _disposed; } }

    private void RunSession(string id, Session s)
    {
        var end = MacroEnd.Failed;
        try { end = new Pass(this, s).Run(); }
        catch (Exception ex) { _log.Error($"macro '{s.Macro.Name}' crashed", ex); }
        finally
        {
            lock (_gate) { _running.Remove(id); Monitor.PulseAll(_gate); }
        }
        _log.Info($"macro '{s.Macro.Name}' ended: {end}");
        try { Ended?.Invoke(id, end); } catch { }
    }

    /// <summary>One run of a macro: the passes, the stop checks and the bookkeeping of what is held down.</summary>
    private sealed class Pass(MacroRunner r, Session s)
    {
        private readonly List<KeyStroke> _keys = [];
        private readonly List<string> _buttons = [];
        private readonly long _start = r._clock.NowMs;
        private bool _escSeenUp, _skipEsc;
        private MacroEnd? _ended;

        public MacroEnd Run()
        {
            var macro = s.Macro;
            try
            {
                if (macro.Steps.Count == 0) return MacroEnd.Finished;
                while (true)
                {
                    foreach (var step in macro.Steps)
                    {
                        if (Check() is { } why) return why;
                        if (!Do(step)) return _ended ?? MacroEnd.Failed;
                    }
                    if (macro.Mode == MacroRunMode.Once || (macro.Mode == MacroRunMode.WhileHeld && s.Released)) return MacroEnd.Finished;
                    if (!Wait(MacroStep.PauseMinMs)) return _ended ?? MacroEnd.Failed; // never spin: at least the shortest pause between passes
                }
            }
            finally { ReleaseAll(); }
        }

        /// <summary>The reason to stop now, or null.</summary>
        private MacroEnd? Check()
        {
            if (_ended is { } e) return e;
            if (s.StopRequested) return _ended = r.Disposed ? MacroEnd.Shutdown : MacroEnd.Pressed;
            if (r._clock.NowMs - _start >= s.Macro.CapSeconds * 1000L) return _ended = MacroEnd.TimeCap;
            if (!r._input.EscapeHeld()) { _escSeenUp = true; _skipEsc = false; }
            // Esc counts only after it was seen up (a macro started from Esc itself is not stopped by that press) and never
            // while the macro's own Esc is down or was just sent.
            else if (_escSeenUp && !_skipEsc && !_keys.Any(k => k.Vk == VkEscape)) return _ended = MacroEnd.Escape;
            return null;
        }

        private bool Wait(int ms)
        {
            var end = r._clock.NowMs + ms;
            while (true)
            {
                if (Check() is not null) return false;
                var left = end - r._clock.NowMs;
                if (left <= 0) return true;
                r._clock.Sleep((int)Math.Min(left, SliceMs));
            }
        }

        private bool Sent(bool ok, string what)
        {
            if (ok) return true;
            r._log.Error($"macro '{s.Macro.Name}': sending {what} failed");
            _ended = MacroEnd.Failed;
            return false;
        }

        private bool Do(MacroStep step)
        {
            var input = r._input;
            switch (step.Kind)
            {
                case MacroStepKind.Pause:
                    return Wait(step.Ms);
                case MacroStepKind.KeyDown:
                    if (!ChordParser.TryParseKey(step.Text, out var down)) return true;
                    if (!Sent(input.KeyDown(down), "a key")) return false;
                    _keys.Add(down);
                    return true;
                case MacroStepKind.KeyUp:
                    if (!ChordParser.TryParseKey(step.Text, out var up)) return true;
                    var ok = input.KeyUp(up);
                    _keys.Remove(up);
                    return Sent(ok, "a key");
                case MacroStepKind.Tap:
                    if (!ChordParser.TryParseSequence(step.Text, out var chords)) return true;
                    for (var i = 0; i < chords.Count; i++)
                    {
                        if (i > 0 && !Wait(SequenceGapMs)) return false;
                        if (chords[i].Any(k => k.Vk == VkEscape)) _skipEsc = true;
                        if (!Sent(input.Chord(chords[i]), "a shortcut")) return false;
                    }
                    return true;
                case MacroStepKind.Type:
                    return step.Text is not { Length: > 0 } t || Sent(input.Type(TextInput.Build(t)), "text");
                case MacroStepKind.MouseDown:
                    if (step.Text is null) return true;
                    if (!Sent(input.MouseButton(step.Text, true), "a mouse button")) return false;
                    _buttons.Add(step.Text);
                    return true;
                case MacroStepKind.MouseUp:
                    if (step.Text is null) return true;
                    var released = input.MouseButton(step.Text, false);
                    _buttons.Remove(step.Text);
                    return Sent(released, "a mouse button");
                case MacroStepKind.Click:
                    if (step.Text is null) return true;
                    if (!Sent(input.MouseButton(step.Text, true), "a click")) return false;
                    return Sent(input.MouseButton(step.Text, false), "a click");
                case MacroStepKind.Move:
                    return Sent(step.Absolute ? input.MoveTo(step.X, step.Y) : input.MoveBy(step.X, step.Y), "a mouse move");
                case MacroStepKind.Wheel:
                    return Sent(input.Wheel(step.Y * 120, step.X * 120), "the wheel");
                case MacroStepKind.Launch:
                    if (step.Text is not null) input.Launch(step.Text, step.Arguments);
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>Lifts every key and button this run still holds, last pressed first. Never throws.</summary>
        private void ReleaseAll()
        {
            for (var i = _keys.Count - 1; i >= 0; i--) { try { r._input.KeyUp(_keys[i]); } catch { } }
            for (var i = _buttons.Count - 1; i >= 0; i--) { try { r._input.MouseButton(_buttons[i], false); } catch { } }
            _keys.Clear();
            _buttons.Clear();
        }
    }
}
