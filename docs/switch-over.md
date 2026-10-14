# Switch-over runbook

Rule: Options+ and Slender must never both own HID++ diverts. Only one of them runs at a time.

All commands run in PowerShell 7 from the repo root. Both scripts report only unless `-Apply` is given; add `-WhatIf` to preview.

## A. Interim: repair Excel and trim Options+

### 1. Repair Excel (close Excel first; the script refuses if EXCEL.EXE runs)

```powershell
.\scripts\Repair-ExcelAddins.ps1                                   # report
.\scripts\Repair-ExcelAddins.ps1 -Apply -EnablePersonalXlsb -RemoveStaleLogi -WhatIf
.\scripts\Repair-ExcelAddins.ps1 -Apply -EnablePersonalXlsb -RemoveStaleLogi
```

- `-EnablePersonalXlsb`: removes the Resiliency\DisabledItems entry for PERSONAL.XLSB so Excel loads the personal macro workbook again.
- `-RemoveStaleLogi`: removes DisabledItems entries and Addins registrations for Logitech add-ins whose files no longer exist (the old `LogiExcelAddIn`).
- Each touched key is exported first to `%LOCALAPPDATA%\Slender\backups\Excel-<timestamp>\*.reg`.
- Rollback: `.\scripts\Repair-ExcelAddins.ps1 -Restore "<backup folder>"` (Excel closed).

### 2. Trim Options+ (reinstall with fewer features)

```powershell
.\scripts\Trim-OptionsPlus.ps1                       # report + exact installer command
.\scripts\Trim-OptionsPlus.ps1 -Apply -WhatIf        # plan
.\scripts\Trim-OptionsPlus.ps1 -Apply                # confirms each step; UAC prompt for the installer only
```

Default keeps `actions-ring` and `backlight`; everything else is passed as `No`. Override with `-Keep`, e.g. `-Keep actions-ring,backlight,smartactions`. `-InstallerPath <exe>` uses a local installer; `-Quiet` adds `/quiet`.

Flags (Yes keeps the feature): analytics (usage sharing), flow (cross-computer), sso (sign-in), update (app updates), dfu (firmware updates), backlight, logivoice, aipromptbuilder, device-recommendation, smartactions, actions-ring.

What `-Apply` does: download the installer into a new temp folder, require a Valid Authenticode signature with a Logitech signer, copy `%LOCALAPPDATA%\LogiOptionsPlus` to `%LOCALAPPDATA%\Slender\backups\OptionsPlus-<timestamp>` (old backups are never deleted), run `/uninstall`, restore the config if the uninstall removed it, run the install with every flag explicit. Transcript: `%LOCALAPPDATA%\Slender\logs`.

Not confirmed: whether the trim shrinks `LogiPluginService`, and whether it is needed for MX Master 4 haptics. Check the report's process list afterwards.

Rollback: re-run with all features, `-Keep analytics,flow,sso,update,dfu,backlight,logivoice,aipromptbuilder,device-recommendation,smartactions,actions-ring`. To recover settings, copy the backup folder back over `%LOCALAPPDATA%\LogiOptionsPlus` with Options+ closed.

## B. Later: switch from Options+ to Slender

1. Quit Options+ from its tray icon.
2. Stop and disable the agent and plugin service startup. Elevated PowerShell; service names vary, so list first:
   ```powershell
   Get-Service | Where-Object Name -match 'logi'
   Stop-Service <name>; Set-Service <name> -StartupType Disabled
   ```
   If they are not services, remove the Run/startup entries (Task Manager > Startup apps) for `logioptionsplus_agent`, `logioptionsplus_updater` and `LogiPluginService`, then end the processes. Confirm none remain: `.\scripts\Trim-OptionsPlus.ps1` lists running Logi processes.
3. Close Excel, then disable the Logitech add-ins (Slender replaces their function):
   ```powershell
   .\scripts\Repair-ExcelAddins.ps1 -Apply -DisableLogiAddins
   ```
4. Start Slender.

## C. Full rollback to Options+

1. Quit Slender.
2. Close Excel, then `.\scripts\Repair-ExcelAddins.ps1 -Restore "%LOCALAPPDATA%\Slender\backups\Excel-<timestamp>"` (use the backup taken before step B.3).
3. Re-enable the services/startup entries disabled in B.2 (`Set-Service <name> -StartupType Automatic; Start-Service <name>`).
4. If Options+ is missing or broken, reinstall untrimmed: `.\scripts\Trim-OptionsPlus.ps1 -Apply -Keep analytics,flow,sso,update,dfu,backlight,logivoice,aipromptbuilder,device-recommendation,smartactions,actions-ring`.
5. Start Options+. Do not start Slender again until step B is repeated.
