using System.Globalization;
using Slender.Engine;
using Slender.Engine.Ring;

namespace Slender.Settings.Logic;

/// <summary>Where a slot lives: a top-level index 0..7, or a child of the folder at Slot.</summary>
public readonly record struct RingPath(int Slot, int? Child = null)
{
    public bool IsChild => Child is not null;

    public RingPath Parent => new(Slot);
}

/// <summary>What the preview draws for one slot.</summary>
public sealed record RingSlotView(string Title, string Sub, string Glyph, string? FilePath, bool IsEmpty, bool IsFolder, int ChildCount);

/// <summary>
/// Pure edits of the Actions Ring for the Settings page: slot edits, folders, copy and paste, customising an app ring,
/// and appearance. Every edit returns a new config; a ring edit on a profile that has no RingSlots first copies the ring
/// it follows (All apps, else the Options+ defaults) into that profile.
/// </summary>
public static class RingOps
{
    public const int FolderNameMax = 24;
    public const int MinOpacity = 40;
    public const int MaxOpacity = 100;

    // ---- reading ----

    public static IReadOnlyList<RingSlot> Slots(SlenderConfig config, string? app) => RingDefaults.Effective(config, app);

    /// <summary>True for an app profile that has no ring of its own (it shows All apps' ring).</summary>
    public static bool FollowsDefault(SlenderConfig config, string? app) =>
        app is not null && ProfileOps.Find(config, app)?.RingSlots is null;

    public static RingSlot? Get(SlenderConfig config, string? app, RingPath path)
    {
        IReadOnlyList<RingSlot> slots = Slots(config, app);
        if (path.Slot < 0 || path.Slot >= slots.Count)
        {
            return null;
        }

        RingSlot top = slots[path.Slot];
        if (path.Child is not { } c)
        {
            return top;
        }

        return top.Folder is { } f && c >= 0 && c < f.Count ? f[c] : null;
    }

    /// <summary>Number of actions inside a folder (empty children do not count).</summary>
    public static int ItemCount(RingSlot slot) => slot.Folder?.Count(s => !s.IsEmpty) ?? 0;

    /// <summary>True when changing or removing this slot throws away actions the user made (a folder with items).</summary>
    public static bool NeedsConfirm(RingSlot? slot) => slot is not null && ItemCount(slot) > 0;

    public static string ConfirmText(RingSlot slot, string verb) =>
        ItemCount(slot) == 1 ? $"{verb} this folder and its 1 action?" : $"{verb} this folder and its {ItemCount(slot)} actions?";

    public static RingSlotView Describe(RingSlot slot)
    {
        string glyph = RingGlyphs.AutoGlyph(slot);
        string? file = null;
        switch (RingGlyphs.Kind(slot.Icon, out string value))
        {
            case RingIconKind.Glyph:
                glyph = value;
                break;
            case RingIconKind.File:
                file = value;
                break;
            default:
                if (slot.Action is ActionSpec.Launch { Target: var target } && LooksLikeApp(target))
                {
                    file = target;
                }

                break;
        }

        if (slot.IsFolder)
        {
            int n = ItemCount(slot);
            return new RingSlotView(slot.Name ?? "Folder", n == 0 ? "Empty folder" : n == 1 ? "1 action" : $"{n} actions", glyph, file, false, true, n);
        }

        if (slot.IsEmpty)
        {
            return new RingSlotView(slot.Name ?? "Empty", "Click to set", glyph, file, true, false, 0);
        }

        ActionLabel label = ActionCatalog.Describe(slot.Action);
        string title = slot.Name ?? (label.Text.Length > 0 ? label.Text : string.Join('+', label.Chips));
        string sub = slot.Action switch
        {
            ActionSpec.Keys k when ActionCatalog.TryDecodePair(k.Chord, out _, out _) => string.Empty,
            ActionSpec.Keys => string.Join(' ', label.Chips),
            ActionSpec.MouseButton => "Mouse",
            ActionSpec.Excel => "Excel",
            ActionSpec.Launch l => ShortTarget(l.Target),
            ActionSpec.Text => "Types text",
            _ => string.Empty,
        };
        if (sub == title)
        {
            sub = string.Empty;
        }

        return new RingSlotView(title, sub, glyph, file, false, false, 0);
    }

    private static bool LooksLikeApp(string target)
    {
        string t = target.Trim();
        return !t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !t.Contains("://", StringComparison.Ordinal)
            && (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || t.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) && Path.IsPathRooted(t);
    }

