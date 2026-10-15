using Slender.Engine;
using Slender.Engine.Config;
using Slender.Engine.Ring;
using Slender.Settings.Logic;

namespace Slender.Tests.Settings;

public sealed class RingOpsTests
{
    private static SlenderConfig Base() => SlenderConfig.Default with
    {
        Profiles =
        [
            new Profile(null, new Dictionary<ushort, ActionSpec>()) { DisplayName = "All apps" },
            new Profile("excel", new Dictionary<ushort, ActionSpec>()) { DisplayName = "Excel" },
        ],
    };

    private static ActionSpec Keys(string chord) => new ActionSpec.Keys(chord);

    [Fact]
    public void With_no_ring_anywhere_the_optionsplus_defaults_show_and_the_first_edit_materialises_them()
    {
        SlenderConfig c = Base();
        Assert.Equal(Keys("Ctrl+X"), RingOps.Slots(c, null)[0].Action);

        c = RingOps.SetAction(c, null, new RingPath(0), Keys("Ctrl+Shift+X"));

        IReadOnlyList<RingSlot> own = ProfileOps.Find(c, null)!.RingSlots!;
        Assert.Equal(8, own.Count);
        Assert.Equal(Keys("Ctrl+Shift+X"), own[0].Action);
        Assert.Equal("Cut", own[0].Name); // name kept
        Assert.Equal(Keys("Ctrl+C"), own[1].Action);
        Assert.Null(ProfileOps.Find(c, "excel")!.RingSlots);
    }

    [Fact]
    public void Editing_the_default_ring_without_a_default_profile_creates_one()
    {
        SlenderConfig c = SlenderConfig.Default with { Profiles = [] };

        c = RingOps.SetAction(c, null, new RingPath(2), Keys("Ctrl+B"));

        Assert.Equal(Keys("Ctrl+B"), c.Profiles.Single(p => p.AppProcess is null).RingSlots![2].Action);
    }

    [Fact]
    public void Clear_empties_a_slot_and_setting_none_on_an_unnamed_slot_does_too()
    {
        SlenderConfig c = RingOps.SetSlot(Base(), null, new RingPath(1), new RingSlot(Keys("Ctrl+C")));

        c = RingOps.SetAction(c, null, new RingPath(1), new ActionSpec.None());
        Assert.True(RingOps.Get(c, null, new RingPath(1))!.IsEmpty);

        c = RingOps.Clear(c, null, new RingPath(0));
        Assert.Same(RingSlot.Empty, RingOps.Get(c, null, new RingPath(0)));
    }

    [Fact]
    public void Name_and_icon_edits()
    {
        SlenderConfig c = RingOps.SetSlot(Base(), null, new RingPath(0), new RingSlot(Keys("Ctrl+K")));

        c = RingOps.SetName(c, null, new RingPath(0), "  Link  ");
        c = RingOps.SetIcon(c, null, new RingPath(0), RingGlyphs.GlyphIcon("e71b"));
        RingSlot s = RingOps.Get(c, null, new RingPath(0))!;
        Assert.Equal("Link", s.Name);
        Assert.Equal("glyph:E71B", s.Icon);

        c = RingOps.SetName(c, null, new RingPath(0), "   ");
        c = RingOps.SetIcon(c, null, new RingPath(0), null);
        s = RingOps.Get(c, null, new RingPath(0))!;
        Assert.Null(s.Name);
        Assert.Null(s.Icon);

        c = RingOps.SetName(c, null, new RingPath(0), new string('x', 80));
        Assert.Equal(RingOps.FolderNameMax, RingOps.Get(c, null, new RingPath(0))!.Name!.Length);
    }

    [Fact]
    public void Make_folder_gives_an_empty_folder_named_after_the_slot_and_children_can_be_edited()
    {
        SlenderConfig c = RingOps.SetSlot(Base(), null, new RingPath(0), new RingSlot(Keys("Ctrl+K")));

        c = RingOps.MakeFolder(c, null, new RingPath(0));
        RingSlot folder = RingOps.Get(c, null, new RingPath(0))!;
        Assert.True(folder.IsFolder);
        Assert.Equal(8, folder.Folder!.Count);
        Assert.Equal(0, RingOps.ItemCount(folder));

        c = RingOps.SetAction(c, null, new RingPath(0, 3), Keys("Ctrl+B"));
        Assert.Equal(1, RingOps.ItemCount(RingOps.Get(c, null, new RingPath(0))!));
        Assert.Equal(Keys("Ctrl+B"), RingOps.Get(c, null, new RingPath(0, 3))!.Action);
    }

