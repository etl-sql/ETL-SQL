<#
.SYNOPSIS
    Runs local pre-release validation before pushing tags or creating release installers.

.DESCRIPTION
    This script is intentionally local-first. It runs the checks that should pass
    before spending GitHub-hosted runner time, records exact commands, and writes
    JSON/Markdown reports under ./release-validation/.

    Use -Resume after fixing a failed phase. Resume reuses completed phases only
    when the current source fingerprint matches the saved run.

.EXAMPLE
    .\scripts\Test-PreRelease.ps1

.EXAMPLE
    .\scripts\Test-PreRelease.ps1 -Resume

.EXAMPLE
    .\scripts\Test-PreRelease.ps1 -IncludeDockerIntegration -IncludeStandardScale

.EXAMPLE
    .\scripts\Test-PreRelease.ps1 -BuildInstallers -Platforms win-x64
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",

    [switch]$Resume,
    [switch]$ForceResume,

    [switch]$SkipNode,
    [switch]$SkipScale,
    [switch]$IncludeDockerIntegration,
    [switch]$IncludeStandardScale,
    [switch]$BuildInstallers,

    [string[]]$Platforms = @("win-x64"),

    [string]$OutDir = "release-validation"
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot "..")
$ValidationRoot = Join-Path $RepoRoot $OutDir
$LatestDir = Join-Path $ValidationRoot "latest"
$StatePath = Join-Path $LatestDir "state.json"
$RunId = Get-Date -Format "yyyyMMdd-HHmmss"
$RunDir = Join-Path $ValidationRoot $RunId
$ReportJsonPath = Join-Path $RunDir "pre-release-report.json"
$ReportMarkdownPath = Join-Path $RunDir "pre-release-report.md"

function Get-PowerShellExecutable {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) {
        return $pwsh.Source
    }

    $powershell = Get-Command powershell -ErrorAction SilentlyContinue
    if ($powershell) {
        return $powershell.Source
    }

    throw "Could not locate pwsh or powershell on PATH."
}

$PowerShellExe = Get-PowerShellExecutable

function New-Sha256 {
    param([string]$Text)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        $hash = $sha.ComputeHash($bytes)
        return -join ($hash | ForEach-Object { $_.ToString("x2") })
    }
    finally {
        $sha.Dispose()
    }
}

function Get-SourceFingerprint {
    Push-Location $RepoRoot
    try {
        $head = ""
        $status = ""
        try { $head = (& git rev-parse HEAD 2>$null) -join "`n" } catch { }
        try { $status = (& git status --short 2>$null) -join "`n" } catch { }
        return New-Sha256 "$head`n$status"
    }
    finally {
        Pop-Location
    }
}

function Read-State {
    if (-not (Test-Path $StatePath)) {
        return $null
    }

    try {
        return Get-Content $StatePath -Raw | ConvertFrom-Json
    }
    catch {
        Write-Warning "Could not read previous validation state: $($_.Exception.Message)"
        return $null
    }
}

function Convert-PhaseMap {
    param($State)

    $map = @{}
    if ($State -and $State.phases) {
        foreach ($phase in $State.phases) {
            $map[$phase.name] = $phase
        }
    }
    return $map
}

function Save-State {
    param(
        [array]$Results,
        [string]$Status,
        [string]$Fingerprint
    )

    New-Item -ItemType Directory -Force -Path $LatestDir | Out-Null
    $state = [ordered]@{
        generatedAt = (Get-Date -Format "o")
        runId = $RunId
        status = $Status
        sourceFingerprint = $Fingerprint
        configuration = $Configuration
        phases = @($Results)
    }

    $state | ConvertTo-Json -Depth 8 | Set-Content -Path $StatePath -Encoding UTF8
}

