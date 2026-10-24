. (Join-Path $PSScriptRoot 'DeploymentCertification.Plan.ps1')
. (Join-Path $PSScriptRoot 'DeploymentTestEvidence.Helpers.ps1')

function Assert-DeploymentDotnetReceipt {
    param([object]$Result, [object]$Phase, [string]$Root, [string]$RunDirectory, [string]$LogName)
    $trxName = [IO.Path]::ChangeExtension($LogName, '.trx')
    $expected = @('test', $Phase.project, '--configuration', 'Release', '--filter', $Phase.filter,
        '--logger', 'console;verbosity=normal', '--logger', "trx;LogFileName=$trxName", '--results-directory', $RunDirectory)
    if ($Result.invocation.executable -cne 'dotnet' -or
        (ConvertTo-Json -InputObject @($Result.invocation.arguments) -Compress) -cne
        (ConvertTo-Json -InputObject $expected -Compress)) { throw 'Phase invocation differs from the required build/test plan.' }
    if ($Result.testEvidence.path -cne $trxName) { throw 'Phase TRX ownership is missing or incorrect.' }
    $trx = Resolve-ReleaseEvidenceFile -Root $Root -RelativePath $trxName
    $actual = Get-DeploymentTestEvidence -Path $trx -Filter $Phase.filter -StartedUtc $Result.startedUtc -CompletedUtc $Result.completedUtc
    foreach ($field in @('sha256', 'total', 'passed')) {
        if ($actual[$field] -cne $Result.testEvidence.$field) { throw 'Phase TRX receipt differs from its retained results.' }
    }
}

