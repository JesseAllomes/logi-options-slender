using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Slender.Engine;
using Slender.Engine.Config;
using Slender.Settings.Logic;

namespace Slender.Settings.Services;

/// <summary>
/// Reads the tray status from status.json (FileSystemWatcher, debounced) and checks whether Options+ and the tray are
/// running every 30 s while the window is open. Changed is raised on the UI thread.
/// </summary>
public sealed class StatusMonitor : IDisposable
{
    private const string TrayMutexName = @"Local\Slender.Tray";
    private const string OptionsPlusAgent = "logioptionsplus_agent";

    private readonly DispatcherQueue _queue;
    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherQueueTimer _debounce;
    private readonly DispatcherQueueTimer _poll;

    private readonly string _statusFile;
    private readonly bool _light;
    private bool _full;

    /// <summary>
    /// <paramref name="statusFile"/> defaults to the tray's status.json (a test hook points it elsewhere). In <paramref name="light"/>
    /// mode (the tray popover) there is no 30 s poll and no Options+ process scan until <see cref="EnableFull"/> is called.
    /// </summary>
    public StatusMonitor(DispatcherQueue queue, string? statusFile = null, bool light = false)
    {
        _queue = queue;
        _statusFile = statusFile ?? SlenderPaths.StatusFile;
        _light = light;
        Refresh();

        _debounce = queue.CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(150);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => Refresh(raise: true);

        _poll = queue.CreateTimer();
        _poll.Interval = TimeSpan.FromSeconds(30);
        _poll.IsRepeating = true;
        _poll.Tick += (_, _) => Refresh(raise: true);
        if (!light)
        {
            _poll.Start();
        }

        try
        {
            string dir = Path.GetDirectoryName(_statusFile)!;
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_statusFile))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            _watcher.Changed += OnFsEvent;
            _watcher.Created += OnFsEvent;
            _watcher.Renamed += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            CrashLog.Write(e); // the 30 s poll still keeps the UI roughly current
        }
    }

    /// <summary>Last status written by the tray, or null when status.json is missing or unreadable.</summary>
    public SlenderStatus? Status { get; private set; }

    /// <summary>True when the tray process holds its single-instance mutex.</summary>
    public bool TrayRunning { get; private set; }

    /// <summary>True when Logi Options+ (its agent process) is running. Checked locally, so it is fresh without the tray.</summary>
    public bool OptionsPlusRunning { get; private set; }

    /// <summary>Device id to firmware version, when the tray publishes one. Empty when the field is absent.</summary>
    public IReadOnlyDictionary<string, string> Firmware { get; private set; } = new Dictionary<string, string>();

    public event Action? Changed;

    /// <summary>Leaves light mode: starts the 30 s poll and checks Options+ now.</summary>
    public void EnableFull()
    {
        if (_light && !_full)
        {
            _full = true;
            _poll.Start();
            Refresh(raise: true);
        }
    }

    public DeviceState? Device(string id) => Status?.Devices.FirstOrDefault(d => d.Id == id);

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Stop();
        _poll.Stop();
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => _queue.TryEnqueue(() =>
    {
        _debounce.Stop();
        _debounce.Start();
    });

    private void Refresh(bool raise = false)
    {
        string? text = ReadText();
        SlenderStatus? status = text is null ? null : StatusJson.Parse(text);
        bool tray = IsTrayRunning();
        bool options = !_light || _full ? IsOptionsPlusRunning() : OptionsPlusRunning;
        if (raise && status?.UpdatedAt == Status?.UpdatedAt && tray == TrayRunning && options == OptionsPlusRunning)
        {
            return;
        }

        Status = status;
        Firmware = PopoverModel.ParseFirmware(text);
        TrayRunning = tray;
        OptionsPlusRunning = options;
        if (raise)
        {
            Changed?.Invoke();
        }
    }

    private string? ReadText()
    {
        try
        {
            return File.Exists(_statusFile) ? File.ReadAllText(_statusFile, System.Text.Encoding.UTF8) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsTrayRunning()
    {
        if (Mutex.TryOpenExisting(TrayMutexName, out Mutex? m))
        {
            m.Dispose();
            return true;
        }

        return false;
    }

    public static bool IsOptionsPlusRunning()
    {
        Process[] found = Process.GetProcessesByName(OptionsPlusAgent);
        try
        {
            return found.Length > 0;
        }
        finally
        {
            foreach (Process p in found)
            {
                p.Dispose();
            }
        }
    }

    /// <summary>Quits Logi Options+ (agent and UI). Returns true when nothing is left running.</summary>
    public static bool QuitOptionsPlus()
    {
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (p.ProcessName.StartsWith("logioptionsplus", StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill();
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone, or not ours to stop.
            }
            finally
            {
                p.Dispose();
            }
        }

        Thread.Sleep(300);
        return !IsOptionsPlusRunning();
    }
}
