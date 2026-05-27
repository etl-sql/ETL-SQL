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

function Invoke-NuGetPackageAudit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Mode
    )

    $args = @(
        "list",
        "ETL-SQL.slnx",
        "package",
        $Mode,
        "--include-transitive",
        "--format",
        "json",
        "--no-restore"
    )

    $output = & dotnet @args 2>&1
    $exitCode = if ($LASTEXITCODE -ne $null) { $LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        throw "dotnet list package $Mode failed with exit code $exitCode"
    }

    $jsonText = ($output -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($jsonText)) {
        throw "dotnet list package $Mode returned no output."
    }

    return $jsonText | ConvertFrom-Json
}

function Get-NuGetAuditFindings {
    param(
        [Parameter(Mandatory = $true)]
        $AuditResult,

        [Parameter(Mandatory = $true)]
        [ValidateSet("outdated", "deprecated", "vulnerable")]
        [string]$Kind
    )

    $findings = New-Object System.Collections.Generic.List[object]
    if (-not $AuditResult.projects) {
        return $findings
    }

    foreach ($project in $AuditResult.projects) {
        if (-not $project.frameworks) {
            continue
        }

        foreach ($framework in $project.frameworks) {
            foreach ($bucketName in @("topLevelPackages", "transitivePackages")) {
                $packages = $framework.$bucketName
                if (-not $packages) {
                    continue
                }

                foreach ($package in $packages) {
                    switch ($Kind) {
                        "outdated" {
                            if ($package.latestVersion) {
                                $findings.Add([ordered]@{
                                    project = $project.path
                                    framework = $framework.framework
                                    bucket = $bucketName
                                    id = $package.id
                                    requestedVersion = $package.requestedVersion
                                    resolvedVersion = $package.resolvedVersion
                                    latestVersion = $package.latestVersion
                                })
                            }
                        }
                        "deprecated" {
                            if ($package.deprecationReasons) {
                                $findings.Add([ordered]@{
                                    project = $project.path
                                    framework = $framework.framework
                                    bucket = $bucketName
                                    id = $package.id
                                    resolvedVersion = $package.resolvedVersion
                                    deprecationReasons = @($package.deprecationReasons)
                                    alternativePackage = $package.alternativePackage
                                })
                            }
                        }
                        "vulnerable" {
                            if ($package.vulnerabilities -or $package.severity -or $package.advisoryUrl -or $package.advisoryTitle) {
                                $entry = [ordered]@{
                                    project = $project.path
                                    framework = $framework.framework
                                    bucket = $bucketName
                                    id = $package.id
                                    resolvedVersion = $package.resolvedVersion
                                }

                                if ($package.vulnerabilities) {
                                    $entry.vulnerabilities = @($package.vulnerabilities)
                                }
                                if ($package.severity) {
                                    $entry.severity = $package.severity
                                }
                                if ($package.advisoryUrl) {
                                    $entry.advisoryUrl = $package.advisoryUrl
                                }
                                if ($package.advisoryTitle) {
                                    $entry.advisoryTitle = $package.advisoryTitle
                                }

                                $findings.Add($entry)
                            }
                        }
                    }
                }
            }
        }
    }

    return $findings
}

