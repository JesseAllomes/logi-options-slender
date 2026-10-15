<#
.SYNOPSIS
  Build artifacts\SlenderSetup.exe, the all-in-one Velopack installer (per user, no admin).
.DESCRIPTION
  Publishes the tray (NativeAOT, win-x64) and the settings app (framework-dependent) into one staging folder,
  then runs `vpk pack` (the local tool from .config\dotnet-tools.json) to produce the installer and update packages.
  Output goes under artifacts\ (git-ignored). Nothing is installed and nothing on this PC is touched.
  Setup installs to %LOCALAPPDATA%\SlenderApp, adds a Start menu shortcut, and installs the .NET 10 desktop runtime
  if it is missing. To ship an update, build again with a higher -Version and point updateSource at artifacts\releases.
.PARAMETER Version
  Package version (semver). Default 1.0.0.
#>
#Requires -Version 7
[CmdletBinding()]
param(
    [string]$Version = '1.0.0'
)
$ErrorActionPreference = 'Stop'

$RepoRoot     = Split-Path -Parent $PSScriptRoot
$TrayProj     = Join-Path $RepoRoot 'src\Slender.Tray\Slender.Tray.csproj'
$SettingsProj = Join-Path $RepoRoot 'src\Slender.Settings\Slender.Settings.csproj'
$Icon         = Join-Path $RepoRoot 'src\Slender.Settings\Assets\Slender.ico'
$Artifacts    = Join-Path $RepoRoot 'artifacts'
$Staging      = Join-Path $Artifacts 'staging'
$Releases     = Join-Path $Artifacts 'releases'
$PackId       = 'SlenderApp'   # install folder name; kept apart from %LOCALAPPDATA%\Slender (status, logs, sync state)

foreach ($p in $TrayProj, $SettingsProj, $Icon) {
    if (-not (Test-Path -LiteralPath $p)) { throw "Missing: $p" }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet SDK not found on PATH.' }

Push-Location $RepoRoot
try {
    & dotnet tool restore | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed ($LASTEXITCODE)." }
    # Rebuilding the same version: drop that version's earlier output (vpk refuses to overwrite it). Other versions stay as delta bases.
    Get-ChildItem -LiteralPath $Releases -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*-$Version-*" -or $_.Name -like '*-Setup.exe' -or $_.Name -like '*-Portable.zip' -or $_.Name -like 'releases.*.json' -or $_.Name -like 'assets.*.json' -or $_.Name -like 'RELEASES*' } | Remove-Item -Force

    if (Test-Path -LiteralPath $Staging) { Remove-Item -LiteralPath $Staging -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Staging, $Releases | Out-Null

    # Settings: framework-dependent so it shares .NET runtime pages (docs/memory.md); vpk --framework installs the runtime.
    & dotnet publish $SettingsProj -c Release -r win-x64 --self-contained false -o $Staging --nologo
    if ($LASTEXITCODE -ne 0) { throw "Settings publish failed ($LASTEXITCODE)." }
    # Tray: NativeAOT (5.3 MB private / 17.6 MB working set idle, docs/memory.md). The linker step needs vswhere on PATH.
    $installer = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
    if ((Test-Path $installer) -and ($env:PATH -notlike "*$installer*")) { $env:PATH = "$installer;$env:PATH" }
    & dotnet publish $TrayProj -c Release -r win-x64 -p:PublishAot=true -p:Version=$Version -o $Staging --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tray publish failed ($LASTEXITCODE)." }
    foreach ($exe in 'Slender.Tray.exe', 'Slender.Settings.exe') {
        if (-not (Test-Path -LiteralPath (Join-Path $Staging $exe))) { throw "Publish did not produce $exe." }
    }

    # The sign-in setup steps, opened by the settings app's "How to set it up" button.
    New-Item -ItemType Directory -Force -Path (Join-Path $Staging 'docs') | Out-Null
    Copy-Item -LiteralPath (Join-Path $RepoRoot 'docs\sync.md') -Destination (Join-Path $Staging 'docs\sync.md') -Force

    & dotnet vpk pack `
        --packId $PackId `
        --packVersion $Version `
        --runtime 'win-x64' `
        --packTitle 'Slender' `
        --packAuthors 'Slender' `
        --packDir $Staging `
        --mainExe 'Slender.Tray.exe' `
        --icon $Icon `
        --framework 'net10.0-x64-desktop' `
        --shortcuts 'StartMenuRoot' `
        --outputDir $Releases
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)." }

    $setup = Get-ChildItem -LiteralPath $Releases -Filter "$PackId-*Setup.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $setup) { throw 'vpk did not produce a Setup exe.' }
    $target = Join-Path $Artifacts 'SlenderSetup.exe'
    Copy-Item -LiteralPath $setup.FullName -Destination $target -Force
    Write-Host ("Built {0} ({1:N1} MB)" -f $target, ((Get-Item -LiteralPath $target).Length / 1MB))
    Write-Host "Update packages: $Releases"
}
finally {
    Pop-Location
}
