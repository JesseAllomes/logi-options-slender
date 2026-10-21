namespace Slender.Settings.Logic;

/// <summary>
/// Mouse wheel over a slider (user 2026-10-21): one step per notch, wheel up = higher. High-resolution wheels send parts of a
/// notch (delta below 120); the remainder is carried so slow scrolling still moves the slider.
/// </summary>
public static class SliderWheel
{
    public const int Notch = 120;

    /// <summary>The new value after a wheel delta, clamped to [min, max]. <paramref name="carry"/> keeps the part of a notch not yet used.</summary>
    public static double Apply(double value, double min, double max, double step, int delta, ref int carry)
    {
        if (step <= 0) step = 1;
        carry += delta;
        int notches = carry / Notch;
        carry -= notches * Notch;
        if (notches == 0) return value;
        return Math.Clamp(value + notches * step, min, max);
    }
}
