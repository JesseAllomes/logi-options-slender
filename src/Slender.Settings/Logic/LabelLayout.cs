namespace Slender.Settings.Logic;

/// <summary>Vertical placement of the label column beside the device render.</summary>
public static class LabelLayout
{
    /// <summary>Spreads label rows so none overlap: pushes down from the top, then back up from the bottom.</summary>
    public static double[] SpreadRows(double[] desired, double gap, double min, double max)
    {
        double[] ys = new double[desired.Length];
        for (int i = 0; i < desired.Length; i++)
        {
            ys[i] = Math.Max(desired[i], i == 0 ? min : ys[i - 1] + gap);
        }

        if (ys.Length > 0 && ys[^1] > max)
        {
            ys[^1] = max;
            for (int i = ys.Length - 2; i >= 0; i--)
            {
                ys[i] = Math.Min(ys[i], ys[i + 1] - gap);
            }
        }

        return ys;
    }
}
