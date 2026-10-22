using System.Globalization;

namespace Slender.Engine.Devices;

/// <summary>
/// The devices that have ever been installed on this computer, kept in known-devices.txt in the state folder (one line per
/// device: id TAB firstSeenUtcTicks TAB lastSeenUtcTicks). A device not listed here stays hidden in the app. A missing or damaged
/// file reads as empty or loses only the bad lines; IO errors are logged and swallowed so the tray never fails on it. The tray is
/// the only writer.
/// </summary>
public sealed class KnownDeviceStore(string stateDirectory, ILog? log = null)
{
    public const string FileName = "known-devices.txt";
    public const int MaxIdLength = 64;

    private readonly Dictionary<string, (DateTimeOffset First, DateTimeOffset Last)> _map = [];
    private readonly object _gate = new();

    public string Path => System.IO.Path.Combine(stateDirectory, FileName);

    public static bool IsValidId(string id) =>
        id.Length is > 0 and <= MaxIdLength && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    public static IReadOnlyDictionary<string, (DateTimeOffset First, DateTimeOffset Last)> Read(string stateDirectory)
    {
        var result = new Dictionary<string, (DateTimeOffset First, DateTimeOffset Last)>();
        try
        {
            foreach (string line in File.ReadLines(System.IO.Path.Combine(stateDirectory, FileName)))
            {
                var f = line.Split('\t');
                if (f.Length != 3 || !IsValidId(f[0]) || result.ContainsKey(f[0])) continue;
                if (!TryTicks(f[1], out var first) || !TryTicks(f[2], out var last)) continue;
                result[f[0]] = (first, last);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return result;
    }

    private static bool TryTicks(string s, out DateTimeOffset value)
    {
        value = default;
        if (!long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out long t) || t > DateTime.MaxValue.Ticks) return false;
        value = new DateTimeOffset(t, TimeSpan.Zero);
        return true;
    }

    /// <summary>Replaces the in-memory copy with the file's contents and returns it.</summary>
    public IReadOnlyDictionary<string, (DateTimeOffset First, DateTimeOffset Last)> Load()
    {
        var read = Read(stateDirectory);
        lock (_gate)
        {
            _map.Clear();
            foreach (var kv in read) _map[kv.Key] = kv.Value;
            return new Dictionary<string, (DateTimeOffset First, DateTimeOffset Last)>(_map);
        }
    }

    /// <summary>Adds the device (first-seen = firstIfNew) or moves its last-seen; first-seen of a known device never changes.</summary>
    public void Upsert(string id, DateTimeOffset firstIfNew, DateTimeOffset last)
    {
        if (!IsValidId(id)) { log?.Error($"known devices: bad id '{id}'"); return; }
        lock (_gate)
        {
            var first = _map.TryGetValue(id, out var old) ? old.First : firstIfNew;
            _map[id] = (first, last);
            Save();
        }
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            if (!_map.Remove(id)) return false;
            Save();
            return true;
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(stateDirectory);
            string temp = Path + ".tmp";
            File.WriteAllLines(temp, _map.Select(kv => string.Join('\t', kv.Key, kv.Value.First.UtcTicks, kv.Value.Last.UtcTicks)));
            File.Move(temp, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Error("known devices save failed", e);
        }
    }
}
