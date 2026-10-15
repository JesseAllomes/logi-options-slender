using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Slender.Hidpp;

namespace Slender.Engine.Import;

/// <summary>
/// One-time conversion of Logi Options+ settings (the JSON blob of settings.db, plus macros.db for macro references)
/// into a SlenderConfig. Pure: no file or registry access, DOM parsing only (the engine is also compiled NativeAOT).
/// Everything that cannot be expressed is reported in <see cref="ImportResult.Notes"/>; nothing is dropped silently.
/// </summary>
public static partial class OptionsPlusImporter
{
    private const string MouseDevicePrefix = "mx-master-4-";
    private const string KeyboardDevicePrefix = "ergo-k860-";
    private const string DefaultProfileId = "420fd454-0c36-499d-bde4-146823b16147";
    private const string DefaultProfileName = "PROFILE_NAME_DEFAULT";
    private const string AllApps = "All apps";

    /// <summary>
    /// Imports into <paramref name="current"/>: profiles for the imported apps are replaced (matched by process name),
    /// Slender-only profiles and settings (Excel, LaunchAtLogin, BatteryAlerts, Keyboard) are kept.
    /// </summary>
    public static ImportResult Import(string settingsJson, string? macrosJson, SlenderConfig current)
    {
        ArgumentNullException.ThrowIfNull(settingsJson);
        ArgumentNullException.ThrowIfNull(current);
        var notes = new List<ImportNote>();

        JsonObject root = JsonNode.Parse(settingsJson) as JsonObject
            ?? throw new JsonException("Options+ settings JSON is not an object.");
        var macros = MacroLibrary.Parse(macrosJson, notes);
        var apps = ReadApplications(root);

        var profiles = new List<ParsedProfile>();
        foreach (JsonNode? keyNode in root["profile_keys"] as JsonArray ?? [])
        {
            string? key = keyNode is JsonValue kv && kv.TryGetValue(out string? k) ? k : null;
            if (key is null || root[key] is not JsonObject p) continue;
            string appId = Str(p, "applicationId") ?? key;
            bool isDefault = Str(p, "baseProfileId") is null && (Str(p, "name") == DefaultProfileName || appId == DefaultProfileId);
            apps.TryGetValue(appId, out var app);
            string display = isDefault ? AllApps : app?.Name ?? appId;
            profiles.Add(new ParsedProfile(p, appId, isDefault, display, app));
        }

        ParsedProfile? def = profiles.FirstOrDefault(p => p.IsDefault);
        if (def is null)
        {
            notes.Add(new(ImportNoteKind.Unmapped, "Settings", "No default (All apps) profile found; nothing imported."));
            return new ImportResult(current, notes);
        }

        var state = new State(notes, macros, new HashSet<string>());

        var defaultMouse = new Dictionary<ushort, ActionSpec>();
        var defaultKeys = new Dictionary<ushort, ActionSpec>();
        ConvertProfile(def, state, null, null, defaultMouse, defaultKeys);

        SlenderConfig result = ApplySettings(def, current, notes);
        var merged = new List<Profile>(current.Profiles);
        MergeProfile(merged, null, AllApps, null, defaultMouse, defaultKeys);

        foreach (ParsedProfile app in profiles.Where(p => !p.IsDefault))
        {
            if (app.App?.Path is null)
            {
                notes.Add(new(ImportNoteKind.Unmapped, app.DisplayName, "Profile has no application path, so it cannot be tied to a process; not imported."));
                continue;
            }
            var mouse = new Dictionary<ushort, ActionSpec>();
            var keys = new Dictionary<ushort, ActionSpec>();
            ConvertProfile(app, state, defaultMouse, defaultKeys, mouse, keys);
            string process = Path.GetFileNameWithoutExtension(app.App.Path).ToLowerInvariant();
            if (app.App.Path.Contains("\\windowsapps\\", StringComparison.OrdinalIgnoreCase))
                notes.Add(new(ImportNoteKind.Approximated, app.DisplayName, "Executable path is a versioned Store path; the app icon may stop resolving after the app updates (the profile still matches by process name)."));
            MergeProfile(merged, process, app.DisplayName, app.App.Path, mouse, keys);
        }

        ApplyGestureSwipes(profiles, state, merged);
        ApplyRingSlots(profiles, state, merged);
        notes.Add(new(ImportNoteKind.Info, "Profiles", "Imported profiles replace the bindings of Slender profiles for the same app (including All apps and the built-in Excel sheet buttons); other Slender profiles were kept."));
        return new ImportResult(result with { Profiles = merged }, notes);
    }

