namespace Slender.Sync;

public enum SyncAction
{
    /// <summary>Nothing changed on either side.</summary>
    None,

    /// <summary>Remote changed, local did not: take the remote copy.</summary>
    Pull,

    /// <summary>Local changed (or remote is missing): upload.</summary>
    Push,

    /// <summary>Both changed: keep local, back up the remote copy, push local.</summary>
    Conflict,
}

/// <summary>The decision table. Pure, so every case is unit-tested.</summary>
public static class SyncDecision
{
    /// <param name="localHash">Hash of local config.json, null when the file does not exist.</param>
    /// <param name="remoteETag">Current remote eTag, null when the remote file does not exist.</param>
    /// <param name="manifest">What the last sync recorded.</param>
    public static SyncAction Decide(string? localHash, string? remoteETag, SyncManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (remoteETag is null)
        {
            // Nothing in the cloud yet (or it was deleted): upload what we have.
            return localHash is null ? SyncAction.None : SyncAction.Push;
        }

        if (localHash is null)
        {
            return SyncAction.Pull; // fresh PC: nothing to lose
        }

        bool everSynced = manifest.LastSyncedHash is not null && manifest.RemoteETag is not null;
        if (!everSynced)
        {
            // First sync on this PC with both sides present: no baseline, so both count as changed.
            return SyncAction.Conflict;
        }

        bool localChanged = !string.Equals(localHash, manifest.LastSyncedHash, StringComparison.Ordinal);
        bool remoteChanged = !string.Equals(remoteETag, manifest.RemoteETag, StringComparison.Ordinal);
        return (localChanged, remoteChanged) switch
        {
            (false, false) => SyncAction.None,
            (false, true) => SyncAction.Pull,
            (true, false) => SyncAction.Push,
            (true, true) => SyncAction.Conflict,
        };
    }
}
