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

## Assets: nothing from Logitech is committed

On first run (and whenever a file is missing) `AssetCache` copies from the local Options+ cache, `C:\ProgramData\LogiOptionsPlus\depots\<n>\<folder>\manifest.json`, the newest folder whose `devices[].modelId` matches:

| Device | Model | Render | Hotspots |
|---|---|---|---|
| MX Master 4 | `2b042` | `device_buttons_image` (side_core.webp), thumbnail from `device_image` | `core_metadata.json`, image key `device_buttons_image` |
| ERGO K860 | `6b359` | `device_keys_image` (front.webp) | `metadata.json`, image key `device_keys_image` |

They land in `%LOCALAPPDATA%\Slender\assets\<device-id>\` as `render.*`, `thumb.*` and a normalized `layout.json`. "Refresh pictures" on the General page re-copies them. If Options+ is not installed, the stage shows a vector silhouette and a plain list of controls.

Slot to control id: `SLOT_NAME_MIDDLE_BUTTON` 0x0052, `MODESHIFT` 0x00C4, `FORWARD` 0x0056, `BACK` 0x0053, `GESTURE` 0x00C3, `ASSIGNMENT_NAME_SHOW_RADIAL_MENU` 0x01A0, `THUMBWHEEL` pseudo id 0xF150. K860 slots end in `_c<decimal CID>`. Hotspot percentages are relative to the metadata "origin" size, not the image file: the mouse picture is centred in its box; the keyboard markers are the top-left of a key (`HotspotMapper`).

App icons are extracted at 256 px (`IShellItemImageFactory`) from `Profile.ExePath` or the running process, or, for Store apps, the largest package logo, and cached as PNG in `assets\icons`.

Fonts (SIL OFL, licences beside them in `Assets/Fonts`): Archivo Expanded ExtraBold, Sora SemiBold, JetBrains Mono Medium. They are static instances cut from the Google Fonts variable files.

## Notes

- Keyboard "Media" picker options store chords such as `MediaPlayPause` and `VolumeUp`; the engine's `ChordParser` does not know these names yet (keyboard support is a later milestone).
- Easy-Switch host chips are informational: the status contract has no active-host field.
- Memory: the window decodes images at display size and keeps no render cache; closing it ends the process.
