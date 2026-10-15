using System.Collections.Concurrent;
using Slender.Engine;
using Slender.Engine.Config;
using Slender.Hidpp;
using Slender.Tray.Native;

namespace Slender.Tray;

/// <summary>
/// The tray without WinForms: a hidden window owns a Shell_NotifyIcon icon, timers are WM_TIMER, and the menu is a native
/// popup built on demand. The caller owns the mouse (starts and disposes it); this class only subscribes to it. Events from
/// worker threads are marshalled to the UI thread with a queue plus one posted window message.
/// Left click starts the settings app in popover mode; right click shows the menu.
/// </summary>
public sealed class TrayApplication : IDisposable
{
    private const uint WM_TRAY = Win32.WM_APP + 1, WM_POST = Win32.WM_APP + 2;
    private const nuint TimerBattery = 1, TimerKeyboard = 2, TimerGuard = 3, TimerWatchdog = 4, TimerLockKeys = 5;
    private const uint CmdExcel = 2, CmdHaptics = 3, CmdLaunch = 4, CmdSettings = 5, CmdQuit = 6, CmdUpdateApply = 7, CmdUpdateCheck = 8;
    private const string WindowClass = UpdateMessages.TrayWindowClass;

    private static readonly uint BatteryIntervalMs = (uint)TimeSpan.FromMinutes(5).TotalMilliseconds;
    private static readonly uint KeyboardBatteryIntervalMs = (uint)TimeSpan.FromMinutes(10).TotalMilliseconds;
    private const uint LockKeyPollMs = 250;
    private const int VkCapital = 0x14, VkNumLock = 0x90;
    private static readonly uint GuardIntervalMs = (uint)OptionsPlusGuard.PollInterval.TotalMilliseconds;

    // The window procedure is a static unmanaged callback; there is exactly one tray window per process.
    private static TrayApplication? s_current;

    private readonly IConfigStore _store;
    private readonly ILogiMouse? _mouse;
    private readonly ILogiKeyboard? _keyboard;
    private readonly OptionsPlusGuard? _guard;
    private readonly IForegroundApp? _foreground;
    private readonly StatusPublisher _status;
    private readonly ConfigFileWatcher? _watcher;
    private readonly StartupRegistration _startup;
    private readonly MemoryWatchdog? _watchdog;
    private readonly ILog _log;
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly uint _taskbarCreated;
    private readonly uint _updateCheckMessage, _updateApplyMessage;

    private nint _hwnd;
    private nint _icon;
    private bool _iconAdded;
    private SlenderConfig _config;
    private bool _connected;
    private BatteryStatus? _battery;
    private int? _dpi;
    private bool _keyboardConnected;
    private BatteryStatus? _keyboardBattery;
    private readonly BatteryAlertState _batteryAlerts = new();
    private bool _paused;
    private bool _disposed;
    private string _statusText = string.Empty;
    private string _keyboardText = string.Empty;
    private UpdateCoordinator? _updates;
    private bool _readyBalloonShown;
    private readonly LockKeyIndicatorState _lockKeys = new();

    public unsafe TrayApplication(IConfigStore store, ILogiMouse? mouse, StartupRegistration? startup = null, IForegroundApp? foreground = null, ILog? log = null, ILogiKeyboard? keyboard = null, OptionsPlusGuard? guard = null, MemoryWatchdog? watchdog = null, string? statusPath = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _mouse = mouse;
        _keyboard = keyboard;
        _guard = guard;
        _foreground = foreground;
        _watchdog = watchdog;
        _log = log ?? NullLog.Instance;
        _status = new StatusPublisher(
            () => new StatusSnapshot(
                _connected, _battery?.Percent, _battery?.Charging ?? false, _foreground?.ProcessName,
                _keyboardConnected, _keyboardBattery?.Percent, _keyboardBattery?.Charging ?? false, _paused, _dpi,
                _updates?.CurrentVersion, _updates?.AvailableVersion, _updates?.UpdateReady ?? false, _keyboard?.CurrentChannel),
            path: statusPath,
            log: log);
        _startup = startup ?? StartupRegistration.ForCurrentProcess();
        _config = store.Load();

        s_current = this;
        fixed (char* tb = "TaskbarCreated")
        {
            _taskbarCreated = Win32.RegisterWindowMessageW(tb);
        }

        fixed (char* check = UpdateMessages.CheckName)
        {
            _updateCheckMessage = Win32.RegisterWindowMessageW(check);
        }

        fixed (char* apply = UpdateMessages.ApplyName)
        {
            _updateApplyMessage = Win32.RegisterWindowMessageW(apply);
        }

        CreateWindow();
        _icon = LoadTrayIcon(_log);
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
        AddIcon();
        if (_watchdog is not null)
        {
            Win32.SetTimer(_hwnd, TimerWatchdog, (uint)MemoryWatchdog.Interval.TotalMilliseconds, 0);
        }
    }

