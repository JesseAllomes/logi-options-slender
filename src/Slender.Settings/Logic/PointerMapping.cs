namespace Slender.Settings.Logic;

/// <summary>Slider to setting mapping for the Point and scroll page. Pure.</summary>
public static class PointerMapping
{
    public const int DpiMin = 400;
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

    public static int ClampThreshold(double value) => Math.Clamp((int)Math.Round(value), ThresholdMin, ThresholdMax);
}
