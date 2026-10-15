using System.Text.Json.Nodes;

namespace Slender.Engine.Import;

/// <summary>
/// One app's Actions Ring profile from the Logi Plugin Service store. AppName is the folder name ("@_defaultwin", "@_excel",
/// "@_word-1daa..."), ProfileDirectory the original profile folder (ActionImages live there; read it only through a copy).
/// </summary>
public sealed record PluginServiceProfile(string AppName, string DisplayName, string ProfileJson, string ProfileDirectory);

/// <summary>
/// Reads the Actions Ring from the Logi Plugin Service store:
/// %LOCALAPPDATA%\Logi\LogiPluginService\Applications\Loupedeck72\&lt;app&gt;\Profiles\&lt;GUID&gt;\ProfileInfo.json.
/// Loupedeck72 is the Actions Ring: Loupedeck70 has 9 controls and Loupedeck71 has 4 (the Creative Console keypad and dialpad),
/// Loupedeck72 has the 8 of the ring, and LoupedeckSettings.ini names it as LastSeenDevice. Nothing under the store is ever
/// opened directly: each file is copied by <c>copyFile</c> into a fresh temp folder first (the same rule as OptionsPlusDb).
/// </summary>
public static class PluginServiceStore
{
    public const string RingDeviceType = "Loupedeck72";

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Logi", "LogiPluginService", "Applications");

