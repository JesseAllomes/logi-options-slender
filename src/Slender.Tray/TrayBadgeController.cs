using Slender.Tray.Native;

namespace Slender.Tray;

/// <summary>
/// Owns the badge icon handles. While there is no badge it holds nothing: no frames, no canvas, no timer state. A kind's frames
/// are rendered once when it starts (the canvas is released straight after) and every HICON is destroyed when the kind or the
/// icon size changes, or the badge ends.
/// </summary>
internal sealed class TrayBadgeController : IDisposable
{
    private readonly Func<BadgeKind, bool, int, int, nint, nint[]> _render;
    private nint[] _frames = [];
    private BadgeKind _kind;
    private bool _animated;
    private int _size;
    private int _fillWidth;
    private long _startMs;
    private int _frameIndex;

    public TrayBadgeController(Func<BadgeKind, bool, int, int, nint, nint[]>? render = null) =>
        _render = render ?? TrayBadgeRenderer.RenderIcons;

    public BadgeKind Kind => _kind;

    public int FrameCount => _frames.Length;

    /// <summary>The icon to show, or 0 for the plain Slender icon.</summary>
    public nint CurrentIcon => _frames.Length == 0 ? 0 : _frames[_frameIndex];

    /// <summary>True while the badge needs the animation timer.</summary>
    public bool NeedsTimer => _frames.Length > 1 && BadgeTiming.NeedsTimer(_kind, _animated);

    /// <summary>
    /// Shows <paramref name="kind"/> at <paramref name="size"/> with a battery fill <paramref name="fillWidth"/> px wide. Frames are rebuilt only when
    /// the kind, the animation mode, the size or the drawn width changes (not on every reading). Returns true when the icon to display changed.
    /// </summary>
    public bool Set(BadgeKind kind, bool animationsOn, int size, int fillWidth, nint baseIcon, long nowMs)
    {
        bool animated = BadgeTiming.NeedsTimer(kind, animationsOn);
        if (kind == BadgeKind.None) fillWidth = 0;
        if (kind == _kind && animated == _animated && size == _size && fillWidth == _fillWidth && (kind == BadgeKind.None || _frames.Length > 0)) return false;

        bool had = _frames.Length > 0;
        Clear();
        _kind = kind;
        _animated = animated;
        _size = size;
        _fillWidth = fillWidth;
        if (kind != BadgeKind.None)
        {
            try { _frames = _render(kind, animated, size, fillWidth, baseIcon); }
            catch { _frames = []; }
            _startMs = nowMs;
            _frameIndex = 0;
            if (_frames.Length == 0) { _kind = BadgeKind.None; _animated = false; }
        }

        return had || _frames.Length > 0;
    }

    /// <summary>Moves to the frame for <paramref name="nowMs"/>. Returns true only when that frame differs from the one showing.</summary>
    public bool Tick(long nowMs)
    {
        if (!NeedsTimer) return false;
        int i = Math.Min(BadgeTiming.FrameIndex(_kind, _animated, nowMs - _startMs), _frames.Length - 1);
        if (i == _frameIndex) return false;
        _frameIndex = i;
        return true;
    }

    private void Clear()
    {
        foreach (nint icon in _frames.Distinct()) Win32.DestroyIcon(icon); // identical frames share one handle
        _frames = [];
        _frameIndex = 0;
        _kind = BadgeKind.None;
        _animated = false;
    }

    public void Dispose() => Clear();
}
