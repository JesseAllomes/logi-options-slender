<#
.SYNOPSIS
  Report and repair Excel state damaged by Logitech add-ins (Slender interim/switch-over).
.DESCRIPTION
  Default is read-only: lists Logitech Excel add-in registrations, AddInLoadTimes, and decodes
  Resiliency\DisabledItems. Write switches refuse to run while EXCEL.EXE is running and first
  `reg export` every key they touch to %LOCALAPPDATA%\Slender\backups\Excel-<timestamp>.
  -Restore <folder> re-imports those .reg files.
.PARAMETER EnablePersonalXlsb   Remove the DisabledItems entry for PERSONAL.XLSB.
.PARAMETER RemoveStaleLogi      Remove DisabledItems entries and Addins registrations for Logi add-ins whose files no longer exist.
.PARAMETER DisableLogiAddins    Set LoadBehavior 0 on Logi add-ins (after Slender takes over).
.PARAMETER Restore              Backup folder to import .reg files from.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Apply,
    [switch]$EnablePersonalXlsb,
    [switch]$RemoveStaleLogi,
    [switch]$DisableLogiAddins,
    [string]$Restore
)
$ErrorActionPreference = 'Stop'
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$BackupRoot = Join-Path $env:LOCALAPPDATA 'Slender\backups'
$AddinRoots = 'HKCU:\Software\Microsoft\Office\Excel\Addins',
              'HKLM:\Software\Microsoft\Office\Excel\Addins',
              'HKLM:\Software\WOW6432Node\Microsoft\Office\Excel\Addins'
$LoadTimesKey = 'HKCU:\Software\Microsoft\Office\16.0\Excel\AddInLoadTimes'
$DisabledKey  = 'HKCU:\Software\Microsoft\Office\16.0\Excel\Resiliency\DisabledItems'

function ConvertTo-RegExe([string]$k) {
    # Provider path like Microsoft.PowerShell.Core\Registry::HKEY_CURRENT_USER\... or HKCU:\...
    $k = $k -replace '^Microsoft\.PowerShell\.Core\\Registry::', ''
    $k = $k -replace '^HKCU:\\', 'HKCU\' -replace '^HKLM:\\', 'HKLM\'
    $k
}

function Resolve-ManifestPath([string]$m) {
    if (-not $m) { return $null }
    $m = [Environment]::ExpandEnvironmentVariables($m)
    $m = $m -replace '\|vstolocal\s*$', ''
    if ($m -match '^file:///') { $m = ([Uri]::UnescapeDataString(($m -replace '^file:///', ''))) -replace '/', '\' }
    $m
}

function Get-LogiAddins {
    foreach ($root in $AddinRoots) {
        if (-not (Test-Path $root)) { continue }
        foreach ($k in Get-ChildItem $root -ErrorAction SilentlyContinue) {
            if ($k.PSChildName -notmatch 'logi') { continue }
            $p = Get-ItemProperty $k.PSPath
            $mp = $p.PSObject.Properties['Manifest']; $manifest = if ($mp) { $mp.Value } else { $null }
            $path = Resolve-ManifestPath $manifest
            $exists = $null
            if ($path) { $exists = Test-Path -LiteralPath $path }
            [pscustomobject]@{ Name = $k.PSChildName; Key = $k.PSPath; LoadBehavior = $p.LoadBehavior; Manifest = $manifest; PathExists = $exists }
        }
    }
}

function Get-DisabledItems {
    if (-not (Test-Path $DisabledKey)) { return }
    $props = Get-ItemProperty $DisabledKey
    foreach ($n in $props.PSObject.Properties.Name | Where-Object { $_ -notmatch '^PS' }) {
        $b = $props.$n
        if ($b -isnot [byte[]] -or $b.Length -lt 12) { continue }
        $type = [BitConverter]::ToInt32($b, 0)
        # payload is NUL-separated strings: path, then (for add-ins) the add-in name
        $parts = @([Text.Encoding]::Unicode.GetString($b, 12, $b.Length - 12).Split([char]0) | Where-Object { $_ })
        $path = if ($parts) { $parts[0] } else { '' }
        $extra = if ($parts.Count -gt 1) { ($parts[1..($parts.Count - 1)] -join ' | ') } else { '' }
        [pscustomobject]@{ ValueName = $n; Type = $type; Path = $path; Extra = $extra }
    }
}

function Get-LoadTimes([object[]]$addins) {
    if (-not (Test-Path $LoadTimesKey)) { return }
    $props = Get-ItemProperty $LoadTimesKey
    foreach ($n in $props.PSObject.Properties.Name | Where-Object { $_ -notmatch '^PS' }) {
        $b = $props.$n
        $isLogi = ($n -match 'logi') -or ($addins | Where-Object { $_.Name -and ($n -like "*$($_.Name)*") })
        if (-not $isLogi) { continue }
        $ms = if ($b -is [byte[]]) { for ($i = 0; $i + 4 -le $b.Length; $i += 4) { [BitConverter]::ToInt32($b, $i) } } else { @($b) }
        if ($ms.Count -ge 2 -and $ms[0] -eq ($ms.Count - 1)) { $ms = $ms[1..($ms.Count - 1)] }  # first Int32 is the entry count
        [pscustomobject]@{ Name = $n; Ms = ($ms -join ', ') }
    }
}

function Write-State([string]$title) {
    $a = @(Get-LogiAddins); $d = @(Get-DisabledItems)
    Write-Host "== $title =="
    Write-Host '-- Logitech add-in registrations --'
    if ($a) { $a | ForEach-Object { Write-Host ("  {0}  LoadBehavior={1}  PathExists={2}`n    {3}`n    Manifest: {4}" -f $_.Name, $_.LoadBehavior, $_.PathExists, $_.Key, $_.Manifest) } } else { Write-Host '  (none)' }
    Write-Host '-- AddInLoadTimes (Logi, ms entries) --'
    $lt = @(Get-LoadTimes $a); if ($lt) { $lt | ForEach-Object { Write-Host ("  {0}: {1}" -f $_.Name, $_.Ms) } } else { Write-Host '  (none)' }
    Write-Host '-- Resiliency\DisabledItems --'
    if ($d) { $d | ForEach-Object { Write-Host ("  type={0}  {1}{2}" -f $_.Type, $_.Path, $(if ($_.Extra) { "  [$($_.Extra)]" } else { '' })) } } else { Write-Host '  (none)' }
}

function Test-DisabledPath([string]$p) {
    # DisabledItems paths may carry a trailing name after the file, or junk characters
    if ($p -match '^(.*?\.[A-Za-z0-9]{2,5})(\s|$)') { $p = $Matches[1] }
    try { Test-Path -LiteralPath $p -ErrorAction Stop } catch { $false }
}

function Assert-ExcelClosed {
    if (-not (Get-Process EXCEL -ErrorAction SilentlyContinue)) { return }
    if ($WhatIfPreference) { Write-Warning 'EXCEL.EXE is running; a real -Apply would refuse.'; return }
    throw 'EXCEL.EXE is running. Close Excel and retry; nothing was changed.'
}

# ---------- Restore ----------
if ($Restore) {
    $files = @(Get-ChildItem -LiteralPath $Restore -Filter *.reg -ErrorAction Stop)
    if (-not $files) { throw "No .reg files in $Restore" }
    Assert-ExcelClosed
    foreach ($f in $files) {
        if ($PSCmdlet.ShouldProcess($f.FullName, 'reg import')) {
            & reg.exe import $f.FullName | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "reg import failed for $($f.Name)" }
        }
    }
    return
}