    private static string ShortTarget(string target)
    {
        string name = ActionCatalog.LaunchName(target);
        return name.StartsWith("Open ", StringComparison.Ordinal) ? name[5..] : name;
    }

    /// <summary>
    /// The slot to move to when an arrow key is pressed in the preview: the slot whose direction from the current one is
    /// closest to the arrow (nearer wins a tie). Null current picks slot 0. Stays put when nothing lies that way.
    /// </summary>
    public static int Navigate(RingStyle style, RingSize size, int? current, int arrowX, int arrowY)
    {
        if (current is not { } cur)
        {
            return 0;
        }

        (double cx, double cy) = RingLayout.SlotCenter(style, size, cur);
        double al = Math.Sqrt(arrowX * arrowX + arrowY * arrowY);
        int best = cur;
        double bestCos = 0.3;
        double bestDist = double.MaxValue;
        for (int i = 0; i < RingLayout.SlotCount; i++)
        {
            if (i == cur)
            {
                continue;
            }

            (double x, double y) = RingLayout.SlotCenter(style, size, i);
            double dx = x - cx, dy = y - cy;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double cos = (dx * arrowX + dy * arrowY) / (dist * al);
            if (cos > bestCos + 1e-9 || (Math.Abs(cos - bestCos) <= 1e-9 && best != cur && dist < bestDist))
            {
                best = i;
                bestCos = cos;
                bestDist = dist;
            }
        }

        return best;
    }

    // ---- slot edits ----

