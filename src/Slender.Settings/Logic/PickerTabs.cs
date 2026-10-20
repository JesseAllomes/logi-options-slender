namespace Slender.Settings.Logic;

/// <summary>Which picker tabs show for a control.</summary>
public static class PickerTabs
{
    /// <summary>
    /// False for the Shortcuts tab when suggestions are switched off, and for the Excel tab outside Excel's own picker (user
    /// 2026-10-20: no Excel actions to pick from in Outlook), unless the control already has an Excel action, which must stay
    /// visible to be changed. Every other tab always shows.
    /// </summary>
    public static bool Show(string tab, string? app, bool ownIsExcel, bool hideShortcuts) => tab switch
    {
        "shortcuts" => !hideShortcuts,
        "excel" => ownIsExcel || (app is not null && string.Equals(Path.GetFileNameWithoutExtension(app), "excel", StringComparison.OrdinalIgnoreCase)),
        _ => true,
    };
}
