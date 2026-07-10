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
    .\scripts\Test-PreRelease.ps1 -Quick -IncludeSlt

.EXAMPLE
    .\scripts\Test-PreRelease.ps1 -Explain -IncludeSlt -IncludeDockerIntegration

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
    [switch]$IncludeSlt,
    [switch]$IncludeStandardScale,
    [switch]$BuildInstallers,
    [switch]$Quick,
    [switch]$Explain,

    [string[]]$Platforms = @("win-x64"),

    [string]$OutDir = "release-validation"
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot "..")

# Shared NuGet dependency-audit helpers (reliable deprecated/vulnerable audit under SDK 10.0.300 + CPM).
. (Join-Path $ScriptRoot "lib/DependencyAudit.ps1")
$ValidationRoot = Join-Path $RepoRoot $OutDir
$LatestDir = Join-Path $ValidationRoot "latest"
$StatePath = Join-Path $LatestDir "state.json"
$RunId = Get-Date -Format "yyyyMMdd-HHmmss"
$RunDir = Join-Path $ValidationRoot $RunId
$ReportJsonPath = Join-Path $RunDir "pre-release-report.json"
$ReportMarkdownPath = Join-Path $RunDir "pre-release-report.md"

$EffectiveSkipNode = $SkipNode -or $Quick
$EffectiveSkipScale = $SkipScale -or $Quick
$EffectiveIncludeDockerIntegration = $IncludeDockerIntegration -and -not $Quick
$EffectiveIncludeStandardScale = $IncludeStandardScale -and -not $Quick
$EffectiveBuildInstallers = $BuildInstallers -and -not $Quick

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

