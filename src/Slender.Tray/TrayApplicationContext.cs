using Slender.Engine;
using Slender.Engine.Config;
using Slender.Hidpp;

namespace Slender.Tray;

/// <summary>
/// Tray icon, context menu and connection-status plumbing. The caller owns the mouse (starts and
/// disposes it); this context only subscribes to it. Mouse events arrive on background threads and
/// are marshalled to the UI thread through a hidden control.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly int BatteryIntervalMs = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;

    private readonly IConfigStore _store;
    private static readonly int KeyboardBatteryIntervalMs = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;

    private readonly ILogiMouse? _mouse;
    private readonly ILogiKeyboard? _keyboard;
    private readonly OptionsPlusGuard? _guard;
    private readonly System.Windows.Forms.Timer _guardTimer;
    private readonly System.Windows.Forms.Timer _keyboardTimer;
    private readonly ToolStripMenuItem _keyboardItem;
    private readonly IForegroundApp? _foreground;
    private readonly StatusPublisher _status;
    private readonly ConfigFileWatcher? _watcher;
    private readonly StartupRegistration _startup;
    private readonly Control _uiProxy;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _excelItem;
    private readonly ToolStripMenuItem _hapticsItem;
    private readonly ToolStripMenuItem _launchItem;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _batteryTimer;
    private readonly CancellationTokenSource _lifetime = new();

    private SlenderConfig _config;
    private bool _connected;
    private BatteryStatus? _battery;
    private bool _keyboardConnected;
    private BatteryStatus? _keyboardBattery;
    private bool _paused;
    private bool _disposed;

    public TrayApplicationContext(IConfigStore store, ILogiMouse? mouse, StartupRegistration? startup = null, IForegroundApp? foreground = null, ILog? log = null, ILogiKeyboard? keyboard = null, OptionsPlusGuard? guard = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _mouse = mouse;
        _keyboard = keyboard;
        _guard = guard;
        _foreground = foreground;
        _status = new StatusPublisher(
            () => new StatusSnapshot(
                _connected, _battery?.Percent, _battery?.Charging ?? false, _foreground?.ProcessName,
                _keyboardConnected, _keyboardBattery?.Percent, _keyboardBattery?.Charging ?? false, _paused),
            log: log);
        _startup = startup ?? StartupRegistration.ForCurrentProcess();
        _config = store.Load();

        _uiProxy = new Control();
        _ = _uiProxy.Handle; // create the handle on the UI thread so BeginInvoke works from workers

        _menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem { Enabled = false };
        _keyboardItem = new ToolStripMenuItem { Enabled = false };
        _excelItem = new ToolStripMenuItem("Excel: thumb wheel scrolls columns") { CheckOnClick = true };
        _excelItem.Click += OnExcelClicked;
        _hapticsItem = new ToolStripMenuItem("Haptics") { CheckOnClick = true };
        _hapticsItem.Click += OnHapticsClicked;
        _launchItem = new ToolStripMenuItem("Launch at login") { CheckOnClick = true };
        _launchItem.Click += OnLaunchClicked;
        var settingsItem = new ToolStripMenuItem("Settings…");
        settingsItem.Click += OnSettingsClicked;
        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => ExitThread();
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _statusItem,
            _keyboardItem,
            _excelItem,
            _hapticsItem,
            _launchItem,
            settingsItem,
            new ToolStripSeparator(),
            quitItem,
        });

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application, // shared system icon; not owned, not disposed
            Text = "Slender",
            ContextMenuStrip = _menu,
        };

        // Timer exists but stays stopped until a mouse is connected.
        _batteryTimer = new System.Windows.Forms.Timer { Interval = BatteryIntervalMs };
        _batteryTimer.Tick += (_, _) => _ = RefreshBatteryAsync();

        // Both timers exist but stay stopped until needed (keyboard connected / Options+ detected).
        _keyboardTimer = new System.Windows.Forms.Timer { Interval = KeyboardBatteryIntervalMs };
        _keyboardTimer.Tick += (_, _) => _ = RefreshKeyboardBatteryAsync();
        _guardTimer = new System.Windows.Forms.Timer { Interval = (int)OptionsPlusGuard.PollInterval.TotalMilliseconds };
        _guardTimer.Tick += (_, _) => EvaluateGuard();

        RefreshChecks();
        UpdateStatus();

        if (_config.LaunchAtLogin && !_startup.IsEnabled)
        {
            try
            {
                _startup.Enable(); // refresh the Run value if the exe has moved
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Non-fatal: the menu still shows the saved preference.
            }
        }

        if (_mouse is not null)
        {
            _mouse.ConnectionChanged += OnMouseConnectionChanged;
            SetConnected(_mouse.IsConnected);
        }

        if (_keyboard is not null)
        {
            _keyboard.ConnectionChanged += OnKeyboardConnectionChanged;
            SetKeyboardConnected(_keyboard.IsConnected);
        }

        if (_foreground is not null)
        {
            _foreground.Changed += OnForegroundChanged;
        }

        if (store is JsonConfigStore json)
        {
            _watcher = new ConfigFileWatcher(json);
            _watcher.Changed += OnConfigFileChanged;
        }

        EvaluateGuard();
        _status.PublishNow();
        _tray.Visible = true;
    }

    /// <summary>Raised on the UI thread after every successful save.</summary>
    public event EventHandler<SlenderConfig>? ConfigChanged;

    public SlenderConfig Config => _config;

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            if (_mouse is not null)
            {
                _mouse.ConnectionChanged -= OnMouseConnectionChanged;
            }

            if (_foreground is not null)
            {
                _foreground.Changed -= OnForegroundChanged;
            }

            if (_keyboard is not null)
            {
                _keyboard.ConnectionChanged -= OnKeyboardConnectionChanged;
            }

            _watcher?.Dispose();
            _guardTimer.Stop();
            _guardTimer.Dispose();
            _keyboardTimer.Stop();
            _keyboardTimer.Dispose();
            _batteryTimer.Stop();
            _batteryTimer.Dispose();

            // Leave an honest last status behind: the tray is gone, so nothing is connected through it.
            _connected = false;
            _battery = null;
            _keyboardConnected = false;
            _keyboardBattery = null;
            _status.PublishNow();
            _status.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();
            _uiProxy.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnForegroundChanged(object? sender, string? process) => _status.Changed();

    /// <summary>config.json was changed by another process (the settings app): reload and apply it.</summary>
    private void OnConfigFileChanged(object? sender, SlenderConfig config) => Post(() =>
    {
        _config = config;
        SyncStartup();
        RefreshChecks();
        ConfigChanged?.Invoke(this, _config);
    });

    private void SyncStartup()
    {
        try
        {
            if (_config.LaunchAtLogin && !_startup.IsEnabled)
            {
                _startup.Enable();
            }
            else if (!_config.LaunchAtLogin && _startup.IsEnabled)
            {
                _startup.Disable();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Non-fatal: the saved preference stays; the next change retries.
        }
    }

    private void OnMouseConnectionChanged(object? sender, bool connected) =>
        Post(() => SetConnected(connected));

    /// <summary>Runs an action on the UI thread. Safe from any thread; dropped after dispose.</summary>
    private void Post(Action action)
    {
        if (_disposed || _uiProxy.IsDisposed)
        {
            return;
        }

        try
        {
            _uiProxy.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Proxy was disposed between the check and the call.
        }
    }

    private void SetConnected(bool connected)
    {
        if (_disposed)
        {
            return;
        }

        _connected = connected;
        if (connected)
        {
            _batteryTimer.Start();
            _ = RefreshBatteryAsync();
        }
        else
        {
            _batteryTimer.Stop();
            _battery = null;
        }

        UpdateStatus();
        _status.Changed();
    }

    private void OnKeyboardConnectionChanged(object? sender, bool connected) =>
        Post(() => SetKeyboardConnected(connected));

    private void SetKeyboardConnected(bool connected)
    {
        if (_disposed)
        {
            return;
        }

        _keyboardConnected = connected;
        if (connected)
        {
            _keyboardTimer.Start();
            _ = RefreshKeyboardBatteryAsync();
        }
        else
        {
            _keyboardTimer.Stop();
            _keyboardBattery = null;
        }

        UpdateStatus();
        _status.Changed();
    }

    private async Task RefreshKeyboardBatteryAsync()
    {
        if (_disposed || !_keyboardConnected || _keyboard is null)
        {
            return;
        }

        try
        {
            BatteryStatus? status = await _keyboard.GetBatteryAsync(_lifetime.Token);
            if (_disposed || !_keyboardConnected)
            {
                return;
            }

            _keyboardBattery = status ?? _keyboardBattery;
            UpdateStatus();
            _status.Changed();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Keep the last known value.
        }
    }

    /// <summary>Probes Options+; starts the engines once it is gone. The 30 s timer runs only while paused.</summary>
    private void EvaluateGuard()
    {
        if (_guard is null || _disposed)
        {
            return;
        }

        bool paused = _guard.Evaluate();
        if (paused)
        {
            _guardTimer.Start();
        }
        else
        {
            _guardTimer.Stop();
        }

        if (paused != _paused)
        {
            _paused = paused;
            UpdateStatus();
            _status.Changed();
        }
    }

    private async Task RefreshBatteryAsync()
    {
        if (_disposed || !_connected || _mouse is null)
        {
            return;
        }

        try
        {
            BatteryStatus? status = await _mouse.GetBatteryAsync(_lifetime.Token);
            if (_disposed || !_connected)
            {
                return;
            }

            _battery = status ?? _battery;
            UpdateStatus();
            _status.Changed();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // A failed battery read is not fatal; keep the last known value.
        }
    }

    private void UpdateStatus()
    {
        _statusItem.Text = _paused
            ? TrayText.Paused
            : TrayText.Device("MX Master 4", _connected, _battery?.Percent, _battery?.Charging ?? false);
        _keyboardItem.Text = TrayText.Device("ERGO K860", _keyboardConnected, _keyboardBattery?.Percent, _keyboardBattery?.Charging ?? false);
    }

    private void RefreshChecks()
    {
        _excelItem.Checked = _config.Excel.ThumbWheelScrollsColumns;
        _hapticsItem.Checked = _config.Haptics.Enabled;
        _launchItem.Checked = _config.LaunchAtLogin;
    }

    private void OnExcelClicked(object? sender, EventArgs e) =>
        ApplyChange(_config with { Excel = _config.Excel with { ThumbWheelScrollsColumns = _excelItem.Checked } });

    private void OnHapticsClicked(object? sender, EventArgs e) =>
        ApplyChange(_config with { Haptics = _config.Haptics with { Enabled = _hapticsItem.Checked } });

    private void OnLaunchClicked(object? sender, EventArgs e)
    {
        bool wanted = _launchItem.Checked;
        try
        {
            if (wanted)
            {
                _startup.Enable();
            }
            else
            {
                _startup.Disable();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RefreshChecks();
            ShowError("Could not change the launch-at-login setting.");
            return;
        }

        ApplyChange(_config with { LaunchAtLogin = wanted });
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        if (!SettingsLauncher.Launch())
        {
            ShowError("Could not start Slender Settings. Reinstall Slender or run it from its own folder.");
        }

        // The window reads status.json when it opens; make sure it is current.
        _status.Changed();
    }

    /// <summary>Saves through the store, then raises ConfigChanged. Returns false on a failed save.</summary>
    private bool ApplyChange(SlenderConfig next)
    {
        bool saved = false;
        try
        {
            _store.Save(next);
            _watcher?.NoteOwnWrite();
            saved = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowError("Could not save settings: " + e.Message);
        }

        if (saved)
        {
            _config = next;
            ConfigChanged?.Invoke(this, _config);
        }

        RefreshChecks();
        return saved;
    }

    private void ShowError(string message) =>
        _tray.ShowBalloonTip(5000, "Slender", message, ToolTipIcon.Warning);
}
