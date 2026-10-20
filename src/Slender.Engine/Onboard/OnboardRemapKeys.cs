using Slender.Hidpp;

namespace Slender.Engine;

/// <summary>One ERGO K860 control that can be remapped on the keyboard itself (0x1C00).</summary>
public sealed record OnboardKeyInfo(ushort Cid, string Name);

/// <summary>A user's on-keyboard remap: CID to chord (see <see cref="OnboardChordEncoder"/>). Stored in config.json, written to the keyboard only on request.</summary>
public sealed record OnboardRemap(ushort Cid, string Chord);

/// <summary>The 17 controls the K860 lists for 0x1C00 (docs/protocol.md) and what the picker may offer for them.</summary>
public static class OnboardRemapKeys
{
    public static IReadOnlyList<OnboardKeyInfo> All { get; } =
    [
        new(199, "Brightness down"), new(200, "Brightness up"), new(224, "Task view"), new(225, "Win+A key"),
        new(110, "Show desktop"), new(213, "Home"), new(228, "Previous track"), new(229, "Play / pause"),
        new(230, "Next track"), new(231, "Mute"), new(232, "Volume down"), new(233, "Volume up"),
        new(257, "Switch apps"), new(191, "Screen capture"), new(10, "Calculator"), new(234, "Look up (Menu)"),
        new(111, "Lock"),
    ];

    public static string NameOf(ushort cid) => All.FirstOrDefault(k => k.Cid == cid)?.Name ?? $"Control {cid}";

    public static bool IsKnown(ushort cid) => All.Any(k => k.Cid == cid);

    /// <summary>Factory actions: the observed ones plus "empty" for the three keys that read Empty (firmware native).</summary>
    public static IReadOnlyDictionary<ushort, OnboardAction> FactoryDefaults { get; } = BuildDefaults();

    private static Dictionary<ushort, OnboardAction> BuildDefaults()
    {
        var d = new Dictionary<ushort, OnboardAction>(OnboardSync.K860ObservedDefaults);
        foreach (ushort cid in new ushort[] { 199, 200, 257 }) d[cid] = default;
        return d;
    }

    /// <summary>Config list to a clean one: known controls only, chords the keyboard can store, one per control (the last wins), by CID.</summary>
    public static List<OnboardRemap> Sanitize(IEnumerable<OnboardRemap>? remaps)
    {
        var byCid = new SortedDictionary<ushort, OnboardRemap>();
        foreach (var r in remaps ?? [])
        {
            if (r is null || !IsKnown(r.Cid) || !OnboardChordEncoder.TryEncode(r.Chord, out _, out _)) continue;
            byCid[r.Cid] = r with { Chord = r.Chord.Trim() };
        }
        return [.. byCid.Values];
    }
}
