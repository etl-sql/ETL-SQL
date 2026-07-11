<#
.SYNOPSIS
    Validates completed PostgreSQL HA soak evidence before it is cited.

.DESCRIPTION
    Phase 6 soaks are operator-run. This script is the cheap evidence gate: it verifies that the
    generated topology metadata, evidence plan, sustained-load report, metrics snapshot, and optional
    large-job/fault artifacts exist, are non-secret, and represent passing results.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TopologyRunRoot,

    [ValidateSet('Sustained', 'LargeJob', 'FaultInjection', 'All')]
    [string]$RequiredGate = 'Sustained',

    [string]$RequiredCommit = '',

    [switch]$AllowDirty,

    [string]$MarkdownReport = ''
)

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot '..')
$EvidenceRoot = (Resolve-Path -LiteralPath (Get-Location)).Path
$issues = New-Object System.Collections.Generic.List[object]
$checkedArtifacts = New-Object System.Collections.Generic.List[string]

function Add-Issue {
    param([string]$Level, [string]$Kind, [string]$Message)
    $script:issues.Add([pscustomobject]@{
        level = $Level
        kind = $Kind
        message = $Message
    }) | Out-Null
}

function Get-RequiredCommit {
    if (-not [string]::IsNullOrWhiteSpace($RequiredCommit)) { return $RequiredCommit }
    try {
        $commit = & git -C $RepoRoot rev-parse HEAD 2>$null
        if ($LASTEXITCODE -eq 0) { return [string]$commit }
    } catch {
        return ''
    }
    return ''
}

function Assert-File {
    param([string]$Path, [string]$Kind)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-Issue 'Error' 'missing-artifact' "$Kind not found: $Path"
        return $false
    }
    $script:checkedArtifacts.Add((Resolve-Path -LiteralPath $Path).Path) | Out-Null
    return $true
}

function Read-Json {
    param([string]$Path, [string]$Kind)
    if (-not (Assert-File $Path $Kind)) { return $null }
    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    } catch {
        Add-Issue 'Error' 'invalid-json' "$Kind is not valid JSON: $($_.Exception.Message)"
        return $null
    }
}

function Test-Redaction {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    $text = Get-Content -LiteralPath $Path -Raw
    $secretPatterns = @(
        'PG_PASSWORD\s*=\s*(?!\*{4,})\S+',
        'PORTAL_JWT_SECRET\s*=\s*(?!\*{4,})\S+',
        'PORTAL_DATASET_KEY\s*=\s*(?!\*{4,})\S+',
        'ORCH_API_KEY\s*=\s*(?!\*{4,})\S+',
        '"apiKey"\s*:\s*"(?!\*{4,}|CHANGE_ME")([^"]+)"',
        '"password"\s*:\s*"(?!\*{4,}|CHANGE_ME")([^"]+)"'
    )
    foreach ($pattern in $secretPatterns) {
        if ($text -match $pattern) {
            Add-Issue 'Error' 'secret-leak' "Potential secret value found in $Path"
            return
        }
    }
}

function Test-WorktreeClean {
    if ($AllowDirty) { return }
    try {
        $status = & git -C $RepoRoot status --porcelain 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($status -join ''))) {
            Add-Issue 'Warning' 'dirty-worktree' 'Worktree is dirty; evidence can still be reviewed but should not be published as final certification evidence.'
        }
    } catch {
        Add-Issue 'Warning' 'git-unavailable' 'Could not inspect git worktree status.'
    }
}

function Test-CapacityReport {
    param([string]$Path)
    $report = Read-Json $Path 'capacity report'
    if ($null -eq $report) { return }
    foreach ($serviceName in @('portal', 'orchestrator')) {
        foreach ($step in @($report.$serviceName)) {
            if ($step.passed -ne $true) {
                Add-Issue 'Error' 'capacity-breach' "$serviceName step at concurrency $($step.concurrency) did not pass."
            }
            if (@($step.breaches).Count -gt 0) {
                Add-Issue 'Error' 'capacity-breach' "$serviceName step at concurrency $($step.concurrency) reported breaches: $(@($step.breaches) -join '; ')"
            }
        }
    }
}

function Test-GenericPassedReport {
    param([string]$Path, [string]$Kind)
    $report = Read-Json $Path $Kind
    if ($null -eq $report) { return }
    if ($null -ne $report.PSObject.Properties['passed'] -and $report.passed -ne $true) {
        Add-Issue 'Error' 'failed-report' "$Kind did not pass."
    }
    if ($null -ne $report.PSObject.Properties['status'] -and [string]$report.status -notin @('Passed', 'Pass', 'Succeeded', 'Success')) {
        Add-Issue 'Error' 'failed-report' "$Kind status is $($report.status)."
    }
}

