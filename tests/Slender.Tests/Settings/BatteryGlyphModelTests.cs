using System.Globalization;
using System.Text;
using Slender.Engine;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public sealed class BatteryGlyphModelTests
{
    private static BatteryGlyphModel M(int? percent, bool charging = false, string? state = null, int threshold = 15, bool hidden = false, string? word = null) =>
        BatteryGlyphModel.Create(percent, charging, state, threshold, hidden, word);

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 0.01)]
    [InlineData(73, 0.73)]
    [InlineData(99, 0.99)]
    [InlineData(100, 1.0)]
    public void Fill_is_exactly_the_percent(int percent, double fraction)
    {
        Assert.Equal(fraction, M(percent, threshold: 0).FillFraction, 10);
        double inner = BatteryGlyphGeometry.InnerWidth(BatteryGlyphSize.Large);
        Assert.Equal(inner * fraction, BatteryGlyphGeometry.For(BatteryGlyphSize.Large, fraction).Fill.Width, 10);
    }

    [Fact]
    public void Percent_out_of_range_is_clamped()
    {
        Assert.Equal(0, M(-5).FillFraction);
        Assert.Equal("100%", M(140, threshold: 0).Label);
    }

    [Fact]
    public void Normal_above_threshold_and_low_at_or_under_it()
    {
        Assert.Equal(BatteryGlyphKind.Normal, M(16).Kind);
        Assert.Equal(BatteryGlyphKind.Low, M(15).Kind);
        Assert.Equal(BatteryGlyphKind.Low, M(0).Kind);
        Assert.Equal("Battery 15 percent, low", M(15).AccessibleText);
        Assert.Equal("Battery 73 percent", M(73).AccessibleText);
        Assert.Equal("73%", M(73).Label);
        Assert.False(M(73).ShowBolt);
    }

    [Fact]
    public void Thresholds_are_per_device()
    {
        var alerts = new BatteryAlertSettings(true, 15, 10);
        Assert.Equal(15, BatteryGlyphModel.ThresholdFor(DeviceIds.Mouse, alerts));
        Assert.Equal(10, BatteryGlyphModel.ThresholdFor(DeviceIds.Keyboard, alerts));
        Assert.Equal(15, BatteryGlyphModel.ThresholdFor(DeviceIds.Headset, alerts));
        Assert.Equal(BatteryGlyphKind.Low, M(12, threshold: BatteryGlyphModel.ThresholdFor(DeviceIds.Mouse, alerts)).Kind);
        Assert.Equal(BatteryGlyphKind.Normal, M(12, threshold: BatteryGlyphModel.ThresholdFor(DeviceIds.Keyboard, alerts)).Kind);
        Assert.Equal(25, BatteryGlyphModel.ThresholdFor(DeviceIds.Mouse, new BatteryAlertSettings(true, 25, 10)));
    }

    [Fact]
    public void Charging_is_green_with_a_bolt_and_never_low()
    {
        BatteryGlyphModel m = M(5, charging: true, state: "charging");
        Assert.Equal(BatteryGlyphKind.Charging, m.Kind);
        Assert.True(m.ShowBolt);
        Assert.Equal(0.05, m.FillFraction, 10);
        Assert.Equal("5%", m.Label);
        Assert.Equal("Battery 5 percent, charging", m.AccessibleText);
        Assert.Equal(BatteryGlyphKind.Charging, M(50, charging: true).Kind); // older tray: no ChargeState
    }

    [Fact]
    public void Full_on_the_charger_reads_Charged_with_a_full_fill()
    {
        foreach (BatteryGlyphModel m in new[] { M(100, true, "full"), M(100, true), M(97, true, "full"), M(null, true, "full") })
        {
            Assert.Equal(BatteryGlyphKind.Full, m.Kind);
            Assert.Equal(1, m.FillFraction);
            Assert.Equal("Charged", m.Label);
            Assert.False(m.ShowBolt);
        }

        Assert.Equal(BatteryGlyphKind.Normal, M(100, false).Kind); // 100% off the charger is just a full battery
        Assert.Equal("100%", M(100).Label);
    }

    [Fact]
    public void Error_shows_Charge_fault_whatever_the_level()
    {
        foreach (BatteryGlyphModel m in new[] { M(40, true, "error"), M(null, true, "error"), M(3, true, "error") })
        {
            Assert.Equal(BatteryGlyphKind.Error, m.Kind);
            Assert.Equal("Charge fault", m.Label);
            Assert.False(m.ShowBolt);
            Assert.Equal("Battery charge fault", m.AccessibleText);
        }
    }

    [Fact]
    public void Unknown_level_is_an_empty_outline_and_a_dash_never_a_number()
    {
        BatteryGlyphModel m = M(null);
        Assert.Equal(BatteryGlyphKind.Unknown, m.Kind);
        Assert.Equal(0, m.FillFraction);
        Assert.Equal(BatteryGlyphModel.Dash, m.Label);
        Assert.Equal("Battery level unknown", m.AccessibleText);
        Assert.False(m.Dimmed);
        BatteryGlyphModel charging = M(null, true, "charging");
        Assert.Equal(BatteryGlyphModel.Dash, charging.Label);
        Assert.True(charging.ShowBolt);
        Assert.Equal(0, charging.FillFraction);
    }

    [Fact]
    public void Hidden_device_hides_the_last_known_state_and_dims()
    {
        BatteryGlyphModel m = M(73, true, "charging", hidden: true);
        Assert.Equal(BatteryGlyphKind.Unknown, m.Kind);
        Assert.Equal(0, m.FillFraction);
        Assert.False(m.ShowBolt);
        Assert.True(m.Dimmed);
        Assert.Equal(BatteryGlyphModel.Dash, m.Label);
        Assert.Equal("Off", M(73, hidden: true, word: "Off").Label);
        Assert.Equal("Asleep", M(null, word: "Asleep").Label);
    }

    private static SlenderStatus Status(params DeviceState[] devices) => new(devices, false, null, DateTimeOffset.UnixEpoch);

    [Fact]
    public void From_status_follows_presence_rules()
    {
        var alerts = new BatteryAlertSettings();
        var mouse = new DeviceState(DeviceIds.Mouse, "MX", true, 12, false) { Presence = DevicePresenceView.Active };
        Assert.Equal(BatteryGlyphKind.Low, BatteryGlyphModel.For(Status(mouse), DeviceIds.Mouse, alerts).Kind);

        var gone = mouse with { Connected = false, Presence = DevicePresenceView.Disconnected };
        BatteryGlyphModel off = BatteryGlyphModel.For(Status(gone), DeviceIds.Mouse, alerts);
        Assert.True(off.Dimmed);
        Assert.Equal("Off", off.Label);

        var asleep = mouse with { Presence = DevicePresenceView.Sleeping };
        Assert.False(BatteryGlyphModel.For(Status(asleep), DeviceIds.Mouse, alerts).Dimmed);

        Assert.True(BatteryGlyphModel.For(null, DeviceIds.Mouse, alerts).Dimmed);

        var headset = new DeviceState(DeviceIds.Headset, "WH64", true, null, false)
        {
            Presence = DevicePresenceView.Active,
            Headset = new HeadsetState(true, 14, true, true, null, true),
        };
        BatteryGlyphModel h = BatteryGlyphModel.For(Status(headset), DeviceIds.Headset, alerts);
        Assert.Equal(BatteryGlyphKind.Charging, h.Kind);
        Assert.Equal("14%", h.Label);
    }

    [Fact]
    public void Low_check_uses_the_configured_threshold_and_ignores_charging()
    {
        var s = new DeviceState(DeviceIds.Keyboard, "K", true, 12, false);
        Assert.False(BatteryGlyphModel.IsLow(s, 10));
        Assert.True(BatteryGlyphModel.IsLow(s, 12));
        Assert.False(BatteryGlyphModel.IsLow(s with { Charging = true }, 50));
        Assert.False(BatteryGlyphModel.IsLow(s with { BatteryPercent = null }, 50));
        Assert.False(BatteryGlyphModel.IsLow(null, 50));
    }

    [Fact]
    public void Geometry_stays_inside_the_glyph_at_both_sizes()
    {
        foreach (BatteryGlyphSize size in new[] { BatteryGlyphSize.Small, BatteryGlyphSize.Large })
        {
            BatteryGlyphGeometry g = BatteryGlyphGeometry.For(size, 0.5);
            Assert.True(g.Cap.X < g.Width && g.Cap.X + g.Cap.Width <= g.Width + 1e-9);
            Assert.True(g.Fill.Y > g.Body.Y && g.Fill.Y + g.Fill.Height < g.Body.Y + g.Body.Height);
            Assert.All(g.Bolt, p => Assert.InRange(p.X, 0, g.Cap.X));
            Assert.All(g.Bolt, p => Assert.InRange(p.Y, 0, g.Height));
        }
    }

    /// <summary>Opt-in: SLENDER_BATTERY_PREVIEW=path writes an HTML sheet of every state from the same geometry the control draws.</summary>
    [Fact]
    public void Writes_the_preview_when_asked()
    {
        string? path = Environment.GetEnvironmentVariable("SLENDER_BATTERY_PREVIEW");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, BatteryPreview.Html());
        Assert.True(File.Exists(path));
    }
}

