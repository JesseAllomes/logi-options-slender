namespace Slender.Settings.Logic;

/// <summary>Which Yealink picture a view of the Headset page shows: the headset (four builds) or the base alone.</summary>
public enum YealinkView
{
    Mono,
    Dual,
    TeamsMono,
    TeamsDual,
    Base,
}

/// <summary>What a callout on the Headset picture stands for. The hotspot's Slot carries the name.</summary>
public static class YealinkSlots
{
    public const string CallButton = "call";
    public const string TeamsButton = "teams";
    public const string Busylight = "busylight";
    public const string BoomMute = "boom";
    public const string Battery = "battery";
    public const string DeskKey = "desk";
    public const string PcKey = "pc";
    public const string BaseTeamsKey = "baseteams";
    public const string MuteLight = "mutelight";
    public const string ChargeLight = "chargelight";
    public const string CallLight = "calllight";
}

/// <summary>
/// The WH64 pictures come from the user's own Yealink USB Connect install, never from this repository: the install is found
/// from its uninstall entry, the largest scale folder is picked and the file for the headset's build is copied
/// into Slender's picture cache (<see cref="AssetCache.EnsureFrom"/>), which keeps it and a backup. The hotspot positions are
/// Slender's own, measured on those images (percent of the picture). Pure so it can be tested.
/// </summary>
public static class YealinkPictures
{
    public const string DisplayName = "Yealink USB Connect";

    private const string ModelDual = "dual";

    /// <summary>
    /// The device-id folder name in the picture cache for a view. The "-2" marks the large (largest scale) copy: caches made before
    /// it hold the small picture, and a new id makes Slender take the large one once.
    /// </summary>
    public static string DeviceId(YealinkView view) => view switch
    {
        YealinkView.Mono => "yealink-wh64-mono-2",
        YealinkView.Dual => "yealink-wh64-dual-2",
        YealinkView.TeamsMono => "yealink-wh64-teams-mono-2",
        YealinkView.TeamsDual => "yealink-wh64-teams-dual-2",
        _ => "yealink-wh64-base-2",
    };

    /// <summary>File name inside a USB Connect scale folder (the large picture).</summary>
    public static string FileName(YealinkView view) => view switch
    {
        YealinkView.Mono => "pic_WH64_mono_big.png",
        YealinkView.Dual => "pic_WH64_dual_big.png",
        YealinkView.TeamsMono => "pic_WH64_TS_mono_big.png",
        YealinkView.TeamsDual => "pic_WH64_TS_dual_big.png",
        _ => "pic_WH64_base_big.png",
    };

    public static bool IsTeams(YealinkView view) => view is YealinkView.TeamsMono or YealinkView.TeamsDual;

    /// <summary>
    /// The headset picture for what the base reported: dual when the handset's own name says so, otherwise mono (also when the name
    /// is not known yet); the Teams build when the base's Platform setting is Teams.
    /// </summary>
    public static YealinkView ChooseHeadset(string? model, string? platform)
    {
        bool dual = model?.Contains(ModelDual, StringComparison.OrdinalIgnoreCase) == true;
        bool teams = string.Equals(platform?.Trim(), "Teams", StringComparison.OrdinalIgnoreCase);
        return (dual, teams) switch
        {
            (true, true) => YealinkView.TeamsDual,
            (true, false) => YealinkView.Dual,
            (false, true) => YealinkView.TeamsMono,
            _ => YealinkView.Mono,
        };
    }

