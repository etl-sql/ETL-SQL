# UI sandbox static server (dev-only).
#
# ES module imports don't work over file://, so this serves the repo root over
# loopback HTTP. Run it, then open the URL it prints. Ctrl+C to stop.
#
#   pwsh -File tools\ui-sandbox\serve.ps1
#
param(
    [int]$Port = 8099,
    [switch]$NoOpen,
    [switch]$Stop,
    [ValidateRange(0, 10080)]
    [int]$IdleTimeoutMinutes = 120
)

$ErrorActionPreference = 'Stop'

# Repo root = two levels up from this script (tools\ui-sandbox\ -> repo root)
$RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$entryUrl = "http://localhost:$Port/tools/ui-sandbox/index.html"
$repoBytes = [System.Text.Encoding]::UTF8.GetBytes($RepoRoot.ToUpperInvariant())
$repoHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($repoBytes)).Substring(0, 16).ToLowerInvariant()
$stateRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'etl-sql-ui-sandbox'
$statePath = Join-Path $stateRoot "$repoHash-$Port.json"

if ($Stop) {
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        Write-Host "No UI sandbox state was found for port $Port." -ForegroundColor Yellow
        exit 0
    }

    try {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        $response = Invoke-WebRequest -UseBasicParsing -Method Post `
            -Uri "http://localhost:$($state.port)/__etl_sql_sandbox/stop" `
            -Headers @{ 'X-ETLSQL-SANDBOX-TOKEN' = [string]$state.token } `
            -TimeoutSec 3
        if ($response.StatusCode -ne 202) { throw "Sandbox returned HTTP $($response.StatusCode)." }

        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
        while ([DateTimeOffset]::UtcNow -lt $deadline -and (Get-Process -Id ([int]$state.processId) -ErrorAction SilentlyContinue)) {
            Start-Sleep -Milliseconds 100
        }
        if (Get-Process -Id ([int]$state.processId) -ErrorAction SilentlyContinue) {
            throw "Sandbox PID $($state.processId) accepted shutdown but did not exit."
        }
        Write-Host "Stopped UI sandbox PID $($state.processId) on port $($state.port)." -ForegroundColor Green
        exit 0
    }
    catch {
        Write-Host "Could not stop the recorded UI sandbox: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Inspect the recorded PID before forcing it: $statePath" -ForegroundColor Yellow
        exit 1
    }
}

$mime = @{
    '.html' = 'text/html; charset=utf-8'
    '.js'   = 'text/javascript; charset=utf-8'
    '.mjs'  = 'text/javascript; charset=utf-8'
    '.css'  = 'text/css; charset=utf-8'
    '.json' = 'application/json; charset=utf-8'
    '.svg'  = 'image/svg+xml'
    '.png'  = 'image/png'
    '.map'  = 'application/json; charset=utf-8'
}

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$shutdownToken = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$stopRequested = $false
$lastRequestUtc = [DateTimeOffset]::UtcNow

try {
    $listener.Start()
}
catch {
    Write-Host "Could not bind http://localhost:$Port/ - $($_.Exception.Message)" -ForegroundColor Red
    if (Test-Path -LiteralPath $statePath -PathType Leaf) {
        Write-Host "Recorded owner: $statePath" -ForegroundColor Yellow
        Write-Host "Stop it with: pwsh -File tools\ui-sandbox\serve.ps1 -Port $Port -Stop" -ForegroundColor Yellow
    }
    Write-Host "Try a different port:  pwsh -File tools\ui-sandbox\serve.ps1 -Port 8100" -ForegroundColor Yellow
    exit 1
}

New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
$state = [ordered]@{
    processId = $PID
    processStartTimeUtc = [Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToString('O')
    port = $Port
    repoRoot = $RepoRoot
    token = $shutdownToken
}
$temporaryStatePath = "$statePath.$PID.tmp"
$state | ConvertTo-Json | Set-Content -LiteralPath $temporaryStatePath -Encoding UTF8
Move-Item -LiteralPath $temporaryStatePath -Destination $statePath -Force

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " UI sandbox server" -ForegroundColor Cyan
Write-Host " Root : $RepoRoot" -ForegroundColor Gray
Write-Host " Open : $entryUrl" -ForegroundColor Green
Write-Host " PID  : $PID" -ForegroundColor Gray
Write-Host " Stop : Ctrl+C or pwsh -File tools\ui-sandbox\serve.ps1 -Port $Port -Stop" -ForegroundColor Gray
if ($IdleTimeoutMinutes -gt 0) {
    Write-Host " Idle : stops after $IdleTimeoutMinutes minutes without a request" -ForegroundColor Gray
}
Write-Host "=======================================================" -ForegroundColor Cyan

if (-not $NoOpen) { Start-Process $entryUrl }

try {
    while ($listener.IsListening -and -not $stopRequested) {
        $asyncResult = $listener.BeginGetContext($null, $null)
        while (-not $asyncResult.IsCompleted -and $listener.IsListening -and -not $stopRequested) {
            Start-Sleep -Milliseconds 100
            if ($IdleTimeoutMinutes -gt 0 -and
                [DateTimeOffset]::UtcNow - $lastRequestUtc -ge [TimeSpan]::FromMinutes($IdleTimeoutMinutes)) {
                Write-Host "Idle timeout reached; stopping UI sandbox." -ForegroundColor Yellow
                $stopRequested = $true
            }
        }
        if (-not $listener.IsListening -or $stopRequested) { break }
        $ctx = $listener.EndGetContext($asyncResult)
        $req = $ctx.Request
        $res = $ctx.Response
        $lastRequestUtc = [DateTimeOffset]::UtcNow
        try {
            if ($req.HttpMethod -eq 'POST' -and $req.Url.AbsolutePath -eq '/__etl_sql_sandbox/stop') {
                if ($req.Headers['X-ETLSQL-SANDBOX-TOKEN'] -ne $shutdownToken) {
                    $res.StatusCode = 403
                    $buf = [System.Text.Encoding]::UTF8.GetBytes('Forbidden')
                }
                else {
                    $res.StatusCode = 202
                    $buf = [System.Text.Encoding]::UTF8.GetBytes('UI sandbox shutdown requested.')
                    $stopRequested = $true
                }
                $res.ContentType = 'text/plain; charset=utf-8'
                $res.ContentLength64 = $buf.Length
                $res.OutputStream.Write($buf, 0, $buf.Length)
                continue
            }

            # Map URL path to a file under the repo root, blocking traversal.
            $rel = [System.Uri]::UnescapeDataString($req.Url.AbsolutePath.TrimStart('/'))
            if ([string]::IsNullOrWhiteSpace($rel)) { $rel = 'tools/ui-sandbox/index.html' }
            
            # Map '/maps/*' requests to the shared maps directory in ReportRuntime
            if ($rel.StartsWith('maps/', [System.StringComparison]::OrdinalIgnoreCase)) {
                $full = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot ("src/ETL-SQL.ReportRuntime/Resources/Shared/" + $rel)))
            } else {
                $full = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $rel))
            }

            if (-not $full.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path $full -PathType Leaf)) {
                $res.StatusCode = 404
                $buf = [System.Text.Encoding]::UTF8.GetBytes("404 Not Found: $rel")
            }
            else {
                $ext = [System.IO.Path]::GetExtension($full).ToLowerInvariant()
                $res.ContentType = if ($mime.ContainsKey($ext)) { $mime[$ext] } else { 'application/octet-stream' }
                $res.Headers['Cache-Control'] = 'no-store'
                $buf = [System.IO.File]::ReadAllBytes($full)
                $res.StatusCode = 200
            }
            $res.ContentLength64 = $buf.Length
            $res.OutputStream.Write($buf, 0, $buf.Length)
            Write-Host ("  {0,3}  {1}" -f $res.StatusCode, $rel) -ForegroundColor DarkGray
        }
        catch {
            Write-Host "  ERR  $($_.Exception.Message)" -ForegroundColor Red
        }
        finally {
            $res.OutputStream.Close()
        }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
    if (Test-Path -LiteralPath $statePath -PathType Leaf) {
        try {
            $recorded = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
            if ([int]$recorded.processId -eq $PID) { Remove-Item -LiteralPath $statePath -Force }
        }
        catch {
            Write-Host "Could not remove sandbox state $statePath - $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
}