function Invoke-LoggedPhase {
    param(
        [string]$Name,
        [string]$Command,
        [scriptblock]$Action,
        [hashtable]$PreviousPhaseMap,
        [string]$Fingerprint,
        [System.Collections.Generic.List[object]]$Results
    )

    if ($Resume -and $PreviousPhaseMap.ContainsKey($Name)) {
        $previous = $PreviousPhaseMap[$Name]
        if ($previous.status -eq "Passed") {
            $Results.Add([ordered]@{
                name = $Name
                command = $Command
                status = "Skipped"
                elapsedSeconds = 0
                log = $previous.log
                note = "Skipped by -Resume; previous phase passed for this source fingerprint."
            })
            Save-State -Results $Results.ToArray() -Status "Running" -Fingerprint $Fingerprint
            Write-Host "SKIP $Name" -ForegroundColor DarkGray
            return
        }
    }

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    Write-Host "    $Command" -ForegroundColor DarkGray

    $phaseLog = Join-Path $RunDir (($Name -replace '[^A-Za-z0-9_.-]', '_') + ".log")
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $status = "Passed"
    $note = ""

    try {
        Push-Location $RepoRoot
        try {
            $output = & $Action 2>&1
            $exitCode = if ($LASTEXITCODE -ne $null) { $LASTEXITCODE } else { 0 }
            $output | Tee-Object -FilePath $phaseLog
            if ($exitCode -ne 0) {
                throw "Command exited with code $exitCode"
            }
        }
        finally {
            Pop-Location
        }
    }
    catch {
        $status = "Failed"
        $note = $_.Exception.Message
        if (-not (Test-Path $phaseLog)) {
            $note | Set-Content -Path $phaseLog -Encoding UTF8
        }
    }
    finally {
        $timer.Stop()
    }

    $result = [ordered]@{
        name = $Name
        command = $Command
        status = $status
        elapsedSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 2)
        log = $phaseLog
        note = $note
    }
    $Results.Add($result)
    Save-State -Results $Results.ToArray() -Status $(if ($status -eq "Passed") { "Running" } else { "Failed" }) -Fingerprint $Fingerprint

    if ($status -eq "Failed") {
        Write-Host "FAILED $Name" -ForegroundColor Red
        Write-Host "Log: $phaseLog" -ForegroundColor Yellow
        throw "Pre-release validation failed at phase '$Name'. Fix the issue and rerun with -Resume."
    }

    Write-Host "PASS $Name ($($result.elapsedSeconds)s)" -ForegroundColor Green
}

function Write-Reports {
    param(
        [array]$Results,
        [string]$Status,
        [string]$Fingerprint,
        [datetime]$StartedAt
    )

    $finishedAt = Get-Date
    $report = [ordered]@{
        generatedAt = $finishedAt.ToString("o")
        runId = $RunId
        status = $Status
        sourceFingerprint = $Fingerprint
        configuration = $Configuration
        elapsedSeconds = [Math]::Round(($finishedAt - $StartedAt).TotalSeconds, 2)
        phases = @($Results)
    }

    $report | ConvertTo-Json -Depth 8 | Set-Content -Path $ReportJsonPath -Encoding UTF8

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# ETL-SQL Pre-Release Validation")
    $lines.Add("")
    $lines.Add(('Run: `{0}`' -f $RunId))
    $lines.Add("")
    $lines.Add(('Status: **{0}**' -f $Status))
    $lines.Add("")
    $lines.Add(('Generated: {0}' -f $finishedAt.ToString('yyyy-MM-dd HH:mm:ss')))
    $lines.Add("")
    $lines.Add(('Configuration: `{0}`' -f $Configuration))
    $lines.Add("")
    $lines.Add(('Source fingerprint: `{0}`' -f $Fingerprint))
    $lines.Add("")
    $lines.Add("| Phase | Status | Seconds | Command | Log |")
    $lines.Add("| :--- | :---: | ---: | :--- | :--- |")
    foreach ($r in $Results) {
        $relativeLog = Resolve-Path -LiteralPath $r.log -ErrorAction SilentlyContinue
        $logText = if ($relativeLog) { $relativeLog.Path } else { $r.log }
        $escapedCommand = ($r.command -replace '\|', '\|')
        $lines.Add(('| {0} | {1} | {2} | `{3}` | `{4}` |' -f $r.name, $r.status, $r.elapsedSeconds, $escapedCommand, $logText))
    }
    $lines.Add("")
    if ($Status -ne "Passed") {
        $lastFailure = $Results | Where-Object { $_.status -eq "Failed" } | Select-Object -Last 1
        if ($lastFailure) {
            $lines.Add("Last failure: **$($lastFailure.name)**")
            $lines.Add("")
            $lines.Add($lastFailure.note)
        }
    }

    $lines | Set-Content -Path $ReportMarkdownPath -Encoding UTF8
}

New-Item -ItemType Directory -Force -Path $RunDir | Out-Null

$startedAt = Get-Date
$fingerprint = Get-SourceFingerprint
$previousState = Read-State
$previousPhaseMap = @{}

if ($Resume) {
    if (-not $previousState) {
        throw "-Resume was specified, but no previous state exists at $StatePath."
    }

    if (-not $ForceResume -and $previousState.sourceFingerprint -ne $fingerprint) {
        throw "Source fingerprint changed since the previous run. Rerun without -Resume, or use -ForceResume to override."
    }

    $previousPhaseMap = Convert-PhaseMap $previousState
}

$results = New-Object System.Collections.Generic.List[object]

