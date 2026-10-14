# Baseline — Logi Options+ on this PC (2026-10-15)

## Installed

| Product | Version |
|---|---|
| Logi Options+ | 2.8.981479 |
| Logi Plugin Service | 6.4.2.3414 |
| Logi Download Assistant | 2.0.1020 |

Device: MX Master 4, Bluetooth LE (no receiver in use).

## Memory (working set, idle)

| Process | MB |
|---|---|
| LogiPluginService | 342 |
| logioptionsplus_agent | 262 |
| logioptionsplus_updater | 42 |
| LogiPluginServiceExt | 21 |
| logioptionsplus_appbroker | 17 |
| logi_crashpad_handler (x2) | 19 |
| **Total** | **~700** |

`LogiPluginService` is the Loupedeck-based plugin host. Installed plugins: BetterClick,
HapticMouseHover, LogiSmartActions, MicrosoftTeams, PowerPoint, Spotify, Word.

## Excel

Two Logitech VSTO add-ins are registered and load on every Excel start:

| Add-in | Source | Load time (last 5 starts, ms) | State |
|---|---|---|---|
| LogiOptionsPlusExcelAddin | `C:\ProgramData\Logishrd\LogiOptionsPlus\Plugins\Excel\*.vsto` (HKCU Office\Excel\Addins, LoadBehavior 2) | 312, 297, 297, 250, 375 | loading |
| LogiExcelAddIn | `%LOCALAPPDATA%\Logi\LogiPluginService\PluginData\Excel\ExcelVsto\LogiExcelAddin.dll` | 375, 984, 516, 797, 360 | **hard-disabled** by Excel (Resiliency\DisabledItems type 1 = crashed while loading); the DLL path no longer exists |

The Options+ add-in ships websocket-sharp, Newtonsoft.Json and nunit.framework: it talks to the
Options+ agent over a local websocket. That means it only works while Options+ runs, and a
replacement app cannot reuse it.

Also in `Resiliency\DisabledItems`: `%APPDATA%\Microsoft\Excel\XLSTART\PERSONAL.XLSB`
(type 1). Excel stopped loading the personal macro workbook after a crash.

Excel faults (Application log, last 30 days): 2026-10-05 ×3 (0xc0000005 / 0xc0000409),
2026-10-12 (0xc0000409 in Windows.FileExplorer.Common.dll), 2026-10-14 (0xc0000005, module
unknown), 2026-09-15 hang. The logs do not attribute any of these to a Logitech module.
UNCONFIRMED whether the add-ins caused them.

## Official installer feature flags

From `logi-options-plus-mini.ps1` (Apache-2.0), which mirrors Logitech's mass-deployment guide.
Installer: `https://download01.logi.com/web/ftp/pub/techsupport/optionsplus/logioptionsplus_installer.exe`.

```
logioptionsplus_installer.exe [/quiet] /analytics Yes|No /flow Yes|No /sso Yes|No /update Yes|No
  /dfu Yes|No /backlight Yes|No /logivoice Yes|No /aipromptbuilder Yes|No
  /device-recommendation Yes|No /smartactions Yes|No /actions-ring Yes|No
logioptionsplus_installer.exe /uninstall
```

Config lives in `%LOCALAPPDATA%\LogiOptionsPlus`; upstream backs it up before uninstalling and
restores it afterwards. Upstream skips checking the downloaded installer's signature.
Slender's script must verify the Authenticode signer is Logitech before running it.

UNCONFIRMED: whether any flag removes or slims `LogiPluginService`, and whether it is needed
for MX Master 4 haptics or the Actions Ring under Options+.

### Trim profile for the interim period (until Slender replaces Options+)

Keep: `actions-ring` (the user uses it), `backlight` (harmless).
Drop: analytics, flow, sso, update, dfu, logivoice, aipromptbuilder, device-recommendation,
smartactions.
