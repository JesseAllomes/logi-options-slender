using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slender.Hidpp.Inventory;

/// <summary>Writes a <see cref="DeviceInventory"/> as JSON and Markdown.</summary>
public static class InventoryReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Reflection-based JSON: only the probe tool calls this (never the tray), so the AOT/trim requirement is declared here instead of failing the tray publish.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Probe tool only; the tray never calls it.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Probe tool only; the tray never calls it.")]
    public static string ToJson(DeviceInventory inv) => JsonSerializer.Serialize(inv, JsonOptions);

    private static string Cell(string? s) => (s ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static string Value(object? v) => v switch
    {
        null => "",
        string s => s,
        System.Collections.IEnumerable e when v is not string => "[" + string.Join(", ", e.Cast<object?>().Select(Value)) + "]",
        _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    public static string ToMarkdown(DeviceInventory inv)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Device inventory: {inv.Device}");
        sb.AppendLine();
        sb.AppendLine("Read-only HID++ 2.0 inventory. Only functions in the vetted getter table were sent; features marked listed-only were never written to.");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {inv.GeneratedAt}");
        sb.AppendLine($"- Device index 0x{inv.DeviceIndex:X2}, SwId 0x{inv.SwId:X2}, protocol {inv.ProtocolVersion ?? "unknown"}");
        if (inv.ProductId is { } pid) sb.AppendLine($"- Product id 0x{pid:X4}");
        if (inv.HidCollection != null) sb.AppendLine($"- HID collection: {inv.HidCollection}");
        if (inv.DeviceName != null) sb.AppendLine($"- Name: {inv.DeviceName} ({inv.DeviceType ?? "type unknown"})");
        if (inv.FriendlyName != null) sb.AppendLine($"- Friendly name: {inv.FriendlyName}");
        sb.AppendLine($"- Features reported: {inv.FeatureCount}; requests sent {inv.RequestsSent}; errors {inv.ErrorCount}");
        if (inv.Aborted != null) sb.AppendLine($"- Stopped early: {inv.Aborted}");
        sb.AppendLine();

        sb.AppendLine("## Firmware entities");
        sb.AppendLine();
        if (inv.Firmware.Count == 0) sb.AppendLine("None read.");
        else
        {
            sb.AppendLine("| Entity | Kind | Name | Version | Build | Active |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var f in inv.Firmware)
                sb.AppendLine($"| {f.Entity} | {Cell(f.Kind)} | {Cell(f.Name)} | {f.Version} | {f.Build} | {(f.Active ? "yes" : "no")} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Features");
        sb.AppendLine();
        sb.AppendLine("| Index | Id | Name | Version | Flags | Disposition | Why |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var f in inv.Features)
        {
            var name = f.Name + (f.OptionsPlusNote != null ? $" ({f.OptionsPlusNote})" : "");
            sb.AppendLine($"| {f.Index} | {f.IdHex} | {Cell(name)} | {f.Version?.ToString() ?? "-"} | {Cell(string.Join(", ", f.Flags))} (0x{f.TypeFlagsRaw:X2}) | {f.Disposition} | {Cell(f.Reason)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Getter results");
        foreach (var f in inv.Features.Where(f => f.Calls.Count > 0 && f.Id > 0x0001))
        {
            sb.AppendLine();
            sb.AppendLine($"### {f.IdHex} {f.Name} (index {f.Index}, v{f.Version})");
            sb.AppendLine();
            sb.AppendLine("| Call | Request | Status | Decoded |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var c in f.Calls)
            {
                string status = c.Status == "hid-error" ? $"HID++ error 0x{c.ErrorCode:X2}" : c.Status;
                string decoded = c.Decoded == null ? (c.ReplyHex ?? "") : string.Join("; ", c.Decoded.Select(kv => $"{kv.Key}={Value(kv.Value)}"));
                sb.AppendLine($"| fn{c.Function} {Cell(c.Getter)} | {c.RequestHex} | {status} | {Cell(decoded)} |");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## Controls (0x1B04)");
        sb.AppendLine();
        if (inv.Cids.Count == 0) sb.AppendLine("None read.");
        else
        {
            sb.AppendLine("| CID | Name | Options+ label | Task | Flags | Pos | Group | GMask | Reporting |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var c in inv.Cids)
            {
                string rep = c.Reporting == null ? "" : $"{Value(c.Reporting["flagNames"])} -> {c.Reporting["mappedTo"]}";
                sb.AppendLine($"| {c.CidHex} | {Cell(c.Name)} | {Cell(c.OptionsPlusLabel)} | 0x{c.Task:X4} | {Cell(string.Join(", ", c.FlagNames))} (0x{c.Flags:X3}) | {c.Position} | {c.Group} | 0x{c.GroupMask:X2} | {Cell(rep)} |");
            }
        }
        sb.AppendLine();

        if (inv.OptionsPlusSlots.Count > 0)
        {
            sb.AppendLine("## Options+ non-control slots (read-only metadata)");
            sb.AppendLine();
            foreach (var s in inv.OptionsPlusSlots) sb.AppendLine($"- {s}");
        }
        return sb.ToString();
    }

    /// <summary>Writes <paramref name="slug"/>.inventory.json and .md into <paramref name="folder"/> and returns both paths.</summary>
    public static (string Json, string Markdown) Write(DeviceInventory inv, string folder, string slug)
    {
        Directory.CreateDirectory(folder);
        var json = Path.Combine(folder, $"{slug}.inventory.json");
        var md = Path.Combine(folder, $"{slug}.inventory.md");
        File.WriteAllText(json, ToJson(inv));
        File.WriteAllText(md, ToMarkdown(inv));
        return (json, md);
    }
}