internal static class BatteryPreview
{
    private sealed record Palette(string Name, string Ground, string Card, string Ink, string Muted, string Live, string Danger, string Warn);

    private static readonly Palette[] Palettes =
    [
        new("Dark (app theme)", "#1B1F23", "#22282D", "#ECEFF1", "#8F989F", "#6CCB9A", "#E06A5C", "#E3B04B"),
        new("Light (future decode, for contrast)", "#F4F6F7", "#FFFFFF", "#1B1F23", "#5B656C", "#1E8E5A", "#C4392B", "#A87100"),
    ];

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Rect(GlyphRect r, string attrs) =>
        $"<rect x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.Width)}\" height=\"{F(r.Height)}\" rx=\"{F(r.Radius)}\" ry=\"{F(r.Radius)}\" {attrs}/>";

    private static string Glyph(BatteryGlyphModel m, BatteryGlyphSize size, Palette p, double scale)
    {
        BatteryGlyphGeometry g = BatteryGlyphGeometry.For(size, m.FillFraction);
        string outline = m.Kind == BatteryGlyphKind.Error ? p.Warn : p.Muted;
        string fill = m.Kind switch { BatteryGlyphKind.Low => p.Danger, BatteryGlyphKind.Charging or BatteryGlyphKind.Full => p.Live, _ => p.Ink };
        string label = m.Kind switch { BatteryGlyphKind.Low => p.Danger, BatteryGlyphKind.Error => p.Warn, _ => size == BatteryGlyphSize.Small ? p.Muted : p.Ink };
        var sb = new StringBuilder();
        double textW = m.Label.Length * g.FontSize * 0.56;
        double w = g.Width + g.TextGap + textW;
        string opacity = m.Dimmed ? "0.5" : "1";
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(w * scale)}\" height=\"{F(g.Height * scale + 4)}\" viewBox=\"0 -2 {F(w)} {F(g.Height + 4)}\" style=\"opacity:{opacity}\">");
        sb.Append(Rect(g.Body, $"fill=\"none\" stroke=\"{outline}\" stroke-width=\"{F(g.BodyStroke)}\""));
        sb.Append(Rect(g.Cap, $"fill=\"{outline}\""));
        if (g.Fill.Width > 0) sb.Append(Rect(g.Fill, $"fill=\"{fill}\""));
        if (m.ShowBolt)
        {
            string pts = string.Join(' ', g.Bolt.Select(b => $"{F(b.X)},{F(b.Y)}"));
            sb.Append($"<polygon points=\"{pts}\" fill=\"{p.Card}\" stroke=\"{p.Card}\" stroke-width=\"{F(g.BoltEdge * 2)}\" stroke-linejoin=\"round\"/>");
            sb.Append($"<polygon points=\"{pts}\" fill=\"{p.Ink}\"/>");
        }

        sb.Append($"<text x=\"{F(g.Width + g.TextGap)}\" y=\"{F(g.Height / 2)}\" dominant-baseline=\"central\" font-family=\"Segoe UI Variable, Segoe UI, sans-serif\" font-size=\"{F(g.FontSize)}\" fill=\"{label}\">{m.Label}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    public static string Html()
    {
        (string Name, BatteryGlyphModel Model)[] states =
        [
            ("0%", BatteryGlyphModel.Create(0, false, null, 15)),
            ("5%", BatteryGlyphModel.Create(5, false, null, 15)),
            ("15% (threshold)", BatteryGlyphModel.Create(15, false, null, 15)),
            ("50%", BatteryGlyphModel.Create(50, false, null, 15)),
            ("73%", BatteryGlyphModel.Create(73, false, null, 15)),
            ("100%", BatteryGlyphModel.Create(100, false, null, 15)),
            ("Charging 42%", BatteryGlyphModel.Create(42, true, "charging", 15)),
            ("Charging 8%", BatteryGlyphModel.Create(8, true, "charging", 15)),
            ("Full on charger", BatteryGlyphModel.Create(100, true, "full", 15)),
            ("Charge fault", BatteryGlyphModel.Create(60, true, "error", 15)),
            ("Unknown", BatteryGlyphModel.Create(null, false, null, 15)),
            ("Disconnected", BatteryGlyphModel.Create(73, false, null, 15, hidden: true, word: "Off")),
        ];
        var sb = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>Battery glyph preview</title>");
        sb.Append("<style>body{margin:0;font:13px Segoe UI,sans-serif}.p{padding:20px 28px}h2{margin:0 0 4px;font-size:15px}h3{margin:18px 0 6px;font-size:12px;opacity:.7}");
        sb.Append(".g{display:grid;grid-template-columns:repeat(4,1fr);gap:10px}.c{padding:12px 14px;border-radius:8px}.c small{display:block;font-size:11px;opacity:.6;margin-bottom:8px}.c div{margin:6px 0}</style>");
        foreach (Palette p in Palettes)
        {
            sb.Append($"<div class=\"p\" style=\"background:{p.Ground};color:{p.Muted}\"><h2 style=\"color:{p.Ink}\">{p.Name}</h2>");
            foreach (double scale in new[] { 1.0, 2.0 })
            {
                sb.Append($"<h3>{scale * 100:0}% scale</h3><div class=\"g\">");
                foreach ((string name, BatteryGlyphModel m) in states)
                {
                    sb.Append($"<div class=\"c\" style=\"background:{p.Card}\"><small>{name}</small>");
                    sb.Append($"<div>{Glyph(m, BatteryGlyphSize.Small, p, scale)}</div><div>{Glyph(m, BatteryGlyphSize.Large, p, scale)}</div></div>");
                }

                sb.Append("</div>");
            }

            sb.Append("</div>");
        }

        return sb.ToString();
    }
}
