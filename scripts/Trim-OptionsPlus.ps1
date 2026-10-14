<#
.SYNOPSIS
  Reinstall Logi Options+ with only the features you keep (Slender interim trim).
.DESCRIPTION
  Default is a read-only report: installed versions, running Logi processes, and the exact
  installer command line that WOULD run. -Apply performs the uninstall/reinstall.
  Based on logi-options-plus-mini.ps1 (Apache-2.0), which mirrors Logitech's mass-deployment
  guide. Differences: no prompts (ShouldProcess/-Confirm), Authenticode signer check,
  timestamped config backups that are never deleted, only the installer is elevated.
.PARAMETER Keep
  Features to keep (Yes); all others are No. Valid: analytics flow sso update dfu backlight
  logivoice aipromptbuilder device-recommendation smartactions actions-ring.
.PARAMETER Apply
  Actually download/verify/back up/uninstall/install. Without it, report only.
.PARAMETER InstallerPath
  Use a local installer instead of downloading (still signature-checked).
.PARAMETER Quiet
  Pass /quiet to the installer.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('analytics','flow','sso','update','dfu','backlight','logivoice','aipromptbuilder','device-recommendation','smartactions','actions-ring')]
    [string[]]$Keep = @('actions-ring','backlight'),
    [switch]$Apply,
    [string]$InstallerPath,
    [switch]$Quiet
)
$ErrorActionPreference = 'Stop'

$DownloadUrl = 'https://download01.logi.com/web/ftp/pub/techsupport/optionsplus/logioptionsplus_installer.exe'
$AllFlags    = 'analytics','flow','sso','update','dfu','backlight','logivoice','aipromptbuilder','device-recommendation','smartactions','actions-ring'
$ConfigPath  = Join-Path $env:LOCALAPPDATA 'LogiOptionsPlus'
$SlenderRoot = Join-Path $env:LOCALAPPDATA 'Slender'
$Stamp       = Get-Date -Format 'yyyyMMdd-HHmmss'

function Get-InstallerArgs([string[]]$Flags, [bool]$QuietMode) {
    $a = @()
    if ($QuietMode) { $a += '/quiet' }
    foreach ($f in $AllFlags) { $a += "/$f"; $a += $(if ($Flags -contains $f) { 'Yes' } else { 'No' }) }
    $a
}

function Get-InstalledVersions {
    $roots = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
             'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
             'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    Get-ItemProperty $roots -ErrorAction SilentlyContinue |
        Where-Object { $_.PSObject.Properties['DisplayName'] -and $_.DisplayName -match 'Logi.*(Options|Plugin Service|Download Assistant)' } |
        Select-Object DisplayName, DisplayVersion -Unique
}

function Write-Report([string[]]$InstArgs) {
    Write-Host '== Installed =='
    $inst = @(Get-InstalledVersions)
    if ($inst) { $inst | ForEach-Object { Write-Host ("  {0}  {1}" -f $_.DisplayName, $_.DisplayVersion) } }
    else { Write-Host '  (no Logi Options+ / Plugin Service uninstall entries found)' }
    Write-Host '== Running Logi processes =='
    $procs = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^(logi|Logi)' })
    if ($procs) {
        $procs | Sort-Object WorkingSet64 -Descending | ForEach-Object { Write-Host ("  {0,-32} {1,8:N1} MB" -f $_.Name, ($_.WorkingSet64 / 1MB)) }
        Write-Host ("  {0,-32} {1,8:N1} MB" -f 'TOTAL', (($procs | Measure-Object WorkingSet64 -Sum).Sum / 1MB))
    } else { Write-Host '  (none)' }
    Write-Host '== Plan =='
    Write-Host "  Config dir : $ConfigPath (exists: $(Test-Path $ConfigPath))"
    Write-Host "  Uninstall  : logioptionsplus_installer.exe /uninstall"
    Write-Host "  Install    : logioptionsplus_installer.exe $($InstArgs -join ' ')"
    if ($InstallerPath) { Write-Host "  Installer  : $InstallerPath" } else { Write-Host "  Installer  : download $DownloadUrl" }
}

