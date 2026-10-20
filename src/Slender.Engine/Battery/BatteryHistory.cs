using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slender.Engine.Battery;

/// <summary>
/// Battery readings per device, kept in battery-history.json under the local Slender folder for BatteryEstimator. A reading is
/// kept when the level or charging state changed, or an hour has passed since the last kept one (and never twice within a minute);
/// anything older than 60 days is dropped. A missing, corrupt or unwritable file never breaks anything: it just starts empty.
/// </summary>
public sealed class BatteryHistory
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(60);
    public static readonly TimeSpan Heartbeat = TimeSpan.FromHours(1);
    public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(1);

    private readonly string _path;
    private readonly Func<DateTimeOffset> _now;
    private readonly ILog _log;
    private readonly object _gate = new();
    private Dictionary<string, List<BatterySample>>? _devices;

    public BatteryHistory(string? path = null, Func<DateTimeOffset>? now = null, ILog? log = null)
    {
        _path = path ?? Path.Combine(SlenderPaths.LocalDirectory, "battery-history.json");
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Notes a reading; returns whether it was kept (and the file written).</summary>
    public bool Record(string deviceId, int percent, bool charging)
    {
        lock (_gate)
        {
            DateTimeOffset now = _now();
            var devices = Load();
            if (!devices.TryGetValue(deviceId, out var list)) devices[deviceId] = list = [];
            var sample = new BatterySample(now, Math.Clamp(percent, 0, 100), charging);
            if (list.Count > 0)
            {
                BatterySample prev = list[^1];
                TimeSpan since = now - prev.At;
                bool changed = prev.Percent != sample.Percent || prev.Charging != sample.Charging;
                if (since >= TimeSpan.Zero && since < MinGap) return false;
                if (since >= TimeSpan.Zero && !changed && since < Heartbeat) return false;
            }

            list.Add(sample);
            list.RemoveAll(s => now - s.At > Retention);
            Save(devices);
            return true;
        }
    }

    /// <summary>The kept readings for a device, oldest first.</summary>
    public IReadOnlyList<BatterySample> Samples(string deviceId)
    {
        lock (_gate) return Load().TryGetValue(deviceId, out var list) ? [.. list] : [];
    }

    public BatteryEstimate Estimate(string deviceId) => BatteryEstimator.Estimate(Samples(deviceId));

    private Dictionary<string, List<BatterySample>> Load()
    {
        if (_devices is not null) return _devices;
        var devices = new Dictionary<string, List<BatterySample>>();
        try
        {
            if (File.Exists(_path))
            {
                var file = JsonSerializer.Deserialize(File.ReadAllText(_path), BatteryJsonContext.Default.BatteryHistoryFile);
                DateTimeOffset now = _now();
                foreach (var (id, rows) in file?.Devices ?? [])
                {
                    var list = (rows ?? []).Where(r => r is not null && r.P is >= 0 and <= 100)
                        .Select(r => new BatterySample(DateTimeOffset.FromUnixTimeSeconds(r.T), r.P, r.C))
                        .Where(s => now - s.At <= Retention)
                        .OrderBy(s => s.At).ToList();
                    if (list.Count > 0) devices[id] = list;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            _log.Error("battery history unreadable; starting empty", ex);
            devices.Clear();
        }

        return _devices = devices;
    }

    private void Save(Dictionary<string, List<BatterySample>> devices)
    {
        try
        {
            var file = new BatteryHistoryFile
            {
                Devices = devices.ToDictionary(kv => kv.Key, kv => (List<SampleRow>?)kv.Value.Select(s => new SampleRow { T = s.At.ToUnixTimeSeconds(), P = s.Percent, C = s.Charging }).ToList()),
            };
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file, BatteryJsonContext.Default.BatteryHistoryFile));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("battery history not saved", ex);
        }
    }

    internal sealed class BatteryHistoryFile
    {
        public Dictionary<string, List<SampleRow>?>? Devices { get; set; }
    }

    internal sealed class SampleRow
    {
        public long T { get; set; }
        public int P { get; set; }
        public bool C { get; set; }
    }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.Never, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BatteryHistory.BatteryHistoryFile))]
internal sealed partial class BatteryJsonContext : JsonSerializerContext
{
}
