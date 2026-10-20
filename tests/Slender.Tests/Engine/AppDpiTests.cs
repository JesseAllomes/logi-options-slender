using Slender.Engine;
using Slender.Engine.Config;
using Slender.Hidpp;

namespace Slender.Tests.Engine;

public class AppDpiTests
{
    private static Profile App(string process, int? dpi) => new(process, new Dictionary<ushort, ActionSpec>()) { Dpi = dpi };

    private static SlenderConfig Cfg(int? global, params Profile[] apps) =>
        SlenderConfig.Default with
        {
            Pointer = new PointerSettings(Dpi: global),
            Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()), .. apps],
        };

    private sealed class Rig : IDisposable
    {
        public FakeMouse Mouse = new() { Dpi = 1000 };
        public FakeForeground Fg = new() { ProcessName = "notepad" };
        public SlenderEngine Engine;

        public Rig(SlenderConfig cfg, DpiCapabilities? caps = null)
        {
            Mouse.DpiCaps = caps;
            Engine = new SlenderEngine(Mouse, new FakeHook(), Fg, new FakeExcel(), new FakeKeyExecutor(), cfg, new ListLog(), new InlineQueue(), () => 0, new FakeHoldTimer())
            {
                ForegroundDebounceMs = 0,
            };
            Engine.Start();
            Settle();
        }

        public void Settle() => Engine.DpiSettled.GetAwaiter().GetResult();

        public void Switch(string? app) { Fg.Switch(app); Settle(); }

        public List<int?> Writes { get { lock (Mouse.DpiWrites) return [.. Mouse.DpiWrites]; } }

        public void Dispose() => Engine.Dispose();
    }

    [Fact]
    public void Resolver_prefers_the_app_then_all_apps_then_the_global_speed()
    {
        var cfg = Cfg(1600, App("excel", 3200), App("chrome", null));
        Assert.Equal(3200, AppDpi.For(cfg, "excel"));
        Assert.Equal(3200, AppDpi.For(cfg, "EXCEL.exe"));
        Assert.Equal(1600, AppDpi.For(cfg, "chrome"));
        Assert.Equal(1600, AppDpi.For(cfg, null));
        Assert.Null(AppDpi.For(Cfg(null, App("excel", 3200)), "notepad"));
        var allApps = cfg with { Profiles = [new Profile(null, new Dictionary<ushort, ActionSpec>()) { Dpi = 800 }, App("excel", 3200)] };
        Assert.Equal(800, AppDpi.For(allApps, "notepad"));
    }

    [Fact]
    public void Nothing_configured_writes_nothing()
    {
        using var r = new Rig(Cfg(null, App("excel", null)));
        r.Switch("excel");
        r.Switch("notepad");
        Assert.Empty(r.Writes);
    }

    [Fact]
    public void Entering_an_override_writes_once_and_leaving_restores_the_mouse_own_speed()
    {
        using var r = new Rig(Cfg(null, App("excel", 3200)));
        r.Switch("excel");
        Assert.Equal([3200], r.Writes);
        r.Switch("excel"); // same app again: nothing to write
        r.Switch("notepad");
        Assert.Equal([3200, 1000], r.Writes);
        r.Switch("chrome"); // still at the restored value
        Assert.Equal(2, r.Writes.Count);
    }

    [Fact]
    public void Leaving_restores_the_global_speed_and_apps_with_the_same_value_share_one_write()
    {
        using var r = new Rig(Cfg(1600, App("excel", 3200), App("word", 3200)));
        int before = r.Writes.Count; // start-up puts the global value on the mouse
        Assert.Equal(1600, r.Mouse.Dpi);
        r.Switch("excel");
        r.Switch("word");
        Assert.Equal(before + 1, r.Writes.Count);
        r.Switch("notepad");
        Assert.Equal(1600, r.Writes[^1]);
        Assert.Equal(before + 2, r.Writes.Count);
    }

    [Fact]
    public void A_target_is_clamped_to_what_the_sensor_reports()
    {
        using var stepped = new Rig(Cfg(null, App("excel", 3333)), DpiCapabilities.Range(200, 8000, 50));
        stepped.Switch("excel");
        Assert.Equal([3350], stepped.Writes);

        using var listed = new Rig(Cfg(null, App("excel", 2000)), DpiCapabilities.List([800, 1600, 3200]));
        listed.Switch("excel");
        Assert.Equal([1600], listed.Writes);

        using var low = new Rig(Cfg(null, App("excel", 200)), DpiCapabilities.Range(400, 4000, 100));
        low.Switch("excel");
        Assert.Equal([400], low.Writes);
    }

    [Fact]
    public void The_sensor_list_is_read_once_per_connection_and_published_without_a_write()
    {
        using var r = new Rig(Cfg(null, App("excel", null)), DpiCapabilities.Range(400, 4000, 100));
        Assert.Equal(400, r.Engine.DpiCapabilities?.Min); // read by the first apply although nobody asked for a speed
        Assert.Equal(4000, r.Engine.DpiCapabilities?.Max);
        Assert.Empty(r.Writes);
        Assert.Equal(1, r.Mouse.DpiCapReads);
        r.Switch("excel");
        r.Switch("notepad");
        Assert.Equal(1, r.Mouse.DpiCapReads); // not again
        r.Mouse.IsConnected = false;
        r.Mouse.Connect(false);
        r.Settle();
        Assert.Null(r.Engine.DpiCapabilities); // gone with the connection
        r.Mouse.IsConnected = true;
        r.Mouse.Connect(true);
        r.Settle();
        Assert.Equal(2, r.Mouse.DpiCapReads);
    }

    [Fact]
    public void A_mouse_that_will_not_list_its_speeds_is_asked_only_once()
    {
        using var r = new Rig(Cfg(null, App("excel", 3200)), null);
        r.Switch("excel");
        r.Switch("notepad");
        r.Switch("excel");
        Assert.Null(r.Engine.DpiCapabilities);
        Assert.Equal(1, r.Mouse.DpiCapReads);
    }

    [Fact]
    public void A_reconnect_writes_again_because_dpi_does_not_survive_it()
    {
        using var r = new Rig(Cfg(null, App("excel", 3200)));
        r.Switch("excel");
        Assert.Equal([3200], r.Writes);
        r.Mouse.IsConnected = false;
        r.Mouse.Connect(false);
        r.Settle();
        r.Mouse.IsConnected = true;
        r.Mouse.Connect(true);
        r.Settle();
        Assert.Equal([3200, 3200], r.Writes);
    }

    [Fact]
    public void Nothing_is_written_while_the_mouse_is_away()
    {
        using var r = new Rig(Cfg(null, App("excel", 3200)));
        r.Mouse.IsConnected = false;
        r.Mouse.Connect(false);
        r.Switch("excel");
        Assert.Empty(r.Writes);
    }

    [Fact]
    public void A_config_change_applies_to_the_app_in_front()
    {
        using var r = new Rig(Cfg(null, App("excel", null)));
        r.Switch("excel");
        Assert.Empty(r.Writes);
        r.Engine.ApplyConfig(Cfg(null, App("excel", 2400)));
        r.Settle();
        Assert.Equal([2400], r.Writes);
    }

    [Fact]
    public void Profile_dpi_round_trips_through_the_config_file_and_is_clamped()
    {
        string dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonConfigStore(dir);
            store.Save(Cfg(null, App("excel", 3200), App("word", 99999), App("chrome", null)));
            var back = store.Load();
            Assert.Equal(3200, back.Profiles.Single(p => p.AppProcess == "excel").Dpi);
            Assert.Equal(8000, back.Profiles.Single(p => p.AppProcess == "word").Dpi);
            Assert.Null(back.Profiles.Single(p => p.AppProcess == "chrome").Dpi);
            Assert.Null(back.Profiles.Single(p => p.AppProcess is null).Dpi);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Dpi_list_parsing_reads_ranges_and_explicit_lists()
    {
        // sensor 0, 200, step marker 0xE032 (50), 8000, end
        byte[] range = [0, 0x00, 0xC8, 0xE0, 0x32, 0x1F, 0x40, 0, 0];
        var r = DpiCapabilities.Parse(range)!;
        Assert.Equal((200, 8000), (r.Min, r.Max));
        Assert.Equal(1050, r.Clamp(1040));
        byte[] list = [0, 0x03, 0x20, 0x06, 0x40, 0x0C, 0x80, 0, 0];
        var l = DpiCapabilities.Parse(list)!;
        Assert.Equal(1600, l.Clamp(1700));
        Assert.Null(DpiCapabilities.Parse(new byte[] { 0, 0, 0 }));
        Assert.Null(DpiCapabilities.Parse(new byte[] { 0, 0xE0, 0x32, 0, 0 }));
    }
}
