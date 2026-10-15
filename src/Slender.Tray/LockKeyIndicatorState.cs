namespace Slender.Tray;

/// <summary>
/// Turns polled Caps Lock / Num Lock states into a label that fires once per change. The first sample after being switched on (or
/// after <see cref="Reset"/>) is only a baseline, so enabling the option or reconnecting the keyboard never shows a label by itself.
/// While the option is off nothing is remembered and nothing fires. Pure logic: the tray feeds it GetKeyState results.
/// </summary>
public sealed class LockKeyIndicatorState
{
    private bool? _caps, _num;

    /// <summary>Feeds one sample. Returns "Caps Lock on", "Num Lock off" (or both, joined with " / ") when something changed, else null.</summary>
    public string? Update(bool enabled, bool caps, bool num)
    {
        if (!enabled)
        {
            Reset();
            return null;
        }

        string? text = null;
        if (_caps is { } c && c != caps) text = Label("Caps Lock", caps);
        if (_num is { } n && n != num) text = text is null ? Label("Num Lock", num) : text + " / " + Label("Num Lock", num);
        _caps = caps;
        _num = num;
        return text;
    }

    /// <summary>Forgets the baseline (keyboard disconnected, option switched off).</summary>
    public void Reset() => _caps = _num = null;

    private static string Label(string key, bool on) => $"{key} {(on ? "on" : "off")}";
}
