<#
.SYNOPSIS
  Tells every running Slender that a release is out, through a free Azure Web PubSub (slender-notify). Dot-sourced by
  Build-Setup.ps1; can also be run on its own:
    .\scripts\ReleaseNotice.ps1 -Announce 1.1.60   # send the notice again
    .\scripts\ReleaseNotice.ps1 -TicketOnly        # renew the listen ticket on the latest release only
.DESCRIPTION
  The tray reads notify.json, an asset of the latest GitHub release
  (https://github.com/JesseAllomes/logi-options-slender/releases/latest/download/notify.json). It holds a listen-only
  ticket (no roles: it can receive broadcasts and nothing else) that lasts 400 days and is renewed by every release.
  The service key never leaves this computer's memory and is never printed. The tray also checks every 15 minutes, so a
  failed notice only delays an update; Build-Setup.ps1 treats it as a warning.

  The Azure subscription is not kept in this public repo. It comes from the SLENDER_NOTICE_SUBSCRIPTION environment
  variable, or from scripts\ReleaseNotice.local.json (git-ignored): {"subscription":"<id>"}. The service is found in it
  by name; its resource group is looked up, not stored. az must be signed in to an account that can read the service key.
#>
param(
    # Not $Version: Build-Setup.ps1 dot-sources this file, and a param of that name would reset its own $Version.
    [string]$Announce,
    [switch]$TicketOnly
)

$NoticeService = 'slender-notify'   # the Web PubSub (free tier)
$NoticeHub = 'slender'              # letters only
$NoticeApp = 'slender'              # the "app" field in the notice; the tray's ReleaseListener is built with the same name
$NoticeRepo = 'JesseAllomes/logi-options-slender'
$NoticeHost = "$NoticeService.webpubsub.azure.com"

function Get-NoticeSubscription {
    if ($env:SLENDER_NOTICE_SUBSCRIPTION) { return $env:SLENDER_NOTICE_SUBSCRIPTION.Trim() }
    $local = Join-Path $PSScriptRoot 'ReleaseNotice.local.json'
    if (Test-Path -LiteralPath $local) {
        $sub = (Get-Content -LiteralPath $local -Raw | ConvertFrom-Json).subscription
        if ($sub) { return $sub.Trim() }
    }
    throw 'No subscription: set SLENDER_NOTICE_SUBSCRIPTION or write scripts\ReleaseNotice.local.json.'
}

function ConvertTo-NoticeBase64Url([byte[]]$Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-NoticeToken([string]$Audience, [string]$Key, [TimeSpan]$Life) {
    $utf8 = [Text.Encoding]::UTF8
    $now = [DateTimeOffset]::UtcNow
    $head = ConvertTo-NoticeBase64Url $utf8.GetBytes('{"alg":"HS256","typ":"JWT"}')
    $claims = [ordered]@{ aud = $Audience; iat = $now.ToUnixTimeSeconds(); exp = $now.Add($Life).ToUnixTimeSeconds() }
    $body = ConvertTo-NoticeBase64Url $utf8.GetBytes(($claims | ConvertTo-Json -Compress))
    $hmac = [Security.Cryptography.HMACSHA256]::new($utf8.GetBytes($Key))
    try { $sig = ConvertTo-NoticeBase64Url $hmac.ComputeHash($utf8.GetBytes("$head.$body")) } finally { $hmac.Dispose() }
    "$head.$body.$sig"
}

function Get-NoticeKey {
    $sub = Get-NoticeSubscription
    $id = az resource list --subscription $sub --resource-type Microsoft.SignalRService/WebPubSub --name $NoticeService --query '[0].id' -o tsv --only-show-errors
    if ($LASTEXITCODE -ne 0 -or -not $id) { throw "Could not find $NoticeService ($LASTEXITCODE)." }
    $key = az rest --method post --url "https://management.azure.com$($id.Trim())/listKeys?api-version=2024-03-01" --query primaryKey -o tsv --only-show-errors
    if ($LASTEXITCODE -ne 0 -or -not $key) { throw "Could not read the $NoticeService key ($LASTEXITCODE)." }
    $key.Trim()
}

<# Uploads notify.json (the listen ticket) to the given release, replacing the one there. #>
function Update-NoticeTicket([string]$Key, [string]$Tag) {
    if (-not $Tag) {
        $Tag = & gh release view --repo $NoticeRepo --json tagName --jq .tagName
        if ($LASTEXITCODE -ne 0 -or -not $Tag) { throw "Could not find the latest release ($LASTEXITCODE)." }
        $Tag = $Tag.Trim()
    }
    $life = [TimeSpan]::FromDays(400)
    $listen = New-NoticeToken "https://$NoticeHost/client/hubs/$NoticeHub" $Key $life
    $ticket = [ordered]@{
        url = "wss://$NoticeHost/client/hubs/$($NoticeHub)?access_token=$listen"
        expires = [DateTimeOffset]::UtcNow.Add($life).ToString('o')
    } | ConvertTo-Json -Compress
    # gh names the asset after the file, so it must be notify.json: a folder of its own, removed afterwards.
    $dir = Join-Path ([IO.Path]::GetTempPath()) ("slender-notice-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $file = Join-Path $dir 'notify.json'
        [IO.File]::WriteAllText($file, $ticket)
        & gh release upload $Tag $file --repo $NoticeRepo --clobber
        if ($LASTEXITCODE -ne 0) { throw "Upload of notify.json failed ($LASTEXITCODE)." }
    }
    finally { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}

<# Broadcasts {"type":"release","app":"slender","version":...} to every connected tray. Throws on failure. #>
function Send-ReleaseNotice([string]$Key, [string]$ReleaseVersion) {
    $url = "https://$NoticeHost/api/hubs/$NoticeHub/:send?api-version=2024-01-01"
    $send = New-NoticeToken "https://$NoticeHost/api/hubs/$NoticeHub/:send" $Key ([TimeSpan]::FromMinutes(5))
    $body = [ordered]@{ type = 'release'; app = $NoticeApp; version = $ReleaseVersion } | ConvertTo-Json -Compress
    $null = Invoke-WebRequest -Method Post -Uri $url -Body $body -ContentType 'application/json' -Headers @{ Authorization = "Bearer $send" } -TimeoutSec 30
}

<# The whole notice step for Build-Setup.ps1: renew the ticket on the release, then announce. Never throws. #>
function Publish-ReleaseNotice([string]$ReleaseVersion, [string]$Tag) {
    try {
        $key = Get-NoticeKey
        Update-NoticeTicket $key $Tag
        if ($ReleaseVersion) {
            Send-ReleaseNotice $key $ReleaseVersion
            Write-Host "  told running copies that $ReleaseVersion is out"
        }
    }
    catch {
        Write-Warning "Release notice not sent ($($_.Exception.Message)). Slender will still find the update within 15 minutes."
    }
    finally { $global:LASTEXITCODE = 0 }
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    if (-not $TicketOnly -and -not $Announce) { throw 'Give -Announce <version>, or -TicketOnly.' }
    Publish-ReleaseNotice $(if ($TicketOnly) { $null } else { $Announce })
}
