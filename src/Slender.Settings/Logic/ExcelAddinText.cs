using Slender.Engine.ExcelAddin;

namespace Slender.Settings.Logic;

/// <summary>What the Excel add-in switch tells the user after it runs. Pure, so it is tested.</summary>
public static class ExcelAddinText
{
    public static string Describe(AddinInstallResult result) => result.Status switch
    {
        AddinInstallStatus.Installed or AddinInstallStatus.AlreadyInstalled when result.ExcelWasRunning =>
            "On. Excel is open: close it completely and start it again to load the add-in.",
        AddinInstallStatus.Installed => "On. The add-in loads the next time Excel starts. If Excel asks about the add-in, choose to enable it.",
        AddinInstallStatus.AlreadyInstalled => "Already on.",
        AddinInstallStatus.Removed when result.ExcelWasRunning => "Off. Excel is open: close it to unload the add-in.",
        AddinInstallStatus.Removed => "Off. Excel no longer loads the add-in.",
        AddinInstallStatus.NotInstalled => "Already off.",
        _ => result.Message,
    };
}