function Assert-DeploymentFaultReceipt {
    param([string]$Root, [string]$Profile, [object]$StartedUtc, [object]$CompletedUtc)
    $matrixPath = Join-Path $PSScriptRoot '../tests/fixtures/provider-neutral-fault-matrix.json'
    $matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json
    $rows = @($matrix.profiles | Where-Object { $_.profile -ceq $Profile })
    if ($rows.Count -ne 1) { throw 'Fault profile does not have one canonical provider/adapter row.' }
    $row = $rows[0]
    $faultRoot = Join-Path $Root 'provider-neutral-faults'
    if (-not (Test-Path -LiteralPath $faultRoot -PathType Container)) { throw 'Provider-neutral fault bundle is missing.' }
    $matches = @(Get-ChildItem -LiteralPath $faultRoot -Filter certification.json -Recurse -File | Where-Object {
        $value = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        $value.schema -ceq 'etl-sql.provider-neutral-fault-matrix-evidence/v1' -and
            @($value.selectedProfiles).Count -eq 1 -and $value.selectedProfiles[0] -ceq $Profile
    })
    if ($matches.Count -ne 1) { throw 'Required provider-neutral fault bundle is missing or ambiguous.' }
    $path = $matches[0].FullName
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($report.configuration -cne 'Release' -or $report.noBuild -isnot [bool] -or $report.noBuild -or
        $report.matrix.sha256 -cne (Get-FileHash -LiteralPath $matrixPath -Algorithm SHA256).Hash.ToLowerInvariant() -or
        $report.repetitions -lt 2 -or $report.repetitions -gt 100 -or $report.scenarioCount -ne $matrix.scenarioIds.Count -or
        @($report.runs).Count -ne 1 -or @($report.reportFiles).Count -ne 1 -or @($report.missingProfiles).Count -ne 0 -or
        $report.invalidReportCount -ne 0) { throw 'Provider-neutral fault evidence has incomplete scope.' }
    $result = $report.runs[0]
    $start = ConvertTo-DeploymentEvidenceTime $result.startedUtc
    $end = ConvertTo-DeploymentEvidenceTime $result.completedUtc
    if ($start -lt (ConvertTo-DeploymentEvidenceTime $StartedUtc) -or $end -gt (ConvertTo-DeploymentEvidenceTime $CompletedUtc) -or $end -lt $start) { throw 'Nested fault execution is outside its parent phase.' }
    if ($result.profile -cne $Profile -or $result.provider -cne $row.provider -or $result.adapter -cne $row.adapter -or
        $result.status -cne 'Passed' -or $result.exitCode -ne 0) { throw 'Provider-neutral fault row did not pass.' }
    $bundle = Split-Path -Parent $path
    $logName = "$Profile-$($row.adapter).log".ToLowerInvariant()
    $log = Resolve-ReleaseEvidenceFile -Root $bundle -RelativePath $logName
    if ($result.log -cne $logName -or (Get-Item -LiteralPath $log).Length -eq 0) { throw 'Provider-neutral phase log is missing.' }
    $filter = 'FullyQualifiedName=ETL_SQL.Tests.Orchestration.ProviderNeutralFaultCertificationTests.RepeatedMatrixProducesDurableInvariantAndRecoveryEvidence'
    Assert-DeploymentDotnetReceipt -Result $result -Phase @{ project = $CoreTests; filter = $filter } -Root $bundle -RunDirectory $report.runDirectory -LogName $logName
    $reference = $report.reportFiles[0]
    $detailsPath = Resolve-ReleaseEvidenceFile -Root $bundle -RelativePath $reference.file
    if ($reference.sha256 -cne (Get-FileHash -LiteralPath $detailsPath -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Fault scenario file changed.' }
    $details = Get-Content -LiteralPath $detailsPath -Raw | ConvertFrom-Json
    if ($details.schema -cne 'etl-sql.provider-neutral-fault-certification/v1' -or $details.passed -isnot [bool] -or -not $details.passed -or
        $details.repetitions -ne $report.repetitions -or @($details.runs).Count -ne ($report.repetitions * $matrix.scenarioIds.Count)) { throw 'Fault scenario coverage is incomplete.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($run in $details.runs) {
        $key = "$($run.request.repetition)/$($run.request.scenario.id)"
        if ($run.request.deploymentProfile -cne $Profile -or $run.request.provider -cne $row.provider -or $run.adapterKind -cne $row.adapter -or
            ($run.request.repetition -isnot [long] -and $run.request.repetition -isnot [int]) -or
            $run.request.repetition -lt 1 -or $run.request.repetition -gt $report.repetitions -or
            $matrix.scenarioIds -cnotcontains $run.request.scenario.id -or -not $seen.Add($key) -or
            $run.faultActivated -isnot [bool] -or -not $run.faultActivated) { throw 'Fault scenario identity, activation or uniqueness is invalid.' }
        foreach ($field in @('noSplitBrainMutation', 'noStaleAuthorityReuse', 'noSilentLoss', 'noDuplicateCommittedResult', 'recoveryClaimMatchesCheckpointContract', 'passed')) {
            if ($run.invariants.$field -isnot [bool] -or -not $run.invariants.$field) { throw 'Fault invariant evidence did not pass.' }
        }
    }
}

function Assert-DeploymentExecutionEvidence {
    param([string]$ReportPath)
    $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
    $root = Split-Path -Parent $ReportPath
    if (@('Profile', 'Transition') -cnotcontains $report.kind) { throw 'Execution evidence has an unknown kind.' }
    if ($report.noBuild -isnot [bool] -or $report.noBuild -or $report.configuration -cne 'Release' -or
        [string]::IsNullOrWhiteSpace($report.runDirectory) -or [string]::IsNullOrWhiteSpace($report.repositoryRoot)) { throw 'Execution evidence needs explicit Release build context.' }
    $names = @(Get-DeploymentCertificationLanes $report.kind)
    foreach ($lane in $report.lanes) { if ($names -cnotcontains $lane) { throw 'Execution evidence has an unknown lane.' } }
    if (@($report.lanes).Count -ne 1 -and
        (ConvertTo-Json -InputObject @($report.lanes) -Compress) -cne (ConvertTo-Json -InputObject $names -Compress)) { throw 'Execution evidence must describe one lane or the canonical All plan.' }
    $plan = @(Get-DeploymentCertificationPlan -Kind $report.kind -Names $report.lanes)
    if (@($report.phases).Count -ne $plan.Count) { throw 'Required execution phases are missing or duplicated.' }
    for ($i = 0; $i -lt $plan.Count; $i++) {
        $expected = $plan[$i]; $actual = $report.phases[$i]
        $logName = Get-DeploymentPhaseLogName $expected.lane $expected.name
        if ($actual.lane -cne $expected.lane -or $actual.phase -cne $expected.name -or $actual.prerequisite -cne $expected.prerequisite -or
            $actual.proof -cne $expected.proof -or $actual.log -cne $logName -or $actual.status -cne 'Passed' -or $actual.exitCode -ne 0) { throw 'Retained phase does not match the required execution plan.' }
        if ($actual.command -cne ($actual.invocation.executable + ' ' + (@($actual.invocation.arguments) -join ' '))) { throw 'Phase command differs from its structured invocation.' }
        if ($expected.faultProfile) {
            $arguments = @($actual.invocation.arguments)
            $script = $report.repositoryRoot.TrimEnd('/', '\') + '/scripts/Test-ProviderNeutralFaultCertification.ps1'
            $output = $report.runDirectory.TrimEnd('/', '\') + '/provider-neutral-faults'
            if ($actual.invocation.executable -cne 'pwsh' -or $arguments.Count -ne 9 -or
                ($arguments[2] -replace '\\', '/') -cne ($script -replace '\\', '/') -or
                ($arguments[8] -replace '\\', '/') -cne ($output -replace '\\', '/') -or
                (ConvertTo-Json -InputObject @($arguments[0..1] + $arguments[3..7]) -Compress) -cne
                (ConvertTo-Json -InputObject @('-NoProfile', '-File', '-Profile', $expected.faultProfile, '-Configuration', 'Release', '-OutputRoot') -Compress)) { throw 'Fault phase invocation differs from the plan.' }
            Assert-DeploymentFaultReceipt -Root $root -Profile $expected.faultProfile -StartedUtc $actual.startedUtc -CompletedUtc $actual.completedUtc
        } else { Assert-DeploymentDotnetReceipt -Result $actual -Phase $expected -Root $root -RunDirectory $report.runDirectory -LogName $logName }
    }
    if ($report.kind -ceq 'Profile' -and $report.lanes -ccontains 'Enterprise') {
        if (@($report.hostedPrerequisites).Count -ne $EnterpriseHostedPrerequisites.Count) { throw 'Enterprise prerequisites are incomplete.' }
        foreach ($name in $EnterpriseHostedPrerequisites) {
            $items = @($report.hostedPrerequisites | Where-Object { $_.prerequisite -ceq $name })
            $phases = @($plan | Where-Object { $_.lane -ceq 'Enterprise' -and $_.prerequisite -ceq $name } | ForEach-Object name)
            if ($items.Count -ne 1 -or $items[0].proven -isnot [bool] -or -not $items[0].proven -or
                (ConvertTo-Json -InputObject @($items[0].phases) -Compress) -cne (ConvertTo-Json -InputObject $phases -Compress)) { throw 'Enterprise prerequisite is unproven or differs from the plan.' }
        }
    }
    $expectedScenarios = @(Get-ExpectedScenarioIds $report.lanes)
    foreach ($id in $expectedScenarios) {
        $matches = @($report.scenarioEvidenceFiles | Where-Object { $_.scenarioId -ceq $id })
        if ($matches.Count -ne 1 -or $matches[0].path -cne "scenario-evidence/$id.json") { throw 'Required concrete scenario evidence is missing or ambiguous.' }
        $path = Resolve-ReleaseEvidenceFile -Root $root -RelativePath $matches[0].path
        if ($matches[0].sha256 -cne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Concrete scenario evidence changed.' }
        $scenario = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($scenario.schemaVersion -cne 'etl-sql.deployment-scenario-evidence/v1' -or $scenario.scenarioId -cne $id) { throw 'Concrete scenario identity does not match the plan.' }
        foreach ($field in @('topology', 'artifactHashes', 'mappingDecisions', 'continuity', 'negativeIsolation', 'rollback')) {
            if ($null -eq $scenario.$field) { throw 'Concrete scenario evidence is incomplete.' }
        }
    }
}
