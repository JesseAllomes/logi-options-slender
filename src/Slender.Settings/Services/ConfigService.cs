using Microsoft.UI.Dispatching;
using Slender.Engine;
using Slender.Engine.Config;
using Slender.Settings.Logic;

namespace Slender.Settings.Services;

/// <summary>
/// Holds the live config for the UI. Every edit updates the in-memory config at once and saves through JsonConfigStore
/// after 300 ms of quiet. Edits made to config.json by another process (the tray menu) are reloaded. All members run on the UI thread.
/// </summary>
public sealed class ConfigService : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly JsonConfigStore _store;
    private readonly DispatcherQueue _queue;
    private readonly ConfigFileWatcher _watcher;
    private DispatcherQueueTimer? _timer;
    private bool _dirty;
    private SlenderConfig? _base; // the config the pending local edit started from

    public ConfigService(JsonConfigStore store, DispatcherQueue queue)
    {
        _store = store;
        _queue = queue;
        Config = store.Load();
        _watcher = new ConfigFileWatcher(store);
        _watcher.Changed += (_, config) => _queue.TryEnqueue(() => OnExternal(config));
    }

    public SlenderConfig Config { get; private set; }

    /// <summary>Raised after every edit and after an external reload.</summary>
    public event Action? Changed;

    public void Update(Func<SlenderConfig, SlenderConfig> edit)
    {
        SlenderConfig next = edit(Config);
        if (ReferenceEquals(next, Config))
        {
            return;
        }

        if (!_dirty)
        {
            _base = Config;
        }

        Config = next;
        _dirty = true;
        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Start();
        Changed?.Invoke();
    }

    /// <summary>Saves any pending edit now (window closing).</summary>
    public void Flush()
    {
        _timer?.Stop();
        Save();
    }

    public void Dispose()
    {
        Flush();
        _watcher.Dispose();
    }

    private DispatcherQueueTimer CreateTimer()
    {
        DispatcherQueueTimer timer = _queue.CreateTimer();
        timer.Interval = SaveDelay;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Save();
        return timer;
    }

    private void Save()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        try
        {
            _store.Save(Config);
            _watcher.NoteOwnWrite();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write(e);
            _dirty = true; // keep trying on the next edit or on close
        }
    }

    private void OnExternal(SlenderConfig config)
    {
        if (_dirty)
        {
            // An unsaved local edit wins; it is written within 300 ms. Fn lock values it did not touch come from the file (the tray saves
            // Fn+Esc there), so the save never writes a stale Fn lock back to the keyboard.
            KeyboardSettings merged = KeyboardOps.AdoptFnLock(Config.Keyboard, _base?.Keyboard ?? Config.Keyboard, config.Keyboard);
            _base = config;
            if (merged != Config.Keyboard)
            {
                Config = Config with { Keyboard = merged };
                Changed?.Invoke();
            }

            return;
        }

        Config = config;
        Changed?.Invoke();
    }
}
