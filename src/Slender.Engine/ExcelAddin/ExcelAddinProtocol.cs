using System.Globalization;

namespace Slender.Engine.ExcelAddin;

// This file and ExcelAddinLogic.cs have no dependencies on the rest of the engine: the Excel add-in project compiles them in
// directly (see src/Slender.ExcelAddin) so the add-in and the tray can never disagree about the wire format.

/// <summary>What Slender can ask the add-in to do.</summary>
public enum AddinVerb
{
    /// <summary>Answers at once from the pipe thread; never touches Excel. Used to tell whether the add-in is there.</summary>
    Ping,
    NextSheet,
    PreviousSheet,
    ZoomIn,
    ZoomOut,
    ZoomReset,
    TableStyle,
    ChartType,
    /// <summary>Context-aware thumb wheel. Argument: columns, positive = right.</summary>
    Wheel,
    /// <summary>Runs a VBA macro by name. Argument: the macro name.</summary>
    Macro,
}

/// <summary>
/// Ok = done. NotApplicable = Excel is fine but the selection does not suit the verb (no table or chart selected): done, do not
/// retry another way. Busy = Excel could not run it inside the time box (edit mode, dialog open). Failed = Excel refused.
/// </summary>
public enum AddinStatus { Ok, NotApplicable, Busy, Failed, BadRequest }

public readonly record struct AddinRequest(int Id, AddinVerb Verb, string? Arg = null);

public readonly record struct AddinReply(int Id, AddinStatus Status, string? Text = null);

/// <summary>
/// One line per message, UTF-8, ending in a newline, at most <see cref="MaxLineChars"/> characters. The client sends exactly one
/// request per connection and reads one reply.
/// <code>
/// request: S1 &lt;id&gt; &lt;verb&gt;[ &lt;arg&gt;]      e.g. "S1 7 wheel -3", "S1 8 macro Module1.Tidy"
/// reply:   S1 &lt;id&gt; &lt;ok|na|busy|fail|bad&gt;[ &lt;text&gt;]
/// </code>
/// The pipe is named "Slender.Excel.&lt;excel pid&gt;" and only the current user can open it. No network port is involved.
/// </summary>
public static class ExcelAddinProtocol
{
    public const string Version = "S1";
    public const int MaxLineChars = 256;
    public const int MaxWheelColumns = 1000;
    public const int MaxMacroNameChars = 128;

    public static string PipeName(int excelProcessId) => "Slender.Excel." + excelProcessId.ToString(CultureInfo.InvariantCulture);

    public static string VerbName(AddinVerb verb) => verb switch
    {
        AddinVerb.Ping => "ping",
        AddinVerb.NextSheet => "next-sheet",
        AddinVerb.PreviousSheet => "prev-sheet",
        AddinVerb.ZoomIn => "zoom-in",
        AddinVerb.ZoomOut => "zoom-out",
        AddinVerb.ZoomReset => "zoom-reset",
        AddinVerb.TableStyle => "table-style",
        AddinVerb.ChartType => "chart-type",
        AddinVerb.Wheel => "wheel",
        AddinVerb.Macro => "macro",
        _ => throw new ArgumentOutOfRangeException(nameof(verb)),
    };

    private static bool TryParseVerb(string text, out AddinVerb verb)
    {
        foreach (var v in Enum.GetValues<AddinVerb>())
        {
            if (VerbName(v) == text) { verb = v; return true; }
        }
        verb = default;
        return false;
    }

    private static bool TakesArgument(AddinVerb verb) => verb is AddinVerb.Wheel or AddinVerb.Macro;

    /// <summary>The request line without the newline, or null when the request is not valid (bad macro name, wheel out of range).</summary>
    public static string? Encode(AddinRequest request)
    {
        if (request.Id < 0 || !ArgumentValid(request.Verb, request.Arg)) return null;
        var line = $"{Version} {request.Id.ToString(CultureInfo.InvariantCulture)} {VerbName(request.Verb)}";
        if (TakesArgument(request.Verb)) line += " " + request.Arg;
        return line.Length <= MaxLineChars ? line : null;
    }

