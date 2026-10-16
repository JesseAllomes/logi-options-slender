namespace Slender.Settings.Logic;

/// <summary>What the recorder's low-level hook does with one key event.</summary>
/// <param name="Swallow">True = Windows and the app never see the event.</param>
/// <param name="Chord">The recorded chord on the key-down that completes one, else null.</param>
/// <param name="Unsupported">True when a system combo used a key the engine cannot send.</param>
public readonly record struct CaptureResult(bool Swallow, string? Chord = null, bool Unsupported = false);

/// <summary>
/// Records the shortcuts Windows acts on before any app sees the key (Win+V opens the clipboard, Alt+Tab switches apps)
/// while the key recorder is open. The Win keys themselves are swallowed and tracked here, so the shell never sees Win
/// down and neither the Start menu nor a Win shortcut fires. A non-modifier key pressed with Win, or Tab/Esc/Space/F4 with
/// Alt, or Esc with Ctrl, is swallowed (down and up) and recorded. Everything else passes through to the recorder's
/// normal key handling. Win+L and Ctrl+Alt+Del are handled by Windows below any hook and cannot be recorded. Pure.
/// </summary>
public sealed class WinChordCapture
{
    private const ushort VkTab = 0x09;
    private const ushort VkEsc = 0x1B;
    private const ushort VkSpace = 0x20;
    private const ushort VkF4 = 0x73;

    private readonly HashSet<ushort> _swallowedDown = [];

    /// <summary>True while a Win key is held (as this capture saw it).</summary>
    public bool WinDown { get; private set; }

    /// <summary>One key event. ctrl/alt/shift are the current modifier states; Win state is tracked here.</summary>
    public CaptureResult OnKey(ushort vk, bool down, bool ctrl, bool alt, bool shift)
    {
        if (vk is ChordRecorder.VkLeftWin or ChordRecorder.VkRightWin)
        {
            WinDown = down;
            return new CaptureResult(true);
        }

        if (!down)
        {
            // Swallow the release of a key whose press was swallowed, so the app never sees half a key.
            return new CaptureResult(_swallowedDown.Remove(vk));
        }

        if (ChordRecorder.IsModifier(vk))
        {
            return new CaptureResult(false);
        }

        bool system = WinDown
            || (alt && vk is VkTab or VkEsc or VkSpace or VkF4)
            || (ctrl && vk == VkEsc);
        if (!system)
        {
            return new CaptureResult(false);
        }

        _swallowedDown.Add(vk);
        string? chord = ChordRecorder.Compose(WinDown, ctrl, alt, shift, vk);
        return chord is null ? new CaptureResult(true, Unsupported: true) : new CaptureResult(true, chord);
    }

    /// <summary>Forget held keys (the hook is being removed).</summary>
    public void Reset()
    {
        WinDown = false;
        _swallowedDown.Clear();
    }
}
