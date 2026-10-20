using System.Globalization;
using Slender.Hidpp;

namespace Slender.Engine;

public enum OnboardOutcome { Applied, Unchanged, Failed }

/// <summary>Result for one control of an apply request.</summary>
public sealed record OnboardKeyResult(ushort Cid, OnboardOutcome Outcome, string? Reason = null);

/// <summary>Reply to "read". <see cref="Error"/> is set (and the rest empty) when the keyboard could not be read.</summary>
public sealed record OnboardReadReply(string? Error, ushort? Capabilities, IReadOnlyList<OnboardMapping> Mappings)
{
    public bool Ok => Error is null;
}

/// <summary>Reply to "apply". <see cref="Error"/> set means nothing was written.</summary>
public sealed record OnboardApplyReply(string? Error, IReadOnlyList<OnboardKeyResult> Results)
{
    public bool Ok => Error is null;
    public bool AllSucceeded => Ok && Results.All(r => r.Outcome != OnboardOutcome.Failed);
}

/// <summary>
/// Settings to tray, one request line and one reply line over a current-user pipe. Tab separated:
/// <c>read</c>, or <c>apply TAB set:CID:chord TAB reset:CID ...</c>. Replies start with <c>ok</c> or <c>err TAB reason</c>.
/// Nothing in the tray writes the keyboard except an <c>apply</c> line.
/// </summary>
public static class OnboardProtocol
{
    public const string PipeName = "Slender.Onboard";
    public const int MaxLineChars = 4096;

    /// <summary>Reply text a client uses when the tray could not be reached.</summary>
    public const string Unreachable = "Slender is not running";

    public static string ReadLine() => "read";

    public static string ApplyLine(IEnumerable<OnboardRemap> sets, IEnumerable<ushort> resets)
    {
        var parts = new List<string> { "apply" };
        foreach (var s in sets) parts.Add($"set:{s.Cid}:{Clean(s.Chord)}");
        foreach (var c in resets) parts.Add($"reset:{c}");
        return string.Join('\t', parts);
    }

    public static bool TryParseApply(string? line, out Dictionary<ushort, string> sets, out List<ushort> resets, out string error)
    {
        sets = []; resets = []; error = "";
        if (line is null || line.Length > MaxLineChars) { error = "bad request"; return false; }
        var parts = line.Split('\t');
        if (parts[0] != "apply") { error = "bad request"; return false; }
        foreach (var p in parts.Skip(1))
        {
            var f = p.Split(':', 3);
            if (f.Length >= 2 && ushort.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out var cid) && cid != 0)
            {
                if (f[0] == "set" && f.Length == 3) { sets[cid] = f[2]; continue; }
                if (f[0] == "reset" && f.Length == 2) { if (!resets.Contains(cid)) resets.Add(cid); continue; }
            }
            error = "bad request item";
            return false;
        }
        return true;
    }

    public static string ErrLine(string reason) => "err\t" + Clean(reason);

    public static string FormatRead(OnboardReadReply r)
    {
        if (!r.Ok) return ErrLine(r.Error!);
        var parts = new List<string> { "ok", "caps=" + (r.Capabilities is { } c ? c.ToString("X4", CultureInfo.InvariantCulture) : "none") };
        foreach (var m in r.Mappings)
            parts.Add(string.Join(',', m.Cid, (byte)m.Action.Type, m.Action.Param, m.Action.Modifiers, m.Status));
        return string.Join('\t', parts);
    }

    public static OnboardReadReply ParseRead(string? line)
    {
        if (line is null) return new(Unreachable, null, []);
        var parts = line.Split('\t');
        if (parts[0] == "err") return new(parts.Length > 1 ? parts[1] : "error", null, []);
        if (parts[0] != "ok" || parts.Length < 2 || !parts[1].StartsWith("caps=", StringComparison.Ordinal)) return new("unreadable reply", null, []);
        ushort? caps = ushort.TryParse(parts[1].AsSpan(5), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cv) ? cv : null;
        var list = new List<OnboardMapping>();
        foreach (var p in parts.Skip(2))
        {
            var f = p.Split(',');
            if (f.Length == 5 && ushort.TryParse(f[0], out var cid) && byte.TryParse(f[1], out var type) && ushort.TryParse(f[2], out var param)
                && byte.TryParse(f[3], out var mods) && byte.TryParse(f[4], out var status))
                list.Add(new OnboardMapping(cid, new OnboardAction((OnboardActionType)type, param, mods), status));
        }
        return new(null, caps, list);
    }

    public static string FormatApply(OnboardApplyReply r)
    {
        if (!r.Ok) return ErrLine(r.Error!);
        var parts = new List<string> { "ok" };
        foreach (var k in r.Results)
            parts.Add(k.Outcome == OnboardOutcome.Failed ? $"{k.Cid}:failed:{Clean(k.Reason ?? "failed")}" : $"{k.Cid}:{k.Outcome.ToString().ToLowerInvariant()}");
        return string.Join('\t', parts);
    }

    public static OnboardApplyReply ParseApply(string? line)
    {
        if (line is null) return new(Unreachable, []);
        var parts = line.Split('\t');
        if (parts[0] == "err") return new(parts.Length > 1 ? parts[1] : "error", []);
        if (parts[0] != "ok") return new("unreadable reply", []);
        var list = new List<OnboardKeyResult>();
        foreach (var p in parts.Skip(1))
        {
            var f = p.Split(':', 3);
            if (f.Length < 2 || !ushort.TryParse(f[0], out var cid)) continue;
            list.Add(f[1] switch
            {
                "applied" => new(cid, OnboardOutcome.Applied),
                "unchanged" => new(cid, OnboardOutcome.Unchanged),
                _ => new(cid, OnboardOutcome.Failed, f.Length == 3 ? f[2] : "failed"),
            });
        }
        return new(null, list);
    }

    private static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
