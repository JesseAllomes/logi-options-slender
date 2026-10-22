namespace Slender.Yealink;

/// <summary>
/// The Windows default capture device's mute. One instance exists only while the module is started with MuteSync on;
/// Dispose unregisters the notification and releases the endpoint.
/// </summary>
public interface IMicMute : IDisposable
{
    bool GetMute();

    void SetMute(bool muted);

    /// <summary>Calls changed (on an audio thread) whenever the mute changes, from anywhere. Replaces any earlier subscription.</summary>
    void Subscribe(Action<bool> changed);

    void Unsubscribe();
}
