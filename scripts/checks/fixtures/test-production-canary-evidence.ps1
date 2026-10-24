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
