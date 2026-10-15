using Slender.Engine.Native;

namespace Slender.Engine;

/// <summary>What the ring capture does with one mouse message: swallow it, and which ring input (if any) it reports.</summary>
internal readonly record struct RingDecision(bool Swallow, HookRingKind? Kind);

/// <summary>
/// Press/release pairing for the Actions Ring's left/right button capture. A swallowed press swallows its release even
/// after capture ended, unless the release comes more than <see cref="StaleMs"/> later (then the press's release was lost
/// and this one belongs to another click). Pure: no Win32 calls. Hook-thread only, O(1), no allocation.
/// </summary>
internal sealed class RingButtonPairing
{
    /// <summary>Longest a swallowed press waits for its release; a later release is treated as unrelated.</summary>
    public const uint StaleMs = 5_000;

    private bool _leftSwallowed, _rightSwallowed;
    private uint _leftTime, _rightTime;

    /// <param name="msg">The WM_* mouse message.</param>
    /// <param name="timeMs">The message's own timestamp in milliseconds.</param>
    /// <param name="capture">Whether ring capture is currently on.</param>
    public RingDecision OnMessage(int msg, uint timeMs, bool capture)
    {
        switch (msg)
        {
            case NativeMethods.WM_LBUTTONUP when _leftSwallowed:
                _leftSwallowed = false;
                return new RingDecision(unchecked(timeMs - _leftTime) < StaleMs, null);
            case NativeMethods.WM_RBUTTONUP when _rightSwallowed:
                _rightSwallowed = false;
                return new RingDecision(unchecked(timeMs - _rightTime) < StaleMs, null);
        }
        if (!capture) return new RingDecision(false, null);
        switch (msg)
        {
            case NativeMethods.WM_LBUTTONDOWN:
                _leftTime = timeMs;
                _leftSwallowed = true;
                return new RingDecision(true, HookRingKind.LeftDown);
            case NativeMethods.WM_RBUTTONDOWN:
                _rightTime = timeMs;
                _rightSwallowed = true;
                return new RingDecision(true, HookRingKind.RightDown);
            case NativeMethods.WM_MOUSEWHEEL:
                return new RingDecision(true, HookRingKind.Wheel);
            case NativeMethods.WM_MOUSEHWHEEL:
                return new RingDecision(true, HookRingKind.ThumbWheel);
            default:
                return new RingDecision(false, null); // an unpaired release passes through
        }
    }
}