    /// <summary>
    /// The install folder from the uninstall entries (display name, install location), or null. The folder must hold the
    /// drawable resources (checked with dirExists), so a stale entry for a removed install is skipped.
    /// </summary>
    public static string? FindInstall(IEnumerable<(string? Name, string? Location)> entries, Func<string, bool> dirExists)
    {
        foreach ((string? name, string? location) in entries)
        {
            if (!string.Equals(name?.Trim(), DisplayName, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(location))
            {
                continue;
            }

            string root = DrawableRoot(location.Trim().Trim('"'));
            if (dirExists(root))
            {
                return root;
            }
        }

        return null;
    }

    public static string DrawableRoot(string installLocation) => Path.Combine(installLocation, "resource", "drawable");

    /// <summary>The scale folder names ("1", "1.25"...) largest first: the biggest picture is copied. Names that are not numbers (the svg folder) are ignored.</summary>
    public static IReadOnlyList<string> RankScales(IEnumerable<string> folderNames)
    {
        var scales = new List<(string Name, double Value)>();
        foreach (string name in folderNames)
        {
            if (double.TryParse(name, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0)
            {
                scales.Add((name, v));
            }
        }

        return [.. scales.OrderByDescending(s => s.Value).Select(s => s.Name)];
    }

    /// <summary>The first existing file for a view over the ranked scale folders, or null.</summary>
    public static string? FindFile(string drawableRoot, YealinkView view, Func<string, bool> fileExists, Func<string, IEnumerable<string>> subfolderNames)
    {
        foreach (string scale in RankScales(subfolderNames(drawableRoot)))
        {
            string path = Path.Combine(drawableRoot, scale, FileName(view));
            if (fileExists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Fills dir with a copy of the view's picture and our hotspots (AssetCache's own layout.json, written last). Null when
    /// USB Connect is not installed or lacks the picture.
    /// </summary>
    public static CachedDevice? Copy(YealinkView view, string? drawableRoot, string dir)
    {
        if (drawableRoot is null)
        {
            return null;
        }

        string? file = FindFile(drawableRoot, view, File.Exists,
            root => Directory.Exists(root) ? Directory.EnumerateDirectories(root).Select(d => Path.GetFileName(d)) : []);
        if (file is null)
        {
            return null;
        }

        (int w, int h) = ImageSize.TryRead(file) ?? (400, 400);
        return AssetCache.WriteCopy(DeviceId(view), dir, file, file, w, h, Hotspots(view));
    }

    /// <summary>Our callout positions for a view, in percent of the picture. LabelSide -1 = left, 1 = right.</summary>
    public static IReadOnlyList<Hotspot> Hotspots(YealinkView view)
    {
        static Hotspot H(ushort id, string name, string slot, double x, double y, int side) => new(id, name, slot, x, y, side);
        switch (view)
        {
            case YealinkView.Mono:
            case YealinkView.Dual:
                return
                [
                    H(0xF301, "Call button", YealinkSlots.CallButton, 33.5, 65.8, 1),
                    H(0xF303, "Busylight", YealinkSlots.Busylight, 29.5, 80.0, -1),
                    H(0xF304, "Boom arm mute", YealinkSlots.BoomMute, 42.0, 83.5, 1),
                    H(0xF305, "Battery", YealinkSlots.Battery, 24.5, 71.0, -1),
                ];
            case YealinkView.TeamsMono:
                return
                [
                    H(0xF302, "Teams button", YealinkSlots.TeamsButton, 48.1, 43.8, 1),
                    H(0xF303, "Busylight", YealinkSlots.Busylight, 38.1, 49.1, -1),
                    H(0xF305, "Battery", YealinkSlots.Battery, 44.0, 54.0, -1),
                ];
            case YealinkView.TeamsDual:
                return
                [
                    H(0xF302, "Teams button", YealinkSlots.TeamsButton, 46.1, 44.7, 1),
                    H(0xF303, "Busylight", YealinkSlots.Busylight, 36.4, 46.6, -1),
                    H(0xF305, "Battery", YealinkSlots.Battery, 41.5, 55.0, -1),
                ];
            default:
                return
                [
                    H(0xF311, "Desk phone key", YealinkSlots.DeskKey, 62.3, 76.4, -1),
                    H(0xF312, "PC key", YealinkSlots.PcKey, 68.3, 71.9, 1),
                    H(0xF313, "Teams key", YealinkSlots.BaseTeamsKey, 74.2, 67.7, 1),
                    H(0xF314, "Mute light", YealinkSlots.MuteLight, 59.1, 68.1, -1),
                    H(0xF315, "Charging light", YealinkSlots.ChargeLight, 62.3, 66.1, -1),
                    H(0xF316, "Call light", YealinkSlots.CallLight, 65.4, 64.0, 1),
                ];
        }
    }
}
