# Check the deployment and production-canary report contracts that Release Evidence produced (W8).
# The Profile and Transition jobs each write a claims index for their own kind; this merges them
# into one complete index and validates it with the release helpers, then checks the canary report
# against its canonical plan. It reruns no certification.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProfileRoot,
    [Parameter(Mandatory)][string]$TransitionRoot,
    [Parameter(Mandatory)][string]$CanaryRoot,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$CandidateSha,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$OutDir
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseEvidence.Helpers.ps1')
. (Join-Path $PSScriptRoot 'ProductionCanaryEvidence.Helpers.ps1')

if (Test-Path -LiteralPath $OutDir) { throw 'OutDir must not exist; merged evidence is never written over earlier output.' }
$merged = New-Item -ItemType Directory -Path $OutDir
$claims = @()
$generated = @()
foreach ($source in @($ProfileRoot, $TransitionRoot)) {
    $root = (Resolve-Path -LiteralPath $source).Path
    $index = Get-Content -LiteralPath (Resolve-ReleaseEvidenceFile -Root $root -RelativePath 'claims-index.json') -Raw | ConvertFrom-Json
    if ($index.schemaVersion -cne 'etl-sql.deployment-profile-release-claims/v1' -or $index.releaseVersion -cne $Version) {
        throw "Claims index in '$source' does not describe v$Version."
    }
    $claims += @($index.claims)
    # ConvertFrom-Json already returns a DateTime; convert it directly so sub-second ticks survive.
    $generated += ConvertTo-DeploymentEvidenceTime $index.generatedUtc
    foreach ($run in Get-ChildItem -LiteralPath $root -Directory) {
        $target = Join-Path $merged $run.Name
        if (Test-Path -LiteralPath $target) { throw "Both deployment jobs wrote run '$($run.Name)'." }
        Copy-Item -LiteralPath $run.FullName -Destination $target -Recurse
    }
}
$mergedIndex = [ordered]@{
    schemaVersion = 'etl-sql.deployment-profile-release-claims/v1'
    releaseVersion = $Version
    generatedUtc = ($generated | Sort-Object -Descending | Select-Object -First 1).ToString('O')
    claims = @($claims | Sort-Object kind, lane)
}
$mergedIndex | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $merged 'claims-index.json') -Encoding utf8
$null = @(Get-ReleaseCertificationInputs -EvidenceRoot $merged -ReleaseVersion $Version -CandidateCommit $CandidateSha `
    -RequireCompleteClaimIndex -RequireValidatedExecutionPlan)
Write-Output "Deployment claims verified: $(@($claims).Count) Profile and Transition lanes passed on $CandidateSha."

$canaryRuns = @(Get-ChildItem -LiteralPath $CanaryRoot -Recurse -Filter certification.json -File)
if ($canaryRuns.Count -ne 1) { throw "Expected one canary certification report, found $($canaryRuns.Count)." }
$path = $canaryRuns[0].FullName
$bundle = Split-Path -Parent $path
$report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
$cleanFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($CandidateSha))).ToLowerInvariant()
if ($report.schema -cne 'etl-sql.production-canary-certification/v1' -or
    $report.commit -cne $CandidateSha -or $report.sourceFingerprint -cne $cleanFingerprint -or
    $report.dirty -isnot [bool] -or $report.dirty -or $report.configuration -cne 'Release' -or
    $report.noBuild -isnot [bool] -or $report.noBuild -or $report.releaseEligible -isnot [bool] -or -not $report.releaseEligible -or
    $report.result -cne 'Passed' -or $report.testExitCode -ne 0 -or $report.evidenceScope -cne 'DeterministicCanaryContracts') {
    throw 'Canary certification has incomplete candidate, execution or scope evidence.'
}
$start = Assert-ReleaseEvidenceTimestamp $report.startedUtc 7 'canary start'
$end = Assert-ReleaseEvidenceTimestamp $report.completedUtc 7 'canary completion'
if ($end -lt $start) { throw 'Canary certification ended before it started.' }
$planPath = Join-Path $PSScriptRoot '../tests/fixtures/production-canary-plan.json'
if ($report.plan.sha256 -cne (Get-FileHash -LiteralPath $planPath -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Canary plan changed.' }
$plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
$journeys = Get-Content -LiteralPath (Resolve-ReleaseEvidenceFile $bundle $report.journeyEvidence) -Raw | ConvertFrom-Json
$provisioning = Get-Content -LiteralPath (Resolve-ReleaseEvidenceFile $bundle $report.provisioningEvidence) -Raw | ConvertFrom-Json
$credentials = Get-Content -LiteralPath (Resolve-ReleaseEvidenceFile $bundle $report.credentialLifecycleEvidence) -Raw | ConvertFrom-Json
if (@(Get-ProductionCanaryRunFindings $plan $journeys).Count -ne 0 -or
    @(Get-ProductionCanaryBoundaryFindings $plan $provisioning $credentials).Count -ne 0 -or
    $report.runFindingCount -ne 0 -or $report.boundaryFindingCount -ne 0 -or
    $report.journeyCount -ne $plan.journeys.Count -or $report.actualRunCount -ne @($journeys.runs).Count -or
    $report.expectedRunCount -ne @($journeys.runs).Count) { throw 'Detailed canary contracts did not pass.' }
if ((Get-Item -LiteralPath (Resolve-ReleaseEvidenceFile $bundle $report.testLog)).Length -eq 0) { throw 'Canary execution log is empty.' }
Write-Output "Production-canary contracts verified: $($report.journeyCount) journeys, $($report.actualRunCount) runs."
