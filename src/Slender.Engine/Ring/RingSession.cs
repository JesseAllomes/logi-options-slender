namespace Slender.Engine.Ring;

public enum RingHaptic { Tick, Bump }

public enum RingCloseReason { Cancelled, Ran, Escape, Idle, FocusLost, DeviceLost, Toggle, Shutdown }

public enum RingKey { Up, Down, Left, Right, Enter, Escape, Back }

public enum RingPhase
{
    Closed,

    /// <summary>The trigger is still down: release on a slot runs it, release in the centre cancels.</summary>
    Holding,

    /// <summary>Open after a tap (or after a folder opened): hover a slot and left-click.</summary>
    Open,
}

/// <summary>
/// One open Actions Ring: the tap/hold state machine, slot selection, folder navigation and idle timeout. No windows,
/// no clocks and no threads: the caller feeds events with a millisecond timestamp and reacts to the events raised.
/// Pointer offsets are in device-independent pixels from the ring centre. Not thread-safe (one UI thread).
/// </summary>
public sealed class RingSession
{
    /// <summary>A press shorter than this, without having pointed at a slot, is a tap (the ring stays open).</summary>
    public const int TapMaxMs = 250;

    public const int IdleTimeoutMs = 30_000;

    private readonly RingMetrics _metrics;
    private readonly IReadOnlyList<RingSlot> _root;
    private readonly long _pressAt;
    private long _lastInput;
    private RingSlot? _folder;
    private int? _pointerSlot;
    private bool _hasPointer, _sawSlot;
    private int _wheelRemainder, _thumbRemainder;

    /// <summary>Opens with the trigger held (the normal case: the ring opens on press).</summary>
    public RingSession(IReadOnlyList<RingSlot> slots, RingMetrics metrics, long now)
    {
        _root = RingSource.Normalize(slots);
        Slots = _root;
        _metrics = metrics;
        _pressAt = now;
        _lastInput = now;
        _wheelRemainder = 0;
        _thumbRemainder = 0;
        Phase = RingPhase.Holding;
    }

    public RingPhase Phase { get; private set; }

    public bool IsOpen => Phase != RingPhase.Closed;

    /// <summary>The slots on screen: the root ring or the open folder's children.</summary>
    public IReadOnlyList<RingSlot> Slots { get; private set; }

    public int? Selected { get; private set; }

    public bool InFolder => _folder is not null;

    /// <summary>The open folder's display name, or null at the root.</summary>
    public string? FolderName => _folder is null ? null : _folder.Name is { Length: > 0 } n ? n : RingDefaults.FolderName;

    /// <summary>Selection, slots or folder changed: redraw.</summary>
    public event EventHandler? Changed;

    public event EventHandler<RingHaptic>? Haptic;

    /// <summary>The ring is closed (raised before <see cref="ActionRequested"/> when a slot ran).</summary>
    public event EventHandler<RingCloseReason>? Closed;

    /// <summary>A slot ran; the ring is already closed. Press then release the action.</summary>
    public event EventHandler<ActionSpec>? ActionRequested;

    /// <summary>An adjustable slot's wheel step; the ring stays open.</summary>
    public event EventHandler<ActionSpec>? Adjust;

    /// <summary>The trigger control went down or up.</summary>
    public void OnTrigger(bool pressed, long now)
    {
        if (!IsOpen) return;
        _lastInput = now;
        if (pressed)
        {
            if (Phase == RingPhase.Open) Close(RingCloseReason.Toggle); // press again while open closes it
            return;
        }
        if (Phase != RingPhase.Holding) return;
        if (now - _pressAt < TapMaxMs && !_sawSlot)
        {
            Phase = RingPhase.Open; // tap: stay open for hover + click
            Raise();
            return;
        }
        // Hold: release on a slot the pointer moved to runs it; release without movement, or in the centre, cancels.
        if (_sawSlot && Selected is { } s) Activate(s);
        else Close(RingCloseReason.Cancelled);
    }

    /// <summary>The pointer moved (or was sampled): dx, dy from the ring centre. The first sample only sets the baseline.</summary>
    public void OnPointer(double dx, double dy, long now)
    {
        if (!IsOpen) return;
        var slot = RingLayout.SlotAt(dx, dy, _metrics);
        var first = !_hasPointer;
        _hasPointer = true;
        if (slot == _pointerSlot && !first) return;
        _pointerSlot = slot;
        _lastInput = now;
        if (!first && slot is not null) _sawSlot = true;
        SetSelected(slot, tick: !first);
    }

    /// <summary>A mouse click while the ring is open (left or right) at dx, dy from the ring centre.</summary>
    public void OnClick(bool left, double dx, double dy, long now)
    {
        if (!IsOpen) return;
        _lastInput = now;
        if (!left) { Close(RingCloseReason.Cancelled); return; }
        if (Math.Sqrt(dx * dx + dy * dy) > _metrics.Extent) { Close(RingCloseReason.Cancelled); return; }
        if (RingLayout.SlotAt(dx, dy, _metrics) is { } slot) Activate(slot);
        else if (InFolder) GoBack();
        else Close(RingCloseReason.Cancelled);
    }

