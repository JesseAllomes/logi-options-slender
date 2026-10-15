<#
.SYNOPSIS
  Build and install Slender (tray + settings) into %LOCALAPPDATA%\Programs\Slender.
.DESCRIPTION
  Default is a plan only. -Apply publishes the tray (framework-dependent, win-x64, Release) and the
  settings app (its own self-contained settings) into one folder, creates a Start menu shortcut
  "Slender", optionally registers launch at login, and starts the tray. Re-run to update.
  Slender never stops Logi Options+. A running Slender.Tray is stopped only with -RestartRunning.
  Without -RestartRunning, a running Slender.Tray or Slender.Settings makes -Apply refuse before
  anything is copied.
.PARAMETER Apply
  Actually build and install. Without it, print the plan only.
.PARAMETER LaunchAtLogin
  Also add the HKCU Run value "Slender" (same value the tray's own menu item uses).
.PARAMETER RestartRunning
  Stop a running Slender.Tray (and Slender.Settings) so the files can be replaced, then start the tray.
.PARAMETER InstallDir
  Override the install folder (default %LOCALAPPDATA%\Programs\Slender).
.PARAMETER NoStart
  Do not start the tray at the end.
#>
#Requires -Version 7
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Apply,
    [switch]$LaunchAtLogin,
    [switch]$RestartRunning,
    [switch]$NoStart,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Slender')
)
$ErrorActionPreference = 'Stop'

$RepoRoot    = Split-Path -Parent $PSScriptRoot
$TrayProj    = Join-Path $RepoRoot 'src\Slender.Tray\Slender.Tray.csproj'
$SettingsProj = Join-Path $RepoRoot 'src\Slender.Settings\Slender.Settings.csproj'
$TrayExe     = Join-Path $InstallDir 'Slender.Tray.exe'
$Shortcut    = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Slender.lnk'
$RunKey      = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$RunValue    = 'Slender'   # must match StartupRegistration.ValueName
$Staging     = Join-Path ([IO.Path]::GetTempPath()) ("slender-publish-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))

Write-Host "Plan:"
Write-Host "  1. dotnet publish tray     (Release, win-x64, framework-dependent) -> staging"
Write-Host "  2. dotnet publish settings (Release, win-x64, self-contained)      -> staging"
Write-Host "  3. copy staging -> $InstallDir"
Write-Host "  4. Start menu shortcut     -> $Shortcut"
Write-Host ("  5. Launch at login (HKCU Run '{0}'): {1}" -f $RunValue, $(if ($LaunchAtLogin) { 'yes' } else { 'no (unchanged)' }))
Write-Host ("  6. Running Slender.Tray: {0}; start tray afterwards: {1}" -f $(if ($RestartRunning) { 'stop it' } else { 'refuse if the installed copy runs' }), $(if ($NoStart) { 'no' } else { 'yes' }))
Write-Host "  Logi Options+ is never touched."

if (-not $Apply) {
    Write-Host "Plan only. Re-run with -Apply to install."
    return
}

foreach ($p in $TrayProj, $SettingsProj) {
    if (-not (Test-Path -LiteralPath $p)) { throw "Missing project: $p" }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet SDK not found on PATH." }

# Only Slender processes that run from the install folder lock its files; copies elsewhere (a dev build) are left alone.
function Get-InstalledSlenderProcess {
    $prefix = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\') + '\'
    Get-Process -Name 'Slender.Tray', 'Slender.Settings' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) }
}

$running = @(Get-InstalledSlenderProcess)
if ($running.Count -gt 0 -and -not $RestartRunning) {
    throw ("Slender is running ({0}). Quit it, or pass -RestartRunning to let this script stop it." -f (($running.Name -join ', ')))
}

if ($PSCmdlet.ShouldProcess($Staging, 'dotnet publish tray + settings')) {
    New-Item -ItemType Directory -Force -Path $Staging | Out-Null
    & dotnet publish $SettingsProj -c Release -r win-x64 --self-contained true -o $Staging --nologo
    if ($LASTEXITCODE -ne 0) { throw "Settings publish failed ($LASTEXITCODE)." }
    & dotnet publish $TrayProj -c Release -r win-x64 --self-contained false -o $Staging --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tray publish failed ($LASTEXITCODE)." }
    foreach ($exe in 'Slender.Tray.exe', 'Slender.Settings.exe') {
        if (-not (Test-Path -LiteralPath (Join-Path $Staging $exe))) { throw "Publish did not produce $exe." }
    }
}

if ($RestartRunning -and $running.Count -gt 0 -and $PSCmdlet.ShouldProcess(($running.Name -join ', '), 'Stop running Slender')) {
    foreach ($p in $running) {
        $p.CloseMainWindow() | Out-Null
        if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id -Force }
    }
}

if ($PSCmdlet.ShouldProcess($InstallDir, 'Copy published files')) {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Copy-Item -Path (Join-Path $Staging '*') -Destination $InstallDir -Recurse -Force
    Remove-Item -LiteralPath $Staging -Recurse -Force
}

if ($PSCmdlet.ShouldProcess($Shortcut, 'Create Start menu shortcut')) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Shortcut)
    $link.TargetPath = $TrayExe
    $link.WorkingDirectory = $InstallDir
    $link.Description = 'Slender'
    $link.Save()
}

if ($LaunchAtLogin -and $PSCmdlet.ShouldProcess("$RunKey\$RunValue", 'Enable launch at login')) {
    New-Item -Path $RunKey -Force | Out-Null
    Set-ItemProperty -Path $RunKey -Name $RunValue -Value ('"{0}"' -f $TrayExe) -Type String
}

if (-not $NoStart -and $PSCmdlet.ShouldProcess($TrayExe, 'Start Slender tray')) {
    Start-Process -FilePath $TrayExe -WorkingDirectory $InstallDir
}

if (-not $WhatIfPreference) { Write-Host "Installed to $InstallDir" }