function Get-PlannedPreReleasePhases {
    $phases = New-Object System.Collections.Generic.List[object]

    $phases.Add([ordered]@{ Phase = "Asset drift check"; Command = "node .\scripts\sync-assets.js -Check"; Reason = "Shared report runtime files must match generated host copies." })
    $phases.Add([ordered]@{ Phase = "Secret scan"; Command = "node scripts/scan-secrets.js"; Reason = "No real credentials (keys/provider tokens) reach the public repo — early local tripwire ahead of GitGuardian." })
    $phases.Add([ordered]@{ Phase = "Dotnet restore"; Command = "dotnet restore ETL-SQL.slnx"; Reason = "Package graph resolves before build and tests." })
    $phases.Add([ordered]@{ Phase = "Dependency-audit self-test"; Command = ".\scripts\Test-DependencyAudit.ps1"; Reason = "The dependency-audit helpers behave correctly (reliable fallback + hard failure)." })
    $phases.Add([ordered]@{ Phase = "NuGet dependency audit"; Command = "dotnet list package --outdated/--deprecated/--vulnerable"; Reason = "Release should not ship known vulnerable or deprecated packages." })
    $phases.Add([ordered]@{ Phase = "SBOM generation"; Command = "node scripts/generate-sbom.js"; Reason = "The released SBOM generates and its component version matches Directory.Build.props." })
    $phases.Add([ordered]@{ Phase = "Dotnet build"; Command = "dotnet build ETL-SQL.slnx --configuration $Configuration --no-restore"; Reason = "All projects compile in the release configuration." })
    $phases.Add([ordered]@{ Phase = "Format verify"; Command = "dotnet format ETL-SQL.slnx --verify-no-changes --no-restore (auto-applies 'dotnet format' on drift)"; Reason = "Code formatting (whitespace + import ordering) matches .editorconfig — same check the CI format gate runs. On drift the fix is applied automatically; commit it and re-run." })
    $phases.Add([ordered]@{ Phase = "Smoke lane"; Command = ".\scripts\test-lane.ps1 -Lane smoke"; Reason = "Critical startup, security, report, and portal checks." })
    $phases.Add([ordered]@{ Phase = "Fast lane"; Command = ".\scripts\test-lane.ps1 -Lane fast"; Reason = "Default local correctness lane across engine, language server, and portal." })
    $phases.Add([ordered]@{ Phase = "N->N+1 upgrade-path drill"; Command = "dotnet test ETL-SQL.ReportPortal.Tests --filter FullyQualifiedName~UpgradePathDrillTests"; Reason = "In-place EF migration over a live release-N catalog keeps permissions, jobs, subscriptions, datasets, and audit history intact (release gate)." })
    $phases.Add([ordered]@{ Phase = "Sample scripts"; Command = ".\scripts\Test-AllSamples.ps1"; Reason = "Published samples remain runnable." })
    $phases.Add([ordered]@{ Phase = "HA soak contract gate"; Command = ".\scripts\Test-HaSoakContracts.ps1"; Reason = "PostgreSQL HA soak topology, workload, metrics, diagnostics, runbook, and fault/soak plan contracts stay usable before release." })

    if ($IncludeSlt) {
        $phases.Add([ordered]@{ Phase = "SLT lane"; Command = ".\scripts\test-lane.ps1 -Lane slt"; Reason = "SQL logic corpus checks parser/evaluator compatibility." })
    }

    if (-not $EffectiveSkipNode) {
        $phases.Add([ordered]@{ Phase = "VS Code npm ci"; Command = "npm ci"; Reason = "Extension dependencies install from lockfile." })
        $phases.Add([ordered]@{ Phase = "VS Code npm audit"; Command = "npm outdated / npm audit"; Reason = "Extension dependency risk is visible before release." })
        $phases.Add([ordered]@{ Phase = "VS Code compile"; Command = "npm run compile"; Reason = "TypeScript extension compiles." })
        $phases.Add([ordered]@{ Phase = "VS Code VSIX package"; Command = "npx @vscode/vsce package --target win32-x64"; Reason = "VSIX packages cleanly — same vsce step release.yml runs; catches manifest/engine errors before the release build." })
        $phases.Add([ordered]@{ Phase = "VS Code unit tests"; Command = "npm run test:unit"; Reason = "Extension unit tests pass." })
    }

    if (-not $EffectiveSkipScale) {
        $phases.Add([ordered]@{ Phase = "Scale certification smoke"; Command = ".\scripts\Test-ScaleCertification.ps1 -Tier Smoke"; Reason = "Small certification workload still meets baseline." })
        $phases.Add([ordered]@{ Phase = "Cert baseline regression check (smoke)"; Command = ".\scripts\Compare-CertBaseline.ps1 -MarkdownReport <run>\cert-baseline-smoke.md"; Reason = "Smoke certification metrics have not regressed; warning evidence is preserved in the validation artifacts." })
    }

    if ($EffectiveIncludeDockerIntegration) {
        $phases.Add([ordered]@{ Phase = "Docker integration lane"; Command = ".\scripts\test-lane.ps1 -Lane integration"; Reason = "External connector boundaries pass against local containers." })
    }

    if ($EffectiveIncludeStandardScale) {
        $phases.Add([ordered]@{ Phase = "Scale certification standard"; Command = ".\scripts\Test-ScaleCertification.ps1 -Tier Standard"; Reason = "Release-size certification workload still meets baseline." })
        $phases.Add([ordered]@{ Phase = "Cert baseline regression check (standard)"; Command = ".\scripts\Compare-CertBaseline.ps1 -MarkdownReport <run>\cert-baseline-standard.md"; Reason = "Standard certification metrics have not regressed; warning evidence is preserved in the validation artifacts." })
        $phases.Add([ordered]@{ Phase = "Spill allocation budget (10M)"; Command = ".\scripts\Test-SpillAllocProfile.ps1 -Rows 10000000 -SkipBuild"; Reason = "Gate F round-trip allocation, GC, and peak-memory containment stay within the checked-in budget." })
    }

    if ($EffectiveBuildInstallers) {
        $phases.Add([ordered]@{ Phase = "Release publish artifacts"; Command = ".\scripts\publish_release.ps1"; Reason = "Release binaries can be published for target platforms." })
        if ($Platforms -contains "win-x64") {
            $phases.Add([ordered]@{ Phase = "Windows MSI"; Command = ".\scripts\build_msi.ps1"; Reason = "Windows installer can be built." })
        }
    }

    return $phases
}