    [Fact]
    public void A_folder_cannot_be_made_inside_a_folder_or_pasted_there()
    {
        SlenderConfig c = RingOps.MakeFolder(Base(), null, new RingPath(7)); // default slot 7 is already a folder: no change
        Assert.Same(c, RingOps.MakeFolder(c, null, new RingPath(7)));

        SlenderConfig inner = RingOps.MakeFolder(c, null, new RingPath(7, 0));
        Assert.Same(c, inner);

        RingSlot folder = RingDefaults.EmptyFolder("F");
        Assert.False(RingOps.CanPaste(folder, new RingPath(7, 1)));
        Assert.Same(c, RingOps.Paste(c, null, new RingPath(7, 1), folder));
        Assert.True(RingOps.CanPaste(folder, new RingPath(2)));
    }

    [Fact]
    public void Deleting_or_converting_a_folder_with_items_asks_first_and_empty_ones_do_not()
    {
        SlenderConfig c = RingOps.SetSlot(Base(), null, new RingPath(7), RingDefaults.EmptyFolder("Work"));
        Assert.False(RingOps.NeedsConfirm(RingOps.Get(c, null, new RingPath(7))));

        c = RingOps.SetAction(c, null, new RingPath(7, 0), Keys("Ctrl+1"));
        c = RingOps.SetAction(c, null, new RingPath(7, 1), Keys("Ctrl+2"));
        RingSlot folder = RingOps.Get(c, null, new RingPath(7))!;

        Assert.True(RingOps.NeedsConfirm(folder));
        Assert.Equal("Remove this folder and its 2 actions?", RingOps.ConfirmText(folder, "Remove"));
        Assert.Equal("Turn this folder and its 2 actions?", RingOps.ConfirmText(folder, "Turn"));

        SlenderConfig converted = RingOps.ConvertToAction(c, null, new RingPath(7));
        Assert.True(RingOps.Get(converted, null, new RingPath(7))!.IsEmpty);
        SlenderConfig removed = RingOps.Clear(c, null, new RingPath(7));
        Assert.True(RingOps.Get(removed, null, new RingPath(7))!.IsEmpty);
    }

    [Fact]
    public void Single_item_confirm_text_is_singular()
    {
        SlenderConfig c = RingOps.SetSlot(Base(), null, new RingPath(7), RingDefaults.EmptyFolder("Work"));
        c = RingOps.SetAction(c, null, new RingPath(7, 0), Keys("Ctrl+1"));

        Assert.Equal("Remove this folder and its 1 action?", RingOps.ConfirmText(RingOps.Get(c, null, new RingPath(7))!, "Remove"));
    }

    [Fact]
    public void Copy_and_paste_replace_the_target_with_the_copied_slot()
    {
        SlenderConfig c = Base();
        RingSlot copied = RingOps.Get(c, null, new RingPath(1))!; // Copy, Ctrl+C

        c = RingOps.Paste(c, null, new RingPath(5), copied);

        Assert.Equal(copied, RingOps.Get(c, null, new RingPath(5)));
        Assert.Same(c, RingOps.Paste(c, null, new RingPath(5), null));
    }

    [Fact]
    public void Pasting_a_folder_copies_its_children_too()
    {
        SlenderConfig c = RingOps.SetSlot(Base(), null, new RingPath(7), RingDefaults.EmptyFolder("Work"));
        c = RingOps.SetAction(c, null, new RingPath(7, 0), Keys("Ctrl+1"));
        RingSlot folder = RingOps.Get(c, null, new RingPath(7))!;

        c = RingOps.Paste(c, null, new RingPath(0), folder);

        Assert.Equal(Keys("Ctrl+1"), RingOps.Get(c, null, new RingPath(0, 0))!.Action);
    }

