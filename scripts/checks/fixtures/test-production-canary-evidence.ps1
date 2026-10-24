$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../ProductionCanaryEvidence.Helpers.ps1')
$plan = Get-Content (Join-Path $PSScriptRoot '../../../tests/fixtures/production-canary-plan.json') -Raw | ConvertFrom-Json
$faults = [ordered]@{
    None = 'None'; EtlSqlCorrectnessFailure = 'EtlSqlCorrectness'; EtlSqlAvailabilityRegression = 'EtlSqlAvailability'
    EtlSqlLatencyRegression = 'EtlSqlLatency'; SyntheticDependencyOutage = 'SyntheticDependency'
}
$runs = @(foreach ($journey in $plan.journeys) {
    foreach ($region in $journey.regions) {
        foreach ($domain in $journey.failureDomains) {
            foreach ($fault in $faults.Keys) {
                $route = if ($fault -eq 'SyntheticDependencyOutage') { $journey.dependencyAlertRoute } else { $journey.etlSqlAlertRoute }
                [pscustomobject]@{
                    request = @{ journey = $journey; region = $region; failureDomain = $domain; fault = $fault; isolation = $plan.isolation }
                    passed = $true; isolationSatisfied = $true; sloSatisfied = $fault -eq 'None'; failureKind = $faults[$fault]
                    alertRoute = if ($fault -eq 'None') { $null } else { $route }
                    alertDelivery = if ($fault -eq 'None') { $null } else { @{ delivered = $true; alertId = 'fixture-alert' } }
                }
            }
        }
    }
})
$report = @{ schema = 'etl-sql.production-canary-evidence/v1'; passed = $true; runs = $runs }
$original = $report | ConvertTo-Json -Depth 20
$planJson = $plan | ConvertTo-Json -Depth 20
function Assert-Rejected {
    param([string]$Name, [scriptblock]$Mutation)
    $candidate = $original | ConvertFrom-Json
    $candidatePlan = $planJson | ConvertFrom-Json
    & $Mutation $candidate $candidatePlan
    $findings = @(Get-ProductionCanaryRunFindings -Plan $candidatePlan -Report $candidate)
    if ($findings.Count -eq 0) { throw "Accepted invalid canary evidence: $Name" }
    Write-Host "Rejected $Name"
}
if (@(Get-ProductionCanaryRunFindings -Plan $plan -Report $report).Count -ne 0) { throw 'Valid complete report failed.' }
$roundTrip = $original | ConvertFrom-Json
if (@(Get-ProductionCanaryRunFindings -Plan $plan -Report $roundTrip).Count -ne 0) { throw 'Valid JSON report failed.' }
$roundTrip.runs[0].request.isolation.maximumMonthlyCostUsd = [long]250
if (@(Get-ProductionCanaryRunFindings -Plan $plan -Report $roundTrip).Count -ne 0) { throw 'Equivalent numeric JSON value failed.' }
Assert-Rejected 'same-count duplicate replacing missing region' { param($r) $r.runs[20] = $r.runs[0] }
Assert-Rejected 'missing run' { param($r) $r.runs = @($r.runs | Select-Object -Skip 1) }
Assert-Rejected 'extra duplicate' { param($r) $r.runs += $r.runs[0] }
Assert-Rejected 'unknown journey' { param($r) $r.runs[0].request.journey.id = 'Unknown' }
Assert-Rejected 'unknown region' { param($r) $r.runs[0].request.region = 'elsewhere' }
Assert-Rejected 'unknown failure domain' { param($r) $r.runs[0].request.failureDomain = 'elsewhere' }
Assert-Rejected 'unknown fault' { param($r) $r.runs[0].request.fault = 'Unknown' }
Assert-Rejected 'wrong fault casing' { param($r) $r.runs[0].request.fault = 'none' }
Assert-Rejected 'false pass flag' { param($r) $r.runs[0].passed = $false }
Assert-Rejected 'string pass flag' { param($r) $r.runs[0].passed = 'true' }
Assert-Rejected 'false isolation flag' { param($r) $r.runs[0].isolationSatisfied = $false }
Assert-Rejected 'wrong tenant' { param($r) $r.runs[0].request.isolation.tenantId = 'other' }
Assert-Rejected 'wrong quota' { param($r) $r.runs[0].request.isolation.quotaPool = 'other' }
Assert-Rejected 'string isolation boolean' { param($r) $r.runs[0].request.isolation.customerNetworkAccessAllowed = 'false' }
Assert-Rejected 'missing isolation' { param($r) $r.runs[0].request.isolation = $null }
Assert-Rejected 'wrong failure attribution' { param($r) $r.runs[1].failureKind = 'SyntheticDependency' }
Assert-Rejected 'wrong alert route' { param($r) $r.runs[1].alertRoute = 'arbitrary-route' }
Assert-Rejected 'missing alert delivery' { param($r) $r.runs[1].alertDelivery = $null }
Assert-Rejected 'undelivered alert' { param($r) $r.runs[1].alertDelivery.delivered = $false }
Assert-Rejected 'string delivered flag' { param($r) $r.runs[1].alertDelivery.delivered = 'true' }
Assert-Rejected 'missing alert identity' { param($r) $r.runs[1].alertDelivery.alertId = '' }
Assert-Rejected 'normal SLO failure' { param($r) $r.runs[0].sloSatisfied = $false }
Assert-Rejected 'normal alert' { param($r) $r.runs[0].alertRoute = 'unexpected' }
Assert-Rejected 'missing report' { param($r) $r.schema = $null }
Assert-Rejected 'string report pass' { param($r) $r.passed = 'true' }
Assert-Rejected 'missing runs' { param($r) $r.runs = $null }
Assert-Rejected 'duplicate plan journey' { param($r, $p) $p.journeys[5] = $p.journeys[0] }
Assert-Rejected 'duplicate plan region' { param($r, $p) $p.journeys[0].regions[1] = $p.journeys[0].regions[0] }
Assert-Rejected 'empty plan domains' { param($r, $p) $p.journeys[0].failureDomains = @() }
Assert-Rejected 'ambiguous routes' { param($r, $p) $p.journeys[0].dependencyAlertRoute = $p.journeys[0].etlSqlAlertRoute }
Write-Host 'Canary run inventory and attribution: valid matrix passed; 30 invalid reports/plans rejected.'

