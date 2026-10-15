namespace Slender.Hidpp;

/// <summary>
/// Persistent (on-keyboard) key remaps through HID++ 0x1C00. WRITES SURVIVE POWER CYCLES AND APPLY ON EVERY PC.
/// Layout and sources: docs/protocol.md, "0x1C00 Persistent remappable action".
/// </summary>
public interface IOnboardKeyboard
{
    /// <summary>0x1C00 capability flags (fn0), or null when the keyboard lacks the feature or is disconnected.</summary>
    Task<ushort?> GetOnboardCapabilitiesAsync(CancellationToken ct);

    /// <summary>Every remappable control and its current mapping for the current host (read only).</summary>
    Task<IReadOnlyList<OnboardMapping>> ReadOnboardMappingsAsync(CancellationToken ct);

    /// <summary>Stores a keystroke (modifiers + HID usage) or consumer action on the keyboard for one control. Persistent.</summary>
    Task SetOnboardActionAsync(ushort cid, OnboardAction action, CancellationToken ct);

    /// <summary>Stores a Slender chord ("Ctrl+Shift+S", "MediaPlayPause") for one control. Throws <see cref="ArgumentException"/> if not encodable. Persistent.</summary>
    Task SetOnboardChordAsync(ushort cid, string chord, CancellationToken ct);

    /// <summary>Restores the factory mapping of one control (fn5). Persistent.</summary>
    Task ResetOnboardKeyAsync(ushort cid, CancellationToken ct);
}
