# Structural checks for deterministic canary reports. These do not establish live-host provenance.
function Test-ProductionCanaryNumber {
    param([object]$Value)
    return ($Value -is [int] -or $Value -is [long] -or $Value -is [decimal] -or
        ($Value -is [double] -and [double]::IsFinite($Value)))
}

function Get-ProductionCanaryBoundaryFindings {
    param([object]$Plan, [object]$Provisioning, [object]$Credential)

    if ($Provisioning.schema -cne 'etl-sql.production-canary-provisioning/v1' -or
        $Provisioning.passed -isnot [bool] -or -not $Provisioning.passed -or
        [string]::IsNullOrWhiteSpace($Provisioning.provisionerKind)) {
        'Missing or unsuccessful canary provisioning evidence.'
    }
    foreach ($field in @('tenantId', 'identityId', 'resourceNamespace', 'quotaPool')) {
        $expected = $Plan.isolation.$field
        if ($expected -isnot [string] -or [string]::IsNullOrWhiteSpace($expected) -or
            $Provisioning.expected.$field -isnot [string] -or $Provisioning.expected.$field -cne $expected -or
            $Provisioning.observed.$field -isnot [string] -or $Provisioning.observed.$field -cne $expected) {
            'Canary provisioning identity does not match the plan.'
        }
    }
    foreach ($field in @('customerNetworkAccessAllowed', 'customerCapacityAllowed')) {
        if ($Plan.isolation.$field -isnot [bool] -or $Plan.isolation.$field -or
            $Provisioning.expected.$field -isnot [bool] -or $Provisioning.expected.$field) {
            'Canary provisioning boundary permits customer access or capacity.'
        }
    }
    $limit = $Plan.isolation.maximumMonthlyCostUsd
    $expectedLimit = $Provisioning.expected.maximumMonthlyCostUsd
    $enforced = $Provisioning.observed.enforcedMonthlyCostLimitUsd
    if (-not (Test-ProductionCanaryNumber $limit) -or $limit -le 0 -or
        -not (Test-ProductionCanaryNumber $expectedLimit) -or $expectedLimit -ne $limit -or
        -not (Test-ProductionCanaryNumber $enforced) -or $enforced -le 0 -or $enforced -gt $limit) {
        'Canary provisioning lacks the planned cost guard.'
    }
    foreach ($field in @('customerResourceGrants', 'customerNetworkRoutes')) {
        $value = $Provisioning.observed.$field
        if (($value -isnot [int] -and $value -isnot [long]) -or $value -ne 0) {
            'Canary provisioning has customer grants or network routes.'
        }
    }
    if ($Provisioning.observed.usesDedicatedCapacity -isnot [bool] -or -not $Provisioning.observed.usesDedicatedCapacity) {
        'Canary provisioning lacks dedicated capacity.'
    }

    if ($Credential.schema -cne 'etl-sql.production-canary-credential-lifecycle/v1' -or
        $Credential.passed -isnot [bool] -or -not $Credential.passed) {
        'Missing or unsuccessful canary credential evidence.'
    }
    $interval = [TimeSpan]::Zero; $lifetime = [TimeSpan]::Zero
    if (-not [TimeSpan]::TryParse($Plan.credentialRotationInterval, [ref]$interval) -or
        -not [TimeSpan]::TryParse($Plan.maximumCredentialLifetime, [ref]$lifetime) -or
        $interval -le [TimeSpan]::Zero -or $lifetime -le $interval) {
        'Invalid canary credential lifetime or rotation plan.'
        return
    }
    foreach ($entry in @(@{ field = 'scheduledRotation'; reason = 'scheduled' }, @{ field = 'compromiseResponse'; reason = 'compromise' })) {
        $rotation = $Credential.($entry.field)
        if ($rotation.previousRevoked -isnot [bool] -or -not $rotation.previousRevoked -or $rotation.reason -cne $entry.reason) {
            'Canary credential replacement lacks its required revocation or reason.'
        }
        foreach ($version in @('previous', 'current')) {
            $value = $rotation.$version
            if ($value.credentialId -isnot [string] -or [string]::IsNullOrWhiteSpace($value.credentialId) -or
                $value.revoked -isnot [bool] -or ($version -eq 'current' -and $value.revoked)) {
                'Canary credential identity or revocation state is invalid.'
            }
        }
        if ($rotation.previous.credentialId -ceq $rotation.current.credentialId) { 'Canary credential replacement reused the previous identity.' }
        try {
            $completed = ConvertTo-ProductionCanaryTime $rotation.completedUtc
            $issued = ConvertTo-ProductionCanaryTime $rotation.current.issuedUtc
            $expires = ConvertTo-ProductionCanaryTime $rotation.current.expiresUtc
            $previousIssued = ConvertTo-ProductionCanaryTime $rotation.previous.issuedUtc
            $previousExpires = ConvertTo-ProductionCanaryTime $rotation.previous.expiresUtc
            if ($issued -ne $completed -or $expires -le $issued -or $expires - $issued -gt $lifetime -or
                $previousExpires -le $previousIssued -or $previousExpires - $previousIssued -gt $lifetime -or
                $completed -lt $previousIssued -or
                ($entry.reason -eq 'scheduled' -and $completed - $previousIssued -lt $interval)) {
                'Canary credential timing does not match the rotation/lifetime plan.'
            }
        } catch { 'Canary credential timing is missing or invalid.' }
    }
    $scheduled = $Credential.scheduledRotation; $compromised = $Credential.compromiseResponse
    foreach ($field in @('credentialId', 'issuedUtc', 'expiresUtc', 'revoked')) {
        if ($scheduled.current.$field -cne $compromised.previous.$field) { 'Canary compromise evidence does not continue the scheduled credential.' }
    }
    try {
        if ((ConvertTo-ProductionCanaryTime $Credential.completedUtc) -ne (ConvertTo-ProductionCanaryTime $compromised.completedUtc) -or
            (ConvertTo-ProductionCanaryTime $compromised.completedUtc) -lt (ConvertTo-ProductionCanaryTime $scheduled.completedUtc)) {
            'Canary credential lifecycle completion is inconsistent.'
        }
    } catch { 'Canary credential lifecycle completion is missing or invalid.' }
}

