using Slender.Engine;
using Slender.Engine.Config;
using Slender.Engine.Ringer;
using Slender.Hidpp;
using Slender.Settings.Logic;
using Slender.Tests.Engine;
using Slender.Yealink.Windows;

namespace Slender.Tests.Ringer;

public sealed class RingSoundTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class FakePlayer : IRingSoundPlayer
    {
        public List<(string Path, string? Device, int Volume, bool Loop)> Plays { get; } = [];
        public int Stops;
        public void Play(string soundPath, string? deviceId, int volume, bool loop) => Plays.Add((soundPath, deviceId, volume, loop));
        public void Stop() => Stops++;
    }

    private sealed record Rig(SlenderEngine Engine, FakeHook Hook, FakePlayer Player, FakeHoldTimer Timer) : IDisposable
    {
        public void Dispose() => Engine.Dispose();
    }

    private static readonly HapticCue Cue = new(true, 9, 1, 150, 100, 1, 1500);

    private static Rig Make(HapticSettings haptics)
    {
        var hook = new FakeHook();
        var timer = new FakeHoldTimer();
        var config = SlenderConfig.Default with { Haptics = haptics };
        var engine = new SlenderEngine(new FakeMouse(), hook, new FakeForeground(), new FakeExcel(), new FakeExecutor(), config, new ListLog(), new InlineQueue(), () => 0, timer);
        var player = new FakePlayer();
        engine.RingSoundPlayer = player;
        engine.RingSoundFolder = "sounds";
        engine.Start();
        return new Rig(engine, hook, player, timer);
    }

    private static HapticSettings Settings(string? sound = "chime", string? device = "{0.0.0.00000000}.{abc}", int volume = 40, bool buzz = true) =>
        new() { Ringer = Cue with { Enabled = buzz }, RingSound = sound, RingSoundDevice = device, RingSoundVolume = volume };

    private static void Ring(Rig r, RingerSignalSource source = RingerSignalSource.Notification) => r.Engine.SignalRing(new RingSignal(true, source));

    private static string SoundPath(string file) => Path.Combine(Repo(), "src", "Slender.Settings", "Assets", "Sounds", file);

    private static string Repo()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Slender.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    [Fact]
    public void The_catalog_has_the_seven_sounds_with_unique_ids_and_files()
    {
        Assert.Equal(["classic", "bell", "chime", "marimba", "soft-pulse", "digital", "rise"], RingSounds.All.Select(s => s.Id));
        Assert.Equal(["Classic", "Old phone bell", "Chime", "Marimba", "Soft pulse", "Digital", "Rise"], RingSounds.All.Select(s => s.Name));
        Assert.Equal(7, RingSounds.All.Select(s => s.File).Distinct().Count());
        Assert.Equal("chime", RingSounds.Find("CHIME")?.Id);
        Assert.Null(RingSounds.Find(null));
        Assert.Null(RingSounds.Find("nope"));
        Assert.Equal(["Off", .. RingSounds.All.Select(s => s.Name)], RingSoundText.SoundChoices());
        Assert.Equal("rise", RingSoundText.SoundAt(RingSoundText.SoundIndex("rise")));
        Assert.Null(RingSoundText.SoundAt(0));
    }

    public static TheoryData<string> Files() => new(RingSounds.All.Select(s => s.File));

    [Theory]
    [MemberData(nameof(Files))]
    public void Each_sound_file_exists_and_is_a_16_bit_pcm_wav(string file)
    {
        byte[] bytes = File.ReadAllBytes(SoundPath(file));
        Assert.True(WavPcm.TryParse(bytes, out var wav));
        Assert.Equal(1, wav.Channels);
        Assert.Equal(24000, wav.SampleRate);
        Assert.InRange(wav.DataLength / wav.BlockAlign / 24000.0, 1.0, 5.0);
    }

    [Fact]
    public void The_sounds_are_published_with_the_settings_app_and_the_tray()
    {
        string src = Path.Combine(Repo(), "src");
        string settings = File.ReadAllText(Path.Combine(src, "Slender.Settings", "Slender.Settings.csproj"));
        Assert.Contains("<Content Include=\"Assets\\Sounds\\*.wav\" CopyToOutputDirectory=\"PreserveNewest\" CopyToPublishDirectory=\"PreserveNewest\" />", settings);
        string tray = File.ReadAllText(Path.Combine(src, "Slender.Tray", "Slender.Tray.csproj"));
        Assert.Contains("Include=\"..\\Slender.Settings\\Assets\\Sounds\\*.wav\"", tray);
        Assert.Contains("Link=\"Assets\\Sounds\\%(Filename)%(Extension)\"", tray);
        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", tray);
    }

    [Fact]
    public void A_wav_that_is_not_16_bit_pcm_is_refused()
    {
        Assert.False(WavPcm.TryParse([], out _));
        Assert.False(WavPcm.TryParse("RIFF\0\0\0\0WAVE"u8, out _));
        byte[] eight = File.ReadAllBytes(SoundPath("classic.wav"));
        eight[34] = 8; // bits per sample
        Assert.False(WavPcm.TryParse(eight, out _));
    }

    [Fact]
    public void The_volume_is_a_straight_gain_on_the_samples()
    {
        byte[] file = File.ReadAllBytes(SoundPath("classic.wav"));
        Assert.True(WavPcm.TryParse(file, out var wav));
        int Peak(byte[] s) => Enumerable.Range(0, s.Length / 2).Max(i => Math.Abs((int)BitConverter.ToInt16(s, i * 2)));
        int full = Peak(WasapiRingSoundPlayer.ScaledSamples(file, wav, 100));
        int half = Peak(WasapiRingSoundPlayer.ScaledSamples(file, wav, 50));
        Assert.Equal(0, Peak(WasapiRingSoundPlayer.ScaledSamples(file, wav, 0)));
        Assert.InRange(half, full / 2 - 2, full / 2 + 2);
    }

    [Fact]
    public void The_defaults_are_off_headset_or_default_device_and_70_percent()
    {
        var h = new HapticSettings();
        Assert.Null(h.RingSound);
        Assert.Null(h.RingSoundDevice);
        Assert.Equal(70, h.RingSoundVolume);
    }

    [Fact]
    public void The_ring_sound_choices_round_trip_and_defaults_stay_out_of_the_file()
    {
        var store = new JsonConfigStore(_dir);
        store.Save(SlenderConfig.Default);
        Assert.DoesNotContain("\"ringSound\": \"", File.ReadAllText(Path.Combine(_dir, "config.json")));
        Assert.Equal(new HapticSettings(), store.Load().Haptics);

        var haptics = new HapticSettings { RingSound = "marimba", RingSoundDevice = "{0.0.0.00000000}.{1234}", RingSoundVolume = 35 };
        store.Save(SlenderConfig.Default with { Haptics = haptics });
        var loaded = store.Load().Haptics;
        Assert.Equal("marimba", loaded.RingSound);
        Assert.Equal("{0.0.0.00000000}.{1234}", loaded.RingSoundDevice);
        Assert.Equal(35, loaded.RingSoundVolume);
        Assert.Equal(haptics, loaded);
    }

    [Fact]
    public void An_old_config_loads_with_the_defaults_and_bad_values_are_repaired()
    {
        Directory.CreateDirectory(_dir);
        string file = Path.Combine(_dir, "config.json");
        File.WriteAllText(file, """{"version":1,"profiles":[],"excel":{},"haptics":{"enabled":true,"level":1}}""");
        var old = new JsonConfigStore(_dir).Load().Haptics;
        Assert.Null(old.RingSound);
        Assert.Equal(70, old.RingSoundVolume);

        File.WriteAllText(file, """{"version":1,"profiles":[],"excel":{},"haptics":{"ringSound":"gone","ringSoundVolume":900}}""");
        var bad = new JsonConfigStore(_dir).Load().Haptics;
        Assert.Null(bad.RingSound);
        Assert.Equal(100, bad.RingSoundVolume);
    }

    [Theory]
    [InlineData(@"\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{8f4e9a6c-1111-2222-3333-444455556666}#{e6327cad-dcec-4949-ae8a-991e976a79d2}", "{0.0.0.00000000}.{8f4e9a6c-1111-2222-3333-444455556666}")]
    [InlineData("{0.0.0.00000000}.{abc}", "{0.0.0.00000000}.{abc}")]
    [InlineData(@"\\?\SWD#MMDEVAPI#{0.0.1.00000000}.{abc}", "{0.0.1.00000000}.{abc}")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData(@"\\?\USB#VID_1234#5&abc", null)]
    [InlineData(@"\\?\SWD#MMDEVAPI#garbage#{e6327cad}", null)]
    public void A_winrt_device_id_converts_to_the_core_audio_endpoint_id(string? winRt, string? expected) =>
        Assert.Equal(expected, RingSoundText.ToCoreAudioId(winRt));

    [Fact]
    public void A_ring_plays_the_chosen_sound_on_the_chosen_device_in_a_loop_and_the_stop_ends_it()
    {
        using var r = Make(Settings());
        Ring(r);
        Assert.Equal([(Path.Combine("sounds", "chime.wav"), "{0.0.0.00000000}.{abc}", 40, true)], r.Player.Plays);
        Assert.Equal(0, r.Player.Stops);

        r.Engine.SignalRing(new RingSignal(false, RingerSignalSource.Notification));
        Assert.Equal(1, r.Player.Stops);
        Assert.False(r.Engine.Ringing);
    }

    [Fact]
    public void A_mouse_click_ends_the_sound()
    {
        using var r = Make(Settings());
        Ring(r);
        r.Hook.Click(HookButton.Back);
        Assert.Equal(1, r.Player.Stops);
    }

    [Fact]
    public void The_safety_cap_ends_the_sound()
    {
        using var r = Make(Settings());
        Ring(r);
        r.Timer.Elapse();
        Assert.Equal(1, r.Player.Stops);
        Assert.False(r.Engine.Ringing);
    }

    [Fact]
    public void The_headset_stop_signal_ends_the_ring_and_the_sound()
    {
        using var r = Make(Settings());
        Ring(r, RingerSignalSource.Base);
        Assert.Single(r.Player.Plays);
        r.Engine.SignalRing(new RingSignal(false, RingerSignalSource.Base));
        Assert.Equal(1, r.Player.Stops);
    }

    [Fact]
    public void Nothing_plays_or_stops_while_the_ring_sound_is_off()
    {
        using var r = Make(Settings(sound: null));
        Ring(r);
        r.Engine.SignalRing(new RingSignal(false, RingerSignalSource.Notification));
        Assert.Empty(r.Player.Plays);
        Assert.Equal(0, r.Player.Stops);
    }

    [Fact]
    public void The_sound_plays_even_with_the_buzz_cue_off()
    {
        using var r = Make(Settings(buzz: false));
        Ring(r);
        Assert.True(r.Engine.Ringing);
        Assert.Single(r.Player.Plays);
        Assert.True(r.Hook.ClickWatch);
        r.Hook.Click(HookButton.Back);
        Assert.Equal(1, r.Player.Stops);
        Assert.False(r.Hook.ClickWatch);
    }

    [Fact]
    public void With_both_off_there_is_no_ring_at_all()
    {
        using var r = Make(Settings(sound: null, buzz: false));
        Ring(r);
        Assert.False(r.Engine.Ringing);
        Assert.Empty(r.Player.Plays);
    }

    [Fact]
    public void A_preview_plays_the_sound_once_without_looping()
    {
        using var r = Make(Settings());
        r.Engine.PreviewRingSound(Settings(), 0, 0);
        Assert.Equal([(Path.Combine("sounds", "chime.wav"), "{0.0.0.00000000}.{abc}", 40, false)], r.Player.Plays);
    }

    [Fact]
    public void A_preview_uses_the_sound_and_volume_in_the_message_but_the_device_from_the_file()
    {
        using var r = Make(Settings(sound: null));
        r.Engine.PreviewRingSound(Settings(device: "{0.0.0.00000000}.{new}"), 7, 1); // Rise, volume 0
        Assert.Equal([(Path.Combine("sounds", "rise.wav"), "{0.0.0.00000000}.{new}", 0, false)], r.Player.Plays);
        r.Engine.PreviewRingSound(Settings(), 99, 0); // not a sound
        Assert.Single(r.Player.Plays);
    }

    [Fact]
    public void A_preview_is_ignored_while_a_call_rings()
    {
        using var r = Make(Settings());
        Ring(r);
        r.Engine.PreviewRingSound(Settings(), 0, 0);
        Assert.Single(r.Player.Plays);
        Assert.True(r.Player.Plays[0].Loop);
    }

    [Fact]
    public void Without_a_player_a_ring_still_buzzes_and_nothing_throws()
    {
        using var r = Make(Settings());
        r.Engine.RingSoundPlayer = null;
        Ring(r);
        Assert.True(r.Engine.Ringing);
        r.Engine.PreviewRingSound(Settings(), 1, 50);
    }

    [Fact]
    public void A_player_that_throws_never_breaks_the_ring()
    {
        using var r = Make(Settings());
        r.Engine.RingSoundPlayer = new ThrowingPlayer();
        Ring(r);
        Assert.True(r.Engine.Ringing);
        r.Engine.SignalRing(new RingSignal(false, RingerSignalSource.Notification));
        Assert.False(r.Engine.Ringing);
    }

    [Fact]
    public void The_base_is_listened_to_when_either_the_buzz_or_the_sound_is_on_and_the_base_can_start_a_ring()
    {
        SlenderConfig With(HapticSettings h) => SlenderConfig.Default with { Haptics = h };
        Assert.True(Slender.Tray.Program.ListensToBaseRing(With(Settings(sound: null))));
        Assert.True(Slender.Tray.Program.ListensToBaseRing(With(Settings(buzz: false))));
        Assert.False(Slender.Tray.Program.ListensToBaseRing(With(Settings(sound: null, buzz: false))));
        Assert.False(Slender.Tray.Program.ListensToBaseRing(With(Settings() with { RingSource = CallRingSource.Extension })));
    }

    private sealed class ThrowingPlayer : IRingSoundPlayer
    {
        public void Play(string soundPath, string? deviceId, int volume, bool loop) => throw new InvalidOperationException();
        public void Stop() => throw new InvalidOperationException();
    }
}
