# Install

Slender installs per user, no admin rights, from one file: `SlenderSetup.exe`.

## Install

1. Build it once (needs the .NET 10 SDK and PowerShell 7): `.\scripts\Build-Setup.ps1` produces `artifacts\SlenderSetup.exe` (the version is the next patch above the newest in `artifacts\releases`, 1.0.0 the first time; `-Version 1.2.0` overrides it).
2. Run `SlenderSetup.exe`. It installs to `%LOCALAPPDATA%\SlenderApp`, adds a Start menu shortcut "Slender", installs the .NET 10 desktop runtime if it is missing, and starts the tray. No admin prompt.
3. Optional: turn on **Launch at login** in Settings, General (or the tray menu). It is the same `Slender` Run value either way, and uninstalling removes it.

Setup keeps its own folder, so it never touches `%APPDATA%\Slender` (config) or `%LOCALAPPDATA%\Slender` (status, logs, sync state).

## Options+ guard

If Options+ is running, the tray still starts but the mouse and keyboard engines wait. The menu shows "Paused - Logi Options+ is running" and `status.json` has `OptionsPlusRunning=true`. The tray checks every 30 seconds and starts the engines once Options+ is gone. To switch over properly see `docs/switch-over.md` section B.

## Update

Slender updates itself from the `artifacts\releases` folder of the repo it was built from: `scripts\Build-Setup.ps1` bakes that path into the tray as the default update source.

**To ship an update, run `.\scripts\Build-Setup.ps1`.** With no `-Version` it picks the next patch above the newest one in `artifacts\releases` and prints it. Nothing else is needed.

- The installed Slender checks 30 seconds after it starts, then every 6 hours, and when you choose **Check for updates** in the tray menu or **Check now** in Settings, General (the last row of the page shows `Version 1.1.0 · up to date`, or `1.1.1 ready`).
- A new version downloads in the background, then a balloon says "Slender 1.1.1 is ready. Click to restart and update." and the tray menu gets **Restart to update (1.1.1)** at the top (Settings, General has **Restart and update**). Either one closes Settings if it is open (it asks the window to close and waits 5 seconds; if it stays open you are told to close it) and restarts into the new version.
- If you ignore it, the update installs silently the next time Slender exits.
- A source that cannot be reached is logged once and otherwise ignored; only **Check for updates** tells you ("Couldn't reach the update folder").
- A `"updateSource"` in `slender.sync.json` overrides the baked-in folder (a folder, UNC share or GitHub repository URL). A dev build without the baked-in path has no source and checks nothing.
- Builds made before this feature have no baked-in source: install one new `SlenderSetup.exe` over them once, and later versions arrive by themselves. Config is untouched. See `docs/sync.md`.

Running a newer `SlenderSetup.exe` over the top still works too.

## Uninstall

Windows Settings, Apps, Installed apps, Slender, Uninstall. Config and data are kept; delete `%APPDATA%\Slender` and `%LOCALAPPDATA%\Slender` by hand to remove them too.

## Sign-in and settings sync

Optional. Sign in with a personal Microsoft account in Settings, General, Sync to keep `config.json` the same on every PC. One-time setup: `docs/sync.md`.

## Fallback: PowerShell scripts

The older scripts still work where the installer cannot be used. They install to a different folder (`%LOCALAPPDATA%\Programs\Slender`) with no self-update, so do not use both on one PC.

```powershell
.\scripts\Install-Slender.ps1                 # plan only
.\scripts\Install-Slender.ps1 -Apply -LaunchAtLogin
.\scripts\Install-Slender.ps1 -Apply -RestartRunning   # update: stops a running copy first
.\scripts\Uninstall-Slender.ps1 -Apply -StopRunning    # add -RemoveConfig to also delete config and data
```

## Where things live

| What | Where |
| --- | --- |
| Program files | `%LOCALAPPDATA%\SlenderApp` (installer), `%LOCALAPPDATA%\Programs\Slender` (scripts) |
| Start menu shortcut | Start menu, "Slender" |
| Launch at login | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Slender` |
| Config | `%APPDATA%\Slender\config.json` |
| Status, logs, backups, sync state | `%LOCALAPPDATA%\Slender` (`status.json`, `logs`, `backups`, `sync.json`) |
| Sign-in setup (client ID) | `%LOCALAPPDATA%\Slender\slender.sync.json` |
