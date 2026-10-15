using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace Slender.Engine.Config;

/// <summary>
/// config.json under %APPDATA%\Slender. Missing file gives the default config. A corrupt or
/// unknown-version file is renamed to config.json.bad-&lt;timestamp&gt; and the default is returned.
/// </summary>
public sealed class JsonConfigStore : IConfigStore
{
    /// <summary>Schema version written and accepted. Matches SlenderConfig.Default.Version.</summary>
    public const int CurrentVersion = SlenderConfig.CurrentVersion;

    private const string FileName = "config.json";

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
        string json = JsonSerializer.Serialize(ToDto(config), ConfigJsonContext.Default.ConfigDto);
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
        ConfigDto dto = JsonSerializer.Deserialize(text, ConfigJsonContext.Default.ConfigDto)
            ?? throw new FormatException("Config file is empty.");
        if (dto.Version is < 1 or > CurrentVersion)
        {
            throw new FormatException($"Unsupported config version {dto.Version}.");
        }

        List<ProfileDto> profileDtos = dto.Profiles ?? throw new FormatException("Missing profiles.");
        var profiles = new List<Profile>(profileDtos.Count);
        foreach (ProfileDto p in profileDtos)
        {
            profiles.Add(new Profile(p.AppProcess, ParseBindings(p.Bindings))
            {
                // Version 1 files have none of these; missing means empty/null.
                KeyBindings = p.KeyBindings is null ? new Dictionary<ushort, ActionSpec>() : ParseBindings(p.KeyBindings),
                DisplayName = string.IsNullOrWhiteSpace(p.DisplayName) ? null : p.DisplayName,
                ExePath = string.IsNullOrWhiteSpace(p.ExePath) ? null : p.ExePath,
                GestureSwipes = ParseSwipes(p.GestureSwipes),
                RingSlots = p.RingSlots is null ? null : NormaliseRing(p.RingSlots),
            });
        }

