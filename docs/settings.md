# Settings app

`Slender.Settings.exe` is a separate WinUI 3 window (unpackaged, self-contained Windows App SDK, dark only, Mica). The tray starts it on demand from its own folder; it exits when closed. The design is locked in `.grok/product-ui/` (theme, design, layout).

## How the pieces fit

```
Slender.Settings.exe ──writes──▶ %APPDATA%\Slender\config.json ◀──watches── Slender.Tray.exe ──▶ engine.ApplyConfig
Slender.Settings.exe ◀──watches── %LOCALAPPDATA%\Slender\status.json ◀──writes── Slender.Tray.exe
```

- **config.json** (roaming, schema version 2). Settings saves through `JsonConfigStore` 300 ms after the last edit (atomic temp file and move). The tray watches the file (`ConfigFileWatcher`, 200 ms settle, content compared so its own saves are ignored), reloads it and raises `ConfigChanged`, which `Program.cs` already feeds to `engine.ApplyConfig`. The same watcher runs in Settings, so the tray menu toggles (Excel, Haptics, Launch at login) show up in an open window. Version 1 files load with defaults for the new fields (`Pointer`, `Keyboard`, `KeyBindings`, `DisplayName`, `ExePath`) and are saved as version 2.
- **status.json** (local). The tray's `StatusPublisher` writes `SlenderStatus` (mouse connection and battery, keyboard entry, foreground app, Options+ running) atomically, at most once per 500 ms, only after a change: connection, battery (every 5 minutes while connected), foreground app, or opening Settings. There is no timer while nothing changes. Options+ is checked at most every 30 s, only when a write happens. Settings also checks Options+ and the tray mutex itself every 30 s while open. If status.json is missing or the tray is not running, Settings shows "Slender isn't running" and keeps working on config.json.
- **Launching.** Tray menu "Settings…" runs `SettingsLauncher`: if the named event `Local\Slender.Settings.Show` exists, it is signalled and the open window comes to the front; otherwise `Slender.Settings.exe` next to the tray is started (in a dev tree, the Settings project output is found as a fallback). Settings is single-instance through the mutex `Local\Slender.Settings`. Ship both apps in one folder.
- **Window state** (size, last device, last app) is kept in `%LOCALAPPDATA%\Slender\settings-ui.json`, not in config.json.

## Assets: the device pictures ship with Slender

The pictures are in `src/Slender.Settings/Assets/Devices` and copy beside the app: `mx-master-4.webp`, `ergo-k860.webp`, `wh64-headset.webp` and `wh64-base.webp`. `BuiltInPictures` declares each picture's pixel size and its hotspots. Nothing is copied from Logi Options+ or Yealink USB Connect, and there is no picture cache or refresh.

Hotspot X and Y are plain percentages of the picture file. A test checks that each declared size matches the file.

Slot to control id (Options+ import): `SLOT_NAME_MIDDLE_BUTTON` 0x0052, `MODESHIFT` 0x00C4, `FORWARD` 0x0056, `BACK` 0x0053, `GESTURE` 0x00C3, `ASSIGNMENT_NAME_SHOW_RADIAL_MENU` 0x01A0, `THUMBWHEEL` pseudo id 0xF150. K860 slots end in `_c<decimal CID>`.

App icons are extracted at 256 px (`IShellItemImageFactory`) from `Profile.ExePath` or the running process, or, for Store apps, the largest package logo, and cached as PNG in `assets\icons`.

Fonts (SIL OFL, licences beside them in `Assets/Fonts`): Archivo Expanded ExtraBold, Sora SemiBold, JetBrains Mono Medium. They are static instances cut from the Google Fonts variable files.

## Notes

- Keyboard "Media" picker options store chords such as `MediaPlayPause` and `VolumeUp`; the engine's `ChordParser` does not know these names yet (keyboard support is a later milestone).
- Easy-Switch host chips are informational: the status contract has no active-host field.
- Memory: the window decodes images at display size and keeps no render cache; closing it ends the process.

## Point & scroll, labels, thumb wheel, test hooks

- Point & scroll page: SmartShift on/off and Sensitivity (1-50, `PointerMapping`), Smooth scrolling, Invert thumb wheel, Invert main wheel, Pointer speed (400-8000 DPI in steps of 50; "Default" writes `Pointer.Dpi = null`). All go through `ConfigService.Update`.
- Labels show key-cap chips after the action text for Keys actions and for Excel commands that have a plain shortcut (Next/Previous sheet = Ctrl+PgDn/PgUp).
- Thumb wheel picker has a "Two-way" tab. A two-direction action is stored as `Right|Left` in a Keys chord (for example `VolumeUp|VolumeDown`) and labelled "Volume up / down". Helpers: `ActionCatalog.EncodePair`, `TryDecodePair`, `PairName`.
- Store app icons: `StoreLogo` reads `AppxManifest.xml` for the logo base name and takes the largest plain variant (Teams uses `TeamsForWorkNewAppList.*`, not `Square44x44Logo`).
- Minimum window size 1040x640. Compare apps uses its own right-aligned "Done" button instead of the dialog's stretched close row.
- Test hooks (ignored unless set): `SLENDER_CONFIG_DIR` redirects config.json, settings-ui.json and the single-instance names to another folder; with it, `SLENDER_PAGE` (device, pointer, haptics, general) picks the start page and `SLENDER_SHOT=<png>` renders the window's XAML tree to a file and exits.