try {
    Invoke-LoggedPhase "Asset drift check" `
        "node .\scripts\sync-assets.js -Check" `
        { & node ".\scripts\sync-assets.js" "-Check" } `
        $previousPhaseMap $fingerprint $results

    Invoke-LoggedPhase "Dotnet restore" `
        "dotnet restore ETL-SQL.slnx" `
        { & dotnet restore "ETL-SQL.slnx" } `
        $previousPhaseMap $fingerprint $results

    Invoke-LoggedPhase "Dotnet build" `
        "dotnet build ETL-SQL.slnx --configuration $Configuration --no-restore" `
        { & dotnet build "ETL-SQL.slnx" "--configuration" $Configuration "--no-restore" } `
        $previousPhaseMap $fingerprint $results

    Invoke-LoggedPhase "Smoke lane" `
        ".\scripts\test-lane.ps1 -Lane smoke -Configuration $Configuration -NoRestore -NoBuild" `
        { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\test-lane.ps1" "-Lane" "smoke" "-Configuration" $Configuration "-NoRestore" "-NoBuild" } `
        $previousPhaseMap $fingerprint $results

    Invoke-LoggedPhase "Fast lane" `
        ".\scripts\test-lane.ps1 -Lane fast -Configuration $Configuration -NoRestore -NoBuild" `
        { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\test-lane.ps1" "-Lane" "fast" "-Configuration" $Configuration "-NoRestore" "-NoBuild" } `
        $previousPhaseMap $fingerprint $results

    Invoke-LoggedPhase "Sample scripts" `
        ".\scripts\Test-AllSamples.ps1" `
        { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\Test-AllSamples.ps1" } `
        $previousPhaseMap $fingerprint $results

    if (-not $SkipNode) {
        Invoke-LoggedPhase "VS Code npm ci" `
            "npm ci (src\etl-sql-vscode)" `
            { Push-Location "src\etl-sql-vscode"; try { & npm ci } finally { Pop-Location } } `
            $previousPhaseMap $fingerprint $results

        Invoke-LoggedPhase "VS Code compile" `
            "npm run compile (src\etl-sql-vscode)" `
            { Push-Location "src\etl-sql-vscode"; try { & npm run compile } finally { Pop-Location } } `
            $previousPhaseMap $fingerprint $results

        Invoke-LoggedPhase "VS Code unit tests" `
            "npm run test:unit (src\etl-sql-vscode)" `
            { Push-Location "src\etl-sql-vscode"; try { & npm run test:unit } finally { Pop-Location } } `
            $previousPhaseMap $fingerprint $results
    }

    if (-not $SkipScale) {
        Invoke-LoggedPhase "Scale certification smoke" `
            ".\scripts\Test-ScaleCertification.ps1 -Tier Smoke" `
            { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\Test-ScaleCertification.ps1" "-Tier" "Smoke" } `
            $previousPhaseMap $fingerprint $results
    }

    if ($IncludeDockerIntegration) {
        Invoke-LoggedPhase "Docker integration lane" `
            ".\scripts\test-lane.ps1 -Lane integration -Configuration $Configuration -NoRestore -NoBuild" `
            { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\test-lane.ps1" "-Lane" "integration" "-Configuration" $Configuration "-NoRestore" "-NoBuild" } `
            $previousPhaseMap $fingerprint $results
    }

    if ($IncludeStandardScale) {
        Invoke-LoggedPhase "Scale certification standard" `
            ".\scripts\Test-ScaleCertification.ps1 -Tier Standard" `
            { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\Test-ScaleCertification.ps1" "-Tier" "Standard" } `
            $previousPhaseMap $fingerprint $results
    }

    if ($BuildInstallers) {
        $platformText = $Platforms -join ","
        Invoke-LoggedPhase "Release publish artifacts" `
            ".\scripts\publish_release.ps1 -Platforms $platformText" `
            { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\publish_release.ps1" "-Platforms" $Platforms } `
            $previousPhaseMap $fingerprint $results

        if ($Platforms -contains "win-x64") {
            Invoke-LoggedPhase "Windows MSI" `
                ".\scripts\build_msi.ps1" `
                { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\build_msi.ps1" } `
                $previousPhaseMap $fingerprint $results
        }
    }

    Save-State -Results $results.ToArray() -Status "Passed" -Fingerprint $fingerprint
    Write-Reports -Results $results.ToArray() -Status "Passed" -Fingerprint $fingerprint -StartedAt $startedAt
    Write-Host ""
    Write-Host "Pre-release validation PASSED." -ForegroundColor Green
    Write-Host "Report: $ReportMarkdownPath" -ForegroundColor Cyan
    exit 0
}
catch {
    Write-Reports -Results $results.ToArray() -Status "Failed" -Fingerprint $fingerprint -StartedAt $startedAt
    Write-Host ""
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "Report: $ReportMarkdownPath" -ForegroundColor Yellow
    exit 1
}
