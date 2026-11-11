using Slender.Tray.Native;

namespace Slender.Tray;

/// <summary>
/// Owns the pulse icon handles. While the icon is plain it holds nothing: no frames, no timer state. A kind's frames are rendered
/// once when it starts and every HICON is destroyed when the kind or the icon size changes, or the pulse ends.
/// </summary>
internal sealed class TrayBadgeController : IDisposable
{
    private readonly Func<BadgeKind, bool, int, nint, nint[]> _render;
    private nint[] _frames = [];
    private BadgeKind _kind;
    private bool _animated;
    private int _size;
    private long _startMs;
    private int _frameIndex;

    private readonly Action<nint> _destroy;

    public TrayBadgeController(Func<BadgeKind, bool, int, nint, nint[]>? render = null, Action<nint>? destroy = null)
    {
        _render = render ?? TrayBadgeRenderer.RenderIcons;
        _destroy = destroy ?? (icon => Win32.DestroyIcon(icon));
    }

    public BadgeKind Kind => _kind;

    public int FrameCount => _frames.Length;

    /// <summary>The icon to show, or 0 for the plain Slender icon.</summary>
    public nint CurrentIcon => _frames.Length == 0 ? 0 : _frames[_frameIndex];

    /// <summary>True while the badge needs the animation timer.</summary>
    public bool NeedsTimer => _frames.Length > 1 && BadgeTiming.NeedsTimer(_kind, _animated);

    /// <summary>
    /// Shows <paramref name="kind"/> at <paramref name="size"/>. Frames are rebuilt only when the kind, the animation mode or the size
    /// changes (not on every reading). Returns true when the icon to display changed.
    /// </summary>
    public bool Set(BadgeKind kind, bool animationsOn, int size, nint baseIcon, long nowMs)
    {
        bool animated = BadgeTiming.NeedsTimer(kind, animationsOn);
        if (kind == _kind && animated == _animated && size == _size && (kind == BadgeKind.None || _frames.Length > 0)) return false;

        bool had = _frames.Length > 0;
        Clear();
        _kind = kind;
        _animated = animated;
        _size = size;
        if (kind != BadgeKind.None)
        {
            try { _frames = _render(kind, animated, size, baseIcon); }
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
        foreach (nint icon in _frames.Distinct()) _destroy(icon); // identical frames share one handle
        _frames = [];
        _frameIndex = 0;
        _kind = BadgeKind.None;
        _animated = false;
    }

    public void Dispose() => Clear();
}