function Format-NuGetFinding {
    param(
        [Parameter(Mandatory = $true)]
        $Finding
    )

    $projectPath = [System.IO.Path]::GetRelativePath($RepoRoot, [string]$Finding.project)
    $scope = if ($Finding.bucket -eq "topLevelPackages") { "top-level" } else { "transitive" }

    switch ($true) {
        { $Finding.latestVersion } {
            return ("{0} [{1}] {2} {3} -> {4}" -f $Finding.id, $scope, $projectPath, $Finding.resolvedVersion, $Finding.latestVersion)
        }
        { $Finding.deprecationReasons } {
            $reasons = ($Finding.deprecationReasons -join ", ")
            return ("{0} [{1}] {2} {3} ({4})" -f $Finding.id, $scope, $projectPath, $Finding.resolvedVersion, $reasons)
        }
        { $Finding.vulnerabilities } {
            $severities = @($Finding.vulnerabilities | ForEach-Object { $_.severity }) -join ", "
            if ([string]::IsNullOrWhiteSpace($severities)) {
                $severities = "unknown severity"
            }
            return ("{0} [{1}] {2} {3} ({4})" -f $Finding.id, $scope, $projectPath, $Finding.resolvedVersion, $severities)
        }
        { $Finding.severity -or $Finding.advisoryUrl -or $Finding.advisoryTitle } {
            $details = @()
            if ($Finding.severity) {
                $details += $Finding.severity
            }
            if ($Finding.advisoryTitle) {
                $details += $Finding.advisoryTitle
            }
            if ($Finding.advisoryUrl) {
                $details += $Finding.advisoryUrl
            }
            return ("{0} [{1}] {2} {3} ({4})" -f $Finding.id, $scope, $projectPath, $Finding.resolvedVersion, ($details -join ", "))
        }
        default {
            return ("{0} [{1}] {2}" -f $Finding.id, $scope, $projectPath)
        }
    }
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
        $output = & npm @Arguments 2>&1
        $exitCode = if ($LASTEXITCODE -ne $null) { $LASTEXITCODE } else { 0 }
        $text = ($output -join "`n").Trim()
        $json = $null

        if (-not [string]::IsNullOrWhiteSpace($text)) {
            $json = $text | ConvertFrom-Json -Depth 100
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

    Invoke-LoggedPhase "NuGet dependency audit" `
        "dotnet list ETL-SQL.slnx package --outdated/--deprecated/--vulnerable --include-transitive --format json --no-restore" `
        {
            $outdatedAudit = Invoke-NuGetPackageAudit -Mode "--outdated"
            $deprecatedAudit = Invoke-NuGetPackageAudit -Mode "--deprecated"
            $vulnerableAudit = Invoke-NuGetPackageAudit -Mode "--vulnerable"

            $outdatedFindings = @(Get-NuGetAuditFindings -AuditResult $outdatedAudit -Kind "outdated")
            $deprecatedFindings = @(Get-NuGetAuditFindings -AuditResult $deprecatedAudit -Kind "deprecated")
            $vulnerableFindings = @(Get-NuGetAuditFindings -AuditResult $vulnerableAudit -Kind "vulnerable")

            Write-Output ("Outdated packages: {0}" -f $outdatedFindings.Count)
            if ($outdatedFindings.Count -gt 0) {
                Write-Output "Recent package updates are available:"
                foreach ($finding in ($outdatedFindings | Select-Object -First 20)) {
                    Write-Output ("  - {0}" -f (Format-NuGetFinding $finding))
                }
                if ($outdatedFindings.Count -gt 20) {
                    Write-Output ("  - ... and {0} more" -f ($outdatedFindings.Count - 20))
                }
            }

            Write-Output ("Deprecated packages: {0}" -f $deprecatedFindings.Count)
            if ($deprecatedFindings.Count -gt 0) {
                foreach ($finding in ($deprecatedFindings | Select-Object -First 20)) {
                    Write-Output ("  - {0}" -f (Format-NuGetFinding $finding))
                }
                if ($deprecatedFindings.Count -gt 20) {
                    Write-Output ("  - ... and {0} more" -f ($deprecatedFindings.Count - 20))
                }
            }

            Write-Output ("Vulnerable packages: {0}" -f $vulnerableFindings.Count)
            if ($vulnerableFindings.Count -gt 0) {
                foreach ($finding in ($vulnerableFindings | Select-Object -First 20)) {
                    Write-Output ("  - {0}" -f (Format-NuGetFinding $finding))
                }
                if ($vulnerableFindings.Count -gt 20) {
                    Write-Output ("  - ... and {0} more" -f ($vulnerableFindings.Count - 20))
                }
            }

            if ($deprecatedFindings.Count -gt 0 -or $vulnerableFindings.Count -gt 0) {
                throw "NuGet audit found deprecated or vulnerable packages. Update or replace them before shipping."
            }
        } `
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

        Invoke-LoggedPhase "Cert baseline regression check (smoke)" `
            ".\scripts\Compare-CertBaseline.ps1" `
            { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\Compare-CertBaseline.ps1" } `
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

        Invoke-LoggedPhase "Cert baseline regression check (standard)" `
            ".\scripts\Compare-CertBaseline.ps1" `
            { & $PowerShellExe "-NoProfile" "-ExecutionPolicy" "Bypass" "-File" ".\scripts\Compare-CertBaseline.ps1" } `
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
