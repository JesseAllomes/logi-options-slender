#Requires -Version 7
<#
.SYNOPSIS
  Switches Logi Options+ fully off (it never loads) or on (only to update device firmware).

.DESCRIPTION
  -Off: stops and disables the "Logi Options+" updater service (it starts the agent and the plugin service), disables the
  "Logi Download Assistant" startup entry (the same switch Task Manager's Startup apps uses), and ends every Options+ and
  Logi Plugin Service process. -On: sets the service to Manual, starts it and opens Options+ for a firmware update; run -Off
  again afterwards. Both write the previous state to %LOCALAPPDATA%\Slender\backups first. Needs an elevated prompt (the
  service and the machine-wide startup entry); without one it re-launches itself elevated (one UAC prompt).

.EXAMPLE
  .\scripts\Set-OptionsPlus.ps1 -Off
  .\scripts\Set-OptionsPlus.ps1 -On     # update firmware in Options+, then:
  .\scripts\Set-OptionsPlus.ps1 -Off
#>
param(
    [Parameter(ParameterSetName = 'off', Mandatory)][switch]$Off,
    [Parameter(ParameterSetName = 'on', Mandatory)][switch]$On,
    [switch]$Wait
)

$ErrorActionPreference = 'Stop'
$service = 'OptionsPlusUpdaterService'
$startupKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$startupName = 'Logi Download Assistant'
$app = 'C:\Program Files\LogiOptionsPlus\logioptionsplus.exe'
$processes = 'logioptionsplus', 'logioptionsplus_agent', 'logioptionsplus_appbroker', 'logioptionsplus_updater',
    'LogiPluginService', 'LogiPluginServiceExt', 'logi_crashpad_handler', 'logi_download_assistant'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    $arguments = @('-NoProfile', '-File', $PSCommandPath, $(if ($Off) { '-Off' } else { '-On' }))
    $p = Start-Process pwsh -Verb RunAs -ArgumentList $arguments -PassThru -Wait
    exit $p.ExitCode
}

# The state before this run, so it can be put back by hand.
$backups = Join-Path $env:LOCALAPPDATA 'Slender\backups'
New-Item -ItemType Directory -Force $backups | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$svc = Get-Service $service -ErrorAction SilentlyContinue
$approved = (Get-ItemProperty $startupKey -Name $startupName -ErrorAction SilentlyContinue).$startupName
@(
    "service $service status=$($svc.Status) start=$($svc.StartType)"
    "startup '$startupName' StartupApproved=$(if ($approved) { ($approved | ForEach-Object { $_.ToString('X2') }) -join ' ' } else { 'absent' })"
) | Set-Content (Join-Path $backups "optionsplus-state-$stamp.txt")

if ($Off) {
    if ($svc) {
        Stop-Service $service -Force -ErrorAction SilentlyContinue
        Set-Service $service -StartupType Disabled
    }
    # 03 = disabled in StartupApproved (02 = enabled); the Run entry itself stays, so -On or Task Manager can turn it back on.
    if (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name $startupName -ErrorAction SilentlyContinue) {
        New-Item -Force $startupKey | Out-Null
        Set-ItemProperty $startupKey -Name $startupName -Type Binary -Value ([byte[]](3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0))
    }
    foreach ($name in $processes) { Get-Process $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    $left = Get-Process | Where-Object { $processes -contains $_.ProcessName }
    "Options+ is off: service $((Get-Service $service -ErrorAction SilentlyContinue).StartType), startup entry disabled, $(@($left).Count) Options+ processes left."
}
else {
    if ($svc) {
        Set-Service $service -StartupType Manual
        Start-Service $service
    }
    if (Test-Path $app) { Start-Process $app }
    "Options+ is on for a firmware update. Run Set-OptionsPlus.ps1 -Off when it is done."
}
if ($Wait) { Read-Host 'Press Enter to close' | Out-Null }