$instArgs = Get-InstallerArgs -Flags $Keep -QuietMode $Quiet.IsPresent
Write-Report $instArgs
if (-not $Apply) { Write-Host "`nReport only. Re-run with -Apply to perform it (add -WhatIf to preview)."; return }

# ---- Apply ----
$logDir = Join-Path $SlenderRoot 'logs'
$transcribing = $false
if ($PSCmdlet.ShouldProcess($logDir, 'Create log folder and start transcript')) {
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    Start-Transcript -Path (Join-Path $logDir "Trim-OptionsPlus-$Stamp.log") | Out-Null
    $transcribing = $true
}
try {
    # 1. Installer: local or download into a NEW empty temp folder
    $installer = $null
    if ($InstallerPath) {
        $installer = (Resolve-Path -LiteralPath $InstallerPath).Path
    } else {
        $dl = Join-Path $env:TEMP "slender-optionsplus-$Stamp"
        if (Test-Path $dl) { throw "Download folder already exists: $dl" }
        if ($PSCmdlet.ShouldProcess($DownloadUrl, "Download installer into new folder $dl")) {
            New-Item -ItemType Directory -Path $dl | Out-Null
            $installer = Join-Path $dl 'logioptionsplus_installer.exe'
            Invoke-WebRequest -Uri $DownloadUrl -OutFile $installer -UseBasicParsing
        }
    }

    # 2. Signature check (Valid + Logitech signer), abort otherwise
    if ($installer) {
        $sig = Get-AuthenticodeSignature -FilePath $installer
        $subject = if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { '' }
        if ($sig.Status -ne 'Valid' -or $subject -notmatch 'Logitech') {
            throw "Installer signature rejected (Status=$($sig.Status), Signer='$subject'). Aborting; nothing was changed."
        }
        Write-Host "Signature OK: $subject"
    } else {
        Write-Host '[WhatIf] would verify Authenticode status Valid and signer containing Logitech'
    }

    # 3. Back up config (never delete older backups)
    $backup = Join-Path $SlenderRoot "backups\OptionsPlus-$Stamp"
    $backedUp = $false
    if (Test-Path $ConfigPath) {
        if ($PSCmdlet.ShouldProcess($ConfigPath, "Back up to $backup")) {
            New-Item -ItemType Directory -Force -Path (Split-Path $backup) | Out-Null
            Copy-Item -LiteralPath $ConfigPath -Destination $backup -Recurse
            $backedUp = $true
        }
    } else { Write-Host 'No existing config dir to back up.' }

    # 4. Uninstall then install; only the installer process is elevated
    $target = if ($installer) { $installer } else { '<installer>' }
    if ($PSCmdlet.ShouldProcess($target, '/uninstall (elevated)')) {
        $p = Start-Process -FilePath $installer -ArgumentList '/uninstall' -Verb RunAs -Wait -PassThru
        if ($p.ExitCode -notin 0, 3010) { throw "Uninstall failed, exit code $($p.ExitCode). Config backup (if any): $backup" }
    }

    # 5. Restore config if the uninstall removed it
    if ($backedUp -and -not (Test-Path $ConfigPath)) {
        if ($PSCmdlet.ShouldProcess($ConfigPath, "Restore from $backup")) {
            Copy-Item -LiteralPath $backup -Destination $ConfigPath -Recurse
        }
    } elseif ($backedUp) { Write-Host 'Config dir still present after uninstall; not overwritten.' }

    if ($PSCmdlet.ShouldProcess($target, "Install: $($instArgs -join ' ') (elevated)")) {
        $p = Start-Process -FilePath $installer -ArgumentList $instArgs -Verb RunAs -Wait -PassThru
        if ($p.ExitCode -notin 0, 3010) { throw "Install failed, exit code $($p.ExitCode). Config backup (if any): $backup" }
        if ($backedUp -and -not (Test-Path $ConfigPath)) { Copy-Item -LiteralPath $backup -Destination $ConfigPath -Recurse }
        Write-Host 'Install completed.'
    }
}
finally {
    if ($transcribing) { Stop-Transcript | Out-Null }
}
