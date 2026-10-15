using System.Runtime.InteropServices;
using Slender.Engine;
using Slender.Tray;

namespace Slender.Tests.Tray;

public sealed class MemoryWatchdogTests
{
    private const long Mb = 1024 * 1024;

    private static (MemoryWatchdog Dog, List<string> Log, Queue<long?> Readings) Make()
    {
        var readings = new Queue<long?>();
        var log = new List<string>();
        var dog = new MemoryWatchdog(() => readings.Dequeue(), log.Add);
        return (dog, log, readings);
    }

    [Fact]
    public void Below_warn_line_logs_nothing()
    {
        var (dog, log, r) = Make();
        r.Enqueue(20 * Mb);
        r.Enqueue(59 * Mb);

        Assert.Equal(WatchdogVerdict.Ok, dog.Check());
        Assert.Equal(WatchdogVerdict.Ok, dog.Check());
        Assert.Empty(log);
    }

    [Fact]
    public void Crossing_warn_line_logs_once_until_it_drops_back()
    {
        var (dog, log, r) = Make();
        foreach (long v in new long[] { 61, 70, 90, 50, 65 })
        {
            r.Enqueue(v * Mb);
        }

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(WatchdogVerdict.Ok, dog.Check());
        }

        Assert.Equal(2, log.Count); // first crossing, and the crossing after the dip to 50
        Assert.All(log, l => Assert.Contains("crossed", l));
    }

    [Fact]
    public void Three_consecutive_checks_above_restart_line_ask_for_restart()
    {
        var (dog, log, r) = Make();
        r.Enqueue(101 * Mb);
        r.Enqueue(150 * Mb);
        r.Enqueue(120 * Mb);

        Assert.Equal(WatchdogVerdict.Ok, dog.Check());
        Assert.Equal(WatchdogVerdict.Ok, dog.Check());
        Assert.Equal(WatchdogVerdict.Restart, dog.Check());
        Assert.Contains(log, l => l.Contains("restarting"));
    }

    [Fact]
    public void A_dip_resets_the_consecutive_count()
    {
        var (dog, _, r) = Make();
        foreach (long v in new long[] { 110, 110, 90, 110, 110, 110 })
        {
            r.Enqueue(v * Mb);
        }

        var verdicts = Enumerable.Range(0, 6).Select(_ => dog.Check()).ToArray();
        Assert.Equal(
            [WatchdogVerdict.Ok, WatchdogVerdict.Ok, WatchdogVerdict.Ok, WatchdogVerdict.Ok, WatchdogVerdict.Ok, WatchdogVerdict.Restart],
            verdicts);
    }

    [Fact]
    public void Exactly_at_restart_line_does_not_count()
    {
        var (dog, _, r) = Make();
        for (int i = 0; i < 4; i++)
        {
            r.Enqueue(MemoryWatchdog.RestartBytes);
        }

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(WatchdogVerdict.Ok, dog.Check());
        }
    }

    [Fact]
    public void Unreadable_value_neither_warns_nor_counts()
    {
        var (dog, log, r) = Make();
        r.Enqueue(110 * Mb);
        r.Enqueue(null);
        r.Enqueue(110 * Mb);

        Assert.Equal(WatchdogVerdict.Ok, dog.Check());
        Assert.Equal(WatchdogVerdict.Ok, dog.Check());
        Assert.Equal(WatchdogVerdict.Ok, dog.Check());
        Assert.DoesNotContain(log, l => l.Contains("restarting"));
    }

    [Fact]
    public void Reads_this_process_private_bytes()
    {
        long? bytes = MemoryWatchdog.ReadCurrentPrivateBytes();
        Assert.NotNull(bytes);
        Assert.True(bytes > 1 * Mb);
    }
}

public sealed class ComDispatchPackingTests
{
    [Fact]
    public void Variant_is_24_bytes_on_x64()
    {
        Assert.Equal(24, Marshal.SizeOf<ComVariant>());
        Assert.Equal(24, Marshal.SizeOf<DispParams>());
    }

    [Fact]
    public void Arguments_are_packed_in_reverse_order_as_I4()
    {
        var dest = new ComVariant[3];
        ComDispatch.PackArgs([1, 2, 3], dest);

        Assert.Equal([3, 2, 1], dest.Select(v => (int)v.Data1));
        Assert.All(dest, v => Assert.Equal(ComVariant.VT_I4, v.Vt));
    }

    [Fact]
    public void Single_named_argument_is_packed_as_one_I4()
    {
        var dest = new ComVariant[1];
        ComDispatch.PackArgs([-7], dest);

        Assert.Equal(ComVariant.VT_I4, dest[0].Vt);
        Assert.Equal(-7, dest[0].ToInt32());
    }

