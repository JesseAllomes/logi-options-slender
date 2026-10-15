# Install

Slender installs per user, no admin rights. PowerShell 7 and the .NET 10 SDK (to build) are required; the tray itself needs the .NET 10 desktop runtime.

## Install

From the repo root:

```powershell
.\scripts\Install-Slender.ps1                 # plan only
.\scripts\Install-Slender.ps1 -Apply -WhatIf  # preview each step
.\scripts\Install-Slender.ps1 -Apply -LaunchAtLogin
```

`-Apply` publishes the tray (framework-dependent, win-x64, Release) and the settings app (self-contained) into one folder, `%LOCALAPPDATA%\Programs\Slender`, so `Slender.Tray.exe` and `Slender.Settings.exe` sit side by side. It adds a Start menu shortcut "Slender", optionally the launch-at-login entry (`-LaunchAtLogin`, the same `Slender` Run value the tray menu toggles), then starts the tray (`-NoStart` to skip).

If Slender is already running from the install folder the script refuses. Pass `-RestartRunning` to let it stop Slender.Tray/Slender.Settings first. It never stops Logi Options+.

## Options+ guard

If Options+ is running, the tray still starts but the mouse and keyboard engines wait. The menu shows "Paused - Logi Options+ is running" and `status.json` has `OptionsPlusRunning=true`. The tray checks every 30 seconds and starts the engines once Options+ is gone. To switch over properly see `docs/switch-over.md` section B.

## Update

Pull or edit the source, then re-run the installer with `-Apply -RestartRunning`. Config is untouched.

## Uninstall

```powershell
.\scripts\Uninstall-Slender.ps1 -Apply -StopRunning
.\scripts\Uninstall-Slender.ps1 -Apply -StopRunning -RemoveConfig   # also deletes config and data
```

Removes the shortcut, the Run value and the install folder. Config is kept unless `-RemoveConfig`.

## Where things live

| What | Where |
| --- | --- |
| Program files | `%LOCALAPPDATA%\Programs\Slender` |
| Start menu shortcut | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Slender.lnk` |
| Launch at login | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Slender` |
| Config | `%APPDATA%\Slender\config.json` |
| Status, logs, backups | `%LOCALAPPDATA%\Slender` (`status.json`, `logs`, `backups`) |