function Show-PreReleasePlan {
    Write-Host "Pre-release validation plan" -ForegroundColor Cyan
    Write-Host ("Configuration: {0}" -f $Configuration)
    Write-Host ("Quick: {0}; IncludeSlt: {1}; Docker: {2}; StandardScale: {3}; BuildInstallers: {4}" -f `
        [bool]$Quick, [bool]$IncludeSlt, [bool]$EffectiveIncludeDockerIntegration, [bool]$EffectiveIncludeStandardScale, [bool]$EffectiveBuildInstallers)
    Write-Host ""

    $index = 1
    foreach ($phase in Get-PlannedPreReleasePhases) {
        Write-Host ("{0,2}. {1}" -f $index, $phase.Phase) -ForegroundColor White
        Write-Host ("    {0}" -f $phase.Command) -ForegroundColor DarkGray
        Write-Host ("    {0}" -f $phase.Reason)
        $index++
    }
}

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

function Invoke-NpmJsonCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [switch]$AllowNonZeroExit
    )

    Push-Location $WorkingDirectory
    try {
        $oldPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $output = & npm @Arguments 2>&1
        }
        finally {
            $ErrorActionPreference = $oldPreference
        }
        $exitCode = if ($LASTEXITCODE -ne $null) { $LASTEXITCODE } else { 0 }
        $text = ($output -join "`n").Trim()
        $json = $null

        if (-not [string]::IsNullOrWhiteSpace($text)) {
            $json = $text | ConvertFrom-Json
        }

        if ($exitCode -ne 0 -and -not $AllowNonZeroExit) {
            throw "npm $($Arguments -join ' ') failed with exit code $exitCode"
        }

        return [ordered]@{
            exitCode = $exitCode
            text = $text
            json = $json
        }
    }
    finally {
        Pop-Location
    }
}

function Get-NpmOutdatedFindings {
    param(
        [Parameter(Mandatory = $true)]
        $AuditResult
    )

    $findings = New-Object System.Collections.Generic.List[object]
    if (-not $AuditResult.json) {
        return $findings
    }

    foreach ($package in $AuditResult.json.PSObject.Properties) {
        $value = $package.Value
        if (-not $value.latest) {
            continue
        }

        $findings.Add([ordered]@{
            name = $package.Name
            current = $value.current
            wanted = $value.wanted
            latest = $value.latest
            location = $value.location
            type = $value.type
        })
    }

    return $findings
}

function Get-NpmAuditFindings {
    param(
        [Parameter(Mandatory = $true)]
        $AuditResult
    )

    $summary = [ordered]@{
        total = 0
        low = 0
        moderate = 0
        high = 0
        critical = 0
        packages = New-Object System.Collections.Generic.List[object]
    }

    if ($AuditResult.json -and $AuditResult.json.metadata -and $AuditResult.json.metadata.vulnerabilities) {
        $vuln = $AuditResult.json.metadata.vulnerabilities
        foreach ($severity in @("info", "low", "moderate", "high", "critical")) {
            if ($vuln.PSObject.Properties.Name -contains $severity) {
                $summary[$severity] = [int]$vuln.$severity
            }
        }
        if ($vuln.PSObject.Properties.Name -contains "total") {
            $summary.total = [int]$vuln.total
        }
    }

    if ($AuditResult.json -and $AuditResult.json.vulnerabilities) {
        foreach ($package in $AuditResult.json.vulnerabilities.PSObject.Properties) {
            $value = $package.Value
            $severity = $value.severity
            if ([string]::IsNullOrWhiteSpace($severity) -and $value.via) {
                $severity = @($value.via | Where-Object { $_.severity } | Select-Object -ExpandProperty severity -First 1)
            }

            $summary.packages.Add([ordered]@{
                name = $package.Name
                severity = $severity
                via = $value.via
                effects = $value.effects
                range = $value.range
                fixAvailable = $value.fixAvailable
            })
        }

        if ($summary.total -eq 0) {
            $summary.total = $summary.packages.Count
        }
    }

    return $summary
}