    [Fact]
    public void Destination_must_fit_all_arguments()
    {
        Assert.Throws<ArgumentException>(() => ComDispatch.PackArgs([1, 2], new ComVariant[1]));
    }

    [Theory]
    [InlineData(ComVariant.VT_I2, 120L, 120)]
    [InlineData(ComVariant.VT_I4, 100L, 100)]
    [InlineData(ComVariant.VT_BOOL, -1L, 1)]
    public void Integer_like_results_convert(ushort vt, long data, int expected)
    {
        Assert.Equal(expected, new ComVariant { Vt = vt, Data1 = data }.ToInt32());
    }

    [Fact]
    public void Double_result_such_as_zoom_rounds_to_int()
    {
        var v = new ComVariant { Vt = ComVariant.VT_R8, Data1 = BitConverter.DoubleToInt64Bits(110.0) };
        Assert.Equal(110, v.ToInt32());
    }

    [Fact]
    public void Non_numeric_result_throws()
    {
        Assert.Throws<InvalidCastException>(() => new ComVariant { Vt = ComVariant.VT_DISPATCH, Data1 = 1 }.ToInt32());
    }

    [Fact]
    public void Busy_hresults_are_recognised()
    {
        Assert.True(ExcelBridge.IsBusy(new COMException("x", unchecked((int)0x800AC472))));
        Assert.True(ExcelBridge.IsBusy(new COMException("x", unchecked((int)0x80010001))));
        Assert.False(ExcelBridge.IsBusy(new COMException("x", unchecked((int)0x80004005))));
    }
}

public sealed class TrayLaunchTests
{
    [Fact]
    public void Real_tray_keeps_the_plain_mutex_name()
    {
        Assert.Equal(SingleInstance.MutexName, SingleInstance.NameFor(null));
        Assert.Equal(SingleInstance.MutexName, SingleInstance.NameFor(""));
    }

    [Fact]
    public void Test_instance_gets_a_name_keyed_off_its_config_dir()
    {
        string a = SingleInstance.NameFor(@"C:\temp\a");
        string b = SingleInstance.NameFor(@"C:\temp\b");

        Assert.NotEqual(SingleInstance.MutexName, a);
        Assert.NotEqual(a, b);
        Assert.Equal(a, SingleInstance.NameFor(@"c:\TEMP\a\"));
    }

    [Fact]
    public void Test_instance_can_start_beside_one_holding_the_real_name()
    {
        using var real = new SingleInstance(SingleInstance.NameFor(null) + ".unit-test");
        using var test = new SingleInstance(SingleInstance.NameFor(Path.GetTempPath()));

        Assert.True(real.IsFirstInstance);
        Assert.True(test.IsFirstInstance);
    }

    [Fact]
    public void Release_lets_a_new_instance_take_the_lock()
    {
        string name = SingleInstance.MutexName + ".unit-test-release";
        var first = new SingleInstance(name);
        Assert.True(first.IsFirstInstance);
        first.Release();
        first.Dispose();

        using var second = new SingleInstance(name);
        Assert.True(second.IsFirstInstance);
    }

    [Fact]
    public void Popover_flag_is_passed_only_when_asked()
    {
        Assert.Equal(["--popover"], SettingsLauncher.StartInfo(@"C:\x\Slender.Settings.exe", popover: true).ArgumentList);
        Assert.Empty(SettingsLauncher.StartInfo(@"C:\x\Slender.Settings.exe", popover: false).ArgumentList);
    }
}

public sealed class ComDispatchLiveTests
{
    [Fact]
    public void Talks_to_a_real_IDispatch()
    {
        // Scripting.Dictionary ships with Windows: Count is a Long property, and Exists/Add exercise the call path.
        Type? type = Type.GetTypeFromProgID("Scripting.Dictionary");
        if (type is null)
        {
            return;
        }

        object dict = Activator.CreateInstance(type)!;
        try
        {
            nint pdisp = Marshal.GetIDispatchForObject(dict);
            try
            {
                using var d = new ComDispatch(pdisp); // takes over one reference (released on Dispose)
                Assert.Equal(0, d.GetInt("Count"));
                d.PutInt("CompareMode", 1);
                Assert.Equal(1, d.GetInt("CompareMode"));
                var ex = Assert.Throws<COMException>(() => d.GetInt("NoSuchMember"));
                Assert.Equal(unchecked((int)0x80020006), ex.HResult); // DISP_E_UNKNOWNNAME
            }
            catch
            {
                Marshal.Release(pdisp);
                throw;
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(dict);
        }
    }
}
