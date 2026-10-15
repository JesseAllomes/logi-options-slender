using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Slender.Settings.Views;
using Slender.Sync;

namespace Slender.Settings;

/// <summary>The General page "SYNC" section: sign in with a personal Microsoft account, sync now, last synced, sign out.</summary>
public sealed partial class MainWindow
{
    private SyncService? _sync;
    private SyncAccount? _syncAccount;
    private bool _syncStateLoaded;
    private bool _syncBusy;
    private string? _syncMessage;

    private SyncService Sync => _sync ??= SyncServiceFactory.Create();

    private void BuildSyncSection()
    {
        AddSection("SYNC");
        SyncService sync = Sync;

        if (!sync.IsConfigured)
        {
            var setup = new Button { Content = "How to set it up", Style = Ui.Res<Style>("QuietButtonStyle") };
            setup.Click += (_, _) => OpenSetupSteps();
            AddRow("Sign in with Microsoft", "Sign-in isn't set up yet. Add your app registration's client ID to slender.sync.json (steps in docs/sync.md).", setup);
            return;
        }

        if (!_syncStateLoaded)
        {
            AddRow("Sync across PCs", "Checking sign-in...", new TextBlock());
            _ = LoadSyncStateAsync();
            return;
        }

        if (_syncAccount is null)
        {
            var signIn = new Button { Content = "Sign in with Microsoft", Style = Ui.Res<Style>("QuietButtonStyle"), IsEnabled = !_syncBusy };
            signIn.Click += async (_, _) => await RunSyncActionAsync(() => Sync.SignInAsync());
            AddRow("Sync across PCs", "Sign in with a personal Microsoft account. Your settings are kept in your OneDrive app folder, nowhere else.", signIn);
        }
        else
        {
            SyncManifest manifest = sync.Manifest;
            string last = manifest.LastSyncedUtc is { } t ? "Last synced " + t.ToLocalTime().ToString("d MMM h:mm tt") : "Not synced yet";

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var now = new Button { Content = _syncBusy ? "Syncing..." : "Sync now", Style = Ui.Res<Style>("QuietButtonStyle"), IsEnabled = !_syncBusy };
            now.Click += async (_, _) => await RunSyncActionAsync(() => Sync.SyncNowAsync());
            var signOut = new Button { Content = "Sign out", Style = Ui.Res<Style>("QuietButtonStyle"), IsEnabled = !_syncBusy };
            signOut.Click += async (_, _) => await SignOutAsync();
            buttons.Children.Add(now);
            buttons.Children.Add(signOut);
            AddRow("Signed in as " + _syncAccount.Name, last + ". Device pictures and status are never synced.", buttons);

            if (manifest.ConflictNotice is { } notice)
            {
                var dismiss = new Button { Content = "Dismiss", Style = Ui.Res<Style>("QuietButtonStyle") };
                dismiss.Click += (_, _) =>
                {
                    Sync.DismissConflictNotice();
                    RebuildAll();
                };
                AddRow("Settings were changed on another PC", "Kept this PC's version. The other copy is saved in your OneDrive app folder as config.conflict-<time>.json.", dismiss);
            }
        }

        if (_syncMessage is not null)
        {
            var m = Ui.Text(_syncMessage, 12, Ui.Muted);
            m.Margin = new Thickness(0, 6, 0, 0);
            m.TextWrapping = TextWrapping.Wrap;
            PagePanel.Children.Add(m);
        }
    }

    private async Task LoadSyncStateAsync()
    {
        _syncAccount = await Sync.GetAccountAsync();
        _syncStateLoaded = true;
        RebuildAll();
    }

    private async Task RunSyncActionAsync(Func<Task<SyncResult>> action)
    {
        _config.Flush(); // an edit still waiting for its save would otherwise miss this sync
        _syncBusy = true;
        _syncMessage = null;
        RebuildAll();
        try
        {
            SyncResult result = await action();
            _syncMessage = result.Outcome == SyncOutcome.UpToDate ? "Settings are in sync." : result.Message;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _syncMessage = "Sync failed: " + e.Message;
        }

        _syncAccount = await Sync.GetAccountAsync();
        _syncBusy = false;
        RebuildAll();
    }

    private async Task SignOutAsync()
    {
        _syncBusy = true;
        RebuildAll();
        try
        {
            await Sync.SignOutAsync();
            _syncMessage = "Signed out. Your settings stay on this PC and in OneDrive.";
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _syncMessage = "Sign out failed: " + e.Message;
        }

        _syncAccount = await Sync.GetAccountAsync();
        _syncBusy = false;
        RebuildAll();
    }

    private static void OpenSetupSteps()
    {
        string? path = SyncServiceFactory.SetupDocsPath();
        if (path is null)
        {
            return;
        }

        try
        {
            using Process? p = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            Services.CrashLog.Write(e);
        }
    }
}
