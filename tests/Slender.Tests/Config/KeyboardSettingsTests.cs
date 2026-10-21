using Slender.Engine;
using Slender.Engine.Config;
using Slender.Hidpp;
using Slender.Settings.Logic;

namespace Slender.Tests.Config;

public sealed class KeyboardSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slender-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void New_keyboard_fields_round_trip()
    {
        var kb = new KeyboardSettings(FnLock: true, DisabledKeys: 0x15, ShowLockKeyChanges: false,
            FnLockChannel1: true, FnLockChannel2: null, FnLockChannel3: false, IdentifySpareKeys: true);
        new JsonConfigStore(_dir).Save(SlenderConfig.Default with { Keyboard = kb });
        Assert.Equal(kb, new JsonConfigStore(_dir).Load().Keyboard);
    }

    [Fact]
    public void An_older_file_with_only_FnLock_gets_the_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"),
            """{"version":2,"profiles":[{"bindings":{}}],"keyboard":{"fnLock":true}}""");
        var kb = new JsonConfigStore(_dir).Load().Keyboard;
        Assert.Equal(new KeyboardSettings(FnLock: true), kb);
        Assert.True(kb.ShowLockKeyChanges);
        Assert.Equal(0, kb.DisabledKeys);
        Assert.All(kb.FnLockChannels, c => Assert.Null(c));
    }

    [Fact]
    public void Out_of_range_disable_bits_are_dropped_on_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"),
            """{"version":2,"profiles":[{"bindings":{}}],"keyboard":{"disabledKeys":255}}""");
        Assert.Equal(0x1F, new JsonConfigStore(_dir).Load().Keyboard.DisabledKeys);
    }

    [Fact]
    public void Settings_edits_set_and_clear_keys_and_channels()
    {
        var k = new KeyboardSettings();
        k = KeyboardOps.SetDisabled(k, DisabledKeys.NumLock, true);
        k = KeyboardOps.SetDisabled(k, DisabledKeys.Windows, true);
        Assert.True(KeyboardOps.IsDisabled(k, DisabledKeys.NumLock));
        Assert.Equal(0x12, k.DisabledKeys);
        k = KeyboardOps.SetDisabled(k, DisabledKeys.NumLock, false);
        Assert.Equal(0x10, k.DisabledKeys);

        k = k.WithChannel(1, true).WithChannel(2, false);
        Assert.Equal(new bool?[] { null, true, false }, k.FnLockChannels);
        Assert.Throws<ArgumentOutOfRangeException>(() => k.WithChannel(3, true));
        Assert.Equal(new bool?[] { null, true, false }, new[] { 0, 1, 2 }.Select(i => KeyboardOps.FromChoiceIndex(KeyboardOps.ChoiceIndex(k.FnLockChannels[i]))));
        Assert.Equal("Channel 2 (in use now)", KeyboardOps.ChannelTitle(1, 1));
        Assert.Equal("Channel 3", KeyboardOps.ChannelTitle(2, 1));
    }

    [Fact]
    public void Spare_keys_are_in_the_keyboard_catalog_with_their_labels()
    {
        Assert.Equal(["Key 0x00EB", "Second left click", "Menu / virtual keyboard / Launchpad"], ControlCatalog.SpareKeyControls.Select(c => c.Name));
        Assert.Contains(ControlCatalog.KeyboardControls, c => c.Id == 0x0101);
    }

    [Fact]
    public void Spare_key_report_round_trips_through_a_file()
    {
        Assert.Null(SpareKeyReport.Read(_dir));
        SpareKeyReport.Write(_dir, KeyboardId.Spare00EC);
        var read = SpareKeyReport.Read(_dir);
        Assert.Equal(KeyboardId.Spare00EC, read!.Value.Cid);
        Assert.True(DateTimeOffset.UtcNow - read.Value.At < TimeSpan.FromMinutes(1));
        Assert.False(SpareKeyReport.TryParse("garbage", out _, out _));
        Assert.False(SpareKeyReport.TryParse("1 4000000000000000000", out _, out _));
    }

    [Fact]
    public void A_pending_edit_takes_fn_lock_from_the_file_unless_it_changed_fn_lock_itself()
    {
        var baseline = new KeyboardSettings(FnLock: true, FnLockChannel1: true);
        var file = baseline with { FnLock = false, FnLockChannel1 = false }; // the tray saved Fn+Esc

        var otherEdit = baseline with { ShowLockKeyChanges = false };
        var merged = KeyboardOps.AdoptFnLock(otherEdit, baseline, file);
        Assert.False(merged.FnLock);
        Assert.False(merged.FnLockChannel1);
        Assert.False(merged.ShowLockKeyChanges); // the user's own edit stays

        var fnEdit = baseline with { FnLock = false, FnLockChannel2 = true };
        merged = KeyboardOps.AdoptFnLock(fnEdit, baseline, baseline with { FnLock = true, FnLockChannel1 = false });
        Assert.False(merged.FnLock);           // changed by the user: kept
        Assert.True(merged.FnLockChannel2);    // changed by the user: kept
        Assert.False(merged.FnLockChannel1);   // untouched: from the file
    }
}
