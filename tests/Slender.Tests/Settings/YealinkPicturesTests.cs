using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public sealed class YealinkPicturesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Install => Path.Combine(_root, "Fake Vendor Connect");

    private string Drawable => YealinkPictures.DrawableRoot(Install);

    /// <summary>A made-up picture: a PNG header for the given size and filler. Not a real Yealink image.</summary>
    private static byte[] Png(int w, int h)
    {
        var b = new byte[64];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }.CopyTo(b, 0);
        b[16] = (byte)(w >> 24); b[17] = (byte)(w >> 16); b[18] = (byte)(w >> 8); b[19] = (byte)w;
        b[20] = (byte)(h >> 24); b[21] = (byte)(h >> 16); b[22] = (byte)(h >> 8); b[23] = (byte)h;
        return b;
    }

    private void WritePicture(string scale, YealinkView view, int size)
    {
        string dir = Path.Combine(Drawable, scale);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, YealinkPictures.FileName(view)), Png(size, size));
    }

    private static IEnumerable<string> Subfolders(string root) => Directory.GetDirectories(root).Select(d => Path.GetFileName(d));

    // ---- install discovery ----

    [Fact]
    public void The_install_is_found_from_the_uninstall_entry_not_a_fixed_path()
    {
        Directory.CreateDirectory(Drawable);
        var entries = new (string?, string?)[]
        {
            ("Some Other App", Path.Combine(_root, "other")),
            ("  yealink usb connect ", Install + Path.DirectorySeparatorChar),
        };
        Assert.Equal(Drawable, YealinkPictures.FindInstall(entries, Directory.Exists));
    }

    [Fact]
    public void A_stale_or_empty_entry_is_skipped_and_none_means_null()
    {
        var entries = new (string?, string?)[]
        {
            ("Yealink USB Connect", Path.Combine(_root, "gone")), // removed folder
            ("Yealink USB Connect", null),
            ("Yealink USB Connect", " "),
            (null, Install),
        };
        Assert.Null(YealinkPictures.FindInstall(entries, Directory.Exists));
        Assert.Null(YealinkPictures.FindInstall([], Directory.Exists));
    }

    [Fact]
    public void A_later_entry_with_the_resources_wins_over_a_stale_earlier_one()
    {
        Directory.CreateDirectory(Drawable);
        var entries = new (string?, string?)[] { ("Yealink USB Connect", Path.Combine(_root, "gone")), ("Yealink USB Connect", Install) };
        Assert.Equal(Drawable, YealinkPictures.FindInstall(entries, Directory.Exists));
    }

    // ---- scale and file choice ----

    [Fact]
    public void Scale_folders_are_ranked_largest_first_ignoring_non_numbers()
    {
        Assert.Equal(["2", "1.5", "1.25", "1"], YealinkPictures.RankScales(["svg", "2", "1", "1.5", "1.25"]));
        Assert.Empty(YealinkPictures.RankScales(["svg"]));
    }

    [Fact]
    public void The_file_comes_from_the_largest_scale_that_has_it()
    {
        WritePicture("1", YealinkView.Mono, 400);
        WritePicture("2", YealinkView.Mono, 800);
        string? two = YealinkPictures.FindFile(Drawable, YealinkView.Mono, File.Exists, Subfolders);
        Assert.EndsWith(Path.Combine("2", "pic_WH64_mono_big.png"), two);

        // The largest folder lacks this view: the next one is used.
        WritePicture("1", YealinkView.Base, 400);
        string? baseFile = YealinkPictures.FindFile(Drawable, YealinkView.Base, File.Exists, Subfolders);
        Assert.EndsWith(Path.Combine("1", "pic_WH64_base_big.png"), baseFile);

        Assert.Null(YealinkPictures.FindFile(Drawable, YealinkView.TeamsDual, File.Exists, Subfolders));
    }

    // ---- variant choice ----

    [Theory]
    [InlineData(null, null, YealinkView.Mono)]
    [InlineData("Example Headset", null, YealinkView.Mono)]
    [InlineData("Example Headset", "UC", YealinkView.Mono)]
    [InlineData("Example Headset Dual", "UC", YealinkView.Dual)]
    [InlineData("example headset dual", "Teams", YealinkView.TeamsDual)]
    [InlineData("Example Headset", " teams ", YealinkView.TeamsMono)]
    [InlineData(null, "Teams", YealinkView.TeamsMono)]
    public void The_headset_picture_follows_the_model_and_platform(string? model, string? platform, YealinkView expected)
    {
        Assert.Equal(expected, YealinkPictures.ChooseHeadset(model, platform));
    }

    [Fact]
    public void Every_view_has_its_own_file_and_cache_folder()
    {
        var views = Enum.GetValues<YealinkView>();
        Assert.Equal(views.Length, views.Select(YealinkPictures.FileName).Distinct().Count());
        Assert.Equal(views.Length, views.Select(YealinkPictures.DeviceId).Distinct().Count());
        Assert.All(views, v => Assert.EndsWith(".png", YealinkPictures.FileName(v)));
    }

    // ---- layout ----

    [Fact]
    public void Our_hotspots_sit_inside_the_picture_with_unique_ids_and_sides()
    {
        foreach (var view in Enum.GetValues<YealinkView>())
        {
            var spots = YealinkPictures.Hotspots(view);
            Assert.NotEmpty(spots);
            Assert.Equal(spots.Count, spots.Select(s => s.ControlId).Distinct().Count());
            Assert.All(spots, s =>
            {
                Assert.InRange(s.X, 0, 100);
                Assert.InRange(s.Y, 0, 100);
                Assert.True(s.LabelSide is -1 or 1);
                Assert.False(string.IsNullOrWhiteSpace(s.Name));
            });
        }

        Assert.Contains(YealinkPictures.Hotspots(YealinkView.Mono), s => s.Slot == YealinkSlots.CallButton);
        Assert.Contains(YealinkPictures.Hotspots(YealinkView.TeamsDual), s => s.Slot == YealinkSlots.TeamsButton);
        Assert.Contains(YealinkPictures.Hotspots(YealinkView.Base), s => s.Slot == YealinkSlots.DeskKey);
        Assert.Contains(YealinkPictures.Hotspots(YealinkView.Mono), s => s.Slot == YealinkSlots.Battery);
    }

    // ---- copy into the shared cache ----

    private AssetCache Cache => new(Path.Combine(_root, "no-options-plus"), Path.Combine(_root, "assets"), Path.Combine(_root, "backup"));

    [Fact]
    public void A_picture_is_copied_into_the_cache_with_our_layout_and_survives_a_reload()
    {
        WritePicture("2", YealinkView.Mono, 800);
        var cache = Cache;

        CachedDevice? device = cache.EnsureFrom(YealinkPictures.DeviceId(YealinkView.Mono), dir => YealinkPictures.Copy(YealinkView.Mono, Drawable, dir));

        Assert.NotNull(device);
        Assert.StartsWith(Path.Combine(_root, "assets"), device.ImagePath);
        Assert.Equal(800, device.ImageWidth);
        Assert.Equal(YealinkPictures.Hotspots(YealinkView.Mono), device.Layout.Hotspots);
        Assert.True(File.Exists(Path.Combine(_root, "assets", "yealink-wh64-mono-2", "layout.json")));

        // Later runs read the cache and never need USB Connect.
        Directory.Delete(Install, recursive: true);
        CachedDevice? again = cache.EnsureFrom(YealinkPictures.DeviceId(YealinkView.Mono), dir => YealinkPictures.Copy(YealinkView.Mono, null, dir));
        Assert.NotNull(again);
        Assert.Equal(device.Layout.Hotspots, again.Layout.Hotspots);
    }

    [Fact]
    public void Without_usb_connect_nothing_is_cached_and_the_caller_gets_null()
    {
        Assert.Null(Cache.EnsureFrom(YealinkPictures.DeviceId(YealinkView.Base), dir => YealinkPictures.Copy(YealinkView.Base, null, dir)));
        Assert.False(Directory.Exists(Path.Combine(_root, "assets", "yealink-wh64-base-2")));
    }

    [Fact]
    public void Refresh_keeps_the_pictures_when_usb_connect_is_gone_and_replaces_them_when_it_is_there()
    {
        WritePicture("1", YealinkView.Base, 400);
        var cache = Cache;
        string id = YealinkPictures.DeviceId(YealinkView.Base);
        Assert.NotNull(cache.EnsureFrom(id, dir => YealinkPictures.Copy(YealinkView.Base, Drawable, dir)));

        Directory.Delete(Install, recursive: true);
        (CachedDevice? kept, bool refreshed) = cache.RefreshFrom(id, dir => YealinkPictures.Copy(YealinkView.Base, null, dir));
        Assert.False(refreshed);
        Assert.NotNull(kept);
        Assert.Equal(400, kept.ImageWidth);

        WritePicture("2", YealinkView.Base, 800);
        (CachedDevice? fresh, bool refreshedNow) = cache.RefreshFrom(id, dir => YealinkPictures.Copy(YealinkView.Base, Drawable, dir));
        Assert.True(refreshedNow);
        Assert.Equal(800, fresh!.ImageWidth);
    }

    [Fact]
    public void A_lost_local_copy_is_restored_from_the_backup()
    {
        WritePicture("1", YealinkView.Dual, 400);
        var cache = Cache;
        string id = YealinkPictures.DeviceId(YealinkView.Dual);
        Assert.NotNull(cache.EnsureFrom(id, dir => YealinkPictures.Copy(YealinkView.Dual, Drawable, dir)));
        Directory.Delete(Path.Combine(_root, "assets", id), recursive: true);
        Directory.Delete(Install, recursive: true);

        CachedDevice? restored = cache.EnsureFrom(id, dir => YealinkPictures.Copy(YealinkView.Dual, null, dir));

        Assert.NotNull(restored);
        Assert.Equal(YealinkPictures.Hotspots(YealinkView.Dual), restored.Layout.Hotspots);
    }

    [Fact]
    public void Legacy_id_is_the_id_before_the_large_copy()
    {
        Assert.Equal("yealink-wh64-base", YealinkPictures.LegacyDeviceId(YealinkView.Base));
        Assert.Equal("yealink-wh64-teams-mono", YealinkPictures.LegacyDeviceId(YealinkView.TeamsMono));
    }
}
