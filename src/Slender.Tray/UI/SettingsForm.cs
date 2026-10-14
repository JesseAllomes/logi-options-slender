using Slender.Engine;

namespace Slender.Tray;

/// <summary>
/// Created on demand by the tray menu and disposed when it closes. OK hands the edited config to
/// <c>commit</c> (the tray context saves it through IConfigStore and raises ConfigChanged); if commit
/// returns false the form stays open. Cancel discards. Profiles and bindings are shown read-only.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly SlenderConfig _config;
    private readonly Func<SlenderConfig, bool> _commit;
    private readonly CheckBox _hapticsEnabled;
    private readonly NumericUpDown _hapticsLevel;
    private readonly NumericUpDown _columnsPerNotch;
    private readonly ListView _bindings;
    private readonly Button _ok;
    private readonly Button _cancel;

    public SettingsForm(SlenderConfig config, Func<SlenderConfig, bool> commit)
    {
        _config = config;
        _commit = commit;

        Text = "Slender settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 460);

        _hapticsEnabled = new CheckBox { Text = "Haptics enabled", Checked = config.Haptics.Enabled, AutoSize = true };
        _hapticsLevel = new NumericUpDown { Minimum = 0, Maximum = 3, Value = config.Haptics.Level, Width = 70 };
        _columnsPerNotch = new NumericUpDown { Minimum = 1, Maximum = 10, Value = config.Excel.ColumnsPerNotch, Width = 70 };

        _bindings = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            Dock = DockStyle.Fill,
        };
        _bindings.Columns.Add("Profile", 130);
        _bindings.Columns.Add("Control", 80);
        _bindings.Columns.Add("Action", 300);
        FillBindings(config);

        _ok = new Button { Text = "OK", DialogResult = DialogResult.None, AutoSize = true };
        _ok.Click += OnOk;
        _cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        AcceptButton = _ok;
        CancelButton = _cancel;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(_hapticsEnabled, 0, 0);
        layout.SetColumnSpan(_hapticsEnabled, 2);
        layout.Controls.Add(new Label { Text = "Haptic level (0-3)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_hapticsLevel, 1, 1);
        layout.Controls.Add(new Label { Text = "Columns per thumb-wheel notch (1-10)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(_columnsPerNotch, 1, 2);
        var bindingsLabel = new Label { Text = "Profiles and bindings (read-only)", AutoSize = true };
        layout.Controls.Add(bindingsLabel, 0, 3);
        layout.SetColumnSpan(bindingsLabel, 2);
        layout.Controls.Add(_bindings, 0, 4);
        layout.SetColumnSpan(_bindings, 2);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_ok);
        layout.Controls.Add(buttons, 0, 5);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
    }

    private void FillBindings(SlenderConfig config)
    {
        foreach (Profile profile in config.Profiles)
        {
            string name = profile.AppProcess ?? "Default";
            foreach (KeyValuePair<ushort, ActionSpec> binding in profile.Bindings.OrderBy(b => b.Key))
            {
                _bindings.Items.Add(new ListViewItem(new[]
                {
                    name,
                    $"0x{binding.Key:X4}",
                    ActionText.Describe(binding.Value),
                }));
            }
        }
    }

    private void OnOk(object? sender, EventArgs e)
    {
        SlenderConfig next = _config with
        {
            Haptics = _config.Haptics with
            {
                Enabled = _hapticsEnabled.Checked,
                Level = (int)_hapticsLevel.Value,
            },
            Excel = _config.Excel with { ColumnsPerNotch = (int)_columnsPerNotch.Value },
        };

        if (_commit(next))
        {
            DialogResult = DialogResult.OK;
        }
    }
}