    public static bool TryDecodeRequest(string? line, out AddinRequest request)
    {
        request = default;
        if (!SplitLine(line, out var id, out var rest)) return false;
        var space = rest.IndexOf(' ');
        var verbText = space < 0 ? rest : rest[..space];
        var arg = space < 0 ? null : rest[(space + 1)..];
        if (!TryParseVerb(verbText, out var verb)) return false;
        if (TakesArgument(verb) == (arg is null)) return false; // an argument exactly where a verb takes one
        if (!ArgumentValid(verb, arg)) return false;
        request = new AddinRequest(id, verb, arg);
        return true;
    }

    public static string Encode(AddinReply reply)
    {
        var line = $"{Version} {reply.Id.ToString(CultureInfo.InvariantCulture)} {StatusName(reply.Status)}";
        if (!string.IsNullOrEmpty(reply.Text)) line += " " + Sanitise(reply.Text);
        return line.Length <= MaxLineChars ? line : line[..MaxLineChars];
    }

    public static bool TryDecodeReply(string? line, out AddinReply reply)
    {
        reply = default;
        if (!SplitLine(line, out var id, out var rest)) return false;
        var space = rest.IndexOf(' ');
        var statusText = space < 0 ? rest : rest[..space];
        var text = space < 0 ? null : rest[(space + 1)..];
        AddinStatus? status = statusText switch
        {
            "ok" => AddinStatus.Ok,
            "na" => AddinStatus.NotApplicable,
            "busy" => AddinStatus.Busy,
            "fail" => AddinStatus.Failed,
            "bad" => AddinStatus.BadRequest,
            _ => null,
        };
        if (status is null) return false;
        reply = new AddinReply(id, status.Value, text);
        return true;
    }

    public static string StatusName(AddinStatus status) => status switch
    {
        AddinStatus.Ok => "ok",
        AddinStatus.NotApplicable => "na",
        AddinStatus.Busy => "busy",
        AddinStatus.Failed => "fail",
        _ => "bad",
    };

    /// <summary>
    /// A macro name as VBA's Application.Run takes it: letters, digits, underscore, dot, '!' and an apostrophe. No spaces, quotes,
    /// parentheses or path characters: the name is never a way to pass anything but a macro to run.
    /// </summary>
    public static bool IsValidMacroName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxMacroNameChars) return false;
        if (!(char.IsAsciiLetter(name[0]) || name[0] == '_' || name[0] == '\'')) return false;
        foreach (var ch in name)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or '!' or '\'')) return false;
        }
        return true;
    }

    public static bool TryParseWheelColumns(string? arg, out int columns)
    {
        columns = 0;
        return arg is not null && int.TryParse(arg, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out columns)
            && columns != 0 && Math.Abs(columns) <= MaxWheelColumns;
    }

    private static bool ArgumentValid(AddinVerb verb, string? arg) => verb switch
    {
        AddinVerb.Wheel => TryParseWheelColumns(arg, out _),
        AddinVerb.Macro => IsValidMacroName(arg),
        _ => arg is null,
    };

    private static bool SplitLine(string? line, out int id, out string rest)
    {
        id = 0;
        rest = string.Empty;
        if (line is null || line.Length > MaxLineChars || line.Length < 6) return false;
        foreach (var ch in line)
        {
            if (ch < ' ' || ch == 0x7F) return false; // no control characters, so no embedded newlines
        }
        if (!line.StartsWith(Version + " ", StringComparison.Ordinal)) return false;
        var body = line[(Version.Length + 1)..];
        var space = body.IndexOf(' ');
        if (space <= 0 || space == body.Length - 1) return false;
        if (!int.TryParse(body[..space], NumberStyles.None, CultureInfo.InvariantCulture, out id)) return false;
        rest = body[(space + 1)..];
        return true;
    }

    private static string Sanitise(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] < ' ' || chars[i] == 0x7F) chars[i] = ' ';
        }
        return new string(chars);
    }
}