        ExcelDto excel = dto.Excel ?? new ExcelDto();
        HapticsDto haptics = dto.Haptics ?? new HapticsDto();
        return new SlenderConfig(
            CurrentVersion,
            profiles,
            new ExcelSettings(excel.ThumbWheelScrollsColumns, Math.Clamp(excel.ColumnsPerNotch, 1, 10)),
            new HapticSettings(haptics.Enabled, Math.Clamp(haptics.Level, 0, 3), haptics.OnButtonPress, haptics.OnActionsRing),
            dto.LaunchAtLogin)
        {
            Pointer = ToPointer(dto.Pointer),
            Keyboard = new KeyboardSettings(dto.Keyboard?.FnLock ?? false),
            BatteryAlerts = ToBatteryAlerts(dto.BatteryAlerts),
        };
    }

    private static BatteryAlertSettings ToBatteryAlerts(BatteryAlertsDto? b)
    {
        if (b is null)
        {
            return new BatteryAlertSettings();
        }

        return new BatteryAlertSettings(
            b.Enabled,
            Math.Clamp(b.MouseThresholdPercent, 1, 99),
            Math.Clamp(b.KeyboardThresholdPercent, 1, 99));
    }

    private static PointerSettings ToPointer(PointerDto? p)
    {
        if (p is null)
        {
            return new PointerSettings();
        }

        return new PointerSettings(
            p.Dpi is { } dpi ? Math.Clamp(dpi, 100, 32000) : null,
            p.SmartShiftEnabled,
            Math.Clamp(p.SmartShiftThreshold, 1, 255),
            p.InvertWheel,
            p.InvertThumbWheel);
    }

    private static Dictionary<GestureDirection, ActionSpec> ParseSwipes(Dictionary<string, ActionSpec>? raw)
    {
        var swipes = new Dictionary<GestureDirection, ActionSpec>();
        if (raw is null)
        {
            return swipes;
        }

        foreach (KeyValuePair<string, ActionSpec> pair in raw)
        {
            if (!Enum.TryParse(pair.Key, ignoreCase: true, out GestureDirection direction))
            {
                throw new FormatException($"Unknown gesture direction '{pair.Key}'.");
            }

            swipes[direction] = pair.Value;
        }

        return swipes;
    }

    /// <summary>Pads or trims to Profile.RingSlotCount so the ring always has a fixed layout.</summary>
    private static List<ActionSpec> NormaliseRing(List<ActionSpec> slots)
    {
        var ring = slots.Take(Profile.RingSlotCount).ToList();
        while (ring.Count < Profile.RingSlotCount)
        {
            ring.Add(new ActionSpec.None());
        }

        return ring;
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
            Pointer = new PointerDto
            {
                Dpi = config.Pointer.Dpi,
                SmartShiftEnabled = config.Pointer.SmartShiftEnabled,
                SmartShiftThreshold = config.Pointer.SmartShiftThreshold,
                InvertWheel = config.Pointer.InvertWheel,
                InvertThumbWheel = config.Pointer.InvertThumbWheel,
            },
            Keyboard = new KeyboardDto { FnLock = config.Keyboard.FnLock },
            BatteryAlerts = new BatteryAlertsDto
            {
                Enabled = config.BatteryAlerts.Enabled,
                MouseThresholdPercent = config.BatteryAlerts.MouseThresholdPercent,
                KeyboardThresholdPercent = config.BatteryAlerts.KeyboardThresholdPercent,
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

            var keyBindings = new Dictionary<string, ActionSpec>(profile.KeyBindings.Count);
            foreach (KeyValuePair<ushort, ActionSpec> pair in profile.KeyBindings)
            {
                keyBindings[FormatControlId(pair.Key)] = pair.Value;
            }

            dto.Profiles.Add(new ProfileDto
            {
                AppProcess = profile.AppProcess,
                DisplayName = profile.DisplayName,
                ExePath = profile.ExePath,
                Bindings = bindings,
                KeyBindings = keyBindings,
                GestureSwipes = profile.GestureSwipes.Count == 0
                    ? null
                    : profile.GestureSwipes.ToDictionary(kv => kv.Key.ToString().ToLowerInvariant(), kv => kv.Value),
                RingSlots = profile.RingSlots?.ToList(),
            });
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

    // Mutable DTOs: STJ needs settable members, and the engine records stay immutable.
    internal sealed class ConfigDto
    {
        public int Version { get; set; }
        public bool LaunchAtLogin { get; set; }
        public List<ProfileDto>? Profiles { get; set; }
        public ExcelDto? Excel { get; set; }
        public HapticsDto? Haptics { get; set; }
        public PointerDto? Pointer { get; set; }
        public KeyboardDto? Keyboard { get; set; }
        public BatteryAlertsDto? BatteryAlerts { get; set; }
    }

    internal sealed class BatteryAlertsDto
    {
        public bool Enabled { get; set; } = true;
        public int MouseThresholdPercent { get; set; } = 15;
        public int KeyboardThresholdPercent { get; set; } = 10;
    }

    internal sealed class PointerDto
    {
        public int? Dpi { get; set; }
        public bool SmartShiftEnabled { get; set; } = true;
        public int SmartShiftThreshold { get; set; } = 12;
        public bool InvertWheel { get; set; }
        public bool InvertThumbWheel { get; set; }
    }

    internal sealed class KeyboardDto
    {
        public bool FnLock { get; set; }
    }

    internal sealed class ProfileDto
    {
        public string? AppProcess { get; set; }
        public string? DisplayName { get; set; }
        public string? ExePath { get; set; }
        public Dictionary<string, ActionSpec>? Bindings { get; set; }
        public Dictionary<string, ActionSpec>? KeyBindings { get; set; }
        public Dictionary<string, ActionSpec>? GestureSwipes { get; set; }
        public List<ActionSpec>? RingSlots { get; set; }
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
