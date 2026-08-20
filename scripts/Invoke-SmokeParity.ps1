<#
.SYNOPSIS
    Runs the same acceptance profile against a locally-hosted Portal and the production container
    image, and fails unless both did the same work with the same outcome.

.DESCRIPTION
    Parity is a **comparison**, not two independent green runs. A container run that quietly skips
    two checks the local run performed would otherwise report success while proving less — and that
    is the exact failure this exists to catch, because it is invisible in any output that only says
    "passed".

    So both targets emit their per-check results as JSON, and this compares them check by check.
    Any check present in one and missing from the other, or with a different outcome, is a parity
    failure even when both runs exited zero.

.PARAMETER SkipLocal / -SkipDocker
    Run only one side. Useful while iterating; a run with either set is reported as *not* a parity
    check, because comparing one thing to nothing proves nothing.

.PARAMETER KeepRunning
    Leave the containers up for inspection after the comparison.

.EXAMPLE
    pwsh -File scripts/Invoke-SmokeParity.ps1
    pwsh -File scripts/Invoke-SmokeParity.ps1 -SkipDocker
#>
[CmdletBinding()]
param(
    [int]$LocalPort = 5399,
    [int]$DockerPort = 5398,
    [switch]$SkipLocal,
    [switch]$SkipDocker,
    [switch]$KeepRunning,
    [string]$ResultsDirectory = "smoke-parity"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

$acceptance = Join-Path $PSScriptRoot "Invoke-AcceptanceProfile.ps1"
$resultsDir = Join-Path $repoRoot $ResultsDirectory
if (-not (Test-Path $resultsDir)) { New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null }

$adminPassword = "Admin@12345!"
$jwtSecret = "smoke-parity-secret-key-0123456789abcdef"
$atRestKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY="

# Publishing a report by script path goes through RequireStudioCapability(ReportPublish,
# SourceControlled), which answers 404 in any other mode -- so without these two settings the
# profile silently seeds no report and three checks disappear. Applied identically to both targets,
# because a parity run that configured them differently would be comparing two different products.

function Wait-ForPortal {
    param([string]$Url, [int]$TimeoutSeconds = 180)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            Invoke-RestMethod -Method Get -Uri "$Url/healthz" -TimeoutSec 5 | Out-Null
            return $true
        }
        catch { Start-Sleep -Seconds 2 }
    }
    return $false
}

$localProcess = $null
$containerName = "etlsql-smoke-parity"
$localResults = Join-Path $resultsDir "local.json"
$dockerResults = Join-Path $resultsDir "docker.json"

