## r53
TICKET r53  (severity low)
FILES:     tests/Slender.Tests/Tray/RingTrayTests.cs lines 104-111 (BundledFontsLoadWhenPresent); tests/Slender.Tests/Slender.Tests.csproj (one ItemGroup)
DEFECT:    The test returns green when Assets/Fonts/Sora-SemiBold.ttf is missing from the test output, and the test project never copies the fonts, so it always passes without checking that the bundled fonts load.
CHANGE:    1. Slender.Tests.csproj: add an ItemGroup copying the same two fonts the tray ships (see src/Slender.Tray/Slender.Tray.csproj lines 26-30): `<None Include="..\..\src\Slender.Settings\Assets\Fonts\Sora-SemiBold.ttf" Link="Assets\Fonts\Sora-SemiBold.ttf" CopyToOutputDirectory="PreserveNewest" />` and the same for JetBrainsMono-Medium.ttf (check the relative path from tests/Slender.Tests resolves).
           2. Test: replace the early return with `Assert.True(File.Exists(Path.Combine(fonts, "Sora-SemiBold.ttf")), "bundled fonts were not copied next to the tests");` and rename the test to BundledFontsLoad.
TEST:      the changed fact; it now fails if the fonts are not copied.
DO NOT:    touch src files or other tests.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the files untouched, return "BLOCKED r53: <reason>"

## r54
TICKET r54  (severity medium)  (also covers r55: same test, same lines)
FILES:     tests/Slender.Tests/Config/JsonConfigStoreTests.cs lines 158-164 (Config_without_battery_alerts_loads_defaults)
DEFECT:    The fixture has no "profiles" key, so JsonConfigStore rejects the file (FormatException), quarantines it and returns SlenderConfig.Default; the assertion passes without ever loading a config that merely lacks batteryAlerts. It also uses launchAtLogin=false (the default), so it cannot show other settings survive.
CHANGE:    1. Build the fixture from a real saved config instead of a hand-written stub: `Store().Save(SlenderConfig.Default with { LaunchAtLogin = true });` then read config.json, remove the "batteryAlerts" property (parse with System.Text.Json.Nodes.JsonNode, `obj.Remove("batteryAlerts")`, check the property name the store writes by reading the saved file), write it back.
           2. Load and assert: BatteryAlerts equals new BatteryAlertSettings(); LaunchAtLogin is true (the file was accepted, not replaced by Default); and no "config.json.bad-*" file exists in _dir.
TEST:      the rewritten fact; it must fail if the loader rejects a config without batteryAlerts (e.g. temporarily make ToBatteryAlerts throw on null: the LaunchAtLogin and no-.bad assertions then fail). Do not leave that experiment in place.
DO NOT:    touch src files or other tests.
DONE WHEN: the fact passes and the suite passes.
IF BLOCKED: stop, leave the file untouched, return "BLOCKED r54: <reason>"

## r55
TICKET r55  (severity low)
FILES:     none beyond ticket r54
DEFECT:    Same test as r54 (launchAtLogin default in the fixture).
CHANGE:    1. No edit of its own: r54 sets LaunchAtLogin = true and asserts it. Apply r54 first, then report APPLIED r55 (covered by r54).
TEST:      r54's fact.
DO NOT:    make any other change.
DONE WHEN: r54 is applied.
IF BLOCKED: return "BLOCKED r55: <reason>"

