# UI sandbox static server (dev-only).
#
# ES module imports don't work over file://, so this serves the repo root over
# loopback HTTP. Run it, then open the URL it prints. Ctrl+C to stop.
#
#   pwsh -File tools\ui-sandbox\serve.ps1
#
param(
    [int]$Port = 8099,
    [switch]$NoOpen
)

$ErrorActionPreference = 'Stop'

# Repo root = two levels up from this script (tools\ui-sandbox\ -> repo root)
$RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$entryUrl = "http://localhost:$Port/tools/ui-sandbox/index.html"

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

try {
    $listener.Start()
}
catch {
    Write-Host "Could not bind http://localhost:$Port/ - $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Try a different port:  pwsh -File tools\ui-sandbox\serve.ps1 -Port 8100" -ForegroundColor Yellow
    exit 1
}

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " UI sandbox server" -ForegroundColor Cyan
Write-Host " Root : $RepoRoot" -ForegroundColor Gray
Write-Host " Open : $entryUrl" -ForegroundColor Green
Write-Host " Stop : Ctrl+C" -ForegroundColor Gray
Write-Host "=======================================================" -ForegroundColor Cyan

if (-not $NoOpen) { Start-Process $entryUrl }

try {
    while ($listener.IsListening) {
        $asyncResult = $listener.BeginGetContext($null, $null)
        while (-not $asyncResult.IsCompleted -and $listener.IsListening) {
            Start-Sleep -Milliseconds 100
        }
        if (-not $listener.IsListening) { break }
        $ctx = $listener.EndGetContext($asyncResult)
        $req = $ctx.Request
        $res = $ctx.Response
        try {
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
}
