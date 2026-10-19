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
                RingSlots = p.RingSlots is null ? null : ToRing(p.RingSlots, allowFolders: true),
                ThumbWheelAlt = p.ThumbWheelAlt is { } tw
                    ? new ThumbWheelSecond(tw.Enabled, tw.Forward ?? new ActionSpec.None(), tw.Back ?? new ActionSpec.None()) { Speed = ClampSpeed(tw.Speed) }
                    : null,
                SideScrollSpeed = ClampSpeed(p.SideScrollSpeed),
            });
        }

        ExcelDto excel = dto.Excel ?? new ExcelDto();
        HapticsDto haptics = dto.Haptics ?? new HapticsDto();
        return new SlenderConfig(
            CurrentVersion,
            profiles,
            new ExcelSettings(excel.ThumbWheelScrollsColumns, Math.Clamp(excel.ColumnsPerNotch, 1, 10)),
            new HapticSettings(haptics.Enabled, Math.Clamp(haptics.Level, 0, 3), haptics.OnButtonPress, haptics.OnActionsRing,
                haptics.Strength is { } strength ? Math.Clamp(strength, 0, 100) : null),
            dto.LaunchAtLogin)
        {
            Pointer = ToPointer(dto.Pointer),
            Keyboard = dto.Keyboard is { } k
                ? new KeyboardSettings(k.FnLock, (byte)(k.DisabledKeys & 0x1F), k.ShowLockKeyChanges, k.FnLockChannel1, k.FnLockChannel2, k.FnLockChannel3, k.IdentifySpareKeys)
                : new KeyboardSettings(),
            BatteryAlerts = ToBatteryAlerts(dto.BatteryAlerts),
            Ring = ToRingAppearance(dto.Ring),
            MyShortcuts = ToMyShortcuts(dto.MyShortcuts),
        };
    }

    /// <summary>Keeps entries with a name and a chord ChordParser accepts; a bad entry is dropped, never the whole file.</summary>
    private static List<CustomShortcut> ToMyShortcuts(List<CustomShortcutDto>? list) =>
        list is null ? [] : list
            .Where(s => !string.IsNullOrWhiteSpace(s.Name) && ChordParser.TryParse(s.Chord, out _))
            .Select(s => new CustomShortcut(s.Name!.Trim(), s.Chord!.Trim(), string.IsNullOrWhiteSpace(s.App) ? null : s.App.Trim()))
            .ToList();

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
            p.Dpi is { } dpi ? Math.Clamp(dpi, 200, 8000) : null,
            p.SmartShiftEnabled,
            Math.Clamp(p.SmartShiftThreshold, 1, 255),
            p.InvertWheel,
            p.InvertThumbWheel,
            p.SmoothScroll,
            p.WheelResistance is { } wr ? Math.Clamp(wr, 0, 100) : null,
            p.SensePanelForce is { } spf ? Math.Clamp(spf, 1, 0xFFFF) : null,
            p.SensePanelBaseline is { } spb ? Math.Clamp(spb, 1, 0xFFFF) : null,
            Math.Clamp(p.SideScrollSpeed, PointerSettings.SpeedMin, PointerSettings.SpeedMax),
            Math.Clamp(p.SecondActionSpeed, PointerSettings.SpeedMin, PointerSettings.SpeedMax),
            Math.Clamp(p.TouchRestMs, PointerSettings.RestMinMs, PointerSettings.RestMaxMs),
            Math.Clamp(p.DoubleTapMs, PointerSettings.DoubleTapMinMs, PointerSettings.DoubleTapMaxMs));
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

    /// <summary>Pads or trims to Profile.RingSlotCount so the ring always has a fixed layout. Folders are one level deep.</summary>
    private static List<RingSlot> ToRing(List<RingSlotDto> slots, bool allowFolders)
    {
        var ring = new List<RingSlot>(Profile.RingSlotCount);
        foreach (RingSlotDto d in slots.Take(Profile.RingSlotCount))
        {
            ring.Add(new RingSlot(d.Action ?? new ActionSpec.None())
            {
                Name = string.IsNullOrWhiteSpace(d.Name) ? null : d.Name,
                Icon = string.IsNullOrWhiteSpace(d.Icon) ? null : d.Icon,
                Folder = allowFolders && d.Folder is not null ? ToRing(d.Folder, allowFolders: false) : null,
            });
        }

        while (ring.Count < Profile.RingSlotCount)
        {
            ring.Add(RingSlot.Empty);
        }

        return ring;
    }

    private static List<RingSlotDto> FromRing(IReadOnlyList<RingSlot> slots) =>
        slots.Select(r => new RingSlotDto
        {
            Action = r.IsFolder ? null : r.Action,
            Name = r.Name,
            Icon = r.Icon,
            Folder = r.Folder is null ? null : FromRing(r.Folder),
        }).ToList();

    private static int? ClampSpeed(int? speed) => speed is { } s ? Math.Clamp(s, PointerSettings.SpeedMin, PointerSettings.SpeedMax) : null;

    private static RingAppearance ToRingAppearance(RingDto? r)
    {
        var d = new RingAppearance();
        if (r is null)
        {
            return d;
        }

        return new RingAppearance(
            Enum.TryParse(r.Style, ignoreCase: true, out RingStyle style) ? style : d.Style,
            Enum.TryParse(r.Size, ignoreCase: true, out RingSize size) ? size : d.Size,
            IsHexColour(r.Accent) ? r.Accent! : d.Accent,
            Math.Clamp(r.OpacityPercent, 40, 100),
            Enum.TryParse(r.Labels, ignoreCase: true, out RingLabels labels) ? labels : d.Labels,
            Enum.TryParse(r.Position, ignoreCase: true, out RingPosition position) ? position : d.Position,
            Enum.TryParse(r.Opening, ignoreCase: true, out RingOpening opening) ? opening : d.Opening,
            Enum.TryParse(r.AnimationSpeed, ignoreCase: true, out RingAnimationSpeed speed) ? speed : d.AnimationSpeed);
    }

    private static bool IsHexColour(string? s) =>
        s is { Length: 7 } && s[0] == '#' && s.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

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
                Strength = config.Haptics.Strength,
            },
            Pointer = new PointerDto
            {
                Dpi = config.Pointer.Dpi,
                SmartShiftEnabled = config.Pointer.SmartShiftEnabled,
                SmartShiftThreshold = config.Pointer.SmartShiftThreshold,
                InvertWheel = config.Pointer.InvertWheel,
                InvertThumbWheel = config.Pointer.InvertThumbWheel,
                SmoothScroll = config.Pointer.SmoothScroll,
                WheelResistance = config.Pointer.WheelResistance,
                SensePanelForce = config.Pointer.SensePanelForce,
                SensePanelBaseline = config.Pointer.SensePanelBaseline,
                SideScrollSpeed = config.Pointer.SideScrollSpeed,
                SecondActionSpeed = config.Pointer.SecondActionSpeed,
                TouchRestMs = config.Pointer.TouchRestMs,
                DoubleTapMs = config.Pointer.DoubleTapMs,
            },
            Keyboard = new KeyboardDto
            {
                FnLock = config.Keyboard.FnLock,
                DisabledKeys = config.Keyboard.DisabledKeys,
                ShowLockKeyChanges = config.Keyboard.ShowLockKeyChanges,
                FnLockChannel1 = config.Keyboard.FnLockChannel1,
                FnLockChannel2 = config.Keyboard.FnLockChannel2,
                FnLockChannel3 = config.Keyboard.FnLockChannel3,
                IdentifySpareKeys = config.Keyboard.IdentifySpareKeys,
            },
            BatteryAlerts = new BatteryAlertsDto
            {
                Enabled = config.BatteryAlerts.Enabled,
                MouseThresholdPercent = config.BatteryAlerts.MouseThresholdPercent,
                KeyboardThresholdPercent = config.BatteryAlerts.KeyboardThresholdPercent,
            },
            MyShortcuts = config.MyShortcuts.Count == 0
                ? null
                : config.MyShortcuts.Select(s => new CustomShortcutDto { Name = s.Name, Chord = s.Chord, App = s.App }).ToList(),
            Ring = new RingDto
            {
                Style = config.Ring.Style.ToString(),
                Size = config.Ring.Size.ToString(),
                Accent = config.Ring.Accent,
                OpacityPercent = config.Ring.OpacityPercent,
                Labels = config.Ring.Labels.ToString(),
                Position = config.Ring.Position.ToString(),
                Opening = config.Ring.Opening.ToString(),
                AnimationSpeed = config.Ring.AnimationSpeed.ToString(),
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
                RingSlots = profile.RingSlots is null ? null : FromRing(profile.RingSlots),
                ThumbWheelAlt = profile.ThumbWheelAlt is { } tw
                    ? new ThumbWheelSecondDto { Enabled = tw.Enabled, Forward = tw.Forward, Back = tw.Back, Speed = tw.Speed }
                    : null,
                SideScrollSpeed = profile.SideScrollSpeed,
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
        public RingDto? Ring { get; set; }
        public List<CustomShortcutDto>? MyShortcuts { get; set; }
    }

    internal sealed class CustomShortcutDto
    {
        public string? Name { get; set; }
        public string? Chord { get; set; }
        public string? App { get; set; }
    }

    internal sealed class ThumbWheelSecondDto
    {
        public bool Enabled { get; set; }
        public ActionSpec? Forward { get; set; }
        public ActionSpec? Back { get; set; }
        public int? Speed { get; set; }
    }

    internal sealed class RingSlotDto
    {
        public ActionSpec? Action { get; set; }
        public string? Name { get; set; }
        public string? Icon { get; set; }
        public List<RingSlotDto>? Folder { get; set; }
    }

    internal sealed class RingDto
    {
        public string? Style { get; set; }
        public string? Size { get; set; }
        public string? Accent { get; set; }
        public int OpacityPercent { get; set; } = 92;
        public string? Labels { get; set; }
        public string? Position { get; set; }
        public string? Opening { get; set; }
        public string? AnimationSpeed { get; set; }
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
        public bool SmoothScroll { get; set; }
        public int? WheelResistance { get; set; }
        public int? SensePanelForce { get; set; }
        public int? SensePanelBaseline { get; set; }
        public int SideScrollSpeed { get; set; } = 100;
        [System.Text.Json.Serialization.JsonPropertyName("tabScrollSpeed")] // its name before the top button took over (kept so the value survives)
        public int SecondActionSpeed { get; set; } = 100;
        public int TouchRestMs { get; set; } = 250;
        public int DoubleTapMs { get; set; } = 300;
    }

    internal sealed class KeyboardDto
    {
        public bool FnLock { get; set; }
        public byte DisabledKeys { get; set; }
        public bool ShowLockKeyChanges { get; set; } = true;
        public bool? FnLockChannel1 { get; set; }
        public bool? FnLockChannel2 { get; set; }
        public bool? FnLockChannel3 { get; set; }
        public bool IdentifySpareKeys { get; set; }
    }

    internal sealed class ProfileDto
    {
        public string? AppProcess { get; set; }
        public string? DisplayName { get; set; }
        public string? ExePath { get; set; }
        public Dictionary<string, ActionSpec>? Bindings { get; set; }
        public Dictionary<string, ActionSpec>? KeyBindings { get; set; }
        public Dictionary<string, ActionSpec>? GestureSwipes { get; set; }
        public List<RingSlotDto>? RingSlots { get; set; }
        public ThumbWheelSecondDto? ThumbWheelAlt { get; set; }
        public int? SideScrollSpeed { get; set; }
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
        public int? Strength { get; set; }
    }
}
