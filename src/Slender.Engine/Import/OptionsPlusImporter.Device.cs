using Slender.Engine;

namespace Slender.Engine.Import;

public static partial class OptionsPlusImporter
{
    /// <summary>SYSTEM BRIGHTNESS_UP / BRIGHTNESS_DOWN: 10 points per press on the monitor under the cursor.</summary>
    private static Conv MapBrightness(string system) =>
        Ok(new ActionSpec.Brightness(system == "BRIGHTNESS_UP" ? 10 : -10));

    /// <summary>
    /// Options+ pointerSpeed is {dpiLevel, value}: an index and a relative speed with no documented DPI list, so no DPI is derived
    /// (guessing would silently change the pointer). The user sets it by hand.
    /// </summary>
    private const string PointerSpeedHint = " Set it in Settings > Point & scroll (the current device DPI is shown there).";
}
