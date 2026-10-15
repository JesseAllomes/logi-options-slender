using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Slender.Engine;
using Slender.Engine.Config;
using Slender.Settings.Logic;
using Slender.Settings.Services;
using Slender.Settings.Views;
using Windows.Graphics;
using Windows.System;

namespace Slender.Settings;

/// <summary>
/// The settings window. The class is split by area: Rail (devices and apps), Stage (render, hotspots, labels), Picker (the
/// action flyout), Bottom (quick settings and InfoBars), Pages (Point and scroll, Haptics, General) and Compare (the grid).
/// </summary>
public sealed partial class MainWindow : Window
{
    private enum Page
    {
        Device,
        Pointer,
        Haptics,
        Ring,
        General,
    }

    /// <summary>Test hook: SLENDER_CONFIG_DIR redirects config.json and window state to another folder.</summary>
    private static readonly string? ConfigDirOverride = Environment.GetEnvironmentVariable("SLENDER_CONFIG_DIR") is { Length: > 0 } d ? d : null;

    private static readonly string? StatusFileOverride = ConfigDirOverride is null ? null : Path.Combine(ConfigDirOverride, "status.json");

    private static readonly string UiPath = Path.Combine(ConfigDirOverride ?? SlenderPaths.LocalDirectory, "settings-ui.json");

    private readonly UiState _ui = UiState.Load(UiPath);
    private readonly ConfigService _config;
    private readonly StatusMonitor _status;
    private readonly AssetCache _assets = new(AssetCache.DefaultDepotsRoot, SlenderPaths.AssetsDirectory);
    private readonly IconService _icons;
    private readonly Dictionary<DeviceKind, CachedDevice?> _devices = [];
    private readonly Dictionary<DeviceKind, ushort> _selected = [];

    private DeviceKind _kind;
    private string? _app;
    private Page _page = Page.Device;
    private bool _ready;
    private bool _syncing;
    private nint _hwnd;
    private double _scale = 1;

    public MainWindow(bool popover = false)
    {
        InitializeComponent();
        _kind = _ui.Device == DeviceIds.Keyboard ? DeviceKind.Keyboard : DeviceKind.Mouse;
        _app = _ui.App;

        _config = new ConfigService(ConfigDirOverride is null ? new JsonConfigStore() : new JsonConfigStore(ConfigDirOverride), DispatcherQueue);
        _status = new StatusMonitor(DispatcherQueue, StatusFileOverride, light: popover);
        _icons = new IconService(_assets.IconsDirectory);

        _config.Changed += OnConfigChanged;
        _status.Changed += OnStatusChanged;
        Closed += OnClosed;

        if (popover)
        {
            // Compact tray popover: only its own content is built; the full window is built when it unfolds (MainWindow.Popover.cs).
            SetUpPopover();
            return;
        }

        SetUpWindow();
        BuildInfoBars();
        AddAccelerators();
        Shell.Loaded += async (_, _) => await InitializeAsync();
    }

    private Task<bool>? _assetsTask;

    /// <summary>Copies/loads the device renders once (off the UI thread). The popover defers it; the window awaits it.</summary>
    private Task<bool> EnsureAssetsAsync() => _assetsTask ??= Task.Run(() =>
    {
        foreach (DeviceKind kind in new[] { DeviceKind.Mouse, DeviceKind.Keyboard })
        {
            _devices[kind] = _assets.Ensure(kind);
        }

        return true;
    });

    private ProfileView CurrentProfile => new(_config.Config, _app);

    private async Task InitializeAsync()
    {
        // Copying the renders from Options+ happens off the UI thread, and only when they are not cached yet.
        await EnsureAssetsAsync();
        PrepareFull();
        if (ConfigDirOverride is not null && Environment.GetEnvironmentVariable("SLENDER_SHOT") is { Length: > 0 } shot)
        {
            await SaveSnapshotAsync(shot); // test hook: renders this window's own XAML tree to a PNG, then closes
        }
    }

    /// <summary>Everything the full window needs once the device renders are known: validates the app, builds all regions.</summary>
    private void PrepareFull()
    {
        if (_app is not null && ProfileOps.Find(_config.Config, _app) is null)
        {
            _app = null;
        }

        if (ConfigDirOverride is not null && Environment.GetEnvironmentVariable("SLENDER_PAGE") is { } start)
        {
            _page = start.ToLowerInvariant() switch { "pointer" => Page.Pointer, "haptics" => Page.Haptics, "ring" => Page.Ring, "general" => Page.General, _ => _page }; // test hook
        }

        _ready = true;
        RebuildAll();
        FocusSelectedHotspot(keyboard: false); // no focus ring until the user presses a key
    }