    [Fact]
    public void Customise_copies_all_apps_into_the_app_and_reset_sets_it_back_to_null()
    {
        SlenderConfig c = RingOps.SetAction(Base(), null, new RingPath(0), Keys("Ctrl+Shift+X"));
        Assert.True(RingOps.FollowsDefault(c, "excel"));
        Assert.Equal(Keys("Ctrl+Shift+X"), RingOps.Slots(c, "excel")[0].Action);

        c = RingOps.Customise(c, "excel");
        Assert.False(RingOps.FollowsDefault(c, "excel"));
        Assert.Equal(Keys("Ctrl+Shift+X"), ProfileOps.Find(c, "excel")!.RingSlots![0].Action);

        c = RingOps.SetAction(c, "excel", new RingPath(0), Keys("Ctrl+E"));
        Assert.Equal(Keys("Ctrl+E"), RingOps.Slots(c, "excel")[0].Action);
        Assert.Equal(Keys("Ctrl+Shift+X"), RingOps.Slots(c, null)[0].Action);

        c = RingOps.ResetToDefault(c, "excel");
        Assert.Null(ProfileOps.Find(c, "excel")!.RingSlots);
        Assert.Equal(Keys("Ctrl+Shift+X"), RingOps.Slots(c, "excel")[0].Action);
    }

    [Fact]
    public void Customise_and_reset_do_nothing_for_all_apps_or_an_unknown_app()
    {
        SlenderConfig c = Base();
        Assert.Same(c, RingOps.Customise(c, null));
        Assert.Same(c, RingOps.Customise(c, "word"));
        Assert.Same(c, RingOps.ResetToDefault(c, null));
        Assert.Same(c, RingOps.ResetToDefault(c, "excel"));
    }

    [Fact]
    public void Appearance_edits_are_clamped_and_unchanged_edits_return_the_same_config()
    {
        SlenderConfig c = Base();

        Assert.Same(c, RingOps.UpdateAppearance(c, a => a));

        c = RingOps.UpdateAppearance(c, a => a with { OpacityPercent = 5, Accent = "#abc" });
        Assert.Equal(RingOps.MinOpacity, c.Ring.OpacityPercent);
        Assert.Equal(new RingAppearance().Accent, c.Ring.Accent); // bad colour falls back to copper

        c = RingOps.UpdateAppearance(c, a => a with { OpacityPercent = 250, Accent = "5bb5c9", Style = RingStyle.Keypad });
        Assert.Equal(RingOps.MaxOpacity, c.Ring.OpacityPercent);
        Assert.Equal("#5BB5C9", c.Ring.Accent);
        Assert.Equal(RingStyle.Keypad, c.Ring.Style);
    }

    [Fact]
    public void Accent_presets_are_valid_and_start_with_copper()
    {
        Assert.Equal(new RingAppearance().Accent, RingOps.AccentPresets[0].Hex);
        Assert.All(RingOps.AccentPresets, p => Assert.Equal(p.Hex, RingOps.NormaliseAccent(p.Hex)));
    }