    /// <summary>Raised on the UI thread after every successful save.</summary>
    public event EventHandler<SlenderConfig>? ConfigChanged;

    /// <summary>Caps Lock or Num Lock flipped ("Caps Lock on"); raised on the UI thread, only while the K860 is connected and the option is on.</summary>
    public event EventHandler<string>? LockKeyChanged;

    public SlenderConfig Config => _config;

    /// <summary>True when the loop ended because the memory watchdog asked for a restart.</summary>
    public bool RestartRequested { get; private set; }

    /// <summary>
    /// Connects the update coordinator (built after the tray, because its callbacks need it). The tray then offers
    /// "Check for updates" and "Restart to update", answers the settings app's update messages and publishes the update state.
    /// </summary>
    public void AttachUpdates(UpdateCoordinator updates)
    {
        _updates = updates;
        _status.Changed();
    }

    /// <summary>The update state changed (found, downloaded). Safe from any thread.</summary>
    public void UpdateStateChanged() => Post(() => _status.Changed());

    /// <summary>Shows an update balloon. Safe from any thread.</summary>
    public void NotifyUpdate(UpdateNoticeKind kind, string text) => Post(() =>
    {
        ShowBalloon("Slender", text, warning: false);
        _readyBalloonShown = kind == UpdateNoticeKind.Ready; // after ShowBalloon, which clears it
    });

    /// <summary>Removes the tray icon right before an update restarts the process, so no ghost icon is left behind.</summary>
    public void PrepareForUpdateRestart() => RemoveIcon();

    /// <summary>Puts the tray icon back when Restart to update failed after PrepareForUpdateRestart removed it.</summary>
    public void RestoreAfterFailedRestart() => Post(AddIcon);

    /// <summary>Runs the message loop on the calling (creating) thread until Quit or a restart request.</summary>
    public unsafe void Run()
    {
        Win32.MSG msg;
        while (Win32.GetMessageW(&msg, 0, 0, 0) > 0)
        {
            Win32.TranslateMessage(&msg);
            Win32.DispatchMessageW(&msg);
        }
    }

    /// <summary>Ends the message loop. Safe from any thread.</summary>
    public void Exit() => Post(() => Win32.PostQuitMessage(0));

    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

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

        // Leave an honest last status behind: the tray is gone, so nothing is connected through it.
        _connected = false;
        _battery = null;
        _keyboardConnected = false;
        _keyboardBattery = null;
        _status.PublishNow();
        _status.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();

        RemoveIcon();
        if (_hwnd != 0)
        {
            nint h = _hwnd;
            Win32.KillTimer(h, TimerBattery);
            Win32.KillTimer(h, TimerKeyboard);
            Win32.KillTimer(h, TimerGuard);
            Win32.KillTimer(h, TimerWatchdog);
            Win32.KillTimer(h, TimerLockKeys);
            _hwnd = 0;
            Win32.DestroyWindow(h);
            fixed (char* cls = WindowClass)
            {
                Win32.UnregisterClassW(cls, Win32.GetModuleHandleW(null));
            }
        }

