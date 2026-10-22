using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Services;
using Slender.Settings.Views;
using Windows.Graphics;
using Windows.System;

namespace Slender.Settings;

/// <summary>
/// Tray popover mode (<c>Slender.Settings.exe --popover</c>): a borderless Acrylic window of 330 px above the taskbar, with the
/// devices, the quick toggles and "Open Slender", which unfolds the same window into the full settings window. Only the
/// popover's own content exists until then. Esc, losing focus or the tray icon again closes it and ends the process.
/// </summary>
public sealed partial class MainWindow
{
    private const double PopoverWidth = 330;
    private const double PopoverGap = 8;
    private const int UnfoldMs = 250;

    private bool _popoverActive;
    private bool _popoverStarted;
    private bool _unfolding;
    private bool _fullChromeBuilt;
    private bool _noDismiss;
    private bool _firstFrame;
    private bool _iconsAllowed;
    private PxRect _monitorRect;
    private PxRect _workArea;
    private PxPoint _cursor;
    private DeferredWork? _deferred;
    private Border _popCard = null!;
    private StackPanel _popRows = null!;
    private Grid _popSummary = null!;
    private Button _openButton = null!;
    private (Image Image, FrameworkElement Holder, string Process, string? Exe)? _pendingIcon;

    // ---- setup (runs in the constructor: keep it light) ----

    private void SetUpPopover()
    {
        _popoverActive = true;
        _popoverStarted = true;
        Title = "Slender";
        _noDismiss = ConfigDirOverride is not null && Environment.GetEnvironmentVariable("SLENDER_NODISMISS") == "1"; // test hook

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        NativeMethods.GetCursorPos(out NativeMethods.Point pt);
        if (ConfigDirOverride is not null && Environment.GetEnvironmentVariable("SLENDER_POPOVER_CURSOR") is { } c && c.Split(',') is [var cx, var cy]
            && int.TryParse(cx, out int tx) && int.TryParse(cy, out int ty))
        {
            pt = new NativeMethods.Point { X = tx, Y = ty }; // test hook
        }

        _cursor = new PxPoint(pt.X, pt.Y);
        nint monitor = NativeMethods.MonitorFromPoint(pt, 2); // MONITOR_DEFAULTTONEAREST
        _scale = NativeMethods.GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
        DisplayArea area = DisplayArea.GetFromPoint(new PointInt32(pt.X, pt.Y), DisplayAreaFallback.Nearest);
        _monitorRect = new PxRect(area.OuterBounds.X, area.OuterBounds.Y, area.OuterBounds.Width, area.OuterBounds.Height);
        _workArea = new PxRect(area.WorkArea.X, area.WorkArea.Y, area.WorkArea.Width, area.WorkArea.Height);

        SystemBackdrop = new DesktopAcrylicBackdrop();
        ExtendsContentIntoTitleBar = true;
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        int round = 2; // DWMWCP_ROUND: rounded corners and the system shadow
        NativeMethods.DwmSetWindowAttribute(_hwnd, 33, ref round, sizeof(int));

        Root.Visibility = Visibility.Collapsed;
        PopRoot.Visibility = Visibility.Visible;
        BuildPopover();
        FitPopover();

        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, e) =>
        {
            e.Handled = true;
            ClosePopover();
        };
        PopRoot.KeyboardAccelerators.Add(escape);
        Activated += OnPopoverActivated;