    private async Task SaveSnapshotAsync(string path)
    {
        await Task.Delay(2500);
        var target = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await target.RenderAsync(Shell);
        Windows.Storage.Streams.IBuffer pixels = await target.GetPixelsAsync();
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        Windows.Graphics.Imaging.BitmapEncoder encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)target.PixelWidth, (uint)target.PixelHeight, 96, 96, System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(pixels));
        await encoder.FlushAsync();
        stream.Seek(0);
        byte[] png = new byte[stream.Size];
        using var reader = new Windows.Storage.Streams.DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(png);
        await File.WriteAllBytesAsync(path, png);
        Close();
    }

    private void SetUpWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        SystemBackdrop = new MicaBackdrop();
        Title = "Slender";

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(_ui.Width * _scale), (int)(_ui.Height * _scale)));
        ApplyFullChrome();
    }

    /// <summary>Icon, minimum size and caption button colours of the full window (also applied when the popover unfolds).</summary>
    private void ApplyFullChrome()
    {
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Slender.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(1040 * _scale);
            presenter.PreferredMinimumHeight = (int)(640 * _scale);
        }

        AppWindowTitleBar bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = ColorFrom("#8F989F");
        bar.ButtonInactiveForegroundColor = ColorFrom("#6B747B");
        bar.ButtonHoverBackgroundColor = ColorFrom("#2A3137");
        bar.ButtonHoverForegroundColor = ColorFrom("#ECEFF1");
        bar.ButtonPressedBackgroundColor = ColorFrom("#343C43");
        bar.ButtonPressedForegroundColor = ColorFrom("#ECEFF1");
    }

    private static Windows.UI.Color ColorFrom(string hex) =>
        Windows.UI.Color.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));

    /// <summary>Called by the tray (through the single-instance event) to show this window again.</summary>
    public void BringToFront()
    {
        if (_popoverActive)
        {
            return;
        }

        if (NativeMethods.IsIconic(_hwnd))
        {
            NativeMethods.ShowWindow(_hwnd, 9); // SW_RESTORE
        }

        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (!_popoverActive) // a popover that never unfolded must not overwrite the remembered window size
        {
            _ui.Device = DeviceIds_For(_kind);
            _ui.App = _app;
            if (AppWindow.Size.Width > 0 && _scale > 0)
            {
                _ui.Width = (int)(AppWindow.Size.Width / _scale);
                _ui.Height = (int)(AppWindow.Size.Height / _scale);
            }

            _ui.Save(UiPath);
        }

        _deferred?.Cancel();
        _config.Dispose();
        _status.Dispose();
        if (_popoverStarted)
        {
            Application.Current.Exit(); // nothing may linger once the popover is dismissed
        }
    }

    private static string DeviceIds_For(DeviceKind kind) => ControlCatalog.DeviceId(kind);

    // ---- keyboard: Ctrl+Tab, Ctrl+1..9, F6 ----

    private void AddAccelerators()
    {
        AddAccelerator(VirtualKey.Tab, VirtualKeyModifiers.Control, () => StepApp(+1));
        AddAccelerator(VirtualKey.Tab, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => StepApp(-1));
        for (int i = 1; i <= 9; i++)
        {
            int index = i - 1;
            AddAccelerator((VirtualKey)((int)VirtualKey.Number1 + index), VirtualKeyModifiers.Control, () => SelectAppByIndex(index));
        }

        AddAccelerator(VirtualKey.F6, VirtualKeyModifiers.None, () => CycleRegion(+1));
        AddAccelerator(VirtualKey.F6, VirtualKeyModifiers.Shift, () => CycleRegion(-1));
    }

    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        Root.KeyboardAccelerators.Add(accelerator);
    }

    private List<string?> AppKeys()
    {
        var keys = new List<string?> { null };
        keys.AddRange(_config.Config.Profiles.Where(p => p.AppProcess is not null).Select(p => p.AppProcess));
        return keys;
    }

    private void StepApp(int delta)
    {
        if (_page is not (Page.Device or Page.Ring))
        {
            return;
        }

        List<string?> keys = AppKeys();
        int i = Math.Max(0, keys.FindIndex(k => string.Equals(k, _app, StringComparison.OrdinalIgnoreCase)));
        SelectApp(keys[(i + delta + keys.Count) % keys.Count]);
    }

    private void SelectAppByIndex(int index)
    {
        List<string?> keys = AppKeys();
        if (_page is Page.Device or Page.Ring && index < keys.Count)
        {
            SelectApp(keys[index]);
        }
    }

    private void CycleRegion(int delta)
    {
        FrameworkElement[] regions = [RailPanel, _page == Page.Device ? StageLayer : PagePanel, StripPanel, BottomBarItems];
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        int current = -1;
        for (int i = 0; i < regions.Length && focused is not null; i++)
        {
            if (IsInside(focused, regions[i]) || (i == 3 && IsInside(focused, CompareButton)))
            {
                current = i;
                break;
            }
        }

        for (int step = 1; step <= regions.Length; step++)
        {
            int next = ((current + delta * step) % regions.Length + regions.Length) % regions.Length;
            if (regions[next].Visibility == Visibility.Visible && FocusRegion(next))
            {
                return;
            }
        }
    }

    private static bool IsInside(DependencyObject child, DependencyObject ancestor)
    {
        for (DependencyObject? d = child; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (ReferenceEquals(d, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private bool FocusRegion(int index)
    {
        if (index == 1 && _page == Page.Device && FocusSelectedHotspot())
        {
            return true;
        }

        UIElement target = index switch
        {
            0 => RailPanel,
            1 => _page == Page.Device ? StageLayer : PagePanel,
            2 => StripPanel,
            _ => BottomBarItems,
        };
        DependencyObject? first = FocusManager.FindFirstFocusableElement(target);
        if (index == 3 && first is null)
        {
            first = CompareButton;
        }

        return first is Control c && c.Focus(FocusState.Keyboard);
    }

    // ---- refresh ----

    private void RebuildAll()
    {
        if (!_ready)
        {
            return;
        }

        BuildRail();
        BuildStrip();
        ApplyPageVisibility();
        UpdateHeading();
        BuildBottomBar();
        UpdateInfoBars();
        if (_page == Page.Device)
        {
            LayoutStage();
        }
        else
        {
            BuildPage();
        }
    }

    private void OnConfigChanged()
    {
        if (_popoverActive)
        {
            RefreshPopover();
            return;
        }

        if (!_ready)
        {
            return;
        }

        if (_app is not null && ProfileOps.Find(_config.Config, _app) is null)
        {
            _app = null; // the app was removed in another way (config reloaded)
            RebuildAll();
            return;
        }

        BuildStrip();
        UpdateHeading();
        SyncBottomBar();
        if (_page == Page.Device)
        {
            RefreshStageLabels();
        }
        else if (_page == Page.Ring)
        {
            RefreshRing();
        }

        RefreshCompare();
    }

    private void OnStatusChanged()
    {
        if (_popoverActive)
        {
            RefreshPopover();
            return;
        }

        if (!_ready)
        {
            return;
        }

        BuildRail();
        UpdateInfoBars();
        UpdateStageDim();
        string versionRow = UpdateText.Version(_status.Status, string.Empty) + _status.TrayRunning;
        if (_page == Page.General && versionRow != _versionRow)
        {
            RebuildAll(); // the tray found or downloaded an update
        }

        _versionRow = versionRow;
    }

    private string _versionRow = string.Empty;

    private void ApplyPageVisibility()
    {
        bool device = _page == Page.Device;
        StageLayer.Visibility = device ? Visibility.Visible : Visibility.Collapsed;
        HeadingPanel.Visibility = device ? Visibility.Visible : Visibility.Collapsed;
        PageScroll.Visibility = device ? Visibility.Collapsed : Visibility.Visible;
        BottomBar.Visibility = device ? Visibility.Visible : Visibility.Collapsed;
        bool strip = device || _page == Page.Ring; // the ring's slots depend on the app chosen in the strip
        StripBorder.Opacity = strip ? 1 : 0.35;
        StripPanel.IsHitTestVisible = strip;
    }

    private void SelectDevice(DeviceKind kind)
    {
        if (_kind == kind && _page == Page.Device)
        {
            return;
        }

        _kind = kind;
        _page = Page.Device;
        RebuildAll();
    }

    private void SelectApp(string? app)
    {
        if (_page is not (Page.Device or Page.Ring))
        {
            _page = Page.Device;
        }

        _ringFolder = null;
        _app = app;
        RebuildAll();
    }

    private void OpenPage(Page page)
    {
        _page = page;
        RebuildAll();
    }
}

/// <summary>Read helpers for the profile being edited.</summary>
internal readonly record struct ProfileView(SlenderConfig Config, string? App)
{
    public (ActionSpec? Action, bool Inherited) Effective(DeviceKind kind, ushort id) => ProfileOps.Effective(Config, App, kind, id);

    public int Changed(DeviceKind kind) => ProfileOps.ChangedCount(Config, App, kind);
}