    /// <summary>
    /// The ring profiles of every app found for the ring device (the global @_defaultwin first), or empty when the store is
    /// missing or unreadable. <paramref name="copyFile"/> (source, destination) is the only code that touches the originals;
    /// tests inject it to prove nothing else does.
    /// </summary>
    public static IReadOnlyList<PluginServiceProfile> Read(string? root = null, Action<string, string>? copyFile = null)
    {
        root ??= DefaultRoot;
        copyFile ??= CopyShared;
        string device = Path.Combine(root, RingDeviceType);
        var result = new List<PluginServiceProfile>();
        string temp = Path.Combine(Path.GetTempPath(), "slender-plugin-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (!Directory.Exists(device)) return result;
            Directory.CreateDirectory(temp);
            int n = 0;
            foreach (string appDir in Directory.EnumerateDirectories(device).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string app = Path.GetFileName(appDir);
                if (!app.StartsWith('@')) continue;
                try
                {
                    string? info = Stage(Path.Combine(appDir, "ApplicationInfo.json"), temp, ++n, copyFile);
                    if (info is null) continue;
                    JsonObject? appInfo = JsonNode.Parse(info) as JsonObject;
                    string display = OptionsPlusImporter.Str(appInfo, "displayName") ?? app;
                    string profiles = Path.Combine(appDir, "Profiles");
                    string? profileDir = OptionsPlusImporter.Str(appInfo, "defaultProfileName") is { Length: > 0 } name && Directory.Exists(Path.Combine(profiles, name))
                        ? Path.Combine(profiles, name)
                        : Directory.Exists(profiles) ? Directory.EnumerateDirectories(profiles).FirstOrDefault() : null;
                    if (profileDir is null) continue;
                    string? json = Stage(Path.Combine(profileDir, "ProfileInfo.json"), temp, ++n, copyFile);
                    if (json is not null) result.Add(new PluginServiceProfile(app, display, json, profileDir));
                }
                catch (System.Text.Json.JsonException)
                {
                    // one unreadable app does not hide the others
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (Exception) { /* temp leftovers are harmless */ }
        }

        return result.OrderBy(p => p.AppName == DefaultApp ? 0 : 1).ToList();
    }

    internal const string DefaultApp = "@_defaultwin";

    /// <summary>Copies one file (through <paramref name="copyFile"/>) into <paramref name="temp"/> and returns its text, or null when the original is missing.</summary>
    internal static string? Stage(string original, string temp, int n, Action<string, string> copyFile)
    {
        if (!File.Exists(original)) return null;
        string copy = Path.Combine(temp, n.ToString(System.Globalization.CultureInfo.InvariantCulture) + Path.GetExtension(original));
        copyFile(original, copy);
        return File.ReadAllText(copy);
    }

    /// <summary>Copies <paramref name="source"/> sharing everything, so a running Plugin Service is never blocked.</summary>
    public static void CopyShared(string source, string destination)
    {
        using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var dst = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        src.CopyTo(dst);
    }
}

/// <summary>
/// Converts the Plugin Service Actions Ring into Slender RingSlots. Control N (controlId 0..7) of a profile's first press page is
/// slot N: Options+ lays the ring out clockwise from the top, as the old radial-menu c408..c415 did. A press action is a macro
/// ("$@Generic___@Macro___id", steps in macroCommands), a profile action ("...@ProfileAction___id": a key chord, an
/// OpenFolder, a file to open...) or a native plugin command ("$DefaultWin___LockWorkstation"). Pure: JSON in, config and notes out.
/// Slots that cannot be expressed stay empty and are reported; a profile whose whole ring is unmappable is left as it was.
/// </summary>
public static class PluginServiceRingImporter
{
    private const string MacroPrefix = "$@Generic___@Macro___";

    /// <summary>
    /// Sets the default profile's and the matching app profiles' RingSlots from <paramref name="profiles"/>, replacing whatever
    /// ring they had (so importing again is safe). <paramref name="iconFor"/> (profile, action key) returns a "file:" icon path
    /// for the action's custom image or null; the Settings app copies the image there, tests pass null.
    /// </summary>
    public static SlenderConfig Apply(IReadOnlyList<PluginServiceProfile> profiles, SlenderConfig current, List<ImportNote> notes,
        Func<PluginServiceProfile, string, string?>? iconFor = null)
    {
        var merged = new List<Profile>(current.Profiles);
        foreach (PluginServiceProfile pp in profiles)
        {
            string? process;
            if (pp.AppName == PluginServiceStore.DefaultApp) process = null;
            else if (ProcessFor(pp.AppName) is { } known) process = known;
            else
            {
                notes.Add(new(ImportNoteKind.Skipped, pp.DisplayName + " ring", $"No Slender app profile is known for '{pp.AppName}'; its ring was not imported."));
                continue;
            }

            JsonObject? json;
            try { json = JsonNode.Parse(pp.ProfileJson) as JsonObject; }
            catch (System.Text.Json.JsonException) { json = null; }
            if (json is null)
            {
                notes.Add(new(ImportNoteKind.Unmapped, pp.DisplayName + " ring", "The Plugin Service profile could not be read; its ring was not imported."));
                continue;
            }

            string label = process is null ? "All apps ring" : pp.DisplayName + " ring";
            RingSlot[]? ring = Convert(pp, json, label, notes, iconFor);
            if (ring is null) continue;

            int i = merged.FindIndex(p => process is null
                ? p.AppProcess is null
                : p.AppProcess is not null && string.Equals(StripExe(p.AppProcess), process, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) merged[i] = merged[i] with { RingSlots = ring };
            else
            {
                var created = new Profile(process, new Dictionary<ushort, ActionSpec>()) { DisplayName = process is null ? "All apps" : pp.DisplayName, RingSlots = ring };
                if (process is null) merged.Insert(0, created); else merged.Add(created);
            }
        }

        if (profiles.Count > 0)
            notes.Add(new(ImportNoteKind.Info, "Actions Ring", "Imported from the Logi Plugin Service (device " + PluginServiceStore.RingDeviceType + "): control 0 is the top slot, then clockwise. Importing again replaces the ring of these profiles."));
        return current with { Profiles = merged };
    }

    private static string StripExe(string s) => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;

    private static string? ProcessFor(string app)
    {
        if (app.Equals("@_excel", StringComparison.OrdinalIgnoreCase)) return "excel";
        if (app.Equals("@_powerpoint", StringComparison.OrdinalIgnoreCase)) return "powerpnt";
        if (app.StartsWith("@_word", StringComparison.OrdinalIgnoreCase)) return "winword";
        return null;
    }

    // ---- one profile ----

    private sealed class Ctx(PluginServiceProfile profile, JsonObject json, string label, List<ImportNote> notes, Func<PluginServiceProfile, string, string?>? iconFor)
    {
        public PluginServiceProfile Profile { get; } = profile;
        public string Label { get; } = label;
        public List<ImportNote> Notes { get; } = notes;
        public Func<PluginServiceProfile, string, string?>? IconFor { get; } = iconFor;
        public Dictionary<string, JsonObject> Macros { get; } = Index(json["macroCommands"], "name");
        public Dictionary<string, JsonObject> Actions { get; } = Index(json["profileActions"], "name");
        public Dictionary<string, JsonObject> Folders { get; } = Index(json["layout"]?["folderPages"], "name");
        public JsonObject Json { get; } = json;
    }

    private static Dictionary<string, JsonObject> Index(JsonNode? array, string key)
    {
        var map = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode? n in array as JsonArray ?? [])
            if (n is JsonObject o && OptionsPlusImporter.Str(o, key) is { } k) map[k] = o;
        return map;
    }

    private static RingSlot[]? Convert(PluginServiceProfile pp, JsonObject json, string label, List<ImportNote> notes,
        Func<PluginServiceProfile, string, string?>? iconFor)
    {
        var ctx = new Ctx(pp, json, label, notes, iconFor);
        JsonObject? mode = (json["layout"]?["layoutModes"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
        var workspaces = mode?["workspaces"] as JsonArray ?? [];
        JsonObject? workspace = workspaces.OfType<JsonObject>().FirstOrDefault(w => OptionsPlusImporter.Str(w, "name") == OptionsPlusImporter.Str(mode, "homeWorkspaceName"))
            ?? workspaces.OfType<JsonObject>().FirstOrDefault();
        var pages = workspace?["pressPages"] as JsonArray ?? [];
        if (pages.FirstOrDefault() is not JsonObject page)
        {
            notes.Add(new(ImportNoteKind.Unmapped, label, "The profile has no ring page; its ring was not imported."));
            return null;
        }

        if (pages.Count > 1 || workspaces.Count > 1)
            notes.Add(new(ImportNoteKind.Unmapped, label, "Slender has one ring page per app; only the first page was imported."));

        RingSlot[] ring = Enumerable.Repeat(RingSlot.Empty, Profile.RingSlotCount).ToArray();
        foreach (JsonObject control in (page["controls"] as JsonArray ?? []).OfType<JsonObject>())
        {
            int id = control["controlId"] is JsonValue v && v.TryGetValue(out int c) ? c : -1;
            if (id is < 0 or >= Profile.RingSlotCount)
            {
                notes.Add(new(ImportNoteKind.Unmapped, label, $"Control {id} is outside the {Profile.RingSlotCount} ring slots; it was not imported."));
                continue;
            }

            string where = $"{label} / slot {id + 1}";
            if (OptionsPlusImporter.Str(control, "rotateAction") is { Length: > 0 } rotate)
                notes.Add(new(ImportNoteKind.Unmapped, where, $"Dial action '{Short(rotate)}' has no Slender equivalent (the ring has no dial binding)."));
            ring[id] = ConvertAction(ctx, OptionsPlusImporter.Str(control, "pressAction"), 0, where);
        }

        foreach (JsonObject rotatePage in (workspace?["rotatePages"] as JsonArray ?? []).OfType<JsonObject>())
            foreach (JsonObject control in (rotatePage["controls"] as JsonArray ?? []).OfType<JsonObject>())
                if (OptionsPlusImporter.Str(control, "pressAction") is { Length: > 0 } || OptionsPlusImporter.Str(control, "rotateAction") is { Length: > 0 })
                    notes.Add(new(ImportNoteKind.Unmapped, label, $"Dial page control {control["controlId"]} has an action; Slender has no dial pages."));

        if (ring.All(s => s.IsEmpty))
        {
            notes.Add(new(ImportNoteKind.Unmapped, label, "Nothing on this ring could be mapped, so the existing Slender ring was left as it was."));
            return null;
        }

        return ring;
    }

    private static string Short(string action) => action.Replace("$@Generic___", "").Replace("@", "");

    // ---- one press action ----

    private static RingSlot ConvertAction(Ctx ctx, string? action, int depth, string where)
    {
        if (string.IsNullOrWhiteSpace(action)) return RingSlot.Empty;

        if (action.StartsWith(MacroPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!ctx.Macros.TryGetValue(action[MacroPrefix.Length..], out JsonObject? macro))
            {
                ctx.Notes.Add(new(ImportNoteKind.Unmapped, where, "The macro is not in the profile."));
                return RingSlot.Empty;
            }

            string name = OptionsPlusImporter.Str(macro, "displayName") ?? "";
            return Finish(ctx, ConvertMacro(ctx, macro, name, where), name, action, where);
        }

        const string profileActionMarker = "@ProfileAction___";
        if (action.Contains(profileActionMarker, StringComparison.Ordinal))
        {
            ctx.Actions.TryGetValue(action, out JsonObject? pa);
            string name = OptionsPlusImporter.Str(pa, "displayName") ?? "";
            string template = OptionsPlusImporter.Str(pa, "templateActionName") ?? "";
            JsonObject? parameters = pa?["actionParameters"]?["parameters"] as JsonObject;
            string folderId = action[(action.IndexOf(profileActionMarker, StringComparison.Ordinal) + profileActionMarker.Length)..];

            if (template.EndsWith("@OpenFolder", StringComparison.Ordinal))
                folderId = OptionsPlusImporter.Str(parameters, "folderName") ?? folderId;
            else if (pa is not null)
            {
                Conv c = ConvertProfileAction(template, parameters, name);
                return Finish(ctx, c, name, action, where);
            }

            // a folder: the ProfileAction id doubles as the folder page's name
            if (!ctx.Folders.TryGetValue(folderId, out JsonObject? page))
            {
                ctx.Notes.Add(new(ImportNoteKind.Unmapped, where, $"Profile action '{name}' is not a known folder or command ({template}); the slot was left empty."));
                return RingSlot.Empty;
            }

            return ConvertFolder(ctx, page, name.Length > 0 ? name : OptionsPlusImporter.Str(page, "displayName") ?? "Folder", action, depth, where);
        }

        if (!action.StartsWith('$') || action.Contains("@AdjustmentAsCommand___", StringComparison.Ordinal) || action.Contains("___VirtualCommand___", StringComparison.Ordinal))
        {
            ctx.Notes.Add(new(ImportNoteKind.Unmapped, where, $"Plugin command '{Short(action)}' has no Slender equivalent; the slot was left empty."));
            return RingSlot.Empty;
        }

        if (Native.TryGetValue(action[1..], out (ActionSpec Spec, string Name, string? Approx) native))
        {
            if (native.Approx is not null) ctx.Notes.Add(new(ImportNoteKind.Approximated, where, native.Approx));
            return Finish(ctx, new Conv(native.Spec), native.Name, action, where);
        }

        ctx.Notes.Add(new(ImportNoteKind.Unmapped, where, $"Plugin command '{Short(action)}' has no Slender equivalent; the slot was left empty."));
        return RingSlot.Empty;
    }

    private static RingSlot ConvertFolder(Ctx ctx, JsonObject page, string name, string action, int depth, string where)
    {
        if (depth > 0)
        {
            ctx.Notes.Add(new(ImportNoteKind.Unmapped, where, $"A folder inside a folder ('{name}') is not supported; it was left empty."));
            return RingSlot.Empty;
        }

        var children = Enumerable.Repeat(RingSlot.Empty, Profile.RingSlotCount).ToArray();
        foreach (JsonObject control in (page["controls"] as JsonArray ?? []).OfType<JsonObject>())
        {
            int id = control["controlId"] is JsonValue v && v.TryGetValue(out int c) ? c : -1;
            string childWhere = $"{where} / {name} / item {id + 1}";
            if (id is < 0 or >= Profile.RingSlotCount)
            {
                if (OptionsPlusImporter.Str(control, "pressAction") is { Length: > 0 })
                    ctx.Notes.Add(new(ImportNoteKind.Unmapped, childWhere, $"Slender folders hold {Profile.RingSlotCount} actions; this one was not imported."));
                continue;
            }

            children[id] = ConvertAction(ctx, OptionsPlusImporter.Str(control, "pressAction"), depth + 1, childWhere);
        }

        RingSlot folder = new RingSlot(new ActionSpec.None()) { Name = name.Length > 0 ? name : "Folder", Folder = children };
        return folder with { Icon = ctx.IconFor?.Invoke(ctx.Profile, action) };
    }

    private static RingSlot Finish(Ctx ctx, Conv c, string name, string actionKey, string where)
    {
        string shown = name.Length > 0 ? $"{where} ({name})" : where;
        if (c.Action is null)
        {
            ctx.Notes.Add(new(ImportNoteKind.Unmapped, shown, c.Problem ?? "No Slender equivalent; the slot was left empty."));
            return RingSlot.Empty;
        }

        if (c.Approximation is not null) ctx.Notes.Add(new(ImportNoteKind.Approximated, shown, c.Approximation));
        return new RingSlot(c.Action) { Name = name.Length > 0 ? name : null, Icon = ctx.IconFor?.Invoke(ctx.Profile, actionKey) };
    }

    private sealed record Conv(ActionSpec? Action, string? Problem = null, string? Approximation = null);

    // ---- macros ----

    private abstract record Step
    {
        public sealed record Open(ActionSpec.Launch Launch, string? Note = null) : Step;
        public sealed record Key(string Chord) : Step;
        public sealed record Type(string Text) : Step;
        public sealed record Sleep : Step;
        public sealed record Other(string Why) : Step;
    }

    private static Conv ConvertMacro(Ctx ctx, JsonObject macro, string name, string where)
    {
        var editors = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonObject e in (macro["actionEditorCommands"] as JsonArray ?? []).OfType<JsonObject>())
            if (OptionsPlusImporter.Str(e, "name") is { } n) editors[n] = e;

        var steps = new List<Step>();
        var approx = new List<string>();
        foreach (JsonNode? n in macro["actions"] as JsonArray ?? [])
        {
            string a = n is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";
            if (a.Length == 0) continue;
            if (editors.TryGetValue(a, out JsonObject? ed))
                steps.Add(EditorStep(OptionsPlusImporter.Str(ed, "templateName") ?? "", ed["actionParameters"] as JsonObject, approx));
            else
                steps.Add(PlainStep(a));
        }

        var real = steps.Where(s => s is not Step.Sleep).ToList();
        if (real.Count == 0) return new Conv(null, "The macro has no steps.");
        if (real.OfType<Step.Other>().FirstOrDefault() is { } bad && real.Count == 1) return new Conv(null, bad.Why);

        if (real.Count == 1)
        {
            return real[0] switch
            {
                Step.Open o => new Conv(o.Launch, null, Join(o.Note is null ? approx : [.. approx, o.Note])),
                Step.Key k => new Conv(new ActionSpec.Keys(k.Chord), null, Join(approx)),
                Step.Type t => new Conv(new ActionSpec.Text(t.Text), null, Join(approx)),
                _ => new Conv(null, "No Slender equivalent."),
            };
        }

        if (real.All(s => s is Step.Key))
        {
            string chords = string.Join(", ", real.Cast<Step.Key>().Select(k => k.Chord));
            if (steps.Any(s => s is Step.Sleep)) approx.Add($"The key sequence '{chords}' is sent about 30 ms apart; the macro's delays were dropped.");
            return new Conv(new ActionSpec.Keys(chords), null, Join(approx));
        }

        if (real[0] is Step.Open first && !real.Skip(1).Any(s => s is Step.Other))
        {
            if (first.Note is not null) approx.Add(first.Note);
            approx.Add($"Only the first step (open {first.Launch.Target}) was imported; Slender runs one action per slot, so the {real.Count - 1} following step(s) ({Describe(real.Skip(1))}) were dropped.");
            return new Conv(first.Launch, null, Join(approx));
        }

        return new Conv(null, $"Macro '{name}' has several steps ({Describe(real)}) that Slender cannot run as one action; the slot was left empty.");
    }

    private static string? Join(List<string> approx) => approx.Count == 0 ? null : string.Join(" ", approx);

    private static string Describe(IEnumerable<Step> steps) => string.Join(", ", steps.Select(s => s switch
    {
        Step.Open o => "open " + o.Launch.Target,
        Step.Key k => k.Chord,
        Step.Type => "type text",
        Step.Other o => o.Why,
        _ => "wait",
    }));

    private static Step EditorStep(string template, JsonObject? p, List<string> approx)
    {
        switch (template)
        {
            case "$@Generic___@KeyboardKey":
                string spec = OptionsPlusImporter.Str(p, "keyboardKey") ?? "";
                if (TryChord(spec, out string chord, out string? a))
                {
                    if (a is not null) approx.Add(a);
                    return new Step.Key(chord);
                }

                return new Step.Other($"key '{spec.Split("___")[0]}' has no Slender chord name");
            case "$@Generic___@SendText":
                return OptionsPlusImporter.Str(p, "text") is { Length: > 0 } text ? new Step.Type(text) : new Step.Other("empty text");
            default:
                return new Step.Other($"step '{template.Replace("$@Generic___@", "")}'");
        }
    }

    private static Step PlainStep(string a)
    {
        if (a.StartsWith("$@Generic___@Sleep___", StringComparison.Ordinal)) return new Step.Sleep();
        if (a.StartsWith("$@Generic___@ExecuteApplication___", StringComparison.Ordinal))
            return LaunchStep(a["$@Generic___@ExecuteApplication___".Length..], true);
        if (a.StartsWith("$@Generic___@OpenUrl___", StringComparison.Ordinal))
            return LaunchStep(a["$@Generic___@OpenUrl___".Length..], false);
        return new Step.Other($"step '{a.Replace("$@Generic___@", "")}'");
    }

    /// <summary>"target||arguments||working folder" (ExecuteApplication) or a URL or path (OpenUrl).</summary>
    private static Step LaunchStep(string value, bool withArguments)
    {
        string[] parts = withArguments ? value.Split("||") : [value];
        string target = parts[0].Trim();
        string? args = withArguments && parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : null;
        if (target.Length == 0) return new Step.Other("open with no target");
        if (IsMacPath(target)) return new Step.Other($"macOS path '{target}'");
        string? workDir = withArguments && parts.Length > 2 && parts[2].Trim().Length > 0 ? parts[2].Trim() : null;
        string? note = workDir is null ? null : $"Working folder '{workDir}' is not supported; the app starts in its own folder.";
        return new Step.Open(new ActionSpec.Launch(target, args), note);
    }

    private static bool IsMacPath(string target) =>
        target.StartsWith('/') || target.EndsWith(".app", StringComparison.OrdinalIgnoreCase) || target.StartsWith("~/", StringComparison.Ordinal);

    // ---- profile actions ----

    private static Conv ConvertProfileAction(string template, JsonObject? p, string name)
    {
        switch (template)
        {
            case "$@Generic___@KeyboardKey":
                string spec = OptionsPlusImporter.Str(p, "keyboardKey") ?? "";
                return TryChord(spec, out string chord, out string? approx)
                    ? new Conv(new ActionSpec.Keys(chord), null, approx)
                    : new Conv(null, $"Key '{spec.Split("___")[0]}' has no Slender chord name.");
            case "$@Generic___@SendText":
                return OptionsPlusImporter.Str(p, "text") is { Length: > 0 } text ? new Conv(new ActionSpec.Text(text)) : new Conv(null, "Empty text.");
            case "$@Generic___@ShellExecute":
                return OptionsPlusImporter.Str(p, "filePath") is { Length: > 0 } path && !IsMacPath(path)
                    ? new Conv(new ActionSpec.Launch(path))
                    : new Conv(null, "The file to open is a macOS path or empty.");
            default:
                return new Conv(null, $"Plugin action '{template.Replace("$@Generic___@", "")}' has no Slender equivalent.");
        }
    }

    // ---- native plugin commands ----

    private static readonly Dictionary<string, (ActionSpec Spec, string Name, string? Approx)> Native = new(StringComparer.Ordinal)
    {
        ["DefaultWin___LockWorkstation"] = (new ActionSpec.Keys("Win+L"), "Lock", null),
        ["DefaultWin___MediaPlayPause"] = (new ActionSpec.Keys("MediaPlayPause"), "Play / pause", null),
        ["Excel___PasteSpecialDialogCommand"] = (new ActionSpec.Keys("Ctrl+Alt+V"), "Paste special", null),
        ["Excel___CreateTableCommand"] = (new ActionSpec.Keys("Ctrl+T"), "Create table", null),
        ["PowerPoint___UndoCommand"] = (new ActionSpec.Keys("Ctrl+Z"), "Undo", null),
        ["PowerPoint___SaveCommand"] = (new ActionSpec.Keys("Ctrl+S"), "Save", null),
        ["PowerPoint___ClosePresentationCommand"] = (new ActionSpec.Keys("Ctrl+W"), "Close", null),
        ["PowerPoint___OpenPresentationCommand"] = (new ActionSpec.Keys("Ctrl+O"), "Open", null),
        ["PowerPoint___DeleteSlidesCommand"] = (new ActionSpec.Keys("Delete"), "Delete slides", "Delete slides became the Delete key, which deletes the selected slides only while the slide thumbnails have focus."),
        ["PowerPoint___DuplicateSlideCommand"] = (new ActionSpec.Keys("Ctrl+D"), "Duplicate slide", "Duplicate slide became Ctrl+D, which duplicates the selected slide only while the slide thumbnails have focus."),
    };

    // ---- "ControlOrCommand+KeyN___67699721___Ctrl+N___win-..." ----

    /// <summary>The first "___" field is the key as the Plugin Service names it; the rest is the layout it was recorded on.</summary>
    internal static bool TryChord(string spec, out string chord, out string? approximation)
    {
        chord = "";
        approximation = null;
        string first = spec.Split("___")[0];
        if (first.Length == 0) return false;
        bool ctrl = false, shift = false, alt = false, win = false;
        string? main = null;
        foreach (string token in first.Split('+'))
        {
            switch (token)
            {
                case "ControlOrCommand" or "Control" or "Ctrl": ctrl = true; break;
                case "Shift": shift = true; break;
                case "Alt" or "Option": alt = true; break;
                case "Command" or "Meta" or "Win": win = true; break;
                default:
                    if (main is not null || MainKey(token, ref approximation) is not { } key) return false;
                    main = key;
                    break;
            }
        }

        if (main is null) return false;
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        if (win) parts.Add("Win");
        parts.Add(main);
        chord = string.Join('+', parts);
        return ChordParser.TryParse(chord, out _);
    }

    private static string? MainKey(string token, ref string? approximation)
    {
        if (token.Length == 4 && token.StartsWith("Key", StringComparison.Ordinal) && char.IsAsciiLetterOrDigit(token[3])) return char.ToUpperInvariant(token[3]).ToString();
        if (token.Length == 6 && token.StartsWith("Digit", StringComparison.Ordinal) && char.IsAsciiDigit(token[5])) return token[5].ToString();
        if (token.Length is 2 or 3 && token[0] == 'F' && int.TryParse(token.AsSpan(1), out int f) && f is >= 1 and <= 24) return token;
        switch (token)
        {
            case "Return" or "Enter" or "NumpadEnter": return "Enter";
            case "Escape" or "Esc": return "Esc";
            case "Tab" or "Space" or "Backspace" or "Delete" or "Insert" or "Home" or "End" or "PageUp" or "PageDown" or "Comma" or "Period" or "Slash" or "Semicolon" or "Backslash" or "Left" or "Right" or "Up" or "Down" or "PrintScreen":
                return token;
            case "ArrowLeft": return "Left";
            case "ArrowRight": return "Right";
            case "ArrowUp": return "Up";
            case "ArrowDown": return "Down";
            case "Oem1": return "Semicolon";
            case "Oem2": return "Slash";
            case "Oem3" or "Backquote": return "Backtick";
            case "Oem5": return "Backslash";
            case "Minus" or "OemMinus": return "Minus";
            case "Equal": return "Equals";
            case "Plus" or "OemPlus": return "Plus";
            case "Subtract":
                approximation = "The numpad minus key was sent as the main minus key.";
                return "Minus";
            case "Add":
                approximation = "The numpad plus key was sent as the main plus key.";
                return "Plus";
            default: return null;
        }
    }
}
