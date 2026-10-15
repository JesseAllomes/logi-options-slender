namespace Slender.Engine.Ring;

/// <summary>The Options+ default Actions Ring, used until a profile defines its own slots.</summary>
public static class RingDefaults
{
    /// <summary>
    /// Clockwise from the top: Cut, Copy, Paste, Forward, Back, Undo, Redo, an empty folder "Folder".
    /// A new list on every call, so callers may keep and edit it.
    /// </summary>
    public static IReadOnlyList<RingSlot> Slots() =>
    [
        new(new ActionSpec.Keys("Ctrl+X")) { Name = "Cut" },
        new(new ActionSpec.Keys("Ctrl+C")) { Name = "Copy" },
        new(new ActionSpec.Keys("Ctrl+V")) { Name = "Paste" },
        new(new ActionSpec.MouseButton("Forward")) { Name = "Forward" },
        new(new ActionSpec.MouseButton("Back")) { Name = "Back" },
        new(new ActionSpec.Keys("Ctrl+Z")) { Name = "Undo" },
        new(new ActionSpec.Keys("Ctrl+Y")) { Name = "Redo" },
        EmptyFolder("Folder"),
    ];

    /// <summary>A folder with no actions in it (RingSlotCount empty children).</summary>
    public static RingSlot EmptyFolder(string name) =>
        new(new ActionSpec.None()) { Name = name, Folder = Enumerable.Repeat(RingSlot.Empty, Profile.RingSlotCount).ToArray() };

    /// <summary>The ring a profile shows: its own slots, else the default profile's, else <see cref="Slots"/>.</summary>
    public static IReadOnlyList<RingSlot> Effective(SlenderConfig config, string? appProcess)
    {
        IReadOnlyList<RingSlot>? own = Find(config, appProcess)?.RingSlots;
        if (own is not null)
        {
            return own;
        }

        return Find(config, null)?.RingSlots ?? Slots();
    }

    private static Profile? Find(SlenderConfig config, string? appProcess) =>
        config.Profiles.FirstOrDefault(p => string.Equals(p.AppProcess, appProcess, StringComparison.OrdinalIgnoreCase));
}
