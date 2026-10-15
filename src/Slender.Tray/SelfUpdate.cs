using Slender.Engine;
using Velopack;
using Velopack.Sources;

namespace Slender.Tray;

/// <summary>
/// Velopack self-update. Off unless updateSource is set in slender.sync.json: a folder path (or UNC share) holding the
/// release files, or a GitHub repository URL. Downloads in the background and applies when Slender next exits.
/// </summary>
public static class SelfUpdate
{
    public static async Task CheckAsync(string? updateSource, ILog log)
    {
        if (string.IsNullOrWhiteSpace(updateSource))
        {
            return;
        }

        try
        {
            IUpdateSource source = updateSource.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
                ? new GithubSource(updateSource, accessToken: null, prerelease: false)
                : new SimpleFileSource(new DirectoryInfo(updateSource));
            var manager = new UpdateManager(source);
            if (!manager.IsInstalled)
            {
                return; // dev build or portable run
            }

            UpdateInfo? update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return;
            }

            await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            manager.WaitExitThenApplyUpdates(update, silent: true, restart: false);
            log.Info("Update " + update.TargetFullRelease.Version + " downloaded; it installs when Slender next exits.");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            log.Error("Update check failed", e);
        }
    }
}
