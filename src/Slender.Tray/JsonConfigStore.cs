using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Slender.Engine;

namespace Slender.Tray;

/// <summary>
/// config.json under %APPDATA%\Slender. Missing file gives the default config. A corrupt or
/// unknown-version file is renamed to config.json.bad-&lt;timestamp&gt; and the default is returned.
/// </summary>
public sealed class JsonConfigStore : IConfigStore
{
    /// <summary>Schema version written and accepted. Matches SlenderConfig.Default.Version.</summary>
    public const int CurrentVersion = 1;

    private const string FileName = "config.json";

    private static readonly JsonSerializerOptions Options = CreateOptions();

    private readonly string _directory;

    public JsonConfigStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Slender"))
    {
    }

    public JsonConfigStore(string directory)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    }

    public string FilePath => Path.Combine(_directory, FileName);

    public SlenderConfig Load()
    {
        string path = FilePath;
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return SlenderConfig.Default;
            }

            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unreadable (locked, no access): do not move a file we could not inspect.
            return SlenderConfig.Default;
        }

        try
        {
            return Parse(text);
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            Quarantine(path);
            return SlenderConfig.Default;
        }
    }

    public void Save(SlenderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Directory.CreateDirectory(_directory);
        string json = JsonSerializer.Serialize(ToDto(config), Options);
        string target = FilePath;
        string temp = target + ".tmp";
        try
        {
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Parses the file text. Throws FormatException (or JsonException) on anything unusable.</summary>
    internal static SlenderConfig Parse(string text)
    {
        ConfigDto dto = JsonSerializer.Deserialize<ConfigDto>(text, Options)
            ?? throw new FormatException("Config file is empty.");
        if (dto.Version != CurrentVersion)
        {
            throw new FormatException($"Unsupported config version {dto.Version}.");
        }

        List<ProfileDto> profileDtos = dto.Profiles ?? throw new FormatException("Missing profiles.");
        var profiles = new List<Profile>(profileDtos.Count);
        foreach (ProfileDto p in profileDtos)
        {
            profiles.Add(new Profile(p.AppProcess, ParseBindings(p.Bindings)));
        }

        ExcelDto excel = dto.Excel ?? new ExcelDto();
        HapticsDto haptics = dto.Haptics ?? new HapticsDto();
        return new SlenderConfig(
            CurrentVersion,
            profiles,
            new ExcelSettings(excel.ThumbWheelScrollsColumns, Math.Clamp(excel.ColumnsPerNotch, 1, 10)),
            new HapticSettings(haptics.Enabled, Math.Clamp(haptics.Level, 0, 3), haptics.OnButtonPress, haptics.OnActionsRing),
            dto.LaunchAtLogin);
    }

    private static Dictionary<ushort, ActionSpec> ParseBindings(Dictionary<string, ActionSpec>? raw)
    {
        if (raw is null)
        {
            throw new FormatException("Missing bindings.");
        }

        var result = new Dictionary<ushort, ActionSpec>(raw.Count);
        foreach (KeyValuePair<string, ActionSpec> pair in raw)
        {
            result[ParseControlId(pair.Key)] = pair.Value ?? throw new FormatException("Null action.");
        }

        return result;
    }

    private static ConfigDto ToDto(SlenderConfig config)
    {
        var dto = new ConfigDto
        {
            Version = CurrentVersion,
            LaunchAtLogin = config.LaunchAtLogin,
            Excel = new ExcelDto
            {
                ThumbWheelScrollsColumns = config.Excel.ThumbWheelScrollsColumns,
                ColumnsPerNotch = config.Excel.ColumnsPerNotch,
            },
            Haptics = new HapticsDto
            {
                Enabled = config.Haptics.Enabled,
                Level = config.Haptics.Level,
                OnButtonPress = config.Haptics.OnButtonPress,
                OnActionsRing = config.Haptics.OnActionsRing,
            },
            Profiles = new List<ProfileDto>(config.Profiles.Count),
        };

        foreach (Profile profile in config.Profiles)
        {
            var bindings = new Dictionary<string, ActionSpec>(profile.Bindings.Count);
            foreach (KeyValuePair<ushort, ActionSpec> pair in profile.Bindings)
            {
                bindings[FormatControlId(pair.Key)] = pair.Value;
            }

            dto.Profiles.Add(new ProfileDto { AppProcess = profile.AppProcess, Bindings = bindings });
        }

        return dto;
    }

    /// <summary>Control IDs are written as "0x00C3".</summary>
    internal static string FormatControlId(ushort id) => $"0x{id:X4}";

    internal static ushort ParseControlId(string text)
    {
        string digits = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        if (digits.Length is < 1 or > 4
            || !ushort.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort id))
        {
            throw new FormatException($"Bad control id '{text}'.");
        }

        return id;
    }

    private void Quarantine(string path)
    {
        string backup = path + ".bad-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        try
        {
            File.Move(path, backup, overwrite: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Could not move it aside; the next successful Save replaces it anyway.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new ActionSpecJsonConverter());
        return options;
    }

    // Mutable DTOs: STJ needs settable members, and the engine records stay immutable.
    internal sealed class ConfigDto
    {
        public int Version { get; set; }
        public bool LaunchAtLogin { get; set; }
        public List<ProfileDto>? Profiles { get; set; }
        public ExcelDto? Excel { get; set; }
        public HapticsDto? Haptics { get; set; }
    }

    internal sealed class ProfileDto
    {
        public string? AppProcess { get; set; }
        public Dictionary<string, ActionSpec>? Bindings { get; set; }
    }

    internal sealed class ExcelDto
    {
        public bool ThumbWheelScrollsColumns { get; set; } = true;
        public int ColumnsPerNotch { get; set; } = 1;
    }

    internal sealed class HapticsDto
    {
        public bool Enabled { get; set; } = true;
        public int Level { get; set; } = 2;
        public bool OnButtonPress { get; set; }
        public bool OnActionsRing { get; set; } = true;
    }
}