# Synthetic contract receipts use model time, not the wall clock or live-host observations.
$boundary = @{
    provisioning = @{
        schema = 'etl-sql.production-canary-provisioning/v1'; passed = $true; provisionerKind = 'fixture'
        expected = $plan.isolation
        observed = @{ tenantId = $plan.isolation.tenantId; identityId = $plan.isolation.identityId
            resourceNamespace = $plan.isolation.resourceNamespace; quotaPool = $plan.isolation.quotaPool
            enforcedMonthlyCostLimitUsd = 250; customerResourceGrants = 0; customerNetworkRoutes = 0; usesDedicatedCapacity = $true }
    }
    credential = @{
        schema = 'etl-sql.production-canary-credential-lifecycle/v1'; passed = $true; completedUtc = '2026-08-21T00:01:00Z'
        scheduledRotation = @{ previousRevoked = $true; reason = 'scheduled'; completedUtc = '2026-08-21T00:00:00Z'
            previous = @{ credentialId = 'fixture-old'; issuedUtc = '2026-08-20T00:00:00Z'; expiresUtc = '2026-08-22T00:00:00Z'; revoked = $false }
            current = @{ credentialId = 'fixture-scheduled'; issuedUtc = '2026-08-21T00:00:00Z'; expiresUtc = '2026-08-23T00:00:00Z'; revoked = $false } }
        compromiseResponse = @{ previousRevoked = $true; reason = 'compromise'; completedUtc = '2026-08-21T00:01:00Z'
            previous = @{ credentialId = 'fixture-scheduled'; issuedUtc = '2026-08-21T00:00:00Z'; expiresUtc = '2026-08-23T00:00:00Z'; revoked = $false }
            current = @{ credentialId = 'fixture-compromise'; issuedUtc = '2026-08-21T00:01:00Z'; expiresUtc = '2026-08-23T00:01:00Z'; revoked = $false } }
    }
}
$boundaryJson = $boundary | ConvertTo-Json -Depth 16
function Assert-BoundaryRejected {
    param([string]$Name, [scriptblock]$Mutation)
    $value = $boundaryJson | ConvertFrom-Json
    $candidatePlan = $planJson | ConvertFrom-Json
    & $Mutation $value $candidatePlan
    if (@(Get-ProductionCanaryBoundaryFindings $candidatePlan $value.provisioning $value.credential).Count -eq 0) {
        throw "Accepted invalid canary boundary evidence: $Name"
    }
    Write-Host "Rejected boundary $Name"
}
foreach ($value in @($boundary, ($boundaryJson | ConvertFrom-Json))) {
    $findings = @(Get-ProductionCanaryBoundaryFindings $plan $value.provisioning $value.credential)
    if ($findings.Count -ne 0) { throw "Valid synthetic boundary rejected: $findings" }
}
foreach ($field in @('tenantId', 'identityId', 'resourceNamespace', 'quotaPool')) {
    Assert-BoundaryRejected "observed $field" { param($b) $b.provisioning.observed.$field = 'wrong' }
    Assert-BoundaryRejected "expected $field" { param($b) $b.provisioning.expected.$field = 'wrong' }
}
Assert-BoundaryRejected 'missing provisioning' { param($b) $b.provisioning = $null }
Assert-BoundaryRejected 'string provisioning pass' { param($b) $b.provisioning.passed = 'true' }
Assert-BoundaryRejected 'missing provisioner' { param($b) $b.provisioning.provisionerKind = '' }
Assert-BoundaryRejected 'unplanned expected cost' { param($b) $b.provisioning.expected.maximumMonthlyCostUsd = 1000 }
foreach ($cost in @(0, -1, 251, '250', $null)) {
    Assert-BoundaryRejected "invalid enforced cost $cost" { param($b) $b.provisioning.observed.enforcedMonthlyCostLimitUsd = $cost }
}
foreach ($field in @('customerNetworkAccessAllowed', 'customerCapacityAllowed')) {
    Assert-BoundaryRejected "expected permits $field" { param($b) $b.provisioning.expected.$field = $true }
    Assert-BoundaryRejected "string $field" { param($b) $b.provisioning.expected.$field = 'false' }
}
foreach ($field in @('customerResourceGrants', 'customerNetworkRoutes')) {
    Assert-BoundaryRejected "observed $field" { param($b) $b.provisioning.observed.$field = 1 }
    Assert-BoundaryRejected "missing $field" { param($b) $b.provisioning.observed.$field = $null }
}
Assert-BoundaryRejected 'shared capacity' { param($b) $b.provisioning.observed.usesDedicatedCapacity = $false }
Assert-BoundaryRejected 'string capacity flag' { param($b) $b.provisioning.observed.usesDedicatedCapacity = 'true' }
Assert-BoundaryRejected 'missing credential report' { param($b) $b.credential = $null }
Assert-BoundaryRejected 'string credential pass' { param($b) $b.credential.passed = 'true' }
foreach ($field in @('scheduledRotation', 'compromiseResponse')) {
    Assert-BoundaryRejected "missing $field" { param($b) $b.credential.$field = $null }
    Assert-BoundaryRejected "unrevoked $field" { param($b) $b.credential.$field.previousRevoked = $false }
    Assert-BoundaryRejected "string revocation $field" { param($b) $b.credential.$field.previousRevoked = 'true' }
    Assert-BoundaryRejected "wrong reason $field" { param($b) $b.credential.$field.reason = 'other' }
    Assert-BoundaryRejected "reused identity $field" { param($b) $b.credential.$field.current.credentialId = $b.credential.$field.previous.credentialId }
    Assert-BoundaryRejected "empty replacement $field" { param($b) $b.credential.$field.current.credentialId = '' }
    Assert-BoundaryRejected "revoked replacement $field" { param($b) $b.credential.$field.current.revoked = $true }
    Assert-BoundaryRejected "invalid issue time $field" { param($b) $b.credential.$field.current.issuedUtc = 'invalid' }
    Assert-BoundaryRejected "expired replacement $field" { param($b) $b.credential.$field.current.expiresUtc = $b.credential.$field.current.issuedUtc }
    Assert-BoundaryRejected "overlong replacement $field" { param($b) $b.credential.$field.current.expiresUtc = '2027-01-01T00:00:00Z' }
}
Assert-BoundaryRejected 'early scheduled rotation' { param($b) $b.credential.scheduledRotation.previous.issuedUtc = '2026-08-20T23:00:00Z' }
Assert-BoundaryRejected 'unrelated compromise credential' { param($b) $b.credential.compromiseResponse.previous.credentialId = 'unrelated' }
Assert-BoundaryRejected 'inconsistent lifecycle completion' { param($b) $b.credential.completedUtc = '2026-08-20T00:00:00Z' }
Assert-BoundaryRejected 'invalid rotation plan' { param($b, $p) $p.credentialRotationInterval = 'invalid' }
Assert-BoundaryRejected 'overlong rotation plan' { param($b, $p) $p.credentialRotationInterval = $p.maximumCredentialLifetime }
Write-Host 'Canary provisioning and credential details reject corrupt receipts despite unchanged Passed summaries.'
