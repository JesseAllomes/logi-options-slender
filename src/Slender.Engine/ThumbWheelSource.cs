using Slender.Hidpp;

namespace Slender.Engine;

/// <summary>One thumb wheel rotation. Delta is in 1/120 notch units, positive = right (the WM_MOUSEHWHEEL convention).</summary>
public sealed class ThumbWheelRotatedEventArgs(int delta, bool fromHid) : EventArgs
{
    public int Delta { get; } = delta;
    /// <summary>True when the wheel is diverted and the rotation came from HID++; false = the OS mouse hook (native scroll).</summary>
    public bool FromHid { get; } = fromHid;
    /// <summary>Set by the consumer: swallow the native horizontal scroll. Only meaningful on the hook path.</summary>
    public bool Handled { get; set; }
}

/// <summary>Where thumb wheel rotation comes from. The hook and HID++ implementations are interchangeable for the engine.</summary>
public interface IThumbWheelSource : IDisposable
{
    event EventHandler<ThumbWheelRotatedEventArgs>? ThumbWheelRotated;
}

/// <summary>Native path: WM_MOUSEHWHEEL from the low-level hook (120 per notch, hi-res smaller).</summary>
internal sealed class HookThumbWheelSource : IThumbWheelSource
{
    private readonly IInputHook _hook;

    public HookThumbWheelSource(IInputHook hook)
    {
        _hook = hook;
        _hook.HorizontalWheel += OnWheel;
    }

    public event EventHandler<ThumbWheelRotatedEventArgs>? ThumbWheelRotated;

    private void OnWheel(object? sender, HookWheelEventArgs e)
    {
        var args = new ThumbWheelRotatedEventArgs(e.Delta, fromHid: false);
        ThumbWheelRotated?.Invoke(this, args);
        if (args.Handled) e.Handled = true;
    }

    public void Dispose() => _hook.HorizontalWheel -= OnWheel;
}

/// <summary>Diverted path: 0x2150 rotation events. divertedResolution is 120 counts per notch, so counts are already notch units.</summary>
internal sealed class HidThumbWheelSource : IThumbWheelSource
{
    private readonly ILogiMouse _mouse;

    public HidThumbWheelSource(ILogiMouse mouse)
    {
        _mouse = mouse;
        _mouse.ThumbWheelMoved += OnMoved;
    }

    public event EventHandler<ThumbWheelRotatedEventArgs>? ThumbWheelRotated;

    private void OnMoved(object? sender, ThumbWheelEvent e)
    {
        if (e.Rotation == 0) return;
        ThumbWheelRotated?.Invoke(this, new ThumbWheelRotatedEventArgs(e.Rotation * 120 / ThumbWheelUnits.PerNotch, fromHid: true));
    }

    public void Dispose() => _mouse.ThumbWheelMoved -= OnMoved;
}

/// <summary>What one thumb wheel report did to the touch state: Down/Up edges and a finished tap.</summary>
public readonly record struct ThumbTouchResult(bool Down, bool Up, bool Tap);

/// <summary>
/// Touch and tap from diverted reports. A tap is touch then release within <see cref="TapMaxMs"/> with less than a quarter
/// notch of rotation (summed absolute counts) in between. Thread-safe.
/// </summary>
public sealed class ThumbTouchTracker
{
    public const long TapMaxMs = 250;
    public const int TapMaxRotation = ThumbWheelUnits.PerNotch / 4;
    private readonly object _gate = new();
    private bool _touching;
    private long _downAt;
    private int _rotation;

    public bool Touching { get { lock (_gate) return _touching; } }

    public ThumbTouchResult OnReport(bool touch, int rotation, long nowMs)
    {
        lock (_gate)
        {
            bool down = false, up = false, tap = false;
            if (touch && !_touching) { _touching = true; _downAt = nowMs; _rotation = 0; down = true; }
            if (_touching) _rotation += Math.Abs(rotation);
            if (!touch && _touching)
            {
                _touching = false;
                up = true;
                tap = nowMs - _downAt <= TapMaxMs && _rotation < TapMaxRotation;
            }
            return new ThumbTouchResult(down, up, tap);
        }
    }

    /// <summary>The device went away: forget a touch in progress. Returns whether one was.</summary>
    public bool Reset()
    {
        lock (_gate) { var was = _touching; _touching = false; _rotation = 0; return was; }
    }
}

/// <summary>When the thumb wheel must be in diverted (HID++) reporting instead of native.</summary>
public static class ThumbWheelDivertPolicy
{
    /// <summary>
    /// True when the foreground app needs non-native thumb wheel behaviour: the Actions Ring is open, the dual wheel is flipped to
    /// its second role, the app's effective thumb wheel binding is set (keys or off), Excel column scrolling applies, or a touch / tap
    /// binding is set (and the device reports touch support; unknown counts as supported).
    /// </summary>
    public static bool Wanted(SlenderConfig config, string? process, bool secondActive, bool ringOpen, bool? touchSupported,
        bool excelColumns = false)
    {
        if (ringOpen || secondActive) return true;
        if (SlenderEngine.EffectiveBinding(config, process, SlenderEngine.ThumbWheelBindingId) is not ActionSpec.None) return true;
        if (excelColumns && config.Excel.ThumbWheelScrollsColumns && process == "excel") return true;
        if (touchSupported != false
            && (SlenderEngine.EffectiveBinding(config, process, SlenderEngine.ThumbWheelTouchBindingId) is not ActionSpec.None
                || SlenderEngine.EffectiveBinding(config, process, SlenderEngine.ThumbWheelTapBindingId) is not ActionSpec.None)) return true;
        return false;
    }
}
