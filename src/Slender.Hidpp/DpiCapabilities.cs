namespace Slender.Hidpp;

/// <summary>
/// The sensor DPI values the mouse accepts (0x2201 fn1 getSensorDpiList): either a stepped range (min, step marker, max) or an
/// explicit list. Used to clamp a requested DPI to something the device really takes.
/// </summary>
public sealed class DpiCapabilities
{
    private readonly int[] _values;   // explicit list; empty when a range
    private readonly int _min, _max, _step;

    private DpiCapabilities(int[] values, int min, int max, int step)
    {
        _values = values; _min = min; _max = max; _step = step;
    }

    public static DpiCapabilities Range(int min, int max, int step) => new([], min, max, Math.Max(1, step));

    public static DpiCapabilities List(IEnumerable<int> values) => new([.. values.Distinct().Order()], 0, 0, 1);

    public int Min => _values.Length > 0 ? _values[0] : _min;
    public int Max => _values.Length > 0 ? _values[^1] : _max;

    /// <summary>The accepted value nearest <paramref name="dpi"/> (ties go lower).</summary>
    public int Clamp(int dpi)
    {
        if (_values.Length > 0)
        {
            int best = _values[0];
            foreach (int v in _values)
            {
                if (Math.Abs(v - dpi) < Math.Abs(best - dpi)) best = v;
            }

            return best;
        }

        int c = Math.Clamp(dpi, _min, _max);
        int steps = (c - _min + _step / 2) / _step;
        return Math.Clamp(_min + steps * _step, _min, _max);
    }

    /// <summary>
    /// Parses the fn1 response parameters: byte 0 is the sensor index, then big-endian 16-bit words ending at 0. A word of
    /// 0xE000 or more is a step (low 13 bits) for the range between its neighbours. Null when nothing usable is in it.
    /// </summary>
    public static DpiCapabilities? Parse(ReadOnlySpan<byte> p)
    {
        var words = new List<int>();
        for (int i = 1; i + 1 < p.Length; i += 2)
        {
            int w = (p[i] << 8) | p[i + 1];
            if (w == 0) break;
            words.Add(w);
        }

        if (words.Count == 0) return null;
        int stepAt = words.FindIndex(w => w >= 0xE000);
        if (stepAt < 0) return List(words);
        if (stepAt == 0 || stepAt + 1 >= words.Count) return null;
        int step = words[stepAt] & 0x1FFF;
        return step > 0 ? Range(words[stepAt - 1], words[stepAt + 1], step) : null;
    }
}
