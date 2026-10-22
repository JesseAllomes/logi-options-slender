<#
.SYNOPSIS
  Build artifacts\SlenderSetup.exe, the all-in-one Velopack installer (per user, no admin).
.DESCRIPTION
  Publishes the tray (NativeAOT, win-x64) and the settings app (framework-dependent) into one staging folder,
  then runs `vpk pack` (the local tool from .config\dotnet-tools.json) to produce the installer and update packages.
  Output goes under artifacts\ (git-ignored). Nothing is installed and nothing on this PC is touched.
  Setup installs to %LOCALAPPDATA%\SlenderApp, adds a Start menu shortcut, and installs the .NET 10 desktop runtime
  if it is missing.
  Updates ship through GitHub Releases on the public repo: the script downloads the latest release as the delta base,
  bakes the repo URL in as the tray's update source, packs, and publishes release v<Version>. The GitHub token comes from
  `gh auth token` at run time and is never written anywhere. HEAD must be committed and pushed first, so every release
  matches a commit on GitHub. Installed apps find the new version within 6 hours, or on "Check for updates".
.PARAMETER Version
  Package version (semver). Default: the next patch above the newest version in artifacts\releases after the download.
.PARAMETER LocalOnly
  Build against the artifacts\releases folder as the update source and upload nothing (testing only).
#>
#Requires -Version 7
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$LocalOnly
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
$RepoUrl      = 'https://github.com/JesseAllomes/logi-options-slender'

foreach ($p in $TrayProj, $SettingsProj, $Icon) {
    if (-not (Test-Path -LiteralPath $p)) { throw "Missing: $p" }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet SDK not found on PATH.' }

$token = $null
if (-not $LocalOnly) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI (gh) not found; run gh auth login, or use -LocalOnly.' }
    $token = (& gh auth token 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $token) { throw 'gh is not signed in; run gh auth login, or use -LocalOnly.' }
    $dirty = & git -C $RepoRoot status --porcelain --untracked-files=no
    if ($dirty) { throw 'Commit your changes first: a release must match a pushed commit.' }
    & git -C $RepoRoot fetch --quiet origin
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed ($LASTEXITCODE)." }
    $head = (& git -C $RepoRoot rev-parse HEAD).Trim()
    & git -C $RepoRoot merge-base --is-ancestor $head origin/main
    if ($LASTEXITCODE -ne 0) { throw 'Push first: HEAD is not on origin/main.' }

    New-Item -ItemType Directory -Force -Path $Releases | Out-Null
    Push-Location $RepoRoot
    try {
        & dotnet tool restore | Out-Null
        # The latest published release becomes the delta base and the version floor. None yet = first release.
        & dotnet vpk download github --repoUrl $RepoUrl --token $token --outputDir $Releases
        if ($LASTEXITCODE -ne 0) { Write-Warning 'No release downloaded from GitHub; packing without a GitHub delta base.' }
    }
    finally { Pop-Location }
}

if (-not $Version) {
    $newest = Get-ChildItem -LiteralPath $Releases -Filter "$PackId-*-full.nupkg" -File -ErrorAction SilentlyContinue |
        ForEach-Object { if ($_.Name -match ('^' + [regex]::Escape($PackId) + '-(\d+\.\d+\.\d+)(?:-[A-Za-z0-9.]+)?-full\.nupkg$')) { [version]$Matches[1] } } |
        Sort-Object -Descending | Select-Object -First 1
    $Version = if ($newest) { '{0}.{1}.{2}' -f $newest.Major, $newest.Minor, ($newest.Build + 1) } else { '1.0.0' }
    Write-Host "Version $Version (next patch above the newest in $Releases)"
}
$updateSource = if ($LocalOnly) { $Releases } else { $RepoUrl }

