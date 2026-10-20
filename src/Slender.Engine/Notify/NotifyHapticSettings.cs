namespace Slender.Engine;

/// <summary>
/// Buzz the mouse when Windows shows a notification (roadmap m45). Off by default; while off the tray installs nothing.
/// Apps maps an app name (as seen by the tray, case-insensitive) to its own cue; a cue with Enabled false silences that app.
/// An app that is not in the map plays Default.
/// </summary>
public sealed record NotifyHapticSettings
{
    public const int MaxAppNameLength = 64, MaxApps = 200;

    public bool Enabled { get; init; }

    /// <summary>What a newly seen app buzzes until the user picks something else.</summary>
    public HapticCue Default { get; init; } = new(true, 5);

    public IReadOnlyDictionary<string, HapticCue> Apps { get; init; } = new Dictionary<string, HapticCue>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The cue for an app (its own, else the default), clamped.</summary>
    public HapticCue CueFor(string app) => (Apps.TryGetValue(app, out var own) ? own : Default).Clamped();

    /// <summary>What to play for a notification from an app; null when the feature or that app's cue is off.</summary>
    public HapticCue? Effective(string app)
    {
        if (!Enabled) return null;
        var cue = CueFor(app);
        return cue.Enabled ? cue : null;
    }

    public bool Equals(NotifyHapticSettings? other) =>
        other is not null && Enabled == other.Enabled && Default == other.Default && Apps.Count == other.Apps.Count
        && Apps.All(kv => other.Apps.TryGetValue(kv.Key, out var v) && v == kv.Value);

    public override int GetHashCode() => HashCode.Combine(Enabled, Default, Apps.Count);
}