function Write-MarkdownSummary {
    param([object]$Summary, [string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    $parent = Split-Path -Parent $Path
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $lines = @(
        '# HA Soak Evidence Validation',
        '',
        ('Run id: `{0}`' -f $Summary.runId),
        ('Required gate: `{0}`' -f $Summary.requiredGate),
        ('Status: **{0}**' -f $Summary.status),
        ('Checked artifacts: `{0}`' -f $Summary.checkedArtifactCount),
        '',
        '## Issues',
        ''
    )
    if (@($Summary.issues).Count -eq 0) {
        $lines += '- None'
    } else {
        foreach ($issue in @($Summary.issues)) {
            $lines += '- **{0} / {1}**: {2}' -f $issue.level, $issue.kind, $issue.message
        }
    }
    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

$runRoot = Resolve-Path -LiteralPath $TopologyRunRoot
$metadataPath = Join-Path $runRoot.Path 'topology-metadata.json'
$evidencePlanPath = Join-Path $runRoot.Path 'ha-soak-evidence-plan.json'
$metadata = Read-Json $metadataPath 'topology metadata'
$evidencePlan = Read-Json $evidencePlanPath 'evidence plan'

Test-WorktreeClean

if ($null -ne $metadata) {
    $expectedCommit = Get-RequiredCommit
    $actualCommit = if ($metadata.PSObject.Properties['commit']) { [string]$metadata.commit } else { '' }
    if ([string]::IsNullOrWhiteSpace($actualCommit)) {
        Add-Issue 'Warning' 'missing-commit' 'Topology metadata does not include a source commit; regenerate topology metadata before publishing final certification evidence.'
    } elseif (-not [string]::IsNullOrWhiteSpace($expectedCommit) -and $actualCommit -ne $expectedCommit) {
        Add-Issue 'Error' 'commit-mismatch' "Topology metadata commit $actualCommit does not match required commit $expectedCommit."
    }
}

$runId = if ($metadata -and $metadata.PSObject.Properties['runId']) { [string]$metadata.runId } else { Split-Path -Leaf $runRoot.Path }
$sustainedDir = Join-Path $EvidenceRoot "certification-results/postgres-ha-soak/$runId"
$largeJobDir = Join-Path $EvidenceRoot "certification-results/ha-large-job-soak/$runId"
$faultDir = Join-Path $EvidenceRoot "certification-results/ha-fault-injection/$runId"

if ($RequiredGate -in @('Sustained', 'All')) {
    $capacityJson = Join-Path $sustainedDir 'capacity-report.json'
    $capacityMd = Join-Path $sustainedDir 'capacity-report.md'
    $metricsJson = Join-Path $sustainedDir 'postgres-ha-metrics.json'
    $metricsMd = Join-Path $sustainedDir 'postgres-ha-metrics.md'
    Test-CapacityReport $capacityJson
    Assert-File $capacityMd 'capacity Markdown report' | Out-Null
    Read-Json $metricsJson 'PostgreSQL metrics snapshot' | Out-Null
    Assert-File $metricsMd 'PostgreSQL metrics Markdown report' | Out-Null
}

if ($RequiredGate -in @('LargeJob', 'All')) {
    Read-Json (Join-Path $largeJobDir 'ha-large-job-soak-plan.json') 'large-job soak plan' | Out-Null
    Assert-File (Join-Path $largeJobDir 'ha-large-job-soak-plan.md') 'large-job soak plan Markdown' | Out-Null
    Test-GenericPassedReport (Join-Path $largeJobDir 'soak-report.json') 'large-job soak report'
    Assert-File (Join-Path $largeJobDir 'soak-report.md') 'large-job soak Markdown report' | Out-Null
}

if ($RequiredGate -in @('FaultInjection', 'All')) {
    Read-Json (Join-Path $faultDir 'ha-fault-injection-plan.json') 'fault-injection plan' | Out-Null
    Assert-File (Join-Path $faultDir 'ha-fault-injection-plan.md') 'fault-injection plan Markdown' | Out-Null
    Test-GenericPassedReport (Join-Path $faultDir 'fault-report.json') 'fault-injection report'
    Assert-File (Join-Path $faultDir 'fault-report.md') 'fault-injection Markdown report' | Out-Null
}

$checkedArtifactArray = $checkedArtifacts.ToArray()
$issueArray = $issues.ToArray()

foreach ($artifact in $checkedArtifactArray) {
    Test-Redaction $artifact
}

$issueArray = $issues.ToArray()
$status = if (@($issueArray | Where-Object { $_.level -eq 'Error' }).Count -eq 0) { 'Passed' } else { 'Failed' }
$summary = [pscustomobject]@{
    schemaVersion = 1
    phase = 'v0.15.0 Phase 6'
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    runId = $runId
    requiredGate = $RequiredGate
    status = $status
    checkedArtifactCount = @($checkedArtifactArray).Count
    issues = @($issueArray)
}

Write-MarkdownSummary $summary $MarkdownReport
$summary

if ($status -ne 'Passed') {
    exit 1
}
