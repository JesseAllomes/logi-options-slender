using System.Globalization;

namespace Slender.Engine;

/// <summary>One app that has notified: first and last time (UTC ticks) and how many times.</summary>
public sealed record SeenApp(string Name, long FirstTicks, long LastTicks, int Count);

/// <summary>
/// The apps that have shown a notification, kept in seen-apps.txt in the state folder (one line per app:
/// name TAB first TAB last TAB count) so Settings can list them without touching the tray or the mouse. Capped at
/// <see cref="Cap"/>: the least recently seen goes first. A missing or damaged file reads as empty or loses only the bad
/// lines. The tray is the only writer; Settings only reads.
/// </summary>
public sealed class SeenAppsStore(string stateDirectory, ILog? log = null)
{
    public const string FileName = "seen-apps.txt";
    public const int Cap = 100;

    private readonly List<SeenApp> _apps = [];
    private bool _loaded;
    private readonly object _gate = new();

    public string Path => System.IO.Path.Combine(stateDirectory, FileName);

    /// <summary>Reads the file; every line that is not four fields with a usable name and numbers is skipped.</summary>
    public static List<SeenApp> Read(string stateDirectory)
    {
        var result = new List<SeenApp>();
        try
        {
            foreach (string line in File.ReadLines(System.IO.Path.Combine(stateDirectory, FileName)))
            {
                var f = line.Split('\t');
                if (f.Length != 4) continue;
                string name = f[0].Trim();
                if (name.Length is 0 or > NotifyHapticSettings.MaxAppNameLength) continue;
                if (!long.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out long first)
                    || !long.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out long last)
                    || !int.TryParse(f[3], NumberStyles.None, CultureInfo.InvariantCulture, out int count)) continue;
                if (result.Exists(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(new SeenApp(name, first, last, Math.Max(1, count)));
                if (result.Count >= Cap) break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return result;
    }

    /// <summary>Records one notification and rewrites the file. Returns true when the app was not known before.</summary>
    public bool Record(string name, DateTimeOffset at)
    {
        name = name.Trim();
        if (name.Length == 0) return false;
        if (name.Length > NotifyHapticSettings.MaxAppNameLength) name = name[..NotifyHapticSettings.MaxAppNameLength];
        lock (_gate)
        {
            if (!_loaded) { _apps.AddRange(Read(stateDirectory)); _loaded = true; }
            int i = _apps.FindIndex(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            bool isNew = i < 0;
            if (isNew) _apps.Add(new SeenApp(name, at.UtcTicks, at.UtcTicks, 1));
            else _apps[i] = _apps[i] with { LastTicks = at.UtcTicks, Count = _apps[i].Count + 1 };
            if (_apps.Count > Cap) _apps.Remove(_apps.MinBy(a => a.LastTicks)!);
            Save();
            return isNew;
        }
    }

    public IReadOnlyList<SeenApp> Snapshot()
    {
        lock (_gate) return _apps.ToArray();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(stateDirectory);
            string temp = Path + ".tmp";
            File.WriteAllLines(temp, _apps.Select(a => string.Join('\t', a.Name.Replace('\t', ' '), a.FirstTicks, a.LastTicks, a.Count)));
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Error("seen apps save failed", e);
        }
    }
}
