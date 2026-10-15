# Install

Slender installs per user, no admin rights, from one file: `SlenderSetup.exe`.

## Install

1. Build it once (needs the .NET 10 SDK and PowerShell 7): `.\scripts\Build-Setup.ps1` produces `artifacts\SlenderSetup.exe` (`-Version 1.0.1` sets the version).
2. Run `SlenderSetup.exe`. It installs to `%LOCALAPPDATA%\SlenderApp`, adds a Start menu shortcut "Slender", installs the .NET 10 desktop runtime if it is missing, and starts the tray. No admin prompt.
3. Optional: turn on **Launch at login** in Settings, General (or the tray menu). It is the same `Slender` Run value either way, and uninstalling removes it.

Setup keeps its own folder, so it never touches `%APPDATA%\Slender` (config) or `%LOCALAPPDATA%\Slender` (status, logs, sync state).

## Options+ guard

If Options+ is running, the tray still starts but the mouse and keyboard engines wait. The menu shows "Paused - Logi Options+ is running" and `status.json` has `OptionsPlusRunning=true`. The tray checks every 30 seconds and starts the engines once Options+ is gone. To switch over properly see `docs/switch-over.md` section B.

## Update

Run a newer `SlenderSetup.exe` over the top, or let Slender update itself: set `"updateSource"` in `slender.sync.json` to a folder holding the contents of `artifacts\releases` (or a GitHub repository URL). Updates download in the background and install the next time Slender exits. With no `updateSource` nothing is checked. Config is untouched. See `docs/sync.md`.

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
