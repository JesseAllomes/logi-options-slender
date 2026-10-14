namespace Slender.Engine;

/// <summary>
/// Press/release pairing for hook-captured buttons. A swallowed press always swallows its matching
/// release (even if the handler does not set Handled on it); a passed-through press never has its
/// release swallowed, so Windows never sees an orphaned half. Hook-thread only, O(1), no allocation.
/// </summary>
internal sealed class HookButtonPairing
{
    private bool _middle, _back, _forward; // true = the press was swallowed, release must be too

    /// <summary>Returns true if the event must be swallowed. Handlers run inline.</summary>
    public bool Process(HookButton button, bool pressed, EventHandler<HookButtonEventArgs>? handlers, object sender)
    {
        var args = new HookButtonEventArgs(button, pressed);
        try { handlers?.Invoke(sender, args); }
        catch { args.Handled = false; if (pressed) return false; }

        if (pressed)
        {
            Set(button, args.Handled);
            return args.Handled;
        }
        // Release: follow the press, ignore the handler's opinion.
        var swallow = Get(button);
        Set(button, false);
        return swallow;
    }

    private bool Get(HookButton b) => b switch
    {
        HookButton.Middle => _middle,
        HookButton.Back => _back,
        _ => _forward,
    };

    private void Set(HookButton b, bool v)
    {
        switch (b)
        {
            case HookButton.Middle: _middle = v; break;
            case HookButton.Back: _back = v; break;
            default: _forward = v; break;
        }
    }
}
