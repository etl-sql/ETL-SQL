# Structural checks for deterministic canary reports. These do not establish live-host provenance.
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