try {
    # ── Local ───────────────────────────────────────────────────────────────────────────────────
    if (-not $SkipLocal) {
        Write-Host "Starting local Portal on $LocalPort..." -ForegroundColor White
        $root = Join-Path ([System.IO.Path]::GetTempPath()) "etlsql-parity-local-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path (Join-Path $root "scripts") -Force | Out-Null

        # ASPNETCORE_ENVIRONMENT is pinned to Production because appsettings.Development.json
        # overrides these values, which would make the local run read a different script root than
        # the one configured here -- and a parity run must compare like with like.
        $env:ASPNETCORE_ENVIRONMENT = "Production"
        $env:ASPNETCORE_URLS = "http://127.0.0.1:$LocalPort"
        $env:Portal__ScriptRootPath = Join-Path $root "scripts"
        $env:Portal__DatabasePath = Join-Path $root "portal.db"
        $env:Orchestrator__DatabasePath = Join-Path $root "orchestrator.db"
        $env:Portal__SnapshotDirectory = Join-Path $root "snapshots"
        $env:Portal__Jwt__Secret = $jwtSecret
        $env:Portal__Dataset__AtRestKey = $atRestKey
        $env:Portal__FirstRun__AdminUsername = "admin"
        $env:Portal__FirstRun__AdminPassword = $adminPassword
        $env:Portal__Studio__Mode = "SourceControlled"
        $env:Portal__Studio__RoleCapabilities__Admin__0 = "StudioAccess"
        $env:Portal__Studio__RoleCapabilities__Admin__1 = "ReportPublish"

        $localProcess = Start-Process -FilePath "dotnet" `
            -ArgumentList @("run", "--project", "src/ETL-SQL.Portal", "-c", "Release", "--no-launch-profile") `
            -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $resultsDir "local-portal.log") `
            -RedirectStandardError (Join-Path $resultsDir "local-portal.err")

        if (-not (Wait-ForPortal "http://127.0.0.1:$LocalPort")) {
            Write-Host "Local Portal did not become healthy. See $resultsDir/local-portal.err" -ForegroundColor Red
            exit 2
        }

        & $acceptance -BaseUrl "http://127.0.0.1:$LocalPort" `
            -ScriptRootPath (Join-Path $root "scripts") -ResultsPath $localResults
    }

    # ── Docker ──────────────────────────────────────────────────────────────────────────────────
    if (-not $SkipDocker) {
        Write-Host "Building the production Portal image..." -ForegroundColor White
        docker build -f src/ETL-SQL.Portal/Dockerfile -t etlsql-portal:smoke-parity . | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host "Image build failed." -ForegroundColor Red; exit 2 }

        docker rm -f $containerName 2>$null | Out-Null

        # The script root is bind-mounted so the acceptance profile can seed the report file, which
        # is the one step it cannot do over HTTP. Without it the container run would skip three
        # checks the local run performs -- and the comparison would correctly call that a parity
        # failure rather than quietly accepting a thinner check.
        $mount = Join-Path ([System.IO.Path]::GetTempPath()) "etlsql-parity-docker-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $mount -Force | Out-Null

        Write-Host "Starting the container on $DockerPort..." -ForegroundColor White
        docker run -d --name $containerName -p "${DockerPort}:8080" `
            -e "ASPNETCORE_URLS=http://+:8080" `
            -e "Portal__ScriptRootPath=/app/reports" `
            -e "Portal__Jwt__Secret=$jwtSecret" `
            -e "Portal__Dataset__AtRestKey=$atRestKey" `
            -e "Portal__FirstRun__AdminUsername=admin" `
            -e "Portal__FirstRun__AdminPassword=$adminPassword" `
            -e "Portal__Studio__Mode=SourceControlled" `
            -e "Portal__Studio__RoleCapabilities__Admin__0=StudioAccess" `
            -e "Portal__Studio__RoleCapabilities__Admin__1=ReportPublish" `
            -v "${mount}:/app/reports" `
            etlsql-portal:smoke-parity | Out-Null

        if (-not (Wait-ForPortal "http://127.0.0.1:$DockerPort")) {
            Write-Host "Container did not become healthy. Logs:" -ForegroundColor Red
            docker logs --tail 40 $containerName
            exit 2
        }

        & $acceptance -BaseUrl "http://127.0.0.1:$DockerPort" `
            -ScriptRootPath $mount -ResultsPath $dockerResults
    }
}
finally {
    if ($localProcess -and -not $localProcess.HasExited) {
        Stop-Process -Id $localProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if (-not $KeepRunning -and -not $SkipDocker) {
        docker rm -f $containerName 2>$null | Out-Null
    }
    Pop-Location
}

# ── Comparison ──────────────────────────────────────────────────────────────────────────────────
if ($SkipLocal -or $SkipDocker) {
    Write-Host ""
    Write-Host "Only one target ran, so this was not a parity check." -ForegroundColor Yellow
    exit 0
}

$local = (Get-Content $localResults -Raw | ConvertFrom-Json).checks
$docker = (Get-Content $dockerResults -Raw | ConvertFrom-Json).checks

$localMap = @{}; $local | ForEach-Object { $localMap[$_.check] = $_.outcome }
$dockerMap = @{}; $docker | ForEach-Object { $dockerMap[$_.check] = $_.outcome }

$differences = @()
foreach ($name in ($localMap.Keys + $dockerMap.Keys | Sort-Object -Unique)) {
    $l = if ($localMap.ContainsKey($name)) { $localMap[$name] } else { "(absent)" }
    $d = if ($dockerMap.ContainsKey($name)) { $dockerMap[$name] } else { "(absent)" }
    if ($l -ne $d) { $differences += "  $name : local=$l  docker=$d" }
}

Write-Host ""
Write-Host "Parity: $($localMap.Count) local checks vs $($dockerMap.Count) container checks" -ForegroundColor White

if ($differences.Count -eq 0) {
    Write-Host "Both targets ran the same checks with the same outcomes." -ForegroundColor Green
    exit 0
}

Write-Host "The two targets disagree:" -ForegroundColor Red
$differences | ForEach-Object { Write-Host $_ -ForegroundColor Red }
Write-Host ""
Write-Host "A difference here is a parity failure even if both runs exited zero -- one target" -ForegroundColor Red
Write-Host "proved less than the other." -ForegroundColor Red
exit 1