    private static SlenderConfig WithSlots(SlenderConfig config, string? app, IReadOnlyList<RingSlot> slots)
    {
        var profiles = config.Profiles.ToList();
        int index = profiles.FindIndex(p => string.Equals(p.AppProcess, app, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            var created = new Profile(app, new Dictionary<ushort, ActionSpec>()) { DisplayName = app is null ? "All apps" : null };
            profiles.Insert(app is null ? 0 : profiles.Count, created);
            index = profiles.IndexOf(created);
        }

        profiles[index] = profiles[index] with { RingSlots = slots.ToArray() };
        return config with { Profiles = profiles };
    }

    private static List<RingSlot> Padded(IReadOnlyList<RingSlot> slots)
    {
        var list = slots.ToList();
        while (list.Count < Profile.RingSlotCount)
        {
            list.Add(RingSlot.Empty);
        }

        return list;
    }

    /// <summary>Replaces the slot at the path. A folder cannot go inside a folder: such a write is ignored.</summary>
    public static SlenderConfig SetSlot(SlenderConfig config, string? app, RingPath path, RingSlot slot)
    {
        List<RingSlot> slots = Padded(Slots(config, app));
        if (path.Slot < 0 || path.Slot >= slots.Count)
        {
            return config;
        }

        if (path.Child is not { } c)
        {
            slots[path.Slot] = slot;
            return WithSlots(config, app, slots);
        }

        if (slot.IsFolder || slots[path.Slot].Folder is not { } folder || c < 0 || c >= Profile.RingSlotCount)
        {
            return config;
        }

        List<RingSlot> children = Padded(folder);
        children[c] = slot;
        slots[path.Slot] = slots[path.Slot] with { Folder = children };
        return WithSlots(config, app, slots);
    }

    /// <summary>Sets the action, keeping the slot's name and icon. Ignored on a folder (use <see cref="ConvertToAction"/>).</summary>
    public static SlenderConfig SetAction(SlenderConfig config, string? app, RingPath path, ActionSpec action)
    {
        RingSlot? slot = Get(config, app, path);
        if (slot is null || slot.IsFolder)
        {
            return config;
        }

        return action is ActionSpec.None && slot.Name is null && slot.Icon is null
            ? SetSlot(config, app, path, RingSlot.Empty)
            : SetSlot(config, app, path, slot with { Action = action });
    }

    /// <summary>Blank or whitespace means "derive the name from the action" (null). Names are kept short; a folder is never nameless.</summary>
    public static SlenderConfig SetName(SlenderConfig config, string? app, RingPath path, string? name)
    {
        RingSlot? slot = Get(config, app, path);
        if (slot is null)
        {
            return config;
        }

        string? clean = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (clean is not null && clean.Length > FolderNameMax)
        {
            clean = clean[..FolderNameMax];
        }

        if (slot.IsFolder && clean is null)
        {
            clean = "Folder";
        }

        return string.Equals(slot.Name, clean, StringComparison.Ordinal) ? config : SetSlot(config, app, path, slot with { Name = clean });
    }

    /// <summary>Null icon = Automatic.</summary>
    public static SlenderConfig SetIcon(SlenderConfig config, string? app, RingPath path, string? icon)
    {
        RingSlot? slot = Get(config, app, path);
        if (slot is null)
        {
            return config;
        }

        string? clean = string.IsNullOrWhiteSpace(icon) ? null : icon;
        return string.Equals(slot.Icon, clean, StringComparison.Ordinal) ? config : SetSlot(config, app, path, slot with { Icon = clean });
    }

    /// <summary>Empties the slot (a folder goes with its contents; confirm first with <see cref="NeedsConfirm"/>).</summary>
    public static SlenderConfig Clear(SlenderConfig config, string? app, RingPath path) =>
        Get(config, app, path) is null ? config : SetSlot(config, app, path, RingSlot.Empty);

    /// <summary>Makes a top-level slot an empty folder (its action is dropped). A slot that is already a folder is left alone.</summary>
    public static SlenderConfig MakeFolder(SlenderConfig config, string? app, RingPath path)
    {
        RingSlot? slot = Get(config, app, path);
        if (slot is null || slot.IsFolder || path.IsChild)
        {
            return config;
        }

        RingSlot folder = RingDefaults.EmptyFolder(slot.Name ?? "Folder");
        return SetSlot(config, app, path, slot.Icon is null ? folder : folder with { Icon = slot.Icon });
    }

    /// <summary>Turns a folder into an empty slot; its actions are deleted (confirm first with <see cref="NeedsConfirm"/>).</summary>
    public static SlenderConfig ConvertToAction(SlenderConfig config, string? app, RingPath path)
    {
        RingSlot? slot = Get(config, app, path);
        return slot is null || !slot.IsFolder ? config : SetSlot(config, app, path, RingSlot.Empty);
    }

    /// <summary>Whether a copied slot can go at the path (a folder cannot go inside a folder).</summary>
    public static bool CanPaste(RingSlot? copied, RingPath path) => copied is not null && !(copied.IsFolder && path.IsChild);

    public static SlenderConfig Paste(SlenderConfig config, string? app, RingPath path, RingSlot? copied) =>
        CanPaste(copied, path) && Get(config, app, path) is not null ? SetSlot(config, app, path, copied!) : config;

    // ---- app rings ----

    /// <summary>An app profile that follows All apps gets its own copy of that ring to edit. No-op otherwise.</summary>
    public static SlenderConfig Customise(SlenderConfig config, string? app) =>
        FollowsDefault(config, app) && ProfileOps.Find(config, app) is not null ? WithSlots(config, app, Slots(config, null)) : config;

    /// <summary>An app profile drops its own ring and follows All apps again.</summary>
    public static SlenderConfig ResetToDefault(SlenderConfig config, string? app)
    {
        if (app is null || ProfileOps.Find(config, app) is not { RingSlots: not null } profile)
        {
            return config;
        }

        return config with { Profiles = config.Profiles.Select(p => ReferenceEquals(p, profile) ? p with { RingSlots = null } : p).ToList() };
    }

    // ---- appearance ----

    public static SlenderConfig UpdateAppearance(SlenderConfig config, Func<RingAppearance, RingAppearance> edit)
    {
        RingAppearance next = Normalise(edit(config.Ring));
        return next == config.Ring ? config : config with { Ring = next };
    }

    public static RingAppearance Normalise(RingAppearance a) =>
        a with { OpacityPercent = Math.Clamp(a.OpacityPercent, MinOpacity, MaxOpacity), Accent = NormaliseAccent(a.Accent) ?? new RingAppearance().Accent };

    /// <summary>"#RRGGBB" in upper case, or null when the text is not a 6-digit hex colour.</summary>
    public static string? NormaliseAccent(string? text)
    {
        string t = (text ?? string.Empty).Trim();
        if (t.StartsWith('#'))
        {
            t = t[1..];
        }

        return t.Length == 6 && int.TryParse(t, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _) ? "#" + t.ToUpperInvariant() : null;
    }

    /// <summary>Accent presets that sit well on graphite; the first is the default copper.</summary>
    public static IReadOnlyList<(string Name, string Hex)> AccentPresets { get; } =
    [
        ("Copper", "#D9925F"), ("Amber", "#E3B04B"), ("Mint", "#6CCB9A"), ("Sky", "#5BB5C9"), ("Lilac", "#A99BE6"), ("Rose", "#E27D8F"),
    ];
}