        _deferred = new DeferredWork(work => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => work()));
        _deferred.Add("icon", () =>
        {
            _iconsAllowed = true;
            LoadPendingIcon();
        });
    }

    /// <summary>Called right after Activate(): takes the foreground, times the first frame, then lets the deferred work run.</summary>
    public void AfterActivate()
    {
        if (!_popoverActive)
        {
            return;
        }

        NativeMethods.SetForegroundWindow(_hwnd);
        _openButton.Focus(FocusState.Programmatic);
        CompositionTarget.Rendering += OnFirstFrame;

        // A popover that never got the foreground (and so will never see a deactivation) must not linger.
        Microsoft.UI.Dispatching.DispatcherQueueTimer guard = DispatcherQueue.CreateTimer();
        guard.Interval = TimeSpan.FromSeconds(2);
        guard.IsRepeating = false;
        guard.Tick += (_, _) =>
        {
            if (_popoverActive && !_unfolding && !_noDismiss && NativeMethods.GetForegroundWindow() != _hwnd)
            {
                ClosePopover();
            }
        };
        guard.Start();
    }

    private void OnFirstFrame(object? sender, object e)
    {
        if (_firstFrame)
        {
            return;
        }

        _firstFrame = true;
        CompositionTarget.Rendering -= OnFirstFrame;
        double sinceMain = Stopwatch.GetElapsedTime(Program.StartTimestamp).TotalMilliseconds;
        _deferred?.Start();

        if (ConfigDirOverride is not null)
        {
            using Process me = Process.GetCurrentProcess();
            double sinceProcessStart = (DateTime.Now - me.StartTime).TotalMilliseconds;
            WriteTiming($"first-frame main={sinceMain:F0}ms process={sinceProcessStart:F0}ms ws={me.WorkingSet64 / 1048576.0:F1}MB private={me.PrivateMemorySize64 / 1048576.0:F1}MB");
            if (Environment.GetEnvironmentVariable("SLENDER_SHOT") is { Length: > 0 } shot)
            {
                _noDismiss = true;
                _ = ShotAsync(shot);
            }
            else if (_noDismiss)
            {
                // Measurement run: log memory a few times while it stays open.
                Microsoft.UI.Dispatching.DispatcherQueueTimer t = DispatcherQueue.CreateTimer();
                t.Interval = TimeSpan.FromSeconds(3);
                t.IsRepeating = true;
                t.Tick += (_, _) =>
                {
                    using Process p = Process.GetCurrentProcess();
                    WriteTiming($"open ws={p.WorkingSet64 / 1048576.0:F1}MB private={p.PrivateMemorySize64 / 1048576.0:F1}MB state={(_popoverActive ? "popover" : "window")}");
                };
                t.Start();
            }
        }
    }

    private static void WriteTiming(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(ConfigDirOverride!, "popover-timing.txt"), line + Environment.NewLine);
        }
        catch (IOException)
        {
            // test hook only
        }
    }

    private async Task ShotAsync(string path)
    {
        if (Environment.GetEnvironmentVariable("SLENDER_POPOVER_UNFOLD") == "1")
        {
            await Task.Delay(1200);
            await UnfoldAsync();
            WriteTiming("unfold-complete");
        }

        await Task.Delay(2300);
        WriteTiming($"shot shell={Shell.ActualWidth}x{Shell.ActualHeight} card={_popCard?.ActualWidth}x{_popCard?.ActualHeight} window={AppWindow.Size.Width}x{AppWindow.Size.Height}@{AppWindow.Position.X},{AppWindow.Position.Y} scale={_scale}");
        await SaveSnapshotAsync(path);
    }

    // ---- content ----

    private void BuildPopover()
    {
        _notchBars.Clear();
        _popRows = new StackPanel { Spacing = 6 };
        _popSummary = new Grid { ColumnSpacing = 8 };
        _popSummary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _popSummary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var stack = new StackPanel { Spacing = 12 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new Viewbox
        {
            Width = 16,
            Height = 16,
            Child = new Microsoft.UI.Xaml.Controls.Canvas
            {
                Width = 16,
                Height = 16,
                Children =
                {
                    new Microsoft.UI.Xaml.Shapes.Path
                    {
                        Stroke = Ui.Mark,
                        StrokeThickness = 1.8,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), "M11,3.5 C9.8,2.6 5,2.3 5,5.2 C5,8.2 11,7.2 11,10.4 C11,13.4 6,13.2 4.6,12.2"),
                    },
                },
            },
        });
        header.Children.Add(Ui.Text("Slender", 14, Ui.Ink, Ui.Heading));
        var headerRow = new Grid();
        headerRow.Children.Add(header);
        var version = Ui.Text("v" + OwnVersion, 11.5, Ui.Muted);
        version.HorizontalAlignment = HorizontalAlignment.Right;
        version.VerticalAlignment = VerticalAlignment.Center;
        headerRow.Children.Add(version);
        stack.Children.Add(headerRow);
        stack.Children.Add(_popRows);

        // Quick toggles: the same ones as the bottom bar, grouped by device.
        var toggles = new StackPanel { Spacing = 2 };
        toggles.Children.Add(PopSection("MX Master 4"));
        toggles.Children.Add(PopRow("Haptic feel", HapticNotches()));
        _smartShiftToggle = Toggle("SmartShift free-spin", on => _config.Update(c => c with { Pointer = c.Pointer with { SmartShiftEnabled = on } }));
        toggles.Children.Add(PopRow("SmartShift free-spin", _smartShiftToggle));
        toggles.Children.Add(PopSection("ERGO K860", 6));
        _fnLockToggle = Toggle("F-keys first (Fn lock)", on =>
        {
            if (!_syncingFn) _config.Update(c => c with { Keyboard = c.Keyboard with { FnLock = on } });
        });
        ToolTipService.SetToolTip(_fnLockToggle, "On: F1 to F12 work without holding Fn; hold Fn for the media keys. Fn+Esc on the keyboard flips it too.");
        toggles.Children.Add(PopRow("F-keys first (Fn lock)", _fnLockToggle));
        stack.Children.Add(toggles);

        stack.Children.Add(new Border { Height = 1, Background = Ui.Rule, Opacity = 0.8 });
        stack.Children.Add(_popSummary);

        _openButton = new Button
        {
            Content = "Open Slender",
            Style = Ui.Res<Style>("PrimaryPillStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(14, 8, 14, 8),
        };
        AutomationProperties.SetName(_openButton, "Open Slender");
        _openButton.Click += async (_, _) => await UnfoldAsync();
        stack.Children.Add(_openButton);

        _popCard = new Border
        {
            Width = PopoverWidth,
            Padding = new Thickness(14, 12, 14, 14),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xA6, 0x1B, 0x1F, 0x23)),
            BorderBrush = Ui.Rule,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = stack,
        };
        PopRoot.Children.Clear();
        PopRoot.Children.Add(_popCard);
        PopRoot.Loaded += (_, _) => FitPopover(); // the real height is known once the templates are applied

        UpdateDeviceRows();
        UpdateSummary();
        SyncBottomBar();
    }

    private static TextBlock PopSection(string text, double top = 0)
    {
        TextBlock t = Ui.Text(text.ToUpperInvariant(), 11, Ui.Muted, Ui.Heading);
        t.Margin = new Thickness(2, top, 0, 2);
        return t;
    }

    private static Grid PopRow(string label, FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 34, Padding = new Thickness(2, 0, 0, 0), ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock text = Ui.Text(label, 13);
        text.VerticalAlignment = VerticalAlignment.Center;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>Both devices with their battery, from status.json. Thumbnails come from the cache if it is already there.</summary>
    private void UpdateDeviceRows()
    {
        _popRows.Children.Clear();
        if (VisibleKinds().Count == 0 && !HeadsetListed())
        {
            _popRows.Children.Add(Ui.Text("No devices yet", 12, Ui.Muted));
        }

        foreach (DeviceKind kind in VisibleKinds())
        {
            string id = ControlCatalog.DeviceId(kind);
            DeviceState? state = _status.Device(id);
            bool trayDown = _status.Status is null;
            bool offline = DevicePresenceView.Dimmed(_status.Status, id);

            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 8, 12, 8), CornerRadius = new CornerRadius(8), Background = Ui.Brush("Paper2Brush") };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement thumb = DeviceThumb(kind);
            thumb.Opacity = offline ? 0.5 : 1;
            row.Children.Add(thumb);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            text.Children.Add(Ui.Text(ControlCatalog.DeviceName(kind), 14, Ui.Ink, Ui.Heading));
            text.Children.Add(Ui.Text(DevicePresenceView.Subtitle(_status.Status, id), 12, Ui.Muted));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            FrameworkElement battery = BatteryText(_status.Status, state, id, offline || trayDown);
            battery.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(battery, 2);
            row.Children.Add(battery);

            string batteryName = state?.BatteryPercent is { } p ? $", battery {p} percent" : string.Empty;
            AutomationProperties.SetName(row, ControlCatalog.DeviceName(kind) + batteryName + DevicePresenceView.AccessibleSuffix(_status.Status, id));
            _popRows.Children.Add(row);
        }

        if (HeadsetListed())
        {
            _popRows.Children.Add(HeadsetPopRow());
        }

        FitPopover();
    }

    private FrameworkElement DeviceThumb(DeviceKind kind)
    {
        string file = BuiltInPictures.For(kind).ThumbPath;
        var image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        if (kind == DeviceKind.Mouse)
        {
            image.Height = 36;
            image.Source = Ui.Bitmap(file, decodePixelHeight: (int)Math.Ceiling(36 * _scale));
        }
        else
        {
            image.Width = 44;
            image.Height = 36;
            image.Source = Ui.Bitmap(file, decodePixelWidth: (int)Math.Ceiling(44 * _scale));
        }

        return image;
    }

    /// <summary>"You're in Excel · N buttons changed". The icon is extracted after the first frame.</summary>
    private void UpdateSummary()
    {
        _popSummary.Children.Clear();
        _pendingIcon = null;
        string? process = _status.Status?.ForegroundApp;
        if (string.IsNullOrWhiteSpace(process))
        {
            _popSummary.Visibility = Visibility.Collapsed;
            return;
        }

        SlenderConfig c = _config.Config;
        Profile? profile = ProfileOps.Find(c, process);
        string name = profile is null ? PopoverModel.ProcessDisplayName(process) : ProfileOps.AppName(profile);
        int changed = profile is null ? 0 : ProfileOps.ChangedCount(c, process, DeviceKind.Mouse) + ProfileOps.ChangedCount(c, process, DeviceKind.Keyboard);

        const double size = 20;
        var holder = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        holder.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(size * 0.25),
            Background = Ui.Paper3,
            Child = new FontIcon { Glyph = "\uF75D", FontSize = size * 0.5, Foreground = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        holder.Children.Add(image);
        _pendingIcon = (image, holder, process, profile?.ExePath);
        if (_iconsAllowed)
        {
            LoadPendingIcon();
        }

        TextBlock text = Ui.Text(PopoverModel.Summary(name, changed)!, 12.5, Ui.Muted);
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        _popSummary.Children.Add(holder);
        _popSummary.Children.Add(text);
        _popSummary.Visibility = Visibility.Visible;
    }

    private void LoadPendingIcon()
    {
        if (_pendingIcon is { } p)
        {
            _pendingIcon = null;
            _ = LoadIconAsync(p.Image, p.Holder, p.Process, p.Exe, 20);
        }
    }

    private void RefreshPopover()
    {
        if (_unfolding)
        {
            return;
        }

        SyncBottomBar();
        UpdateDeviceRows();
        UpdateSummary();
    }

    // ---- placement ----

    /// <summary>Sizes the window to the content (330 DIP wide) and places it against the taskbar edge.</summary>
    private void FitPopover()
    {
        if (!_popoverActive || _unfolding)
        {
            return;
        }

        // Measure, not ActualHeight: the arranged height is clamped to the window, which is what is being sized.
        _popCard.Measure(new Windows.Foundation.Size(PopoverWidth, double.PositiveInfinity));
        double heightDip = _popCard.DesiredSize.Height;

        // The borderless window still has a few invisible frame pixels: ask for them on top so the client area is exactly 330 DIP.
        int frameX = Math.Max(0, AppWindow.Size.Width - AppWindow.ClientSize.Width);
        int frameY = Math.Max(0, AppWindow.Size.Height - AppWindow.ClientSize.Height);
        int w = (int)Math.Round(PopoverWidth * _scale) + frameX;
        int h = (int)Math.Ceiling(heightDip * _scale) + frameY;
        PxRect r = PopoverPlacement.Place(_cursor, _monitorRect, _workArea, w, h, (int)Math.Round(PopoverGap * _scale));
        AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
    }

    // ---- dismissal ----

    private void OnPopoverActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated && _popoverActive && !_unfolding && !_noDismiss)
        {
            Program.NoteFocusDismissal();
            ClosePopover();
        }
    }

    private void ClosePopover()
    {
        if (_popoverActive)
        {
            Close(); // OnClosed flushes the config and exits the process
        }
    }

    /// <summary>The tray icon was clicked again.</summary>
    public void ToggleFromSignal()
    {
        if (_popoverActive)
        {
            if (!_unfolding)
            {
                ClosePopover();
            }

            return;
        }

        BringToFront();
    }

    /// <summary>"Settings…" from the tray while the popover is open: unfold. Otherwise bring the window to the front.</summary>
    public void ShowFromSignal()
    {
        if (_popoverActive)
        {
            _ = UnfoldAsync();
            return;
        }

        BringToFront();
    }

    // ---- unfold: the popover grows into the settings window (250 ms ease-out; instant with animations off) ----

    private async Task UnfoldAsync()
    {
        if (!_popoverActive || _unfolding)
        {
            return;
        }

        _unfolding = true;
        _status.EnableFull();
        if (!_fullChromeBuilt)
        {
            _fullChromeBuilt = true;
            BuildInfoBars();
            AddAccelerators();
        }

        var from = new PxRect(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        int margin = (int)Math.Round(12 * _scale);
        PxRect to = PopoverPlacement.Unfold(from, (int)(_ui.Width * _scale), (int)(_ui.Height * _scale), _workArea, margin);
        bool animate = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        // The tray's screen is too short or narrow for the full window (user 2026-10-21: a 2560x720 strip under the main screen):
        // open it on the screen it was last on, or the primary one, at full size. No grow animation across screens.
        if (to.Width < (int)(_ui.Width * _scale) || to.Height < (int)(_ui.Height * _scale))
        {
            PxPoint? last = _ui.X is { } lx && _ui.Y is { } ly ? new PxPoint(lx, ly) : null;
            if (PopoverPlacement.OtherScreen(Screens(), last, _ui.Width, _ui.Height) is { } other)
            {
                AppWindow.Move(new PointInt32(other.X, other.Y)); // onto the other screen first, so its DPI applies
                _scale = NativeMethods.GetDpiForWindow(_hwnd) is var d and > 0 ? d / 96.0 : _scale;
                DisplayArea area = DisplayArea.GetFromPoint(new PointInt32(other.X, other.Y), DisplayAreaFallback.Nearest);
                _workArea = new PxRect(area.WorkArea.X, area.WorkArea.Y, area.WorkArea.Width, area.WorkArea.Height);
                from = new PxRect(other.X, other.Y, AppWindow.Size.Width, AppWindow.Size.Height);
                to = other;
                animate = false;
            }
        }

        // Build the whole window at its final size behind the popover; it fades in as the frame grows.
        bool right = from.X + from.Width / 2 >= _workArea.X + _workArea.Width / 2;
        bool bottom = from.Y + from.Height / 2 >= _workArea.Y + _workArea.Height / 2;
        Root.Width = to.Width / _scale;
        Root.Height = to.Height / _scale;
        Root.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Root.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        _popCard.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _popCard.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        PopRoot.IsHitTestVisible = false;
        Root.Opacity = 0;
        Root.Visibility = Visibility.Visible;
        PrepareFull();

        if (animate)
        {
            var done = new TaskCompletionSource();
            var clock = Stopwatch.StartNew();
            Microsoft.UI.Dispatching.DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(15);
            timer.IsRepeating = true;
            timer.Tick += (_, _) =>
            {
                double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / UnfoldMs);
                PxRect r = PopoverPlacement.Lerp(from, to, PopoverPlacement.EaseOut(t));
                AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
                _popCard.Opacity = 1 - Math.Clamp(t / 0.4, 0, 1);
                Root.Opacity = Math.Clamp((t - 0.3) / 0.7, 0, 1);
                if (t >= 1)
                {
                    timer.Stop();
                    done.TrySetResult();
                }
            };
            timer.Start();
            await done.Task;
        }

        FinishUnfold(to);
    }

    /// <summary>Every screen's work area (pixels), its scale and whether it is the primary one.</summary>
    private static List<(PxRect Work, double Scale, bool Primary)> Screens()
    {
        var screens = new List<(PxRect, double, bool)>();
        DisplayArea primary = DisplayArea.Primary;
        foreach (DisplayArea a in DisplayArea.FindAll())
        {
            RectInt32 w = a.WorkArea;
            var centre = new NativeMethods.Point { X = w.X + w.Width / 2, Y = w.Y + w.Height / 2 };
            nint monitor = NativeMethods.MonitorFromPoint(centre, 2);
            double scale = NativeMethods.GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
            screens.Add((new PxRect(w.X, w.Y, w.Width, w.Height), scale, a.DisplayId.Value == primary.DisplayId.Value));
        }

        return screens;
    }

    private void FinishUnfold(PxRect to)
    {
        // Real window frame: caption buttons, resize borders, minimum size, Mica, taskbar button.
        AppWindow.SetPresenter(OverlappedPresenter.Create());
        AppWindow.IsShownInSwitchers = true;
        int none = 0; // DWMWCP_DEFAULT
        NativeMethods.DwmSetWindowAttribute(_hwnd, 33, ref none, sizeof(int));
        SystemBackdrop = new MicaBackdrop();
        SetTitleBar(TitleBar);
        ApplyFullChrome();
        AppWindow.MoveAndResize(new RectInt32(to.X, to.Y, to.Width, to.Height));

        PopRoot.Visibility = Visibility.Collapsed;
        PopRoot.Children.Clear();
        Root.ClearValue(FrameworkElement.WidthProperty);
        Root.ClearValue(FrameworkElement.HeightProperty);
        Root.HorizontalAlignment = HorizontalAlignment.Stretch;
        Root.VerticalAlignment = VerticalAlignment.Stretch;
        Root.Opacity = 1;
        _popoverActive = false;
        _unfolding = false;
        Activated -= OnPopoverActivated;
        BringToFront();
        FocusSelectedHotspot(keyboard: false);
    }
}