    public void OnKey(RingKey key, long now)
    {
        if (!IsOpen) return;
        _lastInput = now;
        switch (key)
        {
            case RingKey.Escape:
                Close(RingCloseReason.Escape);
                break;
            case RingKey.Back:
                if (InFolder) GoBack();
                break;
            case RingKey.Enter:
                if (Selected is { } s) Activate(s);
                break;
            case RingKey.Right or RingKey.Down or RingKey.Left or RingKey.Up:
                SetSelected(Step(key), tick: true);
                break;
        }
    }

    /// <summary>
    /// Mouse wheel over the ring: repeats an adjustable slot (up = first action), otherwise nothing.
    /// Deltas accumulate to whole 120-unit notches so hi-res wheels step once per notch.
    /// </summary>
    public void OnWheel(int delta, long now)
    {
        if (!IsOpen) return;
        _lastInput = now;
        if (Selected is not { } s || Slots[s].IsFolder || RingWheel.TryPair(Slots[s].Action) is not { } pair) return;
        _wheelRemainder += delta;
        var notches = Math.Clamp(Math.Abs(_wheelRemainder) / 120, 0, 10);
        if (notches == 0) return;
        var action = _wheelRemainder > 0 ? pair.Up : pair.Down;
        _wheelRemainder -= Math.Sign(_wheelRemainder) * notches * 120;
        for (var i = 0; i < notches && IsOpen; i++) Adjust?.Invoke(this, action);
    }

    /// <summary>
    /// Thumb wheel over the ring: each 120-unit step moves the highlight one slot (forward = clockwise), with the hover tick.
    /// Counts as a deliberate selection, so releasing a held trigger runs the highlighted slot.
    /// </summary>
    public void OnThumbWheel(int delta, long now)
    {
        if (!IsOpen) return;
        _lastInput = now;
        _thumbRemainder += delta;
        var steps = Math.Clamp(Math.Abs(_thumbRemainder) / 120, 0, 10);
        if (steps == 0) return;
        var dir = Math.Sign(_thumbRemainder);
        _thumbRemainder -= dir * steps * 120;
        var n = Profile.RingSlotCount;
        for (var i = 0; i < steps; i++)
        {
            var next = Selected is { } s ? ((s + dir) % n + n) % n : dir > 0 ? 0 : n - 1;
            _sawSlot = true;
            SetSelected(next, tick: true);
        }
    }

    /// <summary>Call periodically while open; closes after <see cref="IdleTimeoutMs"/> without input.</summary>
    public void Tick(long now)
    {
        if (IsOpen && now - _lastInput >= IdleTimeoutMs) Close(RingCloseReason.Idle);
    }

    /// <summary>Closes from outside (focus lost, device gone, shutdown). No-op when already closed.</summary>
    public void Close(RingCloseReason reason)
    {
        if (!IsOpen) return;
        Phase = RingPhase.Closed;
        Closed?.Invoke(this, reason);
    }

    private int? Step(RingKey key)
    {
        if (Selected is not { } s)
            return key switch { RingKey.Up => 0, RingKey.Right => 2, RingKey.Down => 4, _ => 6 };
        var n = Profile.RingSlotCount;
        return key is RingKey.Right or RingKey.Down ? (s + 1) % n : (s + n - 1) % n;
    }

    private void Activate(int slot)
    {
        var item = Slots[slot];
        if (item.IsEmpty || (item.IsFolder && InFolder))
        {
            Close(RingCloseReason.Cancelled);
            return;
        }
        if (item.IsFolder)
        {
            _folder = item;
            Slots = RingSource.Normalize(item.Folder);
            _thumbRemainder = 0;
            Phase = RingPhase.Open; // a folder opened by a hold-release stays open for hover + click
            Haptic?.Invoke(this, RingHaptic.Bump);
            Raise();
            return;
        }
        Haptic?.Invoke(this, RingHaptic.Bump);
        Close(RingCloseReason.Ran);
        ActionRequested?.Invoke(this, item.Action);
    }

    private void GoBack()
    {
        _folder = null;
        Slots = _root;
        _thumbRemainder = 0;
        Raise();
    }

    private void SetSelected(int? slot, bool tick)
    {
        if (Selected == slot) return;
        Selected = slot;
        _wheelRemainder = 0;
        if (tick && slot is { } s && !Slots[s].IsEmpty) Haptic?.Invoke(this, RingHaptic.Tick);
        Raise();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Waveforms for the ring (docs/protocol.md; Options+ uses 4 for hover and 1 for run).</summary>
public static class RingHaptics
{
    public const byte HoverWaveform = 4;
    public const byte RunWaveform = 1;
}