function Format-NpmOutdatedFinding {
    param(
        [Parameter(Mandatory = $true)]
        $Finding
    )

    $location = if ($Finding.location) { " @ $($Finding.location)" } else { "" }
    $type = if ($Finding.type) { " [$($Finding.type)]" } else { "" }
    return ("{0}{1}{2}: {3} -> {4} (latest {5})" -f $Finding.name, $type, $location, $Finding.current, $Finding.wanted, $Finding.latest)
}

function Format-NpmVulnerabilityFinding {
    param(
        [Parameter(Mandatory = $true)]
        $Finding
    )

    $severity = if ($Finding.severity) { $Finding.severity } else { "unknown" }
    return ("{0}: {1}" -f $Finding.name, $severity)
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
        [System.Collections.Generic.List[object]]$Results,
        [string[]]$Artifacts = @()
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
                artifacts = @($previous.artifacts)
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
            $oldPreference = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            try {
                $output = & $Action 2>&1
            }
            finally {
                $ErrorActionPreference = $oldPreference
            }
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
        artifacts = @($Artifacts)
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
    $lines.Add("| Phase | Status | Seconds | Command | Log | Artifacts |")
    $lines.Add("| :--- | :---: | ---: | :--- | :--- | :--- |")
    foreach ($r in $Results) {
        $relativeLog = Resolve-Path -LiteralPath $r.log -ErrorAction SilentlyContinue
        $logText = if ($relativeLog) { $relativeLog.Path } else { $r.log }
        $artifactText = ""
        if ($r.artifacts) {
            $artifactText = (@($r.artifacts) | ForEach-Object {
                $artifactPath = Resolve-Path -LiteralPath $_ -ErrorAction SilentlyContinue
                if ($artifactPath) { $artifactPath.Path } else { $_ }
            }) -join "<br>"
        }
        $escapedCommand = ($r.command -replace '\|', '\|')
        $lines.Add(('| {0} | {1} | {2} | `{3}` | `{4}` | {5} |' -f $r.name, $r.status, $r.elapsedSeconds, $escapedCommand, $logText, $artifactText))
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

if ($Explain) {
    Show-PreReleasePlan
    exit 0
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

    # Early, fast tripwire: catch real credentials (keys/provider tokens) before they reach the
    # public repo (GitGuardian only fires post-push). High-signal patterns only; allowlist in the script.
    Invoke-LoggedPhase "Secret scan" `
        "node scripts/scan-secrets.js" `
        { & node "scripts/scan-secrets.js"; if ($LASTEXITCODE -ne 0) { throw "Secret scan found potential secret(s) (exit $LASTEXITCODE)." } } `
        $previousPhaseMap $fingerprint $results

    Invoke-LoggedPhase "Dotnet restore" `
        "dotnet restore ETL-SQL.slnx" `
        { & dotnet restore "ETL-SQL.slnx" } `
        $previousPhaseMap $fingerprint $results

    Invoke-LoggedPhase "Dependency-audit self-test" `
        ".\scripts\Test-DependencyAudit.ps1" `
        { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\Test-DependencyAudit.ps1" } `
        $previousPhaseMap $fingerprint $results

    Invoke-LoggedPhase "NuGet dependency audit" `
        "dotnet list ETL-SQL.slnx package --outdated/--deprecated/--vulnerable --include-transitive --format json --no-restore" `
        {
            # Reliable under SDK 10.0.300 + CPM: solution-level audit with per-project fallback for
            # deprecated/vulnerable, and a hard, actionable failure if no authoritative audit can run.
            # Keep the diagnostic lines in the phase log; drop only the returned summary object.
            Invoke-NuGetDependencyAudit -RepoRoot $RepoRoot -Solution "ETL-SQL.slnx" |
                Where-Object { $_ -is [string] }
        } `
        $previousPhaseMap $fingerprint $results

    # The release attaches sbom.json; generate it and assert its component version matches the single
    # source of truth so a broken generator or a re-hardcoded version is caught before release.
    Invoke-LoggedPhase "SBOM generation" `
        "node scripts/generate-sbom.js (component version must match Directory.Build.props)" `
        {
            & node "scripts/generate-sbom.js"
            if ($LASTEXITCODE -ne 0) { throw "generate-sbom.js failed with exit code $LASTEXITCODE" }
            $expected = [regex]::Match((Get-Content "Directory.Build.props" -Raw), '<VersionPrefix>([\d.]+)</VersionPrefix>').Groups[1].Value
            $actual = (Get-Content "release/sbom.json" -Raw | ConvertFrom-Json).metadata.component.version
            if ($actual -ne $expected) { throw "SBOM version '$actual' does not match Directory.Build.props '$expected'." }
            Write-Output "SBOM component version $actual matches Directory.Build.props."
        } `
        $previousPhaseMap $fingerprint $results

    Invoke-LoggedPhase "Dotnet build" `
        "dotnet build ETL-SQL.slnx --configuration $Configuration --no-restore" `
        { & dotnet build "ETL-SQL.slnx" "--configuration" $Configuration "--no-restore" } `
        $previousPhaseMap $fingerprint $results

    # Matches the CI 'dotnet format --verify-no-changes' gate so formatting drift fails locally
    # (a fast static check) before the long test lanes run. On drift, the fix is applied automatically
    # with 'dotnet format' so it never has to be run by hand — the phase then fails with an actionable
    # message so the reformatted files are reviewed and committed (and thus reach CI), then re-run.
    Invoke-LoggedPhase "Format verify" `
        "dotnet format ETL-SQL.slnx --verify-no-changes --no-restore (auto-applies 'dotnet format' on drift)" `
        {
            & dotnet format "ETL-SQL.slnx" "--verify-no-changes" "--no-restore"
            if ($LASTEXITCODE -ne 0) {
                Write-Output "Formatting drift detected. Applying 'dotnet format' to fix it..."
                & dotnet format "ETL-SQL.slnx" "--no-restore"
                if ($LASTEXITCODE -ne 0) {
                    throw "dotnet format failed to apply fixes (exit $LASTEXITCODE)."
                }
                throw "Formatting drift was found and automatically fixed in the working tree. Review and commit the reformatted files, then re-run (use -Resume)."
            }
        } `
        $previousPhaseMap $fingerprint $results

    Invoke-LoggedPhase "Smoke lane" `
        ".\scripts\test-lane.ps1 -Lane smoke -Configuration $Configuration -NoRestore -NoBuild" `
        { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\test-lane.ps1" "-Lane" "smoke" "-Configuration" $Configuration "-NoRestore" "-NoBuild" } `
        $previousPhaseMap $fingerprint $results

    Invoke-LoggedPhase "Fast lane" `
        ".\scripts\test-lane.ps1 -Lane fast -Configuration $Configuration -NoRestore -NoBuild" `
        { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\test-lane.ps1" "-Lane" "fast" "-Configuration" $Configuration "-NoRestore" "-NoBuild" } `
        $previousPhaseMap $fingerprint $results

    # Explicit release gate: prove the in-place N->N+1 upgrade drill on its own so it can never be
    # silently lost inside the broad fast lane. (UpgradePathDrillTests is Category=Portal, so the fast
    # lane also exercises it; this named phase makes the upgrade gate visible and independently logged.)
    Invoke-LoggedPhase "N->N+1 upgrade-path drill" `
        "dotnet test tests\ETL-SQL.ReportPortal.Tests\ETL-SQL.ReportPortal.Tests.csproj --filter FullyQualifiedName~UpgradePathDrillTests --configuration $Configuration --no-restore --no-build" `
        { & dotnet test "tests\ETL-SQL.ReportPortal.Tests\ETL-SQL.ReportPortal.Tests.csproj" "--filter" "FullyQualifiedName~UpgradePathDrillTests" "--configuration" $Configuration "--no-restore" "--no-build" } `
        $previousPhaseMap $fingerprint $results

    Invoke-LoggedPhase "Sample scripts" `
        ".\scripts\Test-AllSamples.ps1" `
        { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\Test-AllSamples.ps1" } `
        $previousPhaseMap $fingerprint $results

    Invoke-LoggedPhase "HA soak contract gate" `
        ".\scripts\Test-HaSoakContracts.ps1" `
        { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\Test-HaSoakContracts.ps1" } `
        $previousPhaseMap $fingerprint $results

    if ($IncludeSlt) {
        Invoke-LoggedPhase "SLT lane" `
            ".\scripts\test-lane.ps1 -Lane slt -Configuration $Configuration -NoRestore -NoBuild" `
            { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\test-lane.ps1" "-Lane" "slt" "-Configuration" $Configuration "-NoRestore" "-NoBuild" } `
            $previousPhaseMap $fingerprint $results
    }

    if (-not $EffectiveSkipNode) {
        Invoke-LoggedPhase "VS Code npm ci" `
            "npm ci (src\etl-sql-vscode)" `
            { Push-Location "src\etl-sql-vscode"; try { & npm ci } finally { Pop-Location } } `
            $previousPhaseMap $fingerprint $results

        Invoke-LoggedPhase "VS Code npm audit" `
            "npm outdated / npm audit (src\etl-sql-vscode, src\etl-sql-vscode\ui)" `
            {
                $npmRoots = @(
                    [ordered]@{ label = "src\etl-sql-vscode"; path = "src\etl-sql-vscode" },
                    [ordered]@{ label = "src\etl-sql-vscode\ui"; path = "src\etl-sql-vscode\ui" }
                )

                $totalOutdated = 0
                foreach ($root in $npmRoots) {
                    $outdated = Invoke-NpmJsonCommand -WorkingDirectory $root.path -Arguments @("outdated", "--json") -AllowNonZeroExit
                    $outdatedFindings = @(Get-NpmOutdatedFindings -AuditResult $outdated)
                    $totalOutdated += $outdatedFindings.Count

                    Write-Output ("[{0}] Outdated packages: {1}" -f $root.label, $outdatedFindings.Count)
                    foreach ($finding in ($outdatedFindings | Select-Object -First 20)) {
                        Write-Output ("  - {0}" -f (Format-NpmOutdatedFinding $finding))
                    }
                    if ($outdatedFindings.Count -gt 20) {
                        Write-Output ("  - ... and {0} more" -f ($outdatedFindings.Count - 20))
                    }

                    $audit = Invoke-NpmJsonCommand -WorkingDirectory $root.path -Arguments @("audit", "--json") -AllowNonZeroExit
                    $auditFindings = Get-NpmAuditFindings -AuditResult $audit
                    Write-Output ("[{0}] Vulnerabilities: total={1}, low={2}, moderate={3}, high={4}, critical={5}" -f $root.label, $auditFindings.total, $auditFindings.low, $auditFindings.moderate, $auditFindings.high, $auditFindings.critical)

                    foreach ($finding in ($auditFindings.packages | Select-Object -First 20)) {
                        Write-Output ("  - {0}" -f (Format-NpmVulnerabilityFinding $finding))
                    }
                    if ($auditFindings.packages.Count -gt 20) {
                        Write-Output ("  - ... and {0} more" -f ($auditFindings.packages.Count - 20))
                    }

                    if ($auditFindings.total -gt 0 -or $auditFindings.packages.Count -gt 0) {
                        throw "npm audit found vulnerabilities in $($root.label). Update dependencies before shipping."
                    }
                }

                Write-Output ("Total outdated npm packages across audited roots: {0}" -f $totalOutdated)
            } `
            $previousPhaseMap $fingerprint $results

        Invoke-LoggedPhase "VS Code compile" `
            "npm run compile (src\etl-sql-vscode)" `
            { Push-Location "src\etl-sql-vscode"; try { & npm run compile } finally { Pop-Location } } `
            $previousPhaseMap $fingerprint $results

        # Exercise the same 'vsce package' the tag-triggered release.yml runs (via publish_vsix.ps1),
        # so packaging/manifest errors (e.g. @types/vscode > engines.vscode, missing icon/README) are
        # caught locally and cheaply instead of failing the expensive cross-platform release build.
        Invoke-LoggedPhase "VS Code VSIX package" `
            "npx @vscode/vsce package --target win32-x64 (manifest/packaging validation)" `
            {
                Push-Location "src\etl-sql-vscode"
                try {
                    $vsixOut = Join-Path ([System.IO.Path]::GetTempPath()) "etl-sql-vsce-validate.vsix"
                    & npx "@vscode/vsce" package "--target" "win32-x64" "--out" $vsixOut
                    $code = $LASTEXITCODE
                    Remove-Item $vsixOut -Force -ErrorAction SilentlyContinue
                    if ($code -ne 0) { throw "vsce package validation failed with exit code $code" }
                }
                finally { Pop-Location }
            } `
            $previousPhaseMap $fingerprint $results

        Invoke-LoggedPhase "VS Code unit tests" `
            "npm run test:unit (src\etl-sql-vscode)" `
            { Push-Location "src\etl-sql-vscode"; try { & npm run test:unit } finally { Pop-Location } } `
            $previousPhaseMap $fingerprint $results
    }

    if (-not $EffectiveSkipScale) {
        Invoke-LoggedPhase "Scale certification smoke" `
            ".\scripts\Test-ScaleCertification.ps1 -Tier Smoke" `
            { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\Test-ScaleCertification.ps1" "-Tier" "Smoke" } `
            $previousPhaseMap $fingerprint $results

        $smokeBaselineReport = Join-Path $RunDir "cert-baseline-smoke.md"
        Invoke-LoggedPhase "Cert baseline regression check (smoke)" `
            ".\scripts\Compare-CertBaseline.ps1 -MarkdownReport $smokeBaselineReport" `
            { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\Compare-CertBaseline.ps1" "-MarkdownReport" $smokeBaselineReport } `
            $previousPhaseMap $fingerprint $results @($smokeBaselineReport)
    }

    if ($EffectiveIncludeDockerIntegration) {
        Invoke-LoggedPhase "Docker integration lane" `
            ".\scripts\test-lane.ps1 -Lane integration -Configuration $Configuration -NoRestore -NoBuild" `
            { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\test-lane.ps1" "-Lane" "integration" "-Configuration" $Configuration "-NoRestore" "-NoBuild" } `
            $previousPhaseMap $fingerprint $results
    }

    if ($EffectiveIncludeStandardScale) {
        Invoke-LoggedPhase "Scale certification standard" `
            ".\scripts\Test-ScaleCertification.ps1 -Tier Standard" `
            { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\Test-ScaleCertification.ps1" "-Tier" "Standard" } `
            $previousPhaseMap $fingerprint $results

        $standardBaselineReport = Join-Path $RunDir "cert-baseline-standard.md"
        Invoke-LoggedPhase "Cert baseline regression check (standard)" `
            ".\scripts\Compare-CertBaseline.ps1 -MarkdownReport $standardBaselineReport" `
            { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\Compare-CertBaseline.ps1" "-MarkdownReport" $standardBaselineReport } `
            $previousPhaseMap $fingerprint $results @($standardBaselineReport)

        # Release configuration is already built by the Dotnet build phase, hence -SkipBuild.
        Invoke-LoggedPhase "Spill allocation budget (10M)" `
            ".\scripts\Test-SpillAllocProfile.ps1 -Rows 10000000 -SkipBuild" `
            { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\Test-SpillAllocProfile.ps1" "-Rows" "10000000" "-SkipBuild" } `
            $previousPhaseMap $fingerprint $results
    }

    if ($EffectiveBuildInstallers) {
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
