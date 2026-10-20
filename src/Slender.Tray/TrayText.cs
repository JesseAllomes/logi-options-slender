namespace Slender.Tray;

/// <summary>Menu status-line wording, kept pure so it can be tested.</summary>
public static class TrayText
{
    public const string Paused = "Paused — Logi Options+ is running";

    public static string Device(string name, bool connected, int? percent, bool charging, string? estimateShort = null) =>
        !connected
            ? $"{name} — not connected"
            : percent is { } p
                ? $"{name} — connected, {p}%{(charging ? " charging" : string.IsNullOrEmpty(estimateShort) ? string.Empty : $", {estimateShort} left")}"
                : $"{name} — connected";
}