    // ---- profiles ----

    private sealed record ParsedProfile(JsonObject Json, string AppId, bool IsDefault, string DisplayName, AppInfo? App);

    private sealed record AppInfo(string Name, string? Path);

    private sealed record State(List<ImportNote> Notes, MacroLibrary Macros, HashSet<string> SkippedDevices);

    private static Dictionary<string, AppInfo> ReadApplications(JsonObject root)
    {
        var map = new Dictionary<string, AppInfo>(StringComparer.Ordinal);
        foreach (JsonNode? n in root["applications"]?["applications"] as JsonArray ?? [])
            if (n is JsonObject a && Str(a, "applicationId") is { } id)
                map[id] = new AppInfo(Str(a, "name") ?? id, Str(a, "applicationPath"));
        return map;
    }

    private static void MergeProfile(List<Profile> list, string? process, string display, string? exePath,
        Dictionary<ushort, ActionSpec> mouse, Dictionary<ushort, ActionSpec> keys)
    {
        int i = list.FindIndex(p => process is null
            ? p.AppProcess is null
            : p.AppProcess is not null && string.Equals(Strip(p.AppProcess), process, StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
        {
            list[i] = list[i] with { Bindings = mouse, KeyBindings = keys, ExePath = exePath ?? list[i].ExePath, DisplayName = list[i].DisplayName ?? display };
        }
        else
        {
            var p = new Profile(process, mouse) { KeyBindings = keys, DisplayName = display, ExePath = exePath };
            if (process is null) list.Insert(0, p); else list.Add(p);
        }
    }

    private static string Strip(string s) => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;

    private static void ConvertProfile(ParsedProfile profile, State state,
        IReadOnlyDictionary<ushort, ActionSpec>? defaultMouse, IReadOnlyDictionary<ushort, ActionSpec>? defaultKeys,
        Dictionary<ushort, ActionSpec> mouse, Dictionary<ushort, ActionSpec> keys)
    {
        var notes = state.Notes;
        foreach (JsonNode? n in profile.Json["assignments"] as JsonArray ?? [])
        {
            if (n is not JsonObject a || Str(a, "slotId") is not { } slot || a["card"] is not JsonObject card) continue;
            int us = slot.IndexOf('_');
            if (us <= 0) { notes.Add(new(ImportNoteKind.Unmapped, profile.DisplayName, $"Unrecognised slot '{slot}'.")); continue; }
            string device = slot[..us], rest = slot[(us + 1)..];
            string where = $"{profile.DisplayName} / {slot}";

            if (device.StartsWith("mx-mechanical-", StringComparison.Ordinal))
            {
                if (state.SkippedDevices.Add(device))
                    notes.Add(new(ImportNoteKind.Skipped, device, "Not an owned device (MX Mechanical keyboard); its settings were not imported."));
                continue;
            }

            bool isMouse = device.StartsWith(MouseDevicePrefix, StringComparison.Ordinal);
            bool isKeyboard = device.StartsWith(KeyboardDevicePrefix, StringComparison.Ordinal);
            bool isRadial = device.StartsWith("radial-menu-virtual-device-", StringComparison.Ordinal);
            if (!isMouse && !isKeyboard && !isRadial)
            {
                notes.Add(new(ImportNoteKind.Skipped, where, "Unknown device; not imported."));
                continue;
            }

            string label = Humanize(Str(card, "name"));
            if (rest.Length > 1 && rest[0] == 'c' && ushort.TryParse(rest.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out ushort cid))
            {
                where = $"{profile.DisplayName} / {device} c{cid}" + (isMouse ? $" ({MouseControlName(cid)})" : label.Length > 0 ? $" ({label})" : "");
                if (!isRadial) ConvertButton(card, cid, isMouse, where, state, isMouse ? defaultMouse : defaultKeys, isMouse ? mouse : keys);
            }
            else if (isRadial)
            {
                notes.Add(new(ImportNoteKind.Unmapped, where, $"Actions ring entry not imported: {Compact(card)}"));
            }
            else if (rest == "thumb_wheel_adapter" && isMouse)
            {
                ConvertThumbWheel(card, where, profile, state, mouse);
            }
            else if (profile.IsDefault && SettingsCards.Contains(rest))
            {
                // Device settings cards are consumed by ApplySettings.
            }
            else
            {
                notes.Add(new(ImportNoteKind.Unmapped, where, $"Card '{Str(card, "attribute")}' has no Slender equivalent: {Compact(card)}"));
            }
        }
    }

    private static readonly HashSet<string> SettingsCards = new(StringComparer.Ordinal)
    {
        "mouse_settings", "mouse_scroll_wheel_settings", "mouse_thumb_wheel_settings", "haptic_settings", "force_sensing_settings",
        "keyboard_settings", "disable_keys_settings", "disable_keys_capabilities",
    };

    private static string MouseControlName(ushort cid) => cid switch
    {
        ControlId.Middle => "Middle button",
        ControlId.Back => "Back",
        ControlId.Forward => "Forward",
        ControlId.Gesture => "Gesture button",
        ControlId.ModeShift => "Mode shift",
        ControlId.SensePanel => "Actions ring button",
        _ => "unknown control",
    };

    private static bool IsNativeMouseButton(ushort cid, ActionSpec a) => a is ActionSpec.MouseButton m
        && ((cid == ControlId.Middle && m.Button == "Middle") || (cid == ControlId.Back && m.Button == "Back") || (cid == ControlId.Forward && m.Button == "Forward"));

    private static void ConvertButton(JsonObject card, ushort cid, bool isMouse, string where, State state,
        IReadOnlyDictionary<ushort, ActionSpec>? baseline, Dictionary<ushort, ActionSpec> target)
    {
        var notes = state.Notes;
        ActionSpec? action;
        string attribute = Str(card, "attribute") ?? "";

        if (attribute == "ONE_OF" && isMouse && cid == ControlId.Gesture)
        {
            action = ConvertGesture(card, where, state);
        }
        else if (attribute == "MACRO_PLAYBACK" && Str(card, "id") == "card_global_presets_mode_shift")
        {
            action = new ActionSpec.None(); // the native ratchet / free-spin toggle
        }
        else
        {
            Conv c = ConvertCard(card, state.Macros);
            action = c.Action;
            if (c.Approximation is not null) notes.Add(new(ImportNoteKind.Approximated, where, c.Approximation));
            if (action is null) notes.Add(new(ImportNoteKind.Unmapped, where, c.Problem ?? "Unsupported assignment."));
        }

        if (action is null)
        {
            if (baseline is not null && baseline.ContainsKey(cid))
                notes.Add(new(ImportNoteKind.Unmapped, where, "The default profile's binding for this control applies in this app, because the Options+ action here could not be imported."));
            return;
        }

        bool native = action is ActionSpec.None || (isMouse && IsNativeMouseButton(cid, action));
        bool hasBase = baseline is not null && baseline.ContainsKey(cid);
        if (native)
        {
            if (!hasBase) return; // nothing to override; the control keeps its native behaviour
            // An explicit None (or native button) overrides the default profile's custom binding: native / pass through.
            target[cid] = action;
            return;
        }
        if (baseline is not null && baseline.TryGetValue(cid, out var inherited) && inherited.Equals(action)) return; // same as default: inherit
        target[cid] = action;
    }

    // ---- gesture button ----

    private static ActionSpec? ConvertGesture(JsonObject card, string where, State state)
    {
        var notes = state.Notes;
        string? selected = Str(card, "selectedNestedCard");
        if (selected is null || card["nestedCards"]?[selected] is not JsonObject preset)
        {
            notes.Add(new(ImportNoteKind.Unmapped, where, "Gesture button has no selected preset."));
            return null;
        }

        var directions = new List<string>();
        ActionSpec? click = null;
        foreach (var (dir, node) in preset["nestedCards"] as JsonObject ?? [])
        {
            if (node is not JsonObject sub || sub["macro"] is not JsonObject { Count: > 0 }) continue;
            if (dir == "click")
            {
                Conv c = ConvertCard(sub, state.Macros);
                click = c.Action;
                if (c.Approximation is not null) notes.Add(new(ImportNoteKind.Approximated, where + " click", c.Approximation));
                if (c.Action is null) notes.Add(new(ImportNoteKind.Unmapped, where + " click", c.Problem ?? "Unsupported assignment."));
            }
            else
            {
                directions.Add(dir); // swipes are imported by ApplyGestureSwipes
            }
        }

        if (click is null && directions.Count == 0)
        {
            notes.Add(new(ImportNoteKind.Unmapped, where, $"Gesture preset '{selected}' is a built-in motion feature (pan, zoom, rotate) with no Slender equivalent."));
            return null;
        }
        return click;
    }

    // ---- thumb wheel and actions ring ----

    private static void ConvertThumbWheel(JsonObject card, string where, ParsedProfile profile, State state, Dictionary<ushort, ActionSpec> mouse)
    {
        string? impl = card["gestureInfo"]?["implementation"] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
        switch (impl)
        {
            case "CUSTOM_HORIZONTAL_SCROLL":
                // Slender's default thumb wheel behaviour (horizontal scroll, columns in Excel); nothing to bind.
                if (profile.IsDefault)
                    state.Notes.Add(new(ImportNoteKind.Info, where, "Thumb wheel scrolls horizontally: that is Slender's default, nothing to import."));
                break;
            case "CUSTOM_VOLUME_CONTROL":
                mouse[SlenderEngine.ThumbWheelBindingId] = new ActionSpec.Keys("VolumeUp|VolumeDown");
                break;
            default:
                state.Notes.Add(new(ImportNoteKind.Unmapped, where, $"Thumb wheel adapter '{impl ?? Str(card, "id")}' has no Slender equivalent."));
                break;
        }
    }

    // ---- device settings ----

    private static SlenderConfig ApplySettings(ParsedProfile def, SlenderConfig current, List<ImportNote> notes)
    {
        HapticSettings haptics = current.Haptics;
        PointerSettings pointer = current.Pointer;

        foreach (JsonNode? n in def.Json["assignments"] as JsonArray ?? [])
        {
            if (n is not JsonObject a || Str(a, "slotId") is not { } slot || a["card"] is not JsonObject card) continue;
            int us = slot.IndexOf('_');
            if (us <= 0) continue;
            string device = slot[..us], rest = slot[(us + 1)..];
            bool mouse = device.StartsWith(MouseDevicePrefix, StringComparison.Ordinal);
            bool keyboard = device.StartsWith(KeyboardDevicePrefix, StringComparison.Ordinal);
            string where = $"{AllApps} / {slot}";

            if (mouse && rest == "mouse_settings")
            {
                JsonNode? active = card["mouseSettings"]?["pointerSpeed"]?["active"];
                string speed = active is null ? "no pointer speed" : $"pointer speed {Num(active["value"])} (Options+ DPI level {Num(active["dpiLevel"])})";
                notes.Add(new(ImportNoteKind.Unmapped, where, $"{speed}: Options+ stores a relative speed, not a DPI value, so Pointer.Dpi was left as it was ({(current.Pointer.Dpi is { } d ? d + " DPI" : "device default")}).{PointerSpeedHint}"));
            }
            else if (mouse && rest == "mouse_scroll_wheel_settings")
            {
                JsonNode? s = card["mouseScrollWheelSettings"];
                if (s is null) continue;
                if (Str(s, "dir") is { } dir) pointer = pointer with { InvertWheel = dir == "NATURAL" };
                if (s["isSmooth"] is JsonValue sv && sv.TryGetValue(out bool smooth))
                {
                    pointer = pointer with { SmoothScroll = smooth };
                    if (smooth) notes.Add(new(ImportNoteKind.Approximated, where, "'Smooth scrolling' in Options+ is imported as Slender's hi-res native wheel reports; the feel may differ."));
                }
                JsonNode? ss = s["smartshift"];
                if (ss is not null)
                {
                    if (ss["isEnabled"] is JsonValue ev && ev.TryGetValue(out bool enabled)) pointer = pointer with { SmartShiftEnabled = enabled };
                    notes.Add(new(ImportNoteKind.Unmapped, where,
                        $"SmartShift sensitivity {Num(ss["sensitivity"])} (mode {Str(ss, "mode")}, scroll force {Num(ss["scrollForce"])}, force enabled {Num(ss["isScrollForceEnabled"])}): the Options+ sensitivity scale does not map reliably onto the 1-50 auto-disengage threshold, so SmartShiftThreshold stayed at {pointer.SmartShiftThreshold} (the value Options+ wrote into the mouse); Slender never changes scroll force or ratchet mode."));
                }
            }
            else if (mouse && rest == "mouse_thumb_wheel_settings")
            {
                JsonNode? t = card["mouseThumbWheelSettings"];
                if (t is null) continue;
                if (Str(t, "dir") is { } dir) pointer = pointer with { InvertThumbWheel = dir == "NATURAL" };
                notes.Add(new(ImportNoteKind.Unmapped, where, $"Thumb wheel speed {Num(t["speed"])} and smooth flag {Num(t["isSmooth"])} have no Slender setting."));
            }
            else if (mouse && rest == "haptic_settings")
            {
                haptics = ConvertHaptics(card["hapticSettings"], haptics, where, notes);
            }
            else if (mouse && rest == "force_sensing_settings")
            {
                var buttons = card["forceSensingSettings"]?["buttonsForceSensingSettings"] as JsonArray;
                bool custom = buttons is not null && buttons.Any(b => b is JsonObject o && o.Count > 0);
                notes.Add(custom
                    ? new(ImportNoteKind.Unmapped, where, $"Force-sensing customisation has no Slender equivalent: {Compact(card)}")
                    : new(ImportNoteKind.Info, where, "Force sensing has no customisation set; nothing to import."));
            }
            else if (keyboard && rest == "keyboard_settings")
            {
                notes.Add(new(ImportNoteKind.Unmapped, where, $"Keyboard settings {Compact(card["keyboardSettings"])} (keepKeyboardInOsLayout keeps the keyboard in the OS layout) have no Slender equivalent; Keyboard.FnLock was left as it was."));
            }
            else if (keyboard && rest == "disable_keys_settings")
            {
                JsonNode? k = card["disableKeysSettings"];
                notes.Add(k is JsonObject { Count: > 0 }
                    ? new(ImportNoteKind.Unmapped, where, $"Disabled keys {Compact(k)}: Slender cannot disable Caps Lock, Insert, Num Lock, Scroll Lock or the Windows key.")
                    : new(ImportNoteKind.Info, where, "No keys are disabled in Options+; nothing to import."));
            }
            else if (keyboard && rest == "disable_keys_capabilities")
            {
                notes.Add(new(ImportNoteKind.Info, where, "Lists which keys the keyboard can disable; it is not a setting."));
            }
        }
        return current with { Haptics = haptics, Pointer = pointer };
    }

    private static HapticSettings ConvertHaptics(JsonNode? h, HapticSettings current, string where, List<ImportNote> notes)
    {
        if (h is null) return current;
        var result = current;
        if (h["isGlobalHapticsEnabled"] is JsonValue g && g.TryGetValue(out bool global)) result = result with { Enabled = global };
        if (h["hapticFeedbackLevel"] is JsonValue lv && lv.TryGetValue(out double level))
        {
            // Options+ 0-100 -> Slender's four notches 0-3 (Very light .. Strong), nearest notch.
            int notch = Math.Clamp((int)Math.Round(level / 100.0 * 3, MidpointRounding.AwayFromZero), 0, 3);
            result = result with { Level = notch };
            notes.Add(new(ImportNoteKind.Approximated, where, $"Haptic strength {level:0} (0-100) became notch {notch} of 0-3."));
        }
        if (h["eventSourceSettings"] is JsonObject sources)
        {
            if (Enabled(sources, "BetterClick___Default") is { } click) result = result with { OnButtonPress = click };
            if (Enabled(sources, "actions_ring") is { } ring) result = result with { OnActionsRing = ring };
            foreach (var (name, node) in sources)
                if (name is not ("BetterClick___Default" or "actions_ring") && Enabled(sources, name) == true)
                    notes.Add(new(ImportNoteKind.Unmapped, where, $"Haptics for '{name}' are enabled in Options+; Slender only has button-press and actions-ring haptics."));
        }
        return result;
    }

    private static bool? Enabled(JsonObject sources, string name) =>
        sources[name] is JsonObject o ? o["isHapticsEnabled"] is JsonValue v && v.TryGetValue(out bool b) && b : null;

    // ---- card conversion ----

    /// <summary>Result of converting one card: an action, or a reason it could not be converted. Approximation notes a lossy success.</summary>
    private sealed record Conv(ActionSpec? Action, string? Problem = null, string? Approximation = null);

    private static Conv Ok(ActionSpec a, string? approx = null) => new(a, null, approx);

    private static Conv Fail(string problem) => new(null, problem);

    private static Conv ConvertCard(JsonObject card, MacroLibrary macros)
    {
        string attribute = Str(card, "attribute") ?? "";
        switch (attribute)
        {
            case "":
                return Ok(new ActionSpec.None()); // unassigned slot
            case "MACRO_REF":
                return ConvertMacroRef(card, macros);
            case "ONE_OF":
            case "ADAPTER_4WAYS":
            case "ADAPTER_2WAYS":
                return Fail($"Nested/adapter assignment '{Str(card, "name")}' is only supported on the gesture button.");
            case "FOLDER":
                return Fail("Folder assignment has no Slender equivalent.");
            case "MACRO_PLAYBACK":
                break;
            default:
                return Fail($"Unsupported assignment type '{attribute}'.");
        }

        if (card["macro"] is not JsonObject macro || Str(macro, "type") is not { } type)
            return Fail($"Assignment '{Str(card, "id")}' has no action.");

        switch (type)
        {
            case "KEYSTROKE":
                return ConvertKeystroke(macro["keystroke"] as JsonObject);
            case "MEDIA":
                return Str(macro["media"], "usage") switch
                {
                    "PLAY_PAUSE" => Ok(new ActionSpec.Keys("MediaPlayPause")),
                    "NEXT_TRACK" => Ok(new ActionSpec.Keys("MediaNext")),
                    "PREVIOUS_TRACK" => Ok(new ActionSpec.Keys("MediaPrevious")),
                    "STOP" => Ok(new ActionSpec.Keys("MediaStop")),
                    "MUTE" => Ok(new ActionSpec.Keys("VolumeMute")),
                    "VOLUME_UP" => Ok(new ActionSpec.Keys("VolumeUp")),
                    "VOLUME_DOWN" => Ok(new ActionSpec.Keys("VolumeDown")),
                    var u => Fail($"Media key '{u}' has no Slender chord name."),
                };
            case "MOUSE":
                JsonObject? mouse = macro["mouse"] as JsonObject;
                int usage = mouse?["hidUsage"] is JsonValue hv && hv.TryGetValue(out int b) ? b : 0;
                return Str(mouse, "action") switch
                {
                    "WIN_BACK" => Ok(new ActionSpec.MouseButton("Back")),
                    "WIN_FORWARD" => Ok(new ActionSpec.MouseButton("Forward")),
                    "BUTTON" when usage == 1 => Ok(new ActionSpec.MouseButton("Left")),
                    "BUTTON" when usage == 2 => Ok(new ActionSpec.MouseButton("Right")),
                    "BUTTON" when usage == 3 => Ok(new ActionSpec.MouseButton("Middle")),
                    "BUTTON" => Fail($"Mouse button usage {usage} has no Slender equivalent."),
                    var m => Fail($"Mouse action '{m}' has no Slender equivalent."),
                };
            case "SYSTEM":
                string? system = Str(macro["system"], "action");
                return system switch
                {
                    "SHOW_RADIAL_MENU" => Ok(new ActionSpec.ActionsRing()),
                    "LOCK" => Ok(new ActionSpec.Keys("Win+L")),
                    "TASK_VIEW" => Ok(new ActionSpec.Keys("Win+Tab")),
                    "SWITCH_APPS" => Ok(new ActionSpec.Keys("Alt+Tab")),
                    "APP_MENU" => Ok(new ActionSpec.Keys("Menu")),
                    "BRIGHTNESS_UP" or "BRIGHTNESS_DOWN" => MapBrightness(system),
                    _ => Fail($"System action '{system}' has no Slender equivalent."),
                };
            case "QUICK_LAUNCH":
                return Str(card, "id") == "card_global_presets_calculator"
                    ? Ok(new ActionSpec.Launch("calc.exe"))
                    : Fail($"Quick launch target '{Str(card, "id")}' is unknown.");
            case "OPEN_WEB_PAGE":
                return Str(macro["openWebPage"], "url") is { Length: > 0 } url
                    ? Ok(new ActionSpec.Launch(url))
                    : Fail("Open web page has no URL.");
            case "OPEN_FILE_FOLDER":
                if (Str(macro["openFileFolder"], "path") is not { Length: > 0 } path) return Fail("Open file/folder has no path.");
                if (path.Contains("\\Microsoft.ScreenSketch_", StringComparison.OrdinalIgnoreCase) && path.EndsWith("SnippingTool.exe", StringComparison.OrdinalIgnoreCase))
                    return Ok(new ActionSpec.Launch("snippingtool.exe"), $"Versioned Store path '{path}' replaced by the 'snippingtool.exe' alias so it survives app updates.");
                return Ok(new ActionSpec.Launch(path));
            case "SCREEN_CAPTURE":
                JsonObject? cap = macro["screenCapture"] as JsonObject;
                return Str(cap, "areatocapture") == "CAPTURE_REGION" && Str(cap, "destinationfolder") == "CAPTURE_CLIPBOARD"
                    ? Ok(new ActionSpec.Keys("Win+Shift+S"))
                    : Fail($"Screen capture ({Compact(cap)}) is not the region-to-clipboard capture that Win+Shift+S performs.");
            default:
                return Fail($"Unsupported action type '{type}'.");
        }
    }

    private static Conv ConvertMacroRef(JsonObject card, MacroLibrary macros)
    {
        string? id = Str(card, "id"), name = Str(card, "name");
        MacroLibrary.Macro? m = macros.Find(id, name);
        if (m is null) return Fail($"Macro '{name}' ({id}) was not found in macros.db (or macros.db was not supplied).");
        if (m.Steps.Count == 0) return Fail($"Macro '{m.Name}' contains no key steps.");
        // Keyboard-only macros become a comma-separated chord sequence ("Enter, Ctrl+W"); delays are dropped.
        var chords = new List<string>();
        var approx = new List<string>();
        foreach (var step in m.Steps)
        {
            if (step.Key is not { } key || !HidKeys.TryChord(key, step.Modifiers, out string chord, out string? a))
                return Fail($"Macro '{m.Name}' is a sequence ({m.Describe()}) with a step that is not a single key chord (mouse or other input); Slender's Keys action only sends keyboard chords.");
            chords.Add(chord);
            if (a is not null) approx.Add(a);
        }
        string joined = string.Join(", ", chords);
        if (m.Steps.Count > 1 && m.All.Any(s => s.DelayMs > 0))
            approx.Add($"Macro '{m.Name}' became the key sequence '{joined}'; its delays were dropped and the keys are sent about 30 ms apart.");
        return Ok(new ActionSpec.Keys(joined), approx.Count > 0 ? string.Join(" ", approx) : null);
    }

    private static Conv ConvertKeystroke(JsonObject? ks)
    {
        if (ks is null) return Fail("Keystroke has no key.");
        var mods = new List<int>();
        foreach (JsonNode? m in ks["modifiers"] as JsonArray ?? [])
            if (m is JsonValue v && v.TryGetValue(out int u)) mods.Add(u);
        int code = ks["code"] is JsonValue cv && cv.TryGetValue(out int c) ? c : 0;
        if (code == 0)
            return Fail($"Modifier-only keystroke ({HidKeys.DescribeModifiers(mods)}) cannot be sent as a chord.");
        if (!HidKeys.TryChord(code, mods, out string chord, out string? approx))
            return Fail($"HID key usage {code} with modifiers [{HidKeys.DescribeModifiers(mods)}] has no key name in Slender's chord syntax.");
        return Ok(new ActionSpec.Keys(chord), approx);
    }

    private static string DescribeCard(JsonObject card, MacroLibrary macros)
    {
        Conv c = ConvertCard(card, macros);
        if (c.Action is not null) return DescribeSpec(c.Action);
        string name = Humanize(Str(card, "name"));
        string actionName = Str(card["macro"], "actionName") ?? "";
        string what = actionName.Length > 0 && !actionName.EndsWith("_none", StringComparison.Ordinal) ? actionName : name;
        return $"{(what.Length > 0 ? what : Str(card, "id") ?? "action")} - {c.Problem}";
    }

    private static string DescribeSpec(ActionSpec a) => a switch
    {
        ActionSpec.Keys k => k.Chord,
        ActionSpec.MouseButton m => $"{m.Button} mouse button",
        ActionSpec.ActionsRing => "actions ring",
        ActionSpec.Launch l => $"launch {l.Target}",
        ActionSpec.None => "nothing",
        _ => a.GetType().Name,
    };

    // ---- helpers ----

    internal static string? Str(JsonNode? node, string name) =>
        node is JsonObject o && o[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static string Num(JsonNode? n) => n is null ? "unset" : n.ToJsonString();

    private static string Compact(JsonNode? n) => n is null ? "(none)" : n.ToJsonString();

    private static string Humanize(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        const string prefix = "ASSIGNMENT_NAME_";
        if (name.StartsWith(prefix, StringComparison.Ordinal)) name = name[prefix.Length..];
        return name.Replace('_', ' ').ToLowerInvariant();
    }
}
