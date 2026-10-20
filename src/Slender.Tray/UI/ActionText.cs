using Slender.Engine;

namespace Slender.Tray;

/// <summary>Human-readable text for an action. Pure, no state.</summary>
public static class ActionText
{
    public static string Describe(ActionSpec action) => action switch
    {
        ActionSpec.None => "None",
        ActionSpec.Keys keys => keys.Chord,
        ActionSpec.Excel excel => $"Excel: {excel.Command}",
        ActionSpec.ExcelMacro macro => $"Excel macro: {macro.Name}",
        ActionSpec.MouseButton mouse => $"Mouse: {mouse.Button}",
        ActionSpec.ActionsRing => "Actions ring",
        ActionSpec.Launch launch => $"Open: {launch.Target}",
        ActionSpec.Disabled => "Off",
        ActionSpec.Brightness b => b.StepPercent >= 0 ? "Brightness up" : "Brightness down",
        ActionSpec.Text t => $"Type: {t.Value}",
        ActionSpec.Macro m => $"Macro: {Slender.Engine.Macros.MacroLabels.NameOf(m.Id)}",
        ActionSpec.TapHold th => $"Press: {Describe(th.Tap)} · Hold: {Describe(th.Hold)}",
        _ => action.GetType().Name,
    };
}