Write-State 'Before'
$write = $EnablePersonalXlsb -or $RemoveStaleLogi -or $DisableLogiAddins
if (-not $Apply -or -not $write) {
    if ($Apply -and -not $write) { Write-Host 'Nothing to do: pass -EnablePersonalXlsb, -RemoveStaleLogi and/or -DisableLogiAddins with -Apply.' }
    else { Write-Host "`nReport only. Add -Apply plus one or more repair switches (and -WhatIf to preview)." }
    return
}

# ---------- Plan ----------
$addins = @(Get-LogiAddins); $disabled = @(Get-DisabledItems)
$delDisabled = @(); $delAddins = @(); $disable = @()
if ($EnablePersonalXlsb) { $delDisabled += $disabled | Where-Object { $_.Path -match 'PERSONAL\.XLSB$' } }
if ($RemoveStaleLogi) {
    $delDisabled += $disabled | Where-Object { $_.Path -match 'logi' -and -not (Test-DisabledPath $_.Path) }
    $delAddins   += $addins   | Where-Object { $_.PathExists -eq $false }
}
if ($DisableLogiAddins) { $disable += $addins | Where-Object { $_.LoadBehavior -ne 0 -and $_.Key -notin @($delAddins.Key) } }
$delDisabled = @($delDisabled | Sort-Object ValueName -Unique)

$keys = @()
if ($delDisabled) { $keys += $DisabledKey }
$keys += @($delAddins.Key); $keys += @($disable.Key)
$keys = @($keys | Where-Object { $_ } | Sort-Object -Unique)
if (-not $keys) { Write-Host 'No matching items; nothing to change.'; return }

Write-Host '== Plan =='
$delDisabled | ForEach-Object { Write-Host "  remove DisabledItems '$($_.ValueName)' (type $($_.Type)): $($_.Path)" }
$delAddins   | ForEach-Object { Write-Host "  remove registration $($_.Key)" }
$disable     | ForEach-Object { Write-Host "  LoadBehavior 0 on $($_.Key)" }

Assert-ExcelClosed

# ---------- Backup ----------
$bdir = Join-Path $BackupRoot "Excel-$Stamp"
if ($PSCmdlet.ShouldProcess($bdir, "reg export $($keys.Count) key(s)")) {
    New-Item -ItemType Directory -Force -Path $bdir | Out-Null
    $i = 0
    foreach ($k in $keys) {
        $i++
        $label = (ConvertTo-RegExe $k) -replace '[\\/:*?"<>|]', '_'
        $file = Join-Path $bdir ("{0:00}-{1}.reg" -f $i, $label)
        & reg.exe export (ConvertTo-RegExe $k) $file /y | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "reg export failed for $k; nothing was changed." }
    }
    Write-Host "Backup: $bdir  (undo with -Restore '$bdir')"
}

# ---------- Act ----------
foreach ($d in $delDisabled) {
    if ($PSCmdlet.ShouldProcess("$DisabledKey\$($d.ValueName)", "Remove DisabledItems entry ($($d.Path))")) {
        Remove-ItemProperty -Path $DisabledKey -Name $d.ValueName
    }
}
foreach ($a in $delAddins) {
    if ($PSCmdlet.ShouldProcess($a.Key, 'Remove stale add-in registration')) {
        Remove-Item -Path $a.Key -Recurse
    }
}
foreach ($a in $disable) {
    if ($PSCmdlet.ShouldProcess($a.Key, 'Set LoadBehavior 0')) {
        Set-ItemProperty -Path $a.Key -Name LoadBehavior -Value 0 -Type DWord
    }
}

if (-not $WhatIfPreference) { Write-State 'After' }
