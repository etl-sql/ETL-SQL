# Synthetic complete reports, laid out as the Release Evidence deployment and canary jobs upload
# them, for the real report-contract validator (Test-ReleaseEvidenceReports.ps1). No product tests run.
param([string]$Root, [string]$CandidateSha, [string]$Version)
$ErrorActionPreference = 'Stop'
$scripts = (Get-Item (Join-Path $PSScriptRoot '../..')).FullName
. (Join-Path $scripts 'ReleaseEvidence.Helpers.ps1')
. (Join-Path $PSScriptRoot 'DeploymentEvidence.Fixture.ps1')
$fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($CandidateSha))).ToLowerInvariant()
foreach ($kind in @('Profile', 'Transition')) {
    # Each deployment job writes its own root and claims index, for its own kind only.
    $deployment = Join-Path $Root "deployment-$kind"
    $evidence = @{ root = $deployment; reports = @{}; paths = @{}; index = @{ schemaVersion = 'etl-sql.deployment-profile-release-claims/v1'; releaseVersion = $Version; claims = @() } }
    foreach ($lane in (Get-DeploymentCertificationLanes $kind)) {
        $topology = Get-TopologyClaim $lane
        $relative = "$kind-$lane/certification.json"
        $null = New-Item -ItemType Directory -Path (Join-Path $deployment "$kind-$lane") -Force
        $evidence.paths[$lane] = $relative
        $evidence.reports[$lane] = [ordered]@{
            schemaVersion = 'etl-sql.deployment-profile-certification/v1'; commit = $CandidateSha; sourceFingerprint = $fingerprint
            dirty = $false; releaseEligible = $true; configuration = 'Release'; result = 'Passed'; kind = $kind; lanes = @($lane); topologyClaims = @($topology)
        }
        $evidence.index.claims += [ordered]@{
            lane = $lane; kind = $kind; topology = $topology.topology; claim = $topology.claim
            claimScope = if ($null -eq $topology.claimScope) { 'ProfileAndTransitionContracts' } else { $topology.claimScope }
            sharedSaaS = if ($null -eq $topology.sharedSaaS) { 'N/A' } else { $topology.sharedSaaS }
            uncovered = if ($null -eq $topology.uncovered) { @() } else { @($topology.uncovered) }
            commit = $CandidateSha; result = 'Passed'; releaseEligible = $true; evidence = $relative
        }
    }
    Add-FixtureExecutionEvidence $evidence
    foreach ($lane in $evidence.reports.Keys) { $evidence.reports[$lane] | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $deployment $evidence.paths[$lane]) }
    $evidence.index.generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $evidence.index | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $deployment 'claims-index.json')
}
. (Join-Path $PSScriptRoot 'test-production-canary-evidence.ps1')
$canary = Join-Path $Root 'canary/certification-results/production-canaries/20260101-000000-000'
$journeys = Join-Path $canary 'journey-evidence'
$null = New-Item -ItemType Directory -Path $journeys -Force
$report | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $journeys 'production-canary-report.json')
$boundary.provisioning | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $journeys 'production-canary-provisioning.json')
$boundary.credential | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $journeys 'production-canary-credential-lifecycle.json')
'Synthetic completed test invocation' | Set-Content -LiteralPath (Join-Path $canary 'production-canary-tests.log')
@{
    schema = 'etl-sql.production-canary-certification/v1'; commit = $CandidateSha; sourceFingerprint = $fingerprint
    dirty = $false; configuration = 'Release'; noBuild = $false; releaseEligible = $true; result = 'Passed'; testExitCode = 0
    evidenceScope = 'DeterministicCanaryContracts'; startedUtc = [DateTimeOffset]::UtcNow.AddSeconds(-3).ToString('O'); completedUtc = [DateTimeOffset]::UtcNow.AddSeconds(-2).ToString('O')
    plan = @{ sha256 = (Get-FileHash -LiteralPath (Join-Path $scripts '../tests/fixtures/production-canary-plan.json')).Hash.ToLowerInvariant() }
    journeyCount = $plan.journeys.Count; expectedRunCount = $report.runs.Count; actualRunCount = $report.runs.Count; runFindingCount = 0; boundaryFindingCount = 0
    journeyEvidence = 'journey-evidence/production-canary-report.json'; provisioningEvidence = 'journey-evidence/production-canary-provisioning.json'
    credentialLifecycleEvidence = 'journey-evidence/production-canary-credential-lifecycle.json'; testLog = 'production-canary-tests.log'
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $canary 'certification.json')