        if (_icon != 0)
        {
            Win32.DestroyIcon(_icon);
            _icon = 0;
        }

        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
        }
    }

    // ---- window plumbing ----

    private unsafe void CreateWindow()
    {
        nint instance = Win32.GetModuleHandleW(null);
        fixed (char* cls = WindowClass)
        {
            var wc = new Win32.WNDCLASSW { lpfnWndProc = &WndProc, hInstance = instance, lpszClassName = cls };
            Win32.RegisterClassW(&wc);
            _hwnd = Win32.CreateWindowExW(0, cls, cls, 0, 0, 0, 0, 0, 0, 0, instance, null);
        }

        if (_hwnd == 0)
        {
            throw new InvalidOperationException("Could not create the tray window.");
        }
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            TrayApplication? app = s_current;
            if (app is not null && app.Handle(msg, wParam, lParam))
            {
                return 0;
            }
        }
        catch (Exception e)
        {
            // An exception must never cross an unmanaged boundary.
            s_current?._log.Error("Tray window procedure failed", e);
        }

        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private bool Handle(uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_TRAY)
        {
            switch ((uint)lParam)
            {
                case Win32.WM_RBUTTONUP:
                case Win32.WM_CONTEXTMENU:
                    ShowMenu();
                    break;
                case Win32.WM_LBUTTONUP:
                    OpenSettings(popover: true);
                    break;
                case Win32.NIN_BALLOONUSERCLICK:
                    if (_readyBalloonShown)
                    {
                        _readyBalloonShown = false;
                        _ = _updates?.ApplyAsync();
                    }

                    break;
            }

            return true;
        }

        if (msg == WM_POST)
        {
            while (_posted.TryDequeue(out Action? action))
            {
                if (!_disposed)
                {
                    action();
                }
            }

            return true;
        }

        if (msg != 0 && msg == _updateCheckMessage)
        {
            _ = _updates?.CheckNowAsync();
            return true;
        }

        if (msg != 0 && msg == _updateApplyMessage)
        {
            _ = _updates?.ApplyAsync();
            return true;
        }

        if (msg == Win32.WM_TIMER)
        {
            OnTimer((nuint)wParam);
            return true;
        }

        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            _iconAdded = false; // Explorer restarted: the icon is gone
            AddIcon();
            return true;
        }

        if (msg == Win32.WM_DESTROY)
        {
            Win32.PostQuitMessage(0);
            return true;
        }

        return false;
    }

    private void OnTimer(nuint id)
    {
        switch (id)
        {
            case TimerBattery:
                _ = RefreshBatteryAsync();
                break;
            case TimerKeyboard:
                _ = RefreshKeyboardBatteryAsync();
                break;
            case TimerGuard:
                EvaluateGuard();
                break;
            case TimerLockKeys:
                PollLockKeys();
                break;
            case TimerWatchdog:
                if (_watchdog?.Check() == WatchdogVerdict.Restart)
                {
                    RestartRequested = true;
                    Win32.PostQuitMessage(0);
                }

                break;
        }
    }

    private void StartTimer(nuint id, uint ms)
    {
        if (_hwnd != 0)
        {
            Win32.SetTimer(_hwnd, id, ms, 0);
        }
    }

    private void StopTimer(nuint id)
    {
        if (_hwnd != 0)
        {
            Win32.KillTimer(_hwnd, id);
        }
    }

    /// <summary>Runs an action on the UI thread. Safe from any thread; dropped after dispose.</summary>
    public void PostToUi(Action action) => Post(action);

    private void Post(Action action)
    {
        nint h = _hwnd;
        if (_disposed || h == 0)
        {
            return;
        }

        _posted.Enqueue(action);
        Win32.PostMessageW(h, WM_POST, 0, 0);
    }

    // ---- icon and menu ----

    private static unsafe nint LoadTrayIcon(ILog log)
    {
        nint instance = Win32.GetModuleHandleW(null);
        int cx = Win32.GetSystemMetrics(Win32.SM_CXSMICON);
        int cy = Win32.GetSystemMetrics(Win32.SM_CYSMICON);
        // The ApplicationIcon becomes the exe's first icon resource; try the ids the SDK/ILC may assign it.
        foreach (nint id in (ReadOnlySpan<nint>)[32512, 1])
        {
            nint icon = Win32.LoadImageW(instance, id, Win32.IMAGE_ICON, cx, cy, Win32.LR_DEFAULTCOLOR);
            if (icon != 0)
            {
                log.Info($"Tray icon: embedded resource {id}");
                return icon;
            }
        }

        log.Info("Tray icon: embedded resource not found; using the system application icon");
        return Win32.LoadIconW(0, Win32.IDI_APPLICATION); // shared system icon; DestroyIcon on it is a harmless no-op
    }

    private unsafe Win32.NOTIFYICONDATAW IconData()
    {
        var nid = new Win32.NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(Win32.NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = 1,
            uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _icon,
        };
        "Slender".AsSpan().CopyTo(new Span<char>(nid.szTip, 128));
        return nid;
    }

    private unsafe void AddIcon()
    {
        if (_iconAdded || _hwnd == 0)
        {
            return;
        }

        Win32.NOTIFYICONDATAW nid = IconData();
        _iconAdded = Win32.Shell_NotifyIconW(Win32.NIM_ADD, &nid) != 0;
    }

    private unsafe void ShowBalloon(string title, string text, bool warning = true)
    {
        _readyBalloonShown = false; // a click now belongs to this balloon, not an earlier "ready" one
        if (!_iconAdded || _hwnd == 0)
        {
            return;
        }

        Win32.NOTIFYICONDATAW nid = IconData();
        nid.uFlags |= Win32.NIF_INFO;
        nid.dwInfoFlags = warning ? Win32.NIIF_WARNING : Win32.NIIF_INFO;
        title.AsSpan(0, Math.Min(title.Length, 63)).CopyTo(new Span<char>(nid.szInfoTitle, 64));
        text.AsSpan(0, Math.Min(text.Length, 255)).CopyTo(new Span<char>(nid.szInfo, 256));
        Win32.Shell_NotifyIconW(Win32.NIM_MODIFY, &nid);
    }

    private void CheckBatteryAlert(string deviceId, string name, BatteryStatus? battery, int threshold)
    {
        bool fire = _batteryAlerts.ShouldNotify(
            deviceId, battery?.Percent, battery?.Charging ?? false, true, threshold, _config.BatteryAlerts.Enabled);
        if (fire)
        {
            ShowBalloon("Slender", $"{name} battery low ({battery!.Value.Percent}%) - plug in to charge");
        }
    }

    private unsafe void RemoveIcon()
    {
        if (!_iconAdded || _hwnd == 0)
        {
            return;
        }

        Win32.NOTIFYICONDATAW nid = IconData();
        Win32.Shell_NotifyIconW(Win32.NIM_DELETE, &nid);
        _iconAdded = false;
    }

    private static unsafe void Append(nint menu, uint flags, nuint id, string text)
    {
        fixed (char* p = text)
        {
            Win32.AppendMenuW(menu, Win32.MF_STRING | flags, id, p);
        }
    }

    private unsafe void ShowMenu()
    {
        nint menu = Win32.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            if (_updates is { UpdateReady: true } ready)
            {
                Append(menu, 0, CmdUpdateApply, $"Restart to update ({ready.AvailableVersion})");
                Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, 0, null);
            }

            Append(menu, Win32.MF_GRAYED, 1, _statusText);
            Append(menu, Win32.MF_GRAYED, 1, _keyboardText);
            Append(menu, _config.Excel.ThumbWheelScrollsColumns ? Win32.MF_CHECKED : 0, CmdExcel, "Excel: thumb wheel scrolls columns");
            Append(menu, _config.Haptics.Enabled ? Win32.MF_CHECKED : 0, CmdHaptics, "Haptics");
            Append(menu, _config.LaunchAtLogin ? Win32.MF_CHECKED : 0, CmdLaunch, "Launch at login");
            Append(menu, 0, CmdSettings, "Settings…");
            Append(menu, 0, CmdUpdateCheck, "Check for updates");
            Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, 0, null);
            Append(menu, 0, CmdQuit, "Quit");

            Win32.POINT pt;
            Win32.GetCursorPos(&pt);
            Win32.SetForegroundWindow(_hwnd); // required so the menu closes when the user clicks elsewhere
            int cmd = Win32.TrackPopupMenu(menu, Win32.TPM_RIGHTBUTTON | Win32.TPM_BOTTOMALIGN | Win32.TPM_RETURNCMD, pt.X, pt.Y, 0, _hwnd, 0);
            Win32.PostMessageW(_hwnd, Win32.WM_NULL, 0, 0);
            switch ((uint)cmd)
            {
                case CmdExcel:
                    ApplyChange(_config with { Excel = _config.Excel with { ThumbWheelScrollsColumns = !_config.Excel.ThumbWheelScrollsColumns } });
                    break;
                case CmdHaptics:
                    ApplyChange(_config with { Haptics = _config.Haptics with { Enabled = !_config.Haptics.Enabled } });
                    break;
                case CmdLaunch:
                    OnLaunchClicked(!_config.LaunchAtLogin);
                    break;
                case CmdSettings:
                    OpenSettings(popover: false);
                    break;
                case CmdUpdateApply:
                    _ = _updates?.ApplyAsync();
                    break;
                case CmdUpdateCheck:
                    _ = _updates?.CheckNowAsync();
                    break;
                case CmdQuit:
                    Win32.DestroyWindow(_hwnd);
                    break;
            }
        }
        finally
        {
            Win32.DestroyMenu(menu);
        }
    }

    // ---- state ----

    private void OnForegroundChanged(object? sender, string? process) => _status.Changed();

    /// <summary>config.json was changed by another process (the settings app): reload and apply it.</summary>
    private void OnConfigFileChanged(object? sender, SlenderConfig config) => Post(() =>
    {
        _config = config;
        SyncLockKeyPolling();
        SyncStartup();
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

    private void SetConnected(bool connected)
    {
        if (_disposed)
        {
            return;
        }

        _connected = connected;
        if (connected)
        {
            StartTimer(TimerBattery, BatteryIntervalMs);
            _ = RefreshBatteryAsync();
            _ = RefreshDpiAsync();
        }
        else
        {
            StopTimer(TimerBattery);
            _battery = null;
            _dpi = null;
        }

        UpdateStatus();
        _status.Changed();
    }

    /// <summary>
    /// The poll timer runs only while the K860 is connected and "Show Caps Lock and Num Lock changes" is on. Windows' own toggle state
    /// is read with GetKeyState (no keyboard hook, no raw input); 0x4220 LockKeyState has no publicly documented function layout.
    /// </summary>
    private void SyncLockKeyPolling()
    {
        if (_disposed)
        {
            return;
        }

        if (_keyboardConnected && _config.Keyboard.ShowLockKeyChanges)
        {
            StartTimer(TimerLockKeys, LockKeyPollMs);
        }
        else
        {
            StopTimer(TimerLockKeys);
            _lockKeys.Reset();
        }
    }

    private void PollLockKeys()
    {
        bool enabled = _keyboardConnected && _config.Keyboard.ShowLockKeyChanges;
        string? text = _lockKeys.Update(enabled, (Win32.GetKeyState(VkCapital) & 1) != 0, (Win32.GetKeyState(VkNumLock) & 1) != 0);
        if (text is not null)
        {
            LockKeyChanged?.Invoke(this, text);
        }
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
        SyncLockKeyPolling();
        if (connected)
        {
            StartTimer(TimerKeyboard, KeyboardBatteryIntervalMs);
            _ = RefreshKeyboardBatteryAsync();
        }
        else
        {
            StopTimer(TimerKeyboard);
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
            Post(() =>
            {
                if (!_keyboardConnected)
                {
                    return;
                }

                _keyboardBattery = status ?? _keyboardBattery;
                CheckBatteryAlert("keyboard", "ERGO K860", _keyboardBattery, _config.BatteryAlerts.KeyboardThresholdPercent);
                UpdateStatus();
                _status.Changed();
            });
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
            StartTimer(TimerGuard, GuardIntervalMs);
        }
        else
        {
            StopTimer(TimerGuard);
        }

        if (paused != _paused)
        {
            _paused = paused;
            UpdateStatus();
            _status.Changed();
        }
    }

    /// <summary>Re-reads the device DPI after Slender changed it, so status.json stays current.</summary>
    public void RefreshDpi() => Post(() => _ = RefreshDpiAsync());

    private async Task RefreshDpiAsync()
    {
        if (_disposed || !_connected || _mouse is null)
        {
            return;
        }

        try
        {
            int? dpi = await _mouse.GetDpiAsync(_lifetime.Token);
            Post(() =>
            {
                if (!_connected || dpi is null)
                {
                    return;
                }

                _dpi = dpi;
                _status.Changed();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // A failed DPI read is not fatal; the settings app just shows no current speed.
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
            Post(() =>
            {
                if (!_connected)
                {
                    return;
                }

                _battery = status ?? _battery;
                CheckBatteryAlert("mouse", "MX Master 4", _battery, _config.BatteryAlerts.MouseThresholdPercent);
                UpdateStatus();
                _status.Changed();
            });
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
        _statusText = _paused
            ? TrayText.Paused
            : TrayText.Device("MX Master 4", _connected, _battery?.Percent, _battery?.Charging ?? false);
        _keyboardText = TrayText.Device("ERGO K860", _keyboardConnected, _keyboardBattery?.Percent, _keyboardBattery?.Charging ?? false);
    }

    private void OnLaunchClicked(bool wanted)
    {
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
            ShowError("Could not change the launch-at-login setting.");
            return;
        }

        ApplyChange(_config with { LaunchAtLogin = wanted });
    }

    private void OpenSettings(bool popover)
    {
        if (!SettingsLauncher.Launch(popover))
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
            SyncLockKeyPolling();
            ConfigChanged?.Invoke(this, _config);
        }

        return saved;
    }

    /// <summary>No balloon in the Win32 tray: errors go to the log.</summary>
    private void ShowError(string message) => _log.Error(message);
}