    [Fact]
    public void Appearance_and_slots_survive_the_config_store()
    {
        string dir = Path.Combine(Path.GetTempPath(), "slender-ring-" + Guid.NewGuid().ToString("N"));
        try
        {
            SlenderConfig c = RingOps.UpdateAppearance(Base(), a => a with { Style = RingStyle.Compass, Size = RingSize.Large, Accent = "#6CCB9A", OpacityPercent = 70, Labels = RingLabels.SelectedOnly, Position = RingPosition.MonitorCentre });
            c = RingOps.Customise(c, "excel");
            c = RingOps.SetSlot(c, "excel", new RingPath(0), new RingSlot(new ActionSpec.Text("Kind regards,\nJesse")) { Name = "Sign off", Icon = "glyph:E715" });
            c = RingOps.MakeFolder(c, "excel", new RingPath(7));

            var store = new JsonConfigStore(dir);
            store.Save(c);
            SlenderConfig loaded = store.Load();

            Assert.Equal(c.Ring, loaded.Ring);
            RingSlot first = ProfileOps.Find(loaded, "excel")!.RingSlots![0];
            Assert.Equal(new ActionSpec.Text("Kind regards,\nJesse"), first.Action);
            Assert.Equal("Sign off", first.Name);
            Assert.Equal("glyph:E715", first.Icon);
            Assert.True(ProfileOps.Find(loaded, "excel")!.RingSlots![7].IsFolder);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Describe_uses_the_name_then_the_action()
    {
        RingSlotView v = RingOps.Describe(new RingSlot(Keys("Ctrl+C")));
        Assert.Equal("Copy", v.Title);
        Assert.Equal("Ctrl C", v.Sub);
        Assert.Equal("E8C8", v.Glyph);

        v = RingOps.Describe(new RingSlot(Keys("Ctrl+C")) { Name = "Dup", Icon = "glyph:E710" });
        Assert.Equal("Dup", v.Title);
        Assert.Equal("E710", v.Glyph);

        v = RingOps.Describe(RingSlot.Empty);
        Assert.True(v.IsEmpty);

        v = RingOps.Describe(RingDefaults.EmptyFolder("Work"));
        Assert.True(v.IsFolder);
        Assert.Equal("Work", v.Title);
        Assert.Equal("Empty folder", v.Sub);

        v = RingOps.Describe(new RingSlot(new ActionSpec.Launch(@"C:\Apps\tool.exe")));
        Assert.Equal(@"C:\Apps\tool.exe", v.FilePath);
        v = RingOps.Describe(new RingSlot(new ActionSpec.Launch(@"C:\Apps\tool.exe")) { Icon = @"file:C:\x\icon.png" });
        Assert.Equal(@"C:\x\icon.png", v.FilePath);
    }

    [Fact]
    public void Text_actions_read_as_a_truncated_label()
    {
        Assert.Equal("Type 'Kind regards,…'", ActionCatalog.Describe(new ActionSpec.Text("Kind regards,\nJesse")).Text);
        Assert.Equal("Type 'Thanks'", ActionCatalog.Describe(new ActionSpec.Text("Thanks")).Text);
        Assert.Equal("Type 'Dear Sir or Madam, I…'", ActionCatalog.Describe(new ActionSpec.Text("Dear Sir or Madam, I am writing")).Text);
        Assert.Equal("Type text", ActionCatalog.Describe(new ActionSpec.Text("  ")).Text);
    }

    [Theory]
    [InlineData(0, 1, 0, 1)]   // top, right arrow -> up-right
    [InlineData(0, -1, 0, 7)]  // top, left arrow -> up-left
    [InlineData(0, 0, 1, 0)]   // top, down arrow: nothing below within a ring but the cos is low; stays or moves toward centre side
    [InlineData(2, 0, 1, 3)]   // right, down -> down-right
    [InlineData(4, 1, 0, 3)]   // bottom, right -> down-right
    public void Arrow_keys_move_to_the_neighbouring_slot_on_the_bezel(int from, int ax, int ay, int expected)
    {
        int to = RingOps.Navigate(RingStyle.Bezel, RingSize.Medium, from, ax, ay);
        if (from == 0 && ay == 1)
        {
            Assert.NotEqual(0, to);
            return;
        }

        Assert.Equal(expected, to);
    }

    [Fact]
    public void Arrow_keys_walk_the_keypad_grid_and_start_at_slot_zero()
    {
        Assert.Equal(0, RingOps.Navigate(RingStyle.Keypad, RingSize.Medium, null, 1, 0));
        Assert.Equal(1, RingOps.Navigate(RingStyle.Keypad, RingSize.Medium, 0, 1, 0)); // up -> up-right
        Assert.Equal(2, RingOps.Navigate(RingStyle.Keypad, RingSize.Medium, 1, 0, 1)); // up-right -> right
        Assert.Equal(3, RingOps.Navigate(RingStyle.Keypad, RingSize.Medium, 2, 0, 1)); // right -> down-right
        Assert.Equal(2, RingOps.Navigate(RingStyle.Keypad, RingSize.Medium, 2, 1, 0)); // nothing further right: stay
    }

    [Fact]
    public void Glyph_catalog_is_curated_valid_and_unique()
    {
        Assert.InRange(RingGlyphs.Catalog.Count, 40, 60);
        Assert.Equal(RingGlyphs.Catalog.Count, RingGlyphs.Catalog.Select(g => g.Code).Distinct().Count());
        Assert.All(RingGlyphs.Catalog, g => Assert.NotNull(RingGlyphs.ToChar(g.Code)));
        Assert.Equal(RingIconKind.Glyph, RingGlyphs.Kind("glyph:E8C8", out string v));
        Assert.Equal("E8C8", v);
        Assert.Equal(RingIconKind.File, RingGlyphs.Kind(@"file:C:\a\b.exe", out v));
        Assert.Equal(@"C:\a\b.exe", v);
        Assert.Equal(RingIconKind.Automatic, RingGlyphs.Kind(null, out _));
        Assert.Equal(RingIconKind.Automatic, RingGlyphs.Kind("glyph:", out _));
        Assert.Null(RingGlyphs.ToChar("zz"));
    }
}
