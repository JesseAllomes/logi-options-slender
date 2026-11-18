using System.Diagnostics;
using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace Slender.Tray;

/// <summary>
/// Velopack self-update behind IUpdateBackend. The source is a folder path (or UNC share) holding the release files, or a
/// GitHub repository URL: updateSource in slender.sync.json if set, else the source scripts\Build-Setup.ps1 baked into this
/// build (assembly metadata SlenderUpdateSource): the public GitHub repo, or artifacts\releases with -LocalOnly.
/// A dev build has neither: no updates.
/// </summary>
public sealed class VelopackUpdateBackend : IUpdateBackend
{
    public const string SourceMetadataKey = "SlenderUpdateSource";

    private readonly UpdateManager? _manager;
    // The last check's answer, and the update that finished downloading (what Apply installs). Kept apart so a check after
    // a release notice cannot swap a ready update for one that is not downloaded yet.
    private UpdateInfo? _found, _downloaded;

    public VelopackUpdateBackend(string? updateSource)
    {
        if (!string.IsNullOrWhiteSpace(updateSource))
        {
            IUpdateSource source = updateSource.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
                ? new GithubSource(updateSource, accessToken: null, prerelease: false)
                : new SimpleFileSource(new DirectoryInfo(updateSource));
            _manager = new UpdateManager(source);
        }
    }

    /// <summary>The configured source, else the one baked in at build time, else null.</summary>
    public static string? ResolveSource(string? configured) =>
        !string.IsNullOrWhiteSpace(configured) ? configured : BakedSource(typeof(VelopackUpdateBackend).Assembly);

    public static string? BakedSource(Assembly assembly)
    {
        foreach (AssemblyMetadataAttribute a in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (a.Key == SourceMetadataKey && !string.IsNullOrWhiteSpace(a.Value))
            {
                return a.Value;
            }
        }

        return null;
    }

    public bool IsAvailable => _manager is { IsInstalled: true };

    public string? CurrentVersion => _manager?.CurrentVersion?.ToString()
        ?? typeof(VelopackUpdateBackend).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];

    public async Task<string?> CheckAsync(CancellationToken cancel)
    {
        _found = await _manager!.CheckForUpdatesAsync().ConfigureAwait(false);
        return _found?.TargetFullRelease.Version.ToString();
    }

    public async Task DownloadAsync(CancellationToken cancel)
    {
        UpdateInfo update = _found ?? throw new InvalidOperationException("No update was found.");
        await _manager!.DownloadUpdatesAsync(update, cancelToken: cancel).ConfigureAwait(false);
        _downloaded = update;
    }

    public void ApplyOnExit() =>
        _manager!.WaitExitThenApplyUpdates(_downloaded ?? throw new InvalidOperationException("No update was downloaded."), silent: true, restart: false);

    public void ApplyAndRestart() =>
        _manager!.ApplyUpdatesAndRestart(_downloaded ?? throw new InvalidOperationException("No update was downloaded."));

    /// <summary>
    /// Where the release notice ticket (notify.json, uploaded by scripts\ReleaseNotice.ps1) sits for a GitHub source: an
    /// asset of the latest release. Null for a folder source, which has no notices.
    /// </summary>
    public static Uri? NoticeTicket(string? source) =>
        source is not null && source.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate(source.TrimEnd('/') + "/releases/latest/download/notify.json", UriKind.Absolute, out Uri? ticket)
            ? ticket
            : null;
}

/// <summary>Finds the settings app of this install by process and asks its window to close with WM_CLOSE. Never kills anything.</summary>
public sealed class SettingsProcessCloser : ISettingsCloser
{
    public bool IsRunning()
    {
        List<Process> own = Find();
        try
        {
            return own.Count > 0;
        }
        finally
        {
            DisposeAll(own);
        }
    }

    public async Task<bool> CloseAsync(TimeSpan timeout)
    {
        List<Process> own = Find();
        try
        {
            foreach (Process p in own)
            {
                WindowCloser.CloseWindowsOf((uint)p.Id);
            }

            using var cts = new CancellationTokenSource(timeout);
            try
            {
                foreach (Process p in own)
                {
                    await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        finally
        {
            DisposeAll(own);
        }
    }

    private static void DisposeAll(List<Process> list)
    {
        foreach (Process p in list)
        {
            p.Dispose();
        }
    }

    /// <summary>Settings processes running from this install's folder (a settings app from another folder is not ours).</summary>
    private static List<Process> Find()
    {
        var found = new List<Process>();
        string? exe = SettingsLauncher.ResolveExe();
        string? dir = exe is null ? null : Path.GetDirectoryName(exe);
        foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(SettingsLauncher.ExeName)))
        {
            try
            {
                string? path = p.MainModule?.FileName;
                if (dir is not null && path is not null
                    && string.Equals(Path.GetDirectoryName(path), dir, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(p);
                    continue;
                }
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // Not readable: not ours to close.
            }

            p.Dispose();
        }

        return found;
    }
}

/// <summary>Posts WM_CLOSE to every top-level window of one process.</summary>
internal static unsafe class WindowCloser
{
    public static void CloseWindowsOf(uint processId) =>
        Native.Win32.EnumWindows(&CloseWindowOf, (nint)processId);

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static int CloseWindowOf(nint hwnd, nint processId)
    {
        uint pid;
        Native.Win32.GetWindowThreadProcessId(hwnd, &pid);
        if (pid == (uint)processId)
        {
            Native.Win32.PostMessageW(hwnd, Native.Win32.WM_CLOSE, 0, 0);
        }

        return 1;
    }
}
