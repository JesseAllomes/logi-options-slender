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
        ActionSpec.MouseButton mouse => $"Mouse: {mouse.Button}",
        ActionSpec.ActionsRing => "Actions ring",
        _ => action.GetType().Name,
    };
}
