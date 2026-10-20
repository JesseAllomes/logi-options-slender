using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Hidpp;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>
/// General page, keyboard section: remaps stored ON the ERGO K860. Settings never opens the keyboard; it asks the tray
/// (OnboardClient) to read it, and to write only when the user saves a change here.
/// </summary>
public sealed partial class MainWindow
{
    private readonly OnboardClient _onboard = OnboardClient.ForPipe();
    private StackPanel? _remapPanel;
    private OnboardReadReply? _remapRead;
    private string? _remapNote;
    private bool _remapBusy;

    private void BuildOnboardRemapsSection(SlenderConfig c)
    {
        AddSection("KEYBOARD REMAPS (STORED ON THE KEYBOARD)");
        var note = Ui.Text("These remaps are stored on the keyboard itself. They apply on every PC and Easy-Switch channel, and they stay on the keyboard even after you uninstall Slender. Use Reset to put a key back.", 12, Ui.Muted);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 0, 0, 6);
        PagePanel.Children.Add(note);

        _remapPanel = new StackPanel();
        PagePanel.Children.Add(_remapPanel);
        FillRemapPanel();
        _ = RefreshRemapReadAsync();
    }

    private async Task RefreshRemapReadAsync()
    {
        OnboardReadReply read = await Task.Run(() => _onboard.ReadAsync());
        _remapRead = read;
        FillRemapPanel();
    }

    private void FillRemapPanel()
    {
        if (_remapPanel is null)
        {
            return;
        }

        _remapPanel.Children.Clear();
        OnboardReadReply? read = _remapRead;
        string status = read is null ? "Reading the keyboard…"
            : read.Ok ? (_remapNote ?? "Showing what the keyboard has stored right now.")
            : read.Error == OnboardProtocol.Unreachable ? "Keyboard not connected (Slender is not running)." : "Keyboard not connected: " + read.Error;
        var statusText = Ui.Text(status, 12, read is { Ok: false } ? Ui.Warn : Ui.Muted);
        statusText.TextWrapping = TextWrapping.Wrap;
        statusText.Margin = new Thickness(0, 4, 0, 4);
        _remapPanel.Children.Add(statusText);

        bool connected = read is { Ok: true };
        var resetAll = new Button { Content = "Reset all", Style = Ui.Res<Style>("QuietButtonStyle"), IsEnabled = connected && !_remapBusy };
        resetAll.Click += async (_, _) => await ResetRemapsAsync(OnboardRemapKeys.All.Select(k => k.Cid).ToList());
        AddRow("Reset every key", "Puts all 17 keys back to the keyboard's own mapping.", resetAll, _remapPanel);

        foreach (OnboardKeyInfo key in OnboardRemapKeys.All)
        {
            OnboardKeyInfo captured = key;
            OnboardMapping? mapping = read?.Mappings.Where(m => m.Cid == key.Cid).Select(m => (OnboardMapping?)m).FirstOrDefault();
            string now = !connected ? (read is null ? "…" : "keyboard not connected") : mapping is { } m ? OnboardChoices.DescribeMapping(m) : "not reported";
            var change = new Button { Content = "Change", Style = Ui.Res<Style>("QuietButtonStyle"), IsEnabled = connected && !_remapBusy };
            AutomationProperties.SetName(change, $"Change {key.Name}");
            change.Flyout = BuildRemapFlyout(captured);
            var reset = new Button { Content = "Reset", Style = Ui.Res<Style>("QuietButtonStyle"), IsEnabled = connected && !_remapBusy };
            AutomationProperties.SetName(reset, $"Reset {key.Name}");
            reset.Click += async (_, _) => await ResetRemapsAsync([captured.Cid]);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            buttons.Children.Add(change);
            buttons.Children.Add(reset);
            AddRow(key.Name, "Now: " + now, buttons, _remapPanel);
        }
    }

    /// <summary>The picker only offers what the keyboard can store: one optional modifier plus a key, or a media key.</summary>
    private Flyout BuildRemapFlyout(OnboardKeyInfo key)
    {
        var modifier = new ComboBox { MinWidth = 140, SelectedIndex = 0 };
        foreach (OnboardChoice m in OnboardChoices.Modifiers) modifier.Items.Add(m.Label);
        var main = new ComboBox { MinWidth = 180 };
        foreach (OnboardChoice k in OnboardChoices.MainKeys) main.Items.Add(k.Label);
        AutomationProperties.SetName(modifier, "Modifier");
        AutomationProperties.SetName(main, "Key");
        var error = Ui.Text("", 12, Ui.Warn);
        error.TextWrapping = TextWrapping.Wrap;
        error.MaxWidth = 300;
        var save = new Button { Content = "Save to keyboard", Style = Ui.Res<Style>("QuietButtonStyle") };
        var panel = new StackPanel { Spacing = 8, Width = 300 };
        panel.Children.Add(Ui.Text($"{key.Name} sends", 13));
        panel.Children.Add(modifier);
        panel.Children.Add(main);
        panel.Children.Add(error);
        panel.Children.Add(save);
        var flyout = new Flyout { Content = panel };
        flyout.Opening += (_, _) =>
        {
            error.Text = "";
            OnboardRemap? have = _config.Config.OnboardRemaps.FirstOrDefault(r => r.Cid == key.Cid);
            if (have is null) return;
            (string mod, string k) = OnboardChoices.Split(have.Chord);
            modifier.SelectedIndex = Math.Max(0, OnboardChoices.Modifiers.ToList().FindIndex(x => x.Name == mod));
            main.SelectedIndex = OnboardChoices.MainKeys.ToList().FindIndex(x => string.Equals(x.Name, k, StringComparison.OrdinalIgnoreCase));
        };
        save.Click += async (_, _) =>
        {
            string mod = modifier.SelectedIndex >= 0 ? OnboardChoices.Modifiers[modifier.SelectedIndex].Name : "";
            string? name = main.SelectedIndex >= 0 ? OnboardChoices.MainKeys[main.SelectedIndex].Name : null;
            if (!OnboardChoices.TryBuildChord(mod, name, out string chord, out string why))
            {
                error.Text = "Cannot be stored on the keyboard: " + why;
                return;
            }

            flyout.Hide();
            await SaveRemapAsync(key, chord);
        };
        return flyout;
    }

    /// <summary>The user saved a change: keep it in config, then ask the tray to write that key.</summary>
    private async Task SaveRemapAsync(OnboardKeyInfo key, string chord)
    {
        _config.Update(x => x with { OnboardRemaps = OnboardRemapKeys.Sanitize(x.OnboardRemaps.Where(r => r.Cid != key.Cid).Append(new OnboardRemap(key.Cid, chord))) });
        await ApplyRemapsAsync([new OnboardRemap(key.Cid, chord)], [], key.Name);
    }

    private async Task ResetRemapsAsync(List<ushort> cids)
    {
        _config.Update(x => x with { OnboardRemaps = [.. x.OnboardRemaps.Where(r => !cids.Contains(r.Cid))] });
        await ApplyRemapsAsync([], cids, cids.Count == 1 ? OnboardRemapKeys.NameOf(cids[0]) : "All keys");
    }

    private async Task ApplyRemapsAsync(List<OnboardRemap> sets, List<ushort> resets, string label)
    {
        _remapBusy = true;
        _remapNote = "Writing to the keyboard…";
        FillRemapPanel();
        OnboardApplyReply reply = await Task.Run(() => _onboard.ApplyAsync(sets, resets));
        _remapBusy = false;
        if (!reply.Ok)
        {
            _remapNote = $"{label}: nothing was written. {reply.Error}";
        }
        else if (reply.Results.FirstOrDefault(r => r.Outcome == OnboardOutcome.Failed) is { } bad)
        {
            _remapNote = $"{OnboardRemapKeys.NameOf(bad.Cid)} failed: {bad.Reason}";
        }
        else
        {
            int written = reply.Results.Count(r => r.Outcome == OnboardOutcome.Applied);
            _remapNote = written == 0 ? $"{label}: the keyboard already had this." : $"{label}: stored on the keyboard.";
        }

        FillRemapPanel();
        await RefreshRemapReadAsync();
    }
}
