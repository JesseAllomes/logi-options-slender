using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Engine.Import;
using Slender.Settings.Logic;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>The General page "Import from Options+" row: preview what the import would set, then back up config.json and save.</summary>
public sealed partial class MainWindow
{
    private bool _importing;

    private async Task ImportFromOptionsPlusAsync()
    {
        if (_importing)
        {
            return;
        }

        _importing = true;
        try
        {
            _config.Flush(); // the file on disk must match what is on screen before it is backed up
            ImportPreview preview = await Task.Run(() =>
            {
                // The real Actions Ring lives in Logi Plugin Service, not in settings.db; it wins when present.
                IReadOnlyList<PluginServiceProfile> ring = PluginServiceStore.Read();
                return OptionsPlusImportFlow.Prepare(_config.Config, pluginProfiles: ring, ringIcons: new PluginServiceIcons());
            });
            if (!preview.CanImport)
            {
                await ShowImportMessageAsync("Can't import from Options+", preview.Message);
                return;
            }

            if (!await ConfirmImportAsync(preview))
            {
                return;
            }

            string? backup;
            try
            {
                backup = OptionsPlusImportFlow.BackupConfig(Path.Combine(ConfigDirOverride ?? SlenderPaths.ConfigDirectory, "config.json"), DateTime.Now);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Services.CrashLog.Write(e);
                await ShowImportMessageAsync("Import cancelled", "The current settings couldn't be backed up, so nothing was changed. " + e.Message);
                return;
            }

            preview.AfterImport?.Invoke(); // the ring's images first, so the saved config never points at a missing file
            _config.Update(_ => preview.Result!.Config);
            _config.Flush();
            RebuildAll();
            await ShowImportMessageAsync("Imported from Options+",
                backup is null
                    ? "Your Options+ settings were imported. There was no earlier settings file to back up."
                    : "Your Options+ settings were imported. The settings you had before were saved to:\n" + backup);
        }
        finally
        {
            _importing = false;
        }
    }

    private async Task<bool> ConfirmImportAsync(ImportPreview preview)
    {
        var panel = new StackPanel { Spacing = 10 };
        var summary = Ui.Text(preview.Summary, 13);
        summary.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(summary);
        var note = Ui.Text("Your current settings are backed up first. Profiles for the same apps are replaced, and the Actions Ring comes from Logi Plugin Service when it is there; other profiles are kept.", 12, Ui.Muted);
        note.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(note);

        var report = new StackPanel { Spacing = 4 };
        foreach (ImportReportGroup group in preview.Groups)
        {
            var heading = Ui.Text(group.Title, 12, Ui.Ink, Ui.Heading);
            heading.Margin = new Thickness(0, 8, 0, 0);
            report.Children.Add(heading);
            foreach (string line in group.Lines)
            {
                var t = Ui.Text(line, 12, Ui.Muted);
                t.TextWrapping = TextWrapping.Wrap;
                report.Children.Add(t);
            }
        }

        if (preview.Groups.Count > 0)
        {
            panel.Children.Add(new ScrollViewer { Content = report, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }

        var dialog = new ContentDialog
        {
            Title = "Import from Options+",
            Content = panel,
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Root.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowImportMessageAsync(string title, string text)
    {
        await new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            CloseButtonText = "OK",
            XamlRoot = Root.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        }.ShowAsync();
    }
}
