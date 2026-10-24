# Synthetic receipts for structural validator tests; never execution or release evidence.
function New-FixtureTrx {
    param([string]$Directory, [string]$Name, [string]$Filter, [string]$Start, [string]$End)
    $definitions = [Collections.Generic.List[string]]::new(); $results = [Collections.Generic.List[string]]::new()
    $index = 0
    foreach ($clause in ($Filter -split '\|')) {
        $index++
        $selector = $clause -replace '^FullyQualifiedName[~=]', ''
        $class = $selector; $method = 'Proof'
        if ($clause.Contains('Name=')) { $last = $selector.LastIndexOf('.'); $class = $selector.Substring(0, $last); $method = $selector.Substring($last + 1) }
        $class = [Security.SecurityElement]::Escape($class); $method = [Security.SecurityElement]::Escape($method)
        $definitions.Add("<UnitTest id='test-$index'><TestMethod className='$class' name='$method'/></UnitTest>")
        $results.Add("<UnitTestResult testId='test-$index' outcome='Passed'/>")
    }
    $xml = "<TestRun xmlns='http://microsoft.com/schemas/VisualStudio/TeamTest/2010'><Times start='$Start' finish='$End'/><TestDefinitions>$($definitions -join '')</TestDefinitions><Results>$($results -join '')</Results><ResultSummary><Counters total='$index' executed='$index' passed='$index' failed='0' notExecuted='0'/></ResultSummary></TestRun>"
    [IO.File]::WriteAllText((Join-Path $Directory $Name), $xml)
    return [ordered]@{ path = $Name; sha256 = (Get-FileHash -LiteralPath (Join-Path $Directory $Name) -Algorithm SHA256).Hash.ToLowerInvariant(); total = $index; passed = $index }
}
function New-FixtureDotnetResult {
    param([string]$Directory, [object]$Phase, [string]$LogName)
    $now = [DateTimeOffset]::UtcNow
    $start = $now.AddSeconds(-2).ToString('O'); $end = $now.ToString('O')
    $trx = [IO.Path]::ChangeExtension($LogName, '.trx')
    $arguments = @('test', $Phase.project, '--configuration', 'Release', '--filter', $Phase.filter, '--logger', 'console;verbosity=normal', '--logger', "trx;LogFileName=$trx", '--results-directory', $Directory)
    'Synthetic fixture log' | Set-Content -LiteralPath (Join-Path $Directory $LogName)
    return [ordered]@{
        lane = $Phase.lane; phase = $Phase.name; prerequisite = $Phase.prerequisite; proof = $Phase.proof
        command = 'dotnet ' + ($arguments -join ' '); invocation = @{ executable = 'dotnet'; arguments = $arguments }
        log = $LogName; startedUtc = $start; completedUtc = $end; status = 'Passed'; exitCode = 0
        testEvidence = New-FixtureTrx $Directory $trx $Phase.filter $start $end
    }
}
function Add-FixtureExecutionEvidence {
    param([object]$Evidence)
    $matrixPath = Join-Path $scripts '../tests/fixtures/provider-neutral-fault-matrix.json'
    $matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json
    foreach ($report in $Evidence.reports.Values) {
        $directory = Join-Path $Evidence.root "$($report.kind)-$($report.lanes[0])"
        $report.noBuild = $false; $report.repositoryRoot = '/synthetic-repository'; $report.runDirectory = $directory
        $plan = @(Get-DeploymentCertificationPlan -Kind $report.kind -Names $report.lanes)
        $report.phases = @(foreach ($phase in $plan) {
            $logName = Get-DeploymentPhaseLogName $phase.lane $phase.name
            if (-not $phase.faultProfile) { New-FixtureDotnetResult $directory $phase $logName; continue }
            $now = [DateTimeOffset]::UtcNow.AddSeconds(-3).ToString('O')
            'Synthetic provider invocation' | Set-Content -LiteralPath (Join-Path $directory $logName)
            $arguments = @('-NoProfile', '-File', '/synthetic-repository/scripts/Test-ProviderNeutralFaultCertification.ps1', '-Profile', $phase.faultProfile, '-Configuration', 'Release', '-OutputRoot', (Join-Path $directory 'provider-neutral-faults'))
            $nestedRoot = Join-Path $directory "provider-neutral-faults/$($phase.faultProfile)"
            $null = New-Item -ItemType Directory -Path (Join-Path $nestedRoot 'scenario-evidence') -Force
            $row = $matrix.profiles | Where-Object profile -eq $phase.faultProfile
            $nestedLog = "$($row.profile)-$($row.adapter).log".ToLowerInvariant()
            $test = New-FixtureDotnetResult $nestedRoot @{ project = $CoreTests; filter = 'FullyQualifiedName=ETL_SQL.Tests.Orchestration.ProviderNeutralFaultCertificationTests.RepeatedMatrixProducesDurableInvariantAndRecoveryEvidence' } $nestedLog
            $test.profile = $row.profile; $test.provider = $row.provider; $test.adapter = $row.adapter
            $runs = @(foreach ($repetition in 1..2) { foreach ($id in $matrix.scenarioIds) {
                @{ request = @{ repetition = $repetition; scenario = @{ id = $id }; deploymentProfile = $row.profile; provider = $row.provider }; adapterKind = $row.adapter; faultActivated = $true
                    invariants = @{ noSplitBrainMutation = $true; noStaleAuthorityReuse = $true; noSilentLoss = $true; noDuplicateCommittedResult = $true; recoveryClaimMatchesCheckpointContract = $true; passed = $true } }
            } })
            $details = @{ schema = 'etl-sql.provider-neutral-fault-certification/v1'; repetitions = 2; passed = $true; runs = $runs }
            $relative = 'scenario-evidence/faults.json'
            $details | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $nestedRoot $relative)
            $nested = @{
                schema = 'etl-sql.provider-neutral-fault-matrix-evidence/v1'; commit = $report.commit; sourceFingerprint = $report.sourceFingerprint; dirty = $false; result = 'Passed'
                configuration = 'Release'; noBuild = $false; runDirectory = $nestedRoot; selectedProfiles = @($row.profile); repetitions = 2; scenarioCount = $matrix.scenarioIds.Count
                matrix = @{ sha256 = (Get-FileHash -LiteralPath $matrixPath -Algorithm SHA256).Hash.ToLowerInvariant() }; runs = @($test); missingProfiles = @(); invalidReportCount = 0
                reportFiles = @(@{ file = $relative; sha256 = (Get-FileHash -LiteralPath (Join-Path $nestedRoot $relative) -Algorithm SHA256).Hash.ToLowerInvariant() })
            }
            $nested | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $nestedRoot 'certification.json')
            [ordered]@{ lane = $phase.lane; phase = $phase.name; prerequisite = $phase.prerequisite; proof = $phase.proof; command = 'pwsh ' + ($arguments -join ' ')
                invocation = @{ executable = 'pwsh'; arguments = $arguments }; status = 'Passed'; exitCode = 0; log = $logName; startedUtc = $now; completedUtc = [DateTimeOffset]::UtcNow.ToString('O') }
        })
        $report.hostedPrerequisites = @(if ($report.kind -eq 'Profile' -and $report.lanes -contains 'Enterprise') {
            foreach ($name in $EnterpriseHostedPrerequisites) { @{ prerequisite = $name; proven = $true; phases = @($plan | Where-Object { $_.lane -eq 'Enterprise' -and $_.prerequisite -eq $name } | ForEach-Object name) } }
        })
        $report.scenarioEvidenceFiles = @(foreach ($id in (Get-ExpectedScenarioIds $report.lanes)) {
            $null = New-Item -ItemType Directory -Path (Join-Path $directory 'scenario-evidence') -Force
            $relative = "scenario-evidence/$id.json"
            @{ schemaVersion = 'etl-sql.deployment-scenario-evidence/v1'; scenarioId = $id; topology = @{}; artifactHashes = @{}; mappingDecisions = @(); continuity = @{}; negativeIsolation = @{}; rollback = @{} } |
                ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $directory $relative)
            @{ scenarioId = $id; path = $relative; sha256 = (Get-FileHash -LiteralPath (Join-Path $directory $relative) -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
    }
}
