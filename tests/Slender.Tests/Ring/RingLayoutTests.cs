using Slender.Engine;
using Slender.Engine.Config;
using Slender.Engine.Ring;

namespace Slender.Tests.Ring;

public sealed class RingLayoutTests
{
    private static readonly RingMetrics Bezel = RingLayout.Metrics(RingStyle.Bezel, RingSize.Medium);

    [Theory]
    [InlineData(0, -100, 0)]   // up
    [InlineData(70, -70, 1)]   // up-right
    [InlineData(100, 0, 2)]    // right
    [InlineData(0, 100, 4)]    // down
    [InlineData(-100, 0, 6)]   // left
    [InlineData(-70, -70, 7)]  // up-left
    [InlineData(-10, -100, 0)] // just left of up still belongs to up
    public void Pointer_offset_selects_the_sector(double dx, double dy, int slot) =>
        Assert.Equal(slot, RingLayout.SlotAt(dx, dy, Bezel));

    [Fact]
    public void Dead_zone_selects_nothing() => Assert.Null(RingLayout.SlotAt(20, 20, Bezel));

    [Fact]
    public void Slot_centres_run_clockwise_from_the_top()
    {
        var up = RingLayout.SlotCenter(RingStyle.Compass, RingSize.Medium, 0);
        var right = RingLayout.SlotCenter(RingStyle.Compass, RingSize.Medium, 2);
        Assert.Equal(0, up.X, 6);
        Assert.Equal(-100, up.Y, 6);
        Assert.Equal(100, right.X, 6);
        Assert.Equal(0, right.Y, 6);

        var keyUpLeft = RingLayout.SlotCenter(RingStyle.Keypad, RingSize.Medium, 7);
        Assert.Equal((-112.0, -80.0), keyUpLeft);
    }

    [Fact]
    public void Sizes_scale_every_style()
    {
        Assert.Equal(128 * 1.2, RingLayout.Metrics(RingStyle.Bezel, RingSize.Large).OuterRadius, 6);
        Assert.Equal(104 * 0.8, RingLayout.Metrics(RingStyle.Keypad, RingSize.Small).KeyWidth, 6);
    }

    [Fact]
    public void Ring_near_a_screen_edge_is_moved_fully_on_screen()
    {
        // Medium bezel extent 150 DIP at 150% = 225 px.
        var (x, y) = RingLayout.Place(RingPosition.AtCursor, 5, 1070, 0, 0, 1920, 1080, Bezel, 1.5);
        Assert.Equal(225, x);
        Assert.Equal(1080 - 225, y);

        var centre = RingLayout.Place(RingPosition.MonitorCentre, 5, 5, 1920, 0, 3840, 1080, Bezel, 1.0);
        Assert.Equal((2880, 540), centre);
    }

    [Fact]
    public void Ring_larger_than_the_work_area_is_centred_on_it()
    {
        // Bezel Large extent 180 DIP at 200% = 360 px; the 720 px ring does not fit a 600 x 500 work area.
        var large = RingLayout.Metrics(RingStyle.Bezel, RingSize.Large);
        var (x, y) = RingLayout.Place(RingPosition.AtCursor, 10, 10, 0, 0, 600, 500, large, 2.0);
        Assert.Equal(300, x);
        Assert.Equal(250, y);
    }

    [Fact]
    public void Ring_slots_folders_appearance_and_text_round_trip()
    {
        string dir = Path.Combine(Path.GetTempPath(), "slender-ring-" + Guid.NewGuid().ToString("N"));
        try
        {
            var folder = new RingSlot(new ActionSpec.None())
            {
                Name = "Work",
                Icon = "glyph:E8B7",
                Folder = [new RingSlot(new ActionSpec.Launch("https://chatgpt.com/")) { Name = "ChatGPT" }, new RingSlot(new ActionSpec.Text("Kind regards,"))],
            };
            SlenderConfig config = SlenderConfig.Default with
            {
                Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()) { RingSlots = [new RingSlot(new ActionSpec.Keys("Ctrl+X")), folder] }],
                Ring = new RingAppearance(RingStyle.Keypad, RingSize.Large, "#5FA8D9", 70, RingLabels.SelectedOnly, RingPosition.MonitorCentre),
            };

            var store = new JsonConfigStore(dir);
            store.Save(config);
            SlenderConfig loaded = store.Load();

            IReadOnlyList<RingSlot> ring = loaded.Profiles[0].RingSlots!;
            Assert.Equal(RingLayout.SlotCount, ring.Count);
            Assert.Equal(new ActionSpec.Keys("Ctrl+X"), ring[0].Action);
            Assert.True(ring[1].IsFolder);
            Assert.Equal("Work", ring[1].Name);
            Assert.Equal("glyph:E8B7", ring[1].Icon);
            Assert.Equal(RingLayout.SlotCount, ring[1].Folder!.Count);
            Assert.Equal("ChatGPT", ring[1].Folder![0].Name);
            Assert.Equal(new ActionSpec.Text("Kind regards,"), ring[1].Folder![1].Action);
            Assert.True(ring[2].IsEmpty);
            Assert.Equal(config.Ring, loaded.Ring);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Bad_ring_appearance_values_fall_back_to_defaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), "slender-ring-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonConfigStore(dir);
            store.Save(SlenderConfig.Default);
            string path = Path.Combine(dir, "config.json");
            string json = File.ReadAllText(path)
                .Replace("\"Bezel\"", "\"Hexagon\"", StringComparison.OrdinalIgnoreCase)
                .Replace("#D9925F", "copper", StringComparison.OrdinalIgnoreCase)
                .Replace("92", "5");
            File.WriteAllText(path, json);

            RingAppearance ring = store.Load().Ring;
            Assert.Equal(RingStyle.Bezel, ring.Style);
            Assert.Equal("#D9925F", ring.Accent);
            Assert.Equal(40, ring.OpacityPercent);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
