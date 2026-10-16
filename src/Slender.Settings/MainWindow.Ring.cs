using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Slender.Engine;
using Slender.Engine.Ring;
using Slender.Settings.Logic;
using Slender.Settings.Views;
using Windows.Foundation;
using Windows.System;

namespace Slender.Settings;

/// <summary>
/// The Actions Ring page: how the ring looks (global) on the left, and the slots of the app chosen in the app strip on the
/// right. The preview is the editor: click a slot for the action picker, right-click for the slot menu.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly PickerTab[] RingTabs =
    [
        new("keys", "Keys"), new("shortcuts", "Shortcuts"), new("mouse", "Mouse"), new("excel", "Excel"), new("more", "More"), new("icon", "Icon"), new("default", "Empty"),
    ];

    private RingPreview? _ringPreview;
    private StackPanel? _ringHeader;
    private int? _ringFolder; // top-level index of the folder being edited, or null
    private RingSlot? _ringClipboard;

    private string? RingApp => _app;

    private void BuildRingPage()
    {
        _ringFolder = null;
        PagePanel.MaxWidth = 840;

        var columns = new Grid { ColumnSpacing = 22, Margin = new Thickness(0, 8, 0, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        StackPanel appearance = BuildRingAppearance();
        Grid.SetColumn(appearance, 0);
        columns.Children.Add(appearance);

        var slots = new StackPanel { Spacing = 8 };
        _ringHeader = new StackPanel { Spacing = 6 };
        slots.Children.Add(_ringHeader);
        _ringPreview = new RingPreview { HorizontalAlignment = HorizontalAlignment.Left, FileIconPath = RingFileIconAsync };
        _ringPreview.SlotInvoked += OnRingSlotInvoked;
        _ringPreview.SlotMenu += OnRingSlotMenu;
        _ringPreview.SlotDelete += index => _ = RemoveRingSlotAsync(RingPathOf(index));
        _ringPreview.BackRequested += LeaveRingFolder;
        slots.Children.Add(_ringPreview);
        var hint = Ui.Text("Click a slot to choose what it does. Right-click for copy, paste, clear and folders.", 12, Ui.Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        slots.Children.Add(hint);
        Grid.SetColumn(slots, 1);
        columns.Children.Add(slots);

        PagePanel.Children.Add(columns);
        RefreshRing();
    }

    // ---- appearance ----

    private StackPanel BuildRingAppearance()
    {
        RingAppearance a = _config.Config.Ring;
        var panel = new StackPanel { Spacing = 14 };
        var title = Ui.Text("APPEARANCE", 12, Ui.Muted, Ui.Heading);
        panel.Children.Add(title);

        void Edit(Func<RingAppearance, RingAppearance> f) => _config.Update(c => RingOps.UpdateAppearance(c, f));

        panel.Children.Add(Field("Style", RingChoice("Style", ["Bezel", "Compass", "Keypad"], (int)a.Style, i => Edit(x => x with { Style = (RingStyle)i }))));
        panel.Children.Add(Field("Size", RingChoice("Size", ["Small", "Medium", "Large"], (int)a.Size, i => Edit(x => x with { Size = (RingSize)i }))));
        panel.Children.Add(Field("Accent colour", BuildAccentPicker(a.Accent, hex => Edit(x => x with { Accent = hex }))));

        Slider opacity = PageSlider("Opacity", RingOps.MinOpacity, RingOps.MaxOpacity, 1, a.OpacityPercent,
            v => Edit(x => x with { OpacityPercent = (int)v }), v => $"{(int)v} %", out TextBlock opacityText);
        opacity.Width = 170;
        opacityText.Width = 48;
        panel.Children.Add(Field("Opacity", WithReadout(opacity, opacityText)));

        panel.Children.Add(Field("Labels", RingChoice("Labels", ["Always", "Only the selected slot"], (int)a.Labels, i => Edit(x => x with { Labels = (RingLabels)i }))));
        panel.Children.Add(Field("Position", RingChoice("Position", ["At the cursor", "Centre of the screen"], (int)a.Position, i => Edit(x => x with { Position = (RingPosition)i }))));
        return panel;
    }

    private static StackPanel Field(string label, FrameworkElement control)
    {
        var field = new StackPanel { Spacing = 5 };
        field.Children.Add(Ui.Text(label, 13));
        field.Children.Add(control);
        return field;
    }

    private static ComboBox RingChoice(string name, string[] items, int selected, Action<int> changed)
    {
        var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string item in items)
        {
            box.Items.Add(item);
        }

        box.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1);
        AutomationProperties.SetName(box, name);
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0)
            {
                changed(box.SelectedIndex);
            }
        };
        return box;
    }

    /// <summary>Six presets as round swatches, and a custom colour in a flyout.</summary>
    private FrameworkElement BuildAccentPicker(string current, Action<string> changed)
    {
        string selected = RingOps.NormaliseAccent(current) ?? RingOps.AccentPresets[0].Hex;
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var custom = new Button { Content = "Custom colour...", Style = Ui.Res<Style>("QuietButtonStyle") };
        var buttons = new List<(Button Button, string Hex)>();

        void Mark()
        {
            foreach ((Button b, string hex) in buttons)
            {
                bool on = string.Equals(hex, selected, StringComparison.OrdinalIgnoreCase);
                b.BorderBrush = on ? Ui.Ink : Ui.Rule;
                b.BorderThickness = new Thickness(on ? 2 : 1);
            }
        }

        foreach ((string name, string hex) in RingOps.AccentPresets)
        {
            var swatch = new Button
            {
                Style = Ui.Res<Style>("PlainButtonStyle"),
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(ColorOf(hex)),
            };
            AutomationProperties.SetName(swatch, name);
            ToolTipService.SetToolTip(swatch, name);
            swatch.Click += (_, _) =>
            {
                selected = hex;
                Mark();
                changed(hex);
            };
            buttons.Add((swatch, hex));
            swatches.Children.Add(swatch);
        }

        Mark();

        custom.Click += (_, _) =>
        {
            var picker = new ColorPicker { Color = ColorOf(selected), IsAlphaEnabled = false, IsHexInputVisible = true, IsMoreButtonVisible = false };
            var flyout = new Flyout { Placement = FlyoutPlacementMode.Bottom, FlyoutPresenterStyle = Ui.Res<Style>("PickerFlyoutPresenterStyle"), Content = picker };
            picker.ColorChanged += (_, e) =>
            {
                string hex = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
                selected = hex;
                Mark();
                changed(hex);
            };
            flyout.ShowAt(custom);
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(swatches);
        panel.Children.Add(custom);
        return panel;
    }

    private static Windows.UI.Color ColorOf(string hex)
    {
        string h = (RingOps.NormaliseAccent(hex) ?? "#D9925F")[1..];
        return Windows.UI.Color.FromArgb(255, Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16));
    }

    // ---- slots ----

    /// <summary>Redraws the header and the preview from the config. Cheap; runs on every config change while the page is open.</summary>
    private void RefreshRing()
    {
        if (_ringPreview is null || _ringHeader is null)
        {
            return;
        }

        SlenderConfig c = _config.Config;
        IReadOnlyList<RingSlot> slots = RingOps.Slots(c, RingApp);
        if (_ringFolder is { } f && (f >= slots.Count || !slots[f].IsFolder))
        {
            _ringFolder = null; // the folder went away (config reloaded)
        }

        bool follows = RingOps.FollowsDefault(c, RingApp);
        IReadOnlyList<RingSlot> shown = _ringFolder is { } open ? slots[open].Folder! : slots;
        _ringPreview.Render(c.Ring, shown, _ringFolder is not null, follows);
        BuildRingHeader(c, slots, follows);
    }

    private void BuildRingHeader(SlenderConfig c, IReadOnlyList<RingSlot> slots, bool follows)
    {
        _ringHeader!.Children.Clear();
        Profile? profile = ProfileOps.Find(c, RingApp);
        string appName = RingApp is null ? "All apps" : profile is null ? RingApp : ProfileOps.AppName(profile);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (_ringFolder is { } f)
        {
            var back = new Button { Style = Ui.Res<Style>("QuietButtonStyle"), Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Ui.Glyph("", 12, Ui.Ink), new TextBlock { Text = "Back" } } } };
            AutomationProperties.SetName(back, "Back to the ring");
            back.Click += (_, _) => LeaveRingFolder();
            row.Children.Add(back);
            TextBlock folderName = Ui.Text(slots[f].Name ?? "Folder", 15, Ui.Ink, Ui.Heading);
            TextBlock inApp = Ui.Text($"in {appName}", 12, Ui.Muted);
            folderName.VerticalAlignment = VerticalAlignment.Center;
            inApp.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(folderName);
            row.Children.Add(inApp);
        }
        else
        {
            FrameworkElement icon = AppIcon(profile, 24);
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(icon);
            var name = Ui.Text(follows ? $"{appName} · Same as All apps" : appName, 14, follows ? Ui.Muted : Ui.Ink, Ui.Heading);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            if (follows)
            {
                var customise = new Button { Content = $"Customise for {appName}", Style = Ui.Res<Style>("PrimaryPillStyle") };
                customise.Click += (_, _) =>
                {
                    _config.Update(x => RingOps.Customise(x, RingApp));
                    DispatcherQueue.TryEnqueue(() => _ringPreview?.Focus(FocusState.Programmatic));
                };
                row.Children.Add(customise);
            }
            else if (RingApp is not null)
            {
                var reset = new Button { Content = "Reset to All apps", Style = Ui.Res<Style>("QuietButtonStyle") };
                reset.Click += async (_, _) =>
                {
                    if (await ConfirmAsync($"{appName} will use the All apps ring again. Its own slots are removed.", "Reset to All apps"))
                    {
                        _config.Update(x => RingOps.ResetToDefault(x, RingApp));
                    }
                };
                row.Children.Add(reset);
            }
        }

        _ringHeader.Children.Add(row);
    }

    private RingPath RingPathOf(int index) => _ringFolder is { } f ? new RingPath(f, index) : new RingPath(index);

    private void LeaveRingFolder()
    {
        if (_ringFolder is not { } f)
        {
            return;
        }

        _ringFolder = null;
        if (_ringPreview is not null)
        {
            _ringPreview.Selected = f;
        }

        RefreshRing();
        _ringPreview?.Focus(FocusState.Programmatic);
    }

    private void OnRingSlotInvoked(int index, Point at)
    {
        RingPath path = RingPathOf(index);
        RingSlot? slot = RingOps.Get(_config.Config, RingApp, path);
        if (slot is null)
        {
            return;
        }

        if (slot.IsFolder && !path.IsChild)
        {
            _ringFolder = index; // a click on a folder opens it
            if (_ringPreview is not null)
            {
                _ringPreview.Selected = 0;
            }

            RefreshRing();
            return;
        }

        ShowSlotPicker(path, at);
    }

    /// <summary>The action picker for one slot, with its Name box and Icon tab.</summary>
    private void ShowSlotPicker(RingPath path, Point at)
    {
        if (_ringPreview is null || RingOps.Get(_config.Config, RingApp, path) is not { } slot)
        {
            return;
        }

        string where = path.IsChild ? $"Slot {path.Child + 1} in {RingOps.Get(_config.Config, RingApp, path.Parent)?.Name ?? "Folder"}" : $"Slot {path.Slot + 1}";
        ActionSpec? own = slot.Action is ActionSpec.None ? null : slot.Action;
        string initial = own switch
        {
            ActionSpec.MouseButton or ActionSpec.ToggleThumbWheelMode => "mouse",
            ActionSpec.Excel => "excel",
            ActionSpec.Launch or ActionSpec.Disabled or ActionSpec.Brightness or ActionSpec.Text => "more",
            ActionSpec.Keys => "keys",
            _ => "keys",
        };

        RingPreview preview = _ringPreview;
        var spec = new PickerSpec(where, RingTabs, initial, own, "Leave this slot empty", false,
            action => _config.Update(c => RingOps.SetAction(c, RingApp, path, action ?? new ActionSpec.None())))
        {
            ShortcutApp = RingApp,
            At = at,
            Closed = () => preview.Focus(FocusState.Programmatic),
            Extra = () => RingNameBox(path),
            CustomTab = key => key == "icon" ? RingIconTab(path) : null,
        };
        ShowPickerCore(preview, spec);
    }

    private FrameworkElement RingNameBox(RingPath path)
    {
        RingSlot? slot = RingOps.Get(_config.Config, RingApp, path);
        string derived = slot is null ? string.Empty : RingOps.Describe((slot.IsFolder ? slot : slot with { Name = null })).Title;
        var box = new TextBox { Header = "Name", Text = slot?.Name ?? string.Empty, PlaceholderText = derived, MaxLength = RingOps.FolderNameMax };
        AutomationProperties.SetName(box, "Slot name");
        box.TextChanged += (_, _) => _config.Update(c => RingOps.SetName(c, RingApp, path, box.Text));
        return box;
    }

    /// <summary>Icon tab: Automatic, a curated Segoe Fluent glyph, or a file's icon.</summary>
    private (FrameworkElement Content, FrameworkElement? Focus)? RingIconTab(RingPath path)
    {
        RingSlot? slot = RingOps.Get(_config.Config, RingApp, path);
        RingIconKind kind = RingGlyphs.Kind(slot?.Icon, out string value);

        var automatic = new Button { Content = "Automatic", Style = Ui.Res<Style>("QuietButtonStyle") };
        var grid = new GridView { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true, MaxHeight = 190 };
        AutomationProperties.SetName(grid, "Icons");
        GridViewItem? current = null;
        foreach (RingGlyph g in RingGlyphs.Catalog)
        {
            var item = new GridViewItem
            {
                Tag = g,
                Width = 40,
                Height = 40,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Content = new FontIcon { Glyph = RingGlyphs.ToChar(g.Code) ?? string.Empty, FontSize = 17, Foreground = Ui.Ink, HorizontalAlignment = HorizontalAlignment.Center },
            };
            AutomationProperties.SetName(item, g.Name);
            ToolTipService.SetToolTip(item, g.Name);
            grid.Items.Add(item);
            if (kind == RingIconKind.Glyph && string.Equals(g.Code, value, StringComparison.OrdinalIgnoreCase))
            {
                current = item;
            }
        }

        if (current is not null)
        {
            grid.SelectedItem = current;
        }

        grid.ItemClick += (_, e) =>
        {
            if (ClickedItems.Find<RingGlyph>(e.ClickedItem, grid.Items.OfType<GridViewItem>().Select(i => ((object)i, (object?)i.Content, (object?)i.Tag))) is { } g)
            {
                _config.Update(c => RingOps.SetIcon(c, RingApp, path, RingGlyphs.GlyphIcon(g.Code)));
            }
        };
        automatic.Click += (_, _) =>
        {
            grid.SelectedItem = null;
            _config.Update(c => RingOps.SetIcon(c, RingApp, path, null));
        };

        var file = new Button { Content = "From a file...", Style = Ui.Res<Style>("QuietButtonStyle") };
        file.Click += async (_, _) =>
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            foreach (string ext in new[] { ".exe", ".lnk", ".png", ".ico", ".jpg", ".jpeg" })
            {
                picker.FileTypeFilter.Add(ext);
            }

            Windows.Storage.StorageFile? chosen = await picker.PickSingleFileAsync();
            if (chosen is not null)
            {
                grid.SelectedItem = null;
                _config.Update(c => RingOps.SetIcon(c, RingApp, path, RingGlyphs.FileIcon(chosen.Path)));
            }
        };

        string caption = kind switch
        {
            RingIconKind.File => "File: " + Path.GetFileName(value),
            RingIconKind.Glyph => "Chosen icon",
            _ => "Automatic: the app's icon, else a glyph for the action",
        };
        var note = Ui.Text(caption, 12, Ui.Muted);
        note.TextWrapping = TextWrapping.Wrap;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(automatic);
        row.Children.Add(file);
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(row);
        panel.Children.Add(grid);
        panel.Children.Add(note);
        return (panel, automatic);
    }

    // ---- slot menu ----

    private void OnRingSlotMenu(int index, Point at)
    {
        if (_ringPreview is null)
        {
            return;
        }

        RingPath path = RingPathOf(index);
        RingSlot? slot = RingOps.Get(_config.Config, RingApp, path);
        if (slot is null)
        {
            return;
        }

        var menu = new MenuFlyout();
        MenuFlyoutItem Item(string text, Action act, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
            item.Click += (_, _) => act();
            menu.Items.Add(item);
            return item;
        }

        Item(slot.IsFolder ? "Copy folder" : "Copy", () => _ringClipboard = slot, !slot.IsEmpty);
        Item("Paste", () => _ = PasteRingSlotAsync(path), RingOps.CanPaste(_ringClipboard, path));
        Item(slot.IsFolder ? "Delete folder" : "Clear", () => _ = RemoveRingSlotAsync(path), !slot.IsEmpty);
        if (!path.IsChild)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            if (slot.IsFolder)
            {
                Item("Turn into an action", () => _ = ConvertRingFolderAsync(path));
                Item("Name and icon...", () => ShowFolderDetails(path, at));
            }
            else
            {
                Item("Make folder", () => _config.Update(c => RingOps.MakeFolder(c, RingApp, path)));
            }
        }

        menu.ShowAt(_ringPreview, new FlyoutShowOptions { Position = at, Placement = FlyoutPlacementMode.RightEdgeAlignedTop });
    }

    private async Task RemoveRingSlotAsync(RingPath path)
    {
        RingSlot? slot = RingOps.Get(_config.Config, RingApp, path);
        if (slot is null || slot.IsEmpty)
        {
            return;
        }

        if (RingOps.NeedsConfirm(slot) && !await ConfirmAsync(RingOps.ConfirmText(slot, "Remove"), "Remove"))
        {
            return;
        }

        _config.Update(c => RingOps.Clear(c, RingApp, path));
        _ringPreview?.Focus(FocusState.Programmatic);
    }

    private async Task ConvertRingFolderAsync(RingPath path)
    {
        RingSlot? slot = RingOps.Get(_config.Config, RingApp, path);
        if (slot is null || !slot.IsFolder)
        {
            return;
        }

        if (RingOps.NeedsConfirm(slot) && !await ConfirmAsync(RingOps.ConfirmText(slot, "Remove"), "Turn into an action"))
        {
            return;
        }

        _config.Update(c => RingOps.ConvertToAction(c, RingApp, path));
    }

    private async Task PasteRingSlotAsync(RingPath path)
    {
        RingSlot? target = RingOps.Get(_config.Config, RingApp, path);
        RingSlot? copied = _ringClipboard;
        if (target is null || !RingOps.CanPaste(copied, path))
        {
            return;
        }

        if (RingOps.NeedsConfirm(target) && !await ConfirmAsync(RingOps.ConfirmText(target, "Replace"), "Replace"))
        {
            return;
        }

        _config.Update(c => RingOps.Paste(c, RingApp, path, copied));
    }

    /// <summary>A folder has no action, so its flyout is just the Name box and the Icon tab.</summary>
    private void ShowFolderDetails(RingPath path, Point at)
    {
        if (_ringPreview is null)
        {
            return;
        }

        RingPreview preview = _ringPreview;
        var spec = new PickerSpec("Folder", [new("icon", "Icon")], "icon", null, string.Empty, false, _ => { })
        {
            At = at,
            Closed = () => preview.Focus(FocusState.Programmatic),
            Extra = () => RingNameBox(path),
            CustomTab = key => key == "icon" ? RingIconTab(path) : null,
        };
        ShowPickerCore(preview, spec);
    }

    private async Task<bool> ConfirmAsync(string text, string primary)
    {
        var dialog = new ContentDialog
        {
            Title = text.Length < 60 && text.EndsWith('?') ? text : primary,
            Content = text.Length < 60 && text.EndsWith('?') ? null : new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Root.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>The image to draw for a "file:" icon: the image itself, or the icon extracted from an exe or shortcut.</summary>
    private async Task<string?> RingFileIconAsync(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".png" or ".ico" or ".jpg" or ".jpeg" or ".bmp")
        {
            return path;
        }

        uint hash = 2166136261;
        foreach (char ch in path.ToLowerInvariant())
        {
            hash = (hash ^ ch) * 16777619;
        }

        return await _icons.GetIconPathAsync($"ring-{hash:x8}", path);
    }
}
