using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine.ExcelAddin;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>The General page's Excel add-in switch (m37). Off until the user turns it on; nothing else ever writes the Excel OPEN key.</summary>
public sealed partial class MainWindow
{
    private void BuildExcelAddinSection()
    {
        var installer = ExcelAddinInstaller.CreateDefault();
        bool available = installer.IsAvailable();
        bool installed = installer.IsInstalled();
        if (installed)
        {
            installer.RefreshInstalledCopy(); // an update may have shipped a newer add-in; skipped while Excel is open
        }

        AddSection("EXCEL");
        var note = Ui.Text(string.Empty, 12, Ui.Muted);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 6, 0, 0);

        var toggle = new ToggleSwitch { IsOn = installed, OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0, Width = 44, IsEnabled = available || installed };
        AutomationProperties.SetName(toggle, "Slender Excel add-in");
        bool reverting = false;
        toggle.Toggled += (_, _) =>
        {
            if (reverting) return;
            AddinInstallResult result = toggle.IsOn ? installer.Install() : installer.Uninstall();
            note.Text = ExcelAddinText.Describe(result);
            bool failed = result.Status is AddinInstallStatus.Failed or AddinInstallStatus.SourceMissing;
            if (failed)
            {
                reverting = true;
                toggle.IsOn = !toggle.IsOn;
                reverting = false;
            }
        };
        AddRow("Excel add-in",
            available || installed
                ? "Lets the mouse change sheets, zoom, table styles and chart types, run a macro, and scroll by cells or sheets depending on what is selected. Slender asks it over a private pipe for your account only; with Slender closed it does nothing. Close Excel before changing this."
                : "This Slender install does not include the Excel add-in.",
            toggle);
        PagePanel.Children.Add(note);
    }
}
