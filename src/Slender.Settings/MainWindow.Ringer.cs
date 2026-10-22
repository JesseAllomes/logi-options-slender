using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Engine.Ringer;
using Slender.Settings.Logic;
using Slender.Settings.Services;
using Slender.Settings.Views;

namespace Slender.Settings;

/// <summary>
/// The 3CX page's ringer section (docs/3cx-ringer.md): the 3CX address, the ringer buzz, Set up / Remove, and what the Edge
/// extension last told Slender. Nothing is written until the user presses Set up; the section only reads (one registry value, one file check).
/// </summary>
public sealed partial class MainWindow
{
    private void BuildRingerSection()
    {
        var installer = RingerInstaller.CreateDefault();
        bool available = installer.IsAvailable();
        bool setUp = installer.IsSetUp();

        AddSection("3CX RINGER");
        var help = Ui.Text("Rings the mouse while a call is ringing in the 3CX web client in Edge, until you answer, the caller hangs up, or you click a mouse button. "
            + "Only \"ringing started\" and \"ringing stopped\" leave the page; no names or numbers are read. Off until you set it up.", 12, Ui.Muted);
        help.TextWrapping = TextWrapping.Wrap;
        help.Margin = new Thickness(0, 0, 0, 6);
        PagePanel.Children.Add(help);

        var address = new TextBox { PlaceholderText = "https://pbx.example.com", Width = 320, MaxLength = RingerOrigin.MaxLength, Text = _config.Config.Haptics.RingerOrigin ?? string.Empty };
        AutomationProperties.SetName(address, "3CX web address");
        ToolTipService.SetToolTip(address, RingerText.AddressTip);
        var find = new Button { Content = "Find", Style = Ui.Res<Style>("QuietButtonStyle") };
        ToolTipService.SetToolTip(find, "Reads the address from your installed 3CX web app's shortcut");
        var addressRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        addressRow.Children.Add(address);
        addressRow.Children.Add(find);
        AddRow("3CX address", "The address the 3CX web app opens. Find fills it in from the installed app. https only.", addressRow);

        HapticCue Get() => _config.Config.Haptics.Ringer.Clamped();
        void Set(HapticCue q) => _config.Update(x => x with { Haptics = x.Haptics with { Ringer = q.Clamped() } });
        var cueRow = CueRow("Ringer buzz", "Plays again and again while the call rings. A saved pattern also sets the pause between rings.", Get, Set, Get().Enabled, on => Set(Get() with { Enabled = on }));
        AddRule(cueRow);

        ComboBox RingButtonBox(string name, Func<ushort> get, Action<ushort> set)
        {
            var box = new ComboBox { Width = 170 };
            AutomationProperties.SetName(box, name);
            foreach (string choice in RingerText.RingButtonChoices()) box.Items.Add(choice);
            box.SelectedIndex = RingerText.RingButtonIndex(get());
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex < 0) return;
                ushort id = RingerText.RingButtonAt(box.SelectedIndex);
                if (id != get()) set(id);
            };
            return box;
        }

        AddRow("Answer with", RingerText.RingButtonsHelp,
            RingButtonBox("Answer with", () => _config.Config.Haptics.RingAnswerButton, id => _config.Update(x => x with { Haptics = x.Haptics with { RingAnswerButton = id } })));
        AddRow("Reject with", null,
            RingButtonBox("Reject with", () => _config.Config.Haptics.RingRejectButton, id => _config.Update(x => x with { Haptics = x.Haptics with { RingRejectButton = id } })));

        var setUpButton = new Button { Content = "Set up", Style = Ui.Res<Style>("QuietButtonStyle"), IsEnabled = available };
        ToolTipService.SetToolTip(setUpButton, available ? "Prepares the Edge extension and links it to Slender. Nothing runs until you load the extension in Edge." : "This Slender install does not include the 3CX ringer.");
        var removeButton = new Button { Content = "Remove", Style = Ui.Res<Style>("QuietButtonStyle"), IsEnabled = setUp };
        ToolTipService.SetToolTip(removeButton, "Unlinks the extension and deletes Slender's copy of it. Remove the extension in Edge too.");
        var folderButton = new Button { Content = "Open extension folder", Style = Ui.Res<Style>("QuietButtonStyle"), IsEnabled = setUp };
        ToolTipService.SetToolTip(folderButton, "The folder to pick in Edge with Load unpacked");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(setUpButton);
        buttons.Children.Add(removeButton);
        buttons.Children.Add(folderButton);
        PagePanel.Children.Add(buttons);

        var steps = Ui.Text(RingerText.Steps, 12, Ui.Muted);
        steps.TextWrapping = TextWrapping.Wrap;
        steps.Margin = new Thickness(0, 8, 0, 0);
        PagePanel.Children.Add(steps);

        var message = Ui.Text(string.Empty, 12, Ui.Muted);
        message.TextWrapping = TextWrapping.Wrap;
        message.Margin = new Thickness(0, 6, 0, 0);
        PagePanel.Children.Add(message);

        var status = Ui.Text(string.Empty, 12, Ui.Ink);
        status.Margin = new Thickness(0, 6, 0, 0);
        PagePanel.Children.Add(status);

        void ShowStatus()
        {
            var s = _status.Status;
            status.Text = RingerText.Status(setUp, s?.LastRingSignal, s?.LastRingSource);
        }

        void Apply(RingerInstallResult result, bool ok)
        {
            message.Text = RingerText.Describe(result);
            message.Foreground = ok ? Ui.Muted : Ui.Warn;
            setUp = installer.IsSetUp();
            removeButton.IsEnabled = setUp;
            folderButton.IsEnabled = setUp;
            ShowStatus();
        }

        setUpButton.Click += (_, _) =>
        {
            string? problem = RingerText.AddressProblem(address.Text);
            if (problem is not null)
            {
                message.Text = problem;
                message.Foreground = Ui.Warn;
                return;
            }

            var result = installer.SetUp(address.Text);
            bool ok = result.Status == RingerInstallStatus.Done;
            if (ok)
            {
                string origin = RingerOrigin.Normalize(address.Text)!;
                address.Text = origin;
                _config.Update(x => x with { Haptics = x.Haptics with { RingerOrigin = origin } });
            }

            Apply(result, ok);
            if (ok) OpenFolder(installer.ExtensionDirectory);
        };
        removeButton.Click += (_, _) =>
        {
            var result = installer.Remove();
            Apply(result, result.Status is RingerInstallStatus.Removed or RingerInstallStatus.NotSetUp);
        };
        folderButton.Click += (_, _) => OpenFolder(installer.ExtensionDirectory);
        find.Click += async (_, _) =>
        {
            find.IsEnabled = false;
            string? origin = await Task.Run(() => WebAppShortcuts.Find3cx(WebAppShortcutScan.Read()));
            find.IsEnabled = true;
            if (origin is not null) address.Text = origin;
            message.Text = origin is not null
                ? $"Found your 3CX web app: {origin}. Press Set up to use it."
                : "No installed 3CX web app found. Type the address it opens instead.";
            message.Foreground = origin is not null ? Ui.Muted : Ui.Warn;
        };

        ShowStatus();
        _status.Changed += ShowStatus;
        status.Unloaded += (_, _) => _status.Changed -= ShowStatus;
    }
}
