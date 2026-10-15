namespace Slender.Settings.Logic;

/// <summary>Slider to setting mapping for the Point and scroll page. Pure.</summary>
public static class PointerMapping
{
    public const int DpiMin = 200; // the sensor's real floor (0x2201 reports 200-8000)
    public const int DpiMax = 8000;
    public const int DpiStep = 50;
    public const int DpiShown = 1600; // where the slider rests while the speed is "Default"
    public const int ThresholdMin = 1;
    public const int ThresholdMax = 50;

    /// <summary>Snaps a slider value to the nearest DPI step inside the range.</summary>
    public static int SnapDpi(double value)
    {
        int steps = (int)Math.Round((value - DpiMin) / DpiStep, MidpointRounding.AwayFromZero);
        return Math.Clamp(DpiMin + steps * DpiStep, DpiMin, DpiMax);
    }

    public static string DpiText(int? dpi) => dpi is null ? "Default" : $"{dpi} DPI";

    /// <summary>Readout while the speed is Default: the mouse's own speed when the tray has reported it.</summary>
    public static string DefaultText(int? deviceDpi) => deviceDpi is { } d ? $"Default ({d} DPI)" : "Default";

    /// <summary>Sense Panel slider 0 (Light) to 100 (Firm) across the safe band [min, max] of device force units.</summary>
    public static int ForceToSlider(int force, int min, int max) =>
        max <= min ? 50 : Math.Clamp((int)Math.Round((force - min) * 100.0 / (max - min)), 0, 100);

    public static int SliderToForce(double slider, int min, int max) =>
        Math.Clamp(min + (int)Math.Round(Math.Clamp(slider, 0, 100) * (max - min) / 100.0), min, max);

    /// <summary>Plain words for a Sense Panel slider position.</summary>
    public static string ForceText(double slider) => slider < 25 ? "Light" : slider > 75 ? "Firm" : "Medium";

    public static string WheelResistanceText(double value) => $"{(int)Math.Round(value)}";

    public static int ClampThreshold(double value) => Math.Clamp((int)Math.Round(value), ThresholdMin, ThresholdMax);
}
