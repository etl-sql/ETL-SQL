# Validate retained reports without rebuilding or rerunning their certification suites.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$CandidateSha,
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseEvidence.Helpers.ps1')
. (Join-Path $PSScriptRoot 'ProductionCanaryEvidence.Helpers.ps1')
$root = (Resolve-Path -LiteralPath $Directory).Path
$checklist = Get-Content -LiteralPath (Join-Path $root 'checklist.json') -Raw | ConvertFrom-Json
$cleanFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($CandidateSha))).ToLowerInvariant()
function Get-ReportPath([string]$Id) {
    $rows = @($checklist.reports | Where-Object id -CEQ $Id)
    if ($rows.Count -ne 1) { throw 'Required operator report is missing or duplicated.' }
    Resolve-ReleaseEvidenceFile -Root $root -RelativePath $rows[0].path
}
function Require-Canary {
    $path = Get-ReportPath 'canary'
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($report.schema -cne 'etl-sql.production-canary-certification/v1' -or
        $report.commit -cne $CandidateSha -or $report.sourceFingerprint -cne $cleanFingerprint -or
        $report.dirty -isnot [bool] -or $report.dirty -or $report.configuration -cne 'Release' -or
        $report.noBuild -isnot [bool] -or $report.noBuild -or $report.releaseEligible -isnot [bool] -or -not $report.releaseEligible -or
        $report.result -cne 'Passed' -or $report.testExitCode -ne 0 -or $report.evidenceScope -cne 'DeterministicCanaryContracts') {
        throw 'Canary certification has incomplete candidate, execution or scope evidence.'
    }
    $start = Assert-ReleaseEvidenceTimestamp $report.startedUtc 7 'canary start'
    $end = Assert-ReleaseEvidenceTimestamp $report.completedUtc 7 'canary completion'
    if ($end -lt $start -or $end -gt (ConvertTo-DeploymentEvidenceTime $checklist.reviewedAt)) { throw 'Canary certification is outside the review window.' }
    $planPath = Join-Path $PSScriptRoot '../tests/fixtures/production-canary-plan.json'
    if ($report.plan.sha256 -cne (Get-FileHash -LiteralPath $planPath -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Canary plan changed.' }
    $plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
    $bundle = Split-Path -Parent $path
    $journeys = Get-Content -LiteralPath (Resolve-ReleaseEvidenceFile $bundle $report.journeyEvidence) -Raw | ConvertFrom-Json
    $provisioning = Get-Content -LiteralPath (Resolve-ReleaseEvidenceFile $bundle $report.provisioningEvidence) -Raw | ConvertFrom-Json
    $credentials = Get-Content -LiteralPath (Resolve-ReleaseEvidenceFile $bundle $report.credentialLifecycleEvidence) -Raw | ConvertFrom-Json
    if (@(Get-ProductionCanaryRunFindings $plan $journeys).Count -ne 0 -or
        @(Get-ProductionCanaryBoundaryFindings $plan $provisioning $credentials).Count -ne 0 -or
        $report.runFindingCount -ne 0 -or $report.boundaryFindingCount -ne 0 -or
        $report.journeyCount -ne $plan.journeys.Count -or $report.actualRunCount -ne @($journeys.runs).Count -or
        $report.expectedRunCount -ne @($journeys.runs).Count) { throw 'Detailed canary contracts did not pass.' }
    if ((Get-Item -LiteralPath (Resolve-ReleaseEvidenceFile $bundle $report.testLog)).Length -eq 0) { throw 'Canary execution log is empty.' }
}

$index = Get-ReportPath 'deployment'
$null = @(Get-ReleaseCertificationInputs -EvidenceRoot (Split-Path -Parent $index) -ReleaseVersion $Version `
    -CandidateCommit $CandidateSha -RequireCompleteClaimIndex -RequireValidatedExecutionPlan)
$claims = Get-Content -LiteralPath $index -Raw | ConvertFrom-Json
if ((ConvertTo-DeploymentEvidenceTime $claims.generatedUtc) -gt (ConvertTo-DeploymentEvidenceTime $checklist.reviewedAt)) { throw 'Deployment evidence was created after operator review.' }
Require-Canary
$recovery = Get-Content -LiteralPath (Get-ReportPath 'recovery') -Raw | ConvertFrom-Json
$recoveryRow = @($checklist.reports | Where-Object id -CEQ 'recovery')[0]
$cliVersion = [IO.File]::ReadAllText((Resolve-ReleaseEvidenceFile $root $recoveryRow.cliVersionLog)).Trim()
if ($cliVersion -cne "$Version+$CandidateSha" -or
    (Get-Item -LiteralPath (Resolve-ReleaseEvidenceFile $root $recoveryRow.transcript)).Length -eq 0 -or
    $recovery.appVersion -cne "$Version.0") { throw 'Recovery validation lacks candidate CLI and backup version evidence.' }
if ($recovery.schemaVersion -cne '1.0' -or $recovery.operation -cne 'validate' -or $recovery.status -cne 'Pass' -or
    [string]::IsNullOrWhiteSpace($recovery.backupId) -or $recovery.backupId -cne $recovery.keysBackupId -or
    @($recovery.missingDependencies).Count -ne 0 -or @($recovery.operatorActions).Count -eq 0 -or
    $recovery.restored -isnot [bool] -or $recovery.restored -or $recovery.fileCount -le 0 -or
    $recovery.missingDependencies -isnot [array] -or $recovery.operatorActions -isnot [array] -or
    $null -eq $recovery.achievedRpoSeconds -or $null -eq $recovery.dataLossWindowSeconds -or
    $recovery.achievedRpoSeconds -lt 0 -or $recovery.dataLossWindowSeconds -ne $recovery.achievedRpoSeconds) { throw 'Recovery report is incomplete or failed.' }
$recoveryTime = Assert-ReleaseEvidenceTimestamp $recovery.generatedUtc 7 'recovery validation'
if ($recoveryTime -gt (ConvertTo-DeploymentEvidenceTime $checklist.reviewedAt)) { throw 'Recovery validation occurred after operator review.' }
Write-Output 'Deployment, canary and recovery report contracts verified; operator applicability decisions remain submitted reviews.'