Push-Location $RepoRoot
try {
    & dotnet tool restore | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed ($LASTEXITCODE)." }
    # Rebuilding the same version: drop that version's earlier output (vpk refuses to overwrite it). Other versions stay as delta bases.
    Get-ChildItem -LiteralPath $Releases -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*-$Version-*" -or $_.Name -like '*-Setup.exe' -or $_.Name -like '*-Portable.zip' -or $_.Name -like 'releases.*.json' -or $_.Name -like 'assets.*.json' -or $_.Name -like 'RELEASES*' } | Remove-Item -Force

    if (Test-Path -LiteralPath $Staging) { Remove-Item -LiteralPath $Staging -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Staging, $Releases | Out-Null

    # Settings: framework-dependent so it shares .NET runtime pages (docs/memory.md); vpk --framework installs the runtime.
    & dotnet publish $SettingsProj -c Release -r win-x64 --self-contained false -p:Version=$Version -o $Staging --nologo
    if ($LASTEXITCODE -ne 0) { throw "Settings publish failed ($LASTEXITCODE)." }
    # Tray: NativeAOT (5.3 MB private / 17.6 MB working set idle, docs/memory.md). The linker step needs vswhere on PATH.
    $installer = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
    if ((Test-Path $installer) -and ($env:PATH -notlike "*$installer*")) { $env:PATH = "$installer;$env:PATH" }
    & dotnet publish $TrayProj -c Release -r win-x64 -p:PublishAot=true -p:Version=$Version "-p:SlenderUpdateSource=$updateSource" -o $Staging --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tray publish failed ($LASTEXITCODE)." }
    # Slender.Buzz: the tiny client hooks and scripts call to buzz the mouse (docs/buzz.md). NativeAOT so it starts at once.
    & dotnet publish (Join-Path $RepoRoot 'tools\Slender.Buzz\Slender.Buzz.csproj') -c Release -r win-x64 -p:PublishAot=true -p:Version=$Version -o $Staging --nologo
    if ($LASTEXITCODE -ne 0) { throw "Buzz publish failed ($LASTEXITCODE)." }
    # Slender.RingerHost: the Edge native messaging host of the 3CX ringer (docs/3cx-ringer.md), a one-shot NativeAOT exe. Settings copies it
    # to %LOCALAPPDATA%\Slender\edge-ringer when the user sets the ringer up; the extension is staged as EdgeRinger\extension for the same step.
    & dotnet publish (Join-Path $RepoRoot 'tools\Slender.RingerHost\Slender.RingerHost.csproj') -c Release -r win-x64 -p:PublishAot=true -p:Version=$Version -o $Staging --nologo
    if ($LASTEXITCODE -ne 0) { throw "RingerHost publish failed ($LASTEXITCODE)." }
    $RingerExt = Join-Path $RepoRoot 'tools\Slender.EdgeRinger\extension'
    New-Item -ItemType Directory -Force -Path (Join-Path $Staging 'EdgeRinger\extension') | Out-Null
    foreach ($f in 'manifest.json', 'background.js', 'relay.js', 'controls.js', 'main.js') {
        Copy-Item -LiteralPath (Join-Path $RingerExt $f) -Destination (Join-Path $Staging "EdgeRinger\extension\$f") -Force
    }
    # Slender Excel add-in (opt-in from Settings; docs/excel-addin.md): one packed 64-bit .xll, staged as ExcelAddin\. Nothing registers it
    # at install time; Settings copies it to %LOCALAPPDATA%\Slender\ExcelAddin and writes the HKCU OPEN value only when the user turns it on.
    $AddinProj = Join-Path $RepoRoot 'src\Slender.ExcelAddin\Slender.ExcelAddin.csproj'
    & dotnet build $AddinProj -c Release -p:Version=$Version --nologo
    if ($LASTEXITCODE -ne 0) { throw "Excel add-in build failed ($LASTEXITCODE)." }
    $xll = Join-Path $RepoRoot 'src\Slender.ExcelAddin\bin\Release\net10.0-windows\publish\Slender.ExcelAddin64-packed.xll'
    if (-not (Test-Path -LiteralPath $xll)) { throw 'The Excel add-in build did not produce Slender.ExcelAddin64-packed.xll.' }
    New-Item -ItemType Directory -Force -Path (Join-Path $Staging 'ExcelAddin') | Out-Null
    Copy-Item -LiteralPath $xll -Destination (Join-Path $Staging 'ExcelAddin\Slender.ExcelAddin64-packed.xll') -Force

    foreach ($exe in 'Slender.Tray.exe', 'Slender.Settings.exe', 'Slender.Buzz.exe', 'Slender.RingerHost.exe') {
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

    if (-not $LocalOnly) {
        & dotnet vpk upload github --repoUrl $RepoUrl --token $token --outputDir $Releases --publish `
            --releaseName "Slender $Version" --tag "v$Version" --targetCommitish $head
        if ($LASTEXITCODE -ne 0) { throw "vpk upload failed ($LASTEXITCODE)." }
        Write-Host "Published $RepoUrl/releases/tag/v$Version"
    }
}
finally {
    Pop-Location
}
