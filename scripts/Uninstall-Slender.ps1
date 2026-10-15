<#
.SYNOPSIS
  Remove Slender installed by Install-Slender.ps1.
.DESCRIPTION
  Default is a plan only. -Apply removes the Start menu shortcut, the HKCU Run value "Slender" and the
  install folder. %APPDATA%\Slender (config) is kept unless -RemoveConfig; %LOCALAPPDATA%\Slender
  (logs, backups, status.json) is kept unless -RemoveConfig too. A running Slender.Tray is stopped
  only with -StopRunning.
  Logi Options+ is never touched.
.PARAMETER Apply
  Actually remove things.
.PARAMETER RemoveConfig
  Also delete %APPDATA%\Slender and %LOCALAPPDATA%\Slender.
.PARAMETER StopRunning
  Stop a running Slender.Tray / Slender.Settings first.
.PARAMETER InstallDir
  Override the install folder (default %LOCALAPPDATA%\Programs\Slender).
#>
#Requires -Version 7
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Apply,
    [switch]$RemoveConfig,
    [switch]$StopRunning,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Slender')
)
$ErrorActionPreference = 'Stop'

$Shortcut  = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Slender.lnk'
$RunKey    = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$RunValue  = 'Slender'
$ConfigDir = Join-Path $env:APPDATA 'Slender'
$LocalDir  = Join-Path $env:LOCALAPPDATA 'Slender'

Write-Host "Plan:"
Write-Host "  1. Stop running Slender.Tray/Settings: $(if ($StopRunning) { 'yes' } else { 'no (refuse if running)' })"
Write-Host "  2. Remove Start menu shortcut $Shortcut"
Write-Host "  3. Remove HKCU Run value '$RunValue'"
Write-Host "  4. Remove install folder $InstallDir"
Write-Host "  5. Config ($ConfigDir) and data ($LocalDir): $(if ($RemoveConfig) { 'REMOVE' } else { 'keep' })"
Write-Host "  Logi Options+ is never touched."

if (-not $Apply) {
    Write-Host "Plan only. Re-run with -Apply to uninstall."
    return
}

# Only Slender processes that run from the install folder lock its files; copies elsewhere (a dev build) are left alone.
function Get-InstalledSlenderProcess {
    $prefix = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\') + '\'
    Get-Process -Name 'Slender.Tray', 'Slender.Settings' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) }
}

$running = @(Get-InstalledSlenderProcess)
if ($running.Count -gt 0 -and -not $StopRunning) {
    throw ("Slender is running ({0}). Quit it, or pass -StopRunning." -f (($running.Name -join ', ')))
}
if ($running.Count -gt 0 -and $PSCmdlet.ShouldProcess(($running.Name -join ', '), 'Stop running Slender')) {
    foreach ($p in $running) {
        $p.CloseMainWindow() | Out-Null
        if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id -Force }
    }
}

if ((Test-Path -LiteralPath $Shortcut) -and $PSCmdlet.ShouldProcess($Shortcut, 'Remove shortcut')) {
    Remove-Item -LiteralPath $Shortcut -Force
}

if ((Get-ItemProperty -Path $RunKey -Name $RunValue -ErrorAction SilentlyContinue) -and $PSCmdlet.ShouldProcess("$RunKey\$RunValue", 'Remove launch at login')) {
    Remove-ItemProperty -Path $RunKey -Name $RunValue
}

if ((Test-Path -LiteralPath $InstallDir) -and $PSCmdlet.ShouldProcess($InstallDir, 'Remove install folder')) {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
}

if ($RemoveConfig) {
    foreach ($d in $ConfigDir, $LocalDir) {
        if ((Test-Path -LiteralPath $d) -and $PSCmdlet.ShouldProcess($d, 'Remove config/data')) {
            Remove-Item -LiteralPath $d -Recurse -Force
        }
    }
}

if (-not $WhatIfPreference) { Write-Host "Uninstalled." }
