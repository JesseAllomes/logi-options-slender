namespace Slender.Engine.Ringer;

/// <summary>One built-in ring sound: the id kept in the config, the name Settings shows, the WAV in Assets/Sounds.</summary>
public sealed record RingSound(string Id, string Name, string File);

/// <summary>The built-in ring sounds (24 kHz mono 16-bit WAVs, made to loop; see tools/ringtones).</summary>
public static class RingSounds
{
    public const int DefaultVolume = 70;

    public static readonly IReadOnlyList<RingSound> All =
    [
        new("classic", "Classic", "classic.wav"),
        new("bell", "Old phone bell", "bell.wav"),
        new("chime", "Chime", "chime.wav"),
        new("marimba", "Marimba", "marimba.wav"),
        new("soft-pulse", "Soft pulse", "soft-pulse.wav"),
        new("digital", "Digital", "digital.wav"),
        new("rise", "Rise", "rise.wav"),
    ];

    /// <summary>The sound with this id, or null for null, Off or an id this build does not know.</summary>
    public static RingSound? Find(string? id) => id is null ? null : All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Where the WAVs sit under the app folder.</summary>
    public static string Folder(string baseDirectory) => Path.Combine(baseDirectory, "Assets", "Sounds");
}

/// <summary>Plays a ring sound on a playback device. Play returns at once; Stop ends it and frees everything. Neither throws.</summary>
public interface IRingSoundPlayer
{
    /// <param name="soundPath">The WAV file.</param>
    /// <param name="deviceId">Core Audio endpoint id, or null for the WH64 headset if present, else the Windows default.</param>
    /// <param name="volume">0-100.</param>
    /// <param name="loop">Repeat until Stop.</param>
    void Play(string soundPath, string? deviceId, int volume, bool loop);

    void Stop();
}