function ConvertTo-ProductionCanaryTime {
    param([object]$Value)
    if ($Value -is [DateTimeOffset]) { return $Value }
    if ($Value -is [DateTime]) { return [DateTimeOffset]$Value }
    $parsed = [DateTimeOffset]::MinValue
    if ($Value -isnot [string] -or -not [DateTimeOffset]::TryParse($Value, [ref]$parsed)) { throw 'Invalid canary timestamp.' }
    return $parsed
}

function Get-ProductionCanaryRunFindings {
    param([object]$Plan, [object]$Report)

    $journeyIds = @('ExternalHealth', 'Report', 'Job', 'Gateway', 'Export', 'Notification')
    $failures = [ordered]@{
        None = 'None'
        EtlSqlCorrectnessFailure = 'EtlSqlCorrectness'
        EtlSqlAvailabilityRegression = 'EtlSqlAvailability'
        EtlSqlLatencyRegression = 'EtlSqlLatency'
        SyntheticDependencyOutage = 'SyntheticDependency'
    }
    $expected = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $journeys = @{}
    if ($Plan.schema -cne 'etl-sql.production-canary-plan/v1' -or $Plan.journeys.Count -ne $journeyIds.Count) {
        'Invalid canary plan schema or journey inventory.'
        return
    }
    foreach ($journey in $Plan.journeys) {
        if ($journeyIds -cnotcontains $journey.id -or $journeys.ContainsKey($journey.id)) {
            'Unknown or duplicate canary journey.'
            return
        }
        $journeys[$journey.id] = $journey
        foreach ($dimension in @('regions', 'failureDomains')) {
            $values = @($journey.$dimension)
            if ($values.Count -eq 0 -or @($values | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_) }).Count -gt 0 -or
                @($values | Sort-Object -Unique).Count -ne $values.Count) {
                'Missing or duplicate canary region/failure-domain identifiers.'
                return
            }
        }
        if ([string]::IsNullOrWhiteSpace($journey.etlSqlAlertRoute) -or
            [string]::IsNullOrWhiteSpace($journey.dependencyAlertRoute) -or
            $journey.etlSqlAlertRoute -ceq $journey.dependencyAlertRoute) {
            'Missing or ambiguous canary alert routes.'
            return
        }
        foreach ($region in $journey.regions) {
            foreach ($domain in $journey.failureDomains) {
                foreach ($fault in $failures.Keys) {
                    $key = ConvertTo-Json -InputObject @($journey.id, $region, $domain, $fault) -Compress
                    [void]$expected.Add($key)
                }
            }
        }
    }
    if ($Report.schema -cne 'etl-sql.production-canary-evidence/v1' -or
        $Report.passed -isnot [bool] -or -not $Report.passed -or $Report.runs -isnot [array]) {
        'Missing or unsuccessful canary run report.'
        return
    }
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($run in $Report.runs) {
        $request = $run.request
        $key = ConvertTo-Json -InputObject @($request.journey.id, $request.region, $request.failureDomain, $request.fault) -Compress
        if (-not $expected.Contains($key) -or -not $seen.Add($key)) {
            'Unexpected or duplicate canary run.'
            continue
        }
        foreach ($field in @('passed', 'isolationSatisfied')) {
            if ($run.$field -isnot [bool] -or -not $run.$field) { 'Canary run failed or lacks an explicit success flag.' }
        }
        foreach ($field in @('tenantId', 'identityId', 'resourceNamespace', 'quotaPool', 'maximumMonthlyCostUsd',
                'customerNetworkAccessAllowed', 'customerCapacityAllowed')) {
            if ($null -eq $Plan.isolation.$field -or $null -eq $request.isolation.$field -or
                (($Plan.isolation.$field -is [bool] -or $Plan.isolation.$field -is [string]) -and
                    $request.isolation.$field.GetType() -ne $Plan.isolation.$field.GetType()) -or
                ($Plan.isolation.$field -isnot [bool] -and $Plan.isolation.$field -isnot [string] -and
                    $request.isolation.$field -isnot [long] -and $request.isolation.$field -isnot [int] -and
                    $request.isolation.$field -isnot [double] -and $request.isolation.$field -isnot [decimal]) -or
                $request.isolation.$field -cne $Plan.isolation.$field) {
                'Canary run isolation does not match the plan.'
            }
        }
        $journey = $journeys[$request.journey.id]
        if ($run.failureKind -cne $failures[$request.fault]) { 'Canary failure attribution does not match the fault.' }
        if ($request.fault -ceq 'None') {
            if ($run.sloSatisfied -isnot [bool] -or -not $run.sloSatisfied -or
                $null -ne $run.alertRoute -or $null -ne $run.alertDelivery) { 'Invalid normal canary result.' }
        } else {
            $route = if ($request.fault -ceq 'SyntheticDependencyOutage') { $journey.dependencyAlertRoute } else { $journey.etlSqlAlertRoute }
            if ($run.alertRoute -cne $route -or $run.alertDelivery.delivered -isnot [bool] -or
                -not $run.alertDelivery.delivered -or [string]::IsNullOrWhiteSpace($run.alertDelivery.alertId)) {
                'Canary fault lacks the expected delivered alert.'
            }
        }
    }
    if ($seen.Count -ne $expected.Count) { 'Canary evidence omits planned journey/region/failure-domain/fault combinations.' }
}
