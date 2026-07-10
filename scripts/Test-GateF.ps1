<#
.SYNOPSIS
    Runs the resumable Gate F billion-row certification matrix.

.DESCRIPTION
    Runs the bounded native operator core and spill-backed #temp round-trip in isolated Release hosts.
    Each scenario has durable stdout/stderr logs and a result JSON. status.json is refreshed while a
    child is active, so another session can inspect progress without attaching to this console. The
    billion-row test is opt-in to this script and is skipped by ordinary test, smoke, and release lanes.

.PARAMETER TempBatchRows
    Row batch used by the spill-backed temp-table scenario. The default 25,000 is the measured
    workstation crossover; the value is recorded in the manifest and result reuse key.
#>
param(
    [ValidateSet('All', 'ColumnarCore', 'TempTableRoundTrip', 'AllocProfile', 'ExternalSort', 'ExternalJoin')]
    [string]$Scenario = 'All',
    [ValidateRange(1000, 1000000000)]
    [long]$Rows = 1000000000,
    [string]$OutDir = '.\certification-results\gate-f-1b',
    [double]$MemoryBoundMB = 16384,
    [int]$MemoryGrantMB = 8192,
    [ValidateRange(1000, 1000000)]
    [int]$TempBatchRows = 25000,
    [double]$MinimumRowsPerSecond = 50000,
    [double]$MinimumFreeDiskGB = 25,
    [switch]$SkipBuild,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outRoot = if ([System.IO.Path]::IsPathRooted($OutDir)) {
    [System.IO.Path]::GetFullPath($OutDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutDir))
}
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
$statusPath = Join-Path $outRoot 'status.json'
$runLog = Join-Path $outRoot 'gate-f.log'
$startedAt = Get-Date
$activeScenario = ''
$commit = ''
$runKey = ''

function New-Sha256 {
    param([string]$Text)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        $hash = $sha.ComputeHash($bytes)
        return -join ($hash | ForEach-Object { $_.ToString('x2') })
    } finally {
        $sha.Dispose()
    }
}

function Write-Status([string]$state, [string]$current, [int]$childPid = 0, [string]$detail = '') {
    $status = [ordered]@{
        state = $state
        currentScenario = $current
        runnerPid = $PID
        childPid = $childPid
        rows = $Rows
        startedAt = $startedAt.ToString('o')
        updatedAt = (Get-Date).ToString('o')
        elapsedSeconds = [math]::Round(((Get-Date) - $startedAt).TotalSeconds, 1)
        commit = $commit
        detail = $detail
        outputDirectory = $outRoot
    }
    $temp = "$statusPath.tmp"
    $status | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $statusPath -Force
}

function Get-GateFScenarioManifests {
    $commonTelemetry = @(
        'elapsedMs',
        'rowsPerSecond',
        'peakProcessWorkingSetMB',
        'allocatedMB',
        'gcPauseMs',
        'cpuTimeMs'
    )

    return [ordered]@{
        columnarCore = [ordered]@{
            scenarioId = 'GateF_NativeScanFilterProjectionAggregate_1B'
            operator = 'ColumnarScanFilterProjectionAggregate'
            state = 'Certified'
            rows = [ordered]@{ input = $Rows }
            shape = [ordered]@{
                columns = @('Id INT', 'GroupId INT', 'Amount BIGINT')
                filter = 'Id > rows / 2'
                groups = 100
                skew = 'none'
            }
            admission = [ordered]@{
                minimumFreeDiskGB = 0
                memoryBoundMB = $MemoryBoundMB
                operatorMemoryGrantMB = $null
                spillPath = 'none'
                adaptiveExecution = 'off'
                nativePathRequired = $true
                releaseBuildRequired = $true
            }
            correctnessOracle = 'generated formula for selected rows, grouped count, Id sum, and Amount sum'
            telemetryContract = $commonTelemetry
            nonGoals = @('row-engine fallback', 'high-cardinality grouping', 'arbitrary expressions', 'provider-backed sources')
            resumeKeyFields = @('commit', 'rows', 'memoryBoundMB', 'minimumRowsPerSecond', 'batchRows')
        }
        tempTableRoundTrip = [ordered]@{
            scenarioId = 'GateF_TempTableRoundTrip_1B'
            operator = 'TempTableSpillRoundTrip'
            state = 'Certified'
            rows = [ordered]@{ input = $Rows }
            shape = [ordered]@{
                columns = @('grp INT', 'val BIGINT')
                spillThresholdRows = 10000
                skew = 'none'
            }
            admission = [ordered]@{
                minimumFreeDiskGB = $MinimumFreeDiskGB
                memoryBoundMB = $MemoryBoundMB
                operatorMemoryGrantMB = $MemoryGrantMB
                spillPath = 'temp-volume'
                adaptiveExecution = 'off'
                nativePathRequired = $false
                releaseBuildRequired = $true
            }
            correctnessOracle = 'exact row count and checksum after SELECT INTO temp-table round trip'
            telemetryContract = @($commonTelemetry + @('spillBytes', 'spillWriteBytes', 'spillReadBytes', 'spillExtentCount'))
            nonGoals = @('secondary operators downstream of the temp table', 'persistent temp-table retention', 'provider-backed sources')
            resumeKeyFields = @('commit', 'rows', 'memoryBoundMB', 'memoryGrantMB', 'tempBatchRows', 'minimumRowsPerSecond')
        }
        allocProfile = [ordered]@{
            scenarioId = 'GateF_TempTableAllocProfile_1B'
            operator = 'TempTableSpillAllocationProfile'
            state = 'Candidate'
            rows = [ordered]@{ input = $Rows }
            shape = [ordered]@{
                columns = @('grp INT', 'val BIGINT')
                profile = 'allocation, GC, process memory, CPU, and I/O for temp-table round trip'
                skew = 'none'
            }
            admission = [ordered]@{
                minimumFreeDiskGB = $MinimumFreeDiskGB
                memoryBoundMB = $MemoryBoundMB
                operatorMemoryGrantMB = $MemoryGrantMB
                spillPath = 'temp-volume'
                adaptiveExecution = 'off'
                nativePathRequired = $false
                releaseBuildRequired = $true
            }
            correctnessOracle = 'profile run must complete the same temp-table row count and checksum contract'
            telemetryContract = @($commonTelemetry + @('bytesAllocatedPerRow', 'gcGen2Collections', 'physicalReadBytes', 'physicalWriteBytes'))
            nonGoals = @('new operator certification', 'provider-backed sources')
            resumeKeyFields = @('commit', 'rows', 'memoryGrantMB', 'tempBatchRows')
        }
        externalSort = [ordered]@{
            scenarioId = 'ExternalSort_MultiKey_1B'
            operator = 'ExternalSort'
            state = 'Candidate'
            rows = [ordered]@{ input = $Rows }
            shape = [ordered]@{
                columns = @('Id BIGINT', 'SortKey INT', 'TieBreaker BIGINT', 'Payload BIGINT')
                sortKeys = @('SortKey ASC', 'TieBreaker DESC')
                randomSeed = 15041
                skew = 'bounded duplicate sort keys'
            }
            admission = [ordered]@{
                minimumFreeDiskGB = $MinimumFreeDiskGB
                memoryBoundMB = $MemoryBoundMB
                operatorMemoryGrantMB = $MemoryGrantMB
                spillPath = 'temp-volume'
                adaptiveExecution = 'off'
                nativePathRequired = $false
                releaseBuildRequired = $true
            }
            correctnessOracle = 'streaming ordered-output validator with row count, first/last key, checksum, and tie-breaker checks'
            telemetryContract = @($commonTelemetry + @('sortRunCount', 'mergePassCount', 'spillBytes', 'spillExtentCount'))
            nonGoals = @('Top-N optimized plans', 'locale-specific collation', 'arbitrary expression sort keys', 'downstream operator certification')
            resumeKeyFields = @('commit', 'rows', 'sortKeys', 'memoryBoundMB', 'memoryGrantMB', 'randomSeed')
        }
        externalJoin = [ordered]@{
            scenarioId = 'ExternalEquiJoin_ControlledSkew_1B'
            operator = 'ExternalEquiJoin'
            state = 'Candidate'
            rows = [ordered]@{ left = $Rows; right = $Rows }
            shape = [ordered]@{
                columns = @('Id BIGINT', 'JoinKey INT', 'Payload BIGINT')
                joinType = 'INNER'
                keyOverlap = '50 percent'
                duplicateFactor = 'bounded by generated key space'
                randomSeed = 24017
                skew = 'controlled hot-key distribution, no adversarial single-key collapse'
            }
            admission = [ordered]@{
                minimumFreeDiskGB = $MinimumFreeDiskGB
                memoryBoundMB = $MemoryBoundMB
                operatorMemoryGrantMB = $MemoryGrantMB
                spillPath = 'temp-volume'
                adaptiveExecution = 'off'
                nativePathRequired = $false
                releaseBuildRequired = $true
            }
            correctnessOracle = 'mathematical result-count formula plus checksum over matched generated keys'
            telemetryContract = @($commonTelemetry + @('partitionCount', 'partitionPassCount', 'repartitionPassCount', 'spillBytes', 'spillExtentCount'))
            nonGoals = @('non-equi joins', 'outer joins with high null expansion', 'adversarial single-key skew', 'provider-backed sources')
            resumeKeyFields = @('commit', 'rows', 'joinType', 'keyOverlap', 'duplicateFactor', 'memoryBoundMB', 'memoryGrantMB', 'randomSeed')
        }
    }
}

function Get-GateFAdmissionResults {
    param(
        [object]$Manifests,
        [double]$FreeDiskGB,
        [string]$SpillDriveRoot
    )

    $results = [ordered]@{}
    foreach ($property in $Manifests.GetEnumerator()) {
        $manifest = $property.Value
        $requiredDisk = 0.0
        if ($null -ne $manifest.admission.minimumFreeDiskGB) {
            $requiredDisk = [double]$manifest.admission.minimumFreeDiskGB
        }
        $requiresDisk = $requiredDisk -gt 0
        $admitted = (-not $requiresDisk) -or ($FreeDiskGB -ge $requiredDisk)
        $results[$property.Key] = [ordered]@{
            scenarioId = $manifest.scenarioId
            admitted = $admitted
            reason = if ($admitted) { 'admitted' } else { 'insufficient spill disk' }
            requiredFreeDiskGB = $requiredDisk
            actualFreeDiskGB = [math]::Round($FreeDiskGB, 3)
            spillDrive = $SpillDriveRoot
            memoryBoundMB = $MemoryBoundMB
            operatorMemoryGrantMB = $manifest.admission.operatorMemoryGrantMB
            adaptiveExecution = $manifest.admission.adaptiveExecution
        }
    }
    return $results
}

function Invoke-LoggedProcess(
    [string]$name,
    [string]$filePath,
    [string[]]$arguments,
    [string]$stdoutPath,
    [string]$stderrPath) {
    $script:activeScenario = $name
    Write-Status 'running' $name 0 'starting child process'
    $process = Start-Process -FilePath $filePath -ArgumentList $arguments -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -NoNewWindow -PassThru
    while (-not $process.HasExited) {
        Start-Sleep -Seconds 15
        $tail = if (Test-Path $stdoutPath) {
            (Get-Content -LiteralPath $stdoutPath -Tail 1 -ErrorAction SilentlyContinue) -replace '\s+', ' '
        } else { '' }
        Write-Status 'running' $name $process.Id $tail
        Write-Host ("[{0:HH:mm:ss}] {1} | child {2} | {3}" -f (Get-Date), $name, $process.Id, $tail)
    }
    $process.WaitForExit()
    if (Test-Path $stderrPath) { Get-Content -LiteralPath $stderrPath | Add-Content -LiteralPath $runLog }
    if ($process.ExitCode -ne 0) {
        Write-Status 'failed' $name 0 "exit code $($process.ExitCode); see $stdoutPath and $stderrPath"
        throw "$name failed with exit code $($process.ExitCode)."
    }
}

try {
    $tempRoot = [System.IO.Path]::GetTempPath()
    $tempDriveName = [System.IO.Path]::GetPathRoot($tempRoot).TrimEnd(':','\')
    $tempDrive = Get-PSDrive -Name $tempDriveName
    $freeDiskGB = $tempDrive.Free / 1GB
    if (($Scenario -eq 'All' -or $Scenario -eq 'TempTableRoundTrip' -or $Scenario -eq 'AllocProfile' -or $Scenario -eq 'ExternalSort' -or $Scenario -eq 'ExternalJoin') -and $freeDiskGB -lt $MinimumFreeDiskGB) {
        $message = ("Gate F requires at least {0:N1} GB free on spill drive {1}; only {2:N1} GB is available. " +
            "Free disk space or override -MinimumFreeDiskGB only with a justified measured estimate.") -f `
            $MinimumFreeDiskGB, $tempDrive.Root, $freeDiskGB
        throw $message
    }

    $commit = (git -C $repoRoot rev-parse HEAD).Trim()
    git -C $repoRoot diff --quiet
    $workingTreeDirty = $LASTEXITCODE -ne 0
    git -C $repoRoot diff --cached --quiet
    $indexDirty = $LASTEXITCODE -ne 0
    if ($workingTreeDirty -or $indexDirty) {
        throw 'Gate F requires a clean tracked worktree so results identify reproducible code.'
    }

    @(
        '',
        ('=' * 72),
        "Gate F started: $($startedAt.ToString('o'))",
        "Commit: $commit",
        "Rows: $Rows",
        "Scenario: $Scenario",
        "Memory bound MB: $MemoryBoundMB",
        "Memory grant MB: $MemoryGrantMB",
        "Temp-table batch rows: $TempBatchRows",
        "Minimum rows/s: $MinimumRowsPerSecond",
        ("Spill drive free GB: {0:N1}" -f $freeDiskGB)
    ) | Add-Content -LiteralPath $runLog -Encoding UTF8
    Write-Status 'preparing' '' 0 "commit $commit"

    $config = [ordered]@{
        rows = $Rows
        requestedScenario = $Scenario
        memoryBoundMB = $MemoryBoundMB
        memoryGrantMB = $MemoryGrantMB
        tempBatchRows = $TempBatchRows
        minimumRowsPerSecond = $MinimumRowsPerSecond
        minimumFreeDiskGB = $MinimumFreeDiskGB
    }
    $configJson = $config | ConvertTo-Json -Depth 10 -Compress
    $configFingerprint = New-Sha256 $configJson
    $sourceFingerprint = New-Sha256 "$commit`nclean`n$configJson"
    $hostProfile = [ordered]@{
        machineName = [Environment]::MachineName
        operatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        runtimeVersion = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
        logicalProcessorCount = [Environment]::ProcessorCount
        processor = $env:PROCESSOR_IDENTIFIER
        gcAvailableMemoryBytes = if ([GC]::GetGCMemoryInfo().TotalAvailableMemoryBytes -gt 0) {
            [long][GC]::GetGCMemoryInfo().TotalAvailableMemoryBytes
        } else { 0 }
    }
    $scenarioManifests = Get-GateFScenarioManifests
    $admissionResults = Get-GateFAdmissionResults $scenarioManifests $freeDiskGB $tempDrive.Root

    $runManifest = [ordered]@{
        schemaVersion = 2
        startedAt = $startedAt.ToString('o')
        capturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        commit = [ordered]@{
            sha = $commit
            isDirty = $false
        }
        commitSha = $commit
        sourceFingerprint = $sourceFingerprint
        configFingerprint = $configFingerprint
        config = $config
        rows = $Rows
        requestedScenario = $Scenario
        spillRoot = $tempRoot
        spillDrive = $tempDrive.Root
        spillDriveFreeBytesAtStart = [long]$tempDrive.Free
        host = $hostProfile
        scenarioManifests = $scenarioManifests
        admission = $admissionResults
    }
    $runManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outRoot 'run-manifest.json') -Encoding UTF8

    if (-not $SkipBuild) {
        & dotnet build (Join-Path $repoRoot 'ETL-SQL.slnx') -c Release --no-restore -v quiet *>&1 |
            Tee-Object -FilePath $runLog -Append
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    }

    if ($Scenario -eq 'All' -or $Scenario -eq 'ColumnarCore') {
        $result = Join-Path $outRoot 'columnar-core.json'
        $resultKey = Join-Path $outRoot 'columnar-core.key'
        $runKey = "$commit|$Rows|$MemoryBoundMB|$MinimumRowsPerSecond|100000"
        $reusable = (Test-Path $result) -and (Test-Path $resultKey) -and
            ((Get-Content -LiteralPath $resultKey -Raw).Trim() -eq $runKey)
        if ($Force -or -not $reusable) {
            $env:GATE_F_ROWS = $Rows.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_CERTIFICATION = '1'
            $env:GATE_F_BATCH_ROWS = '100000'
            $env:GATE_F_MEMORY_BOUND_MB = $MemoryBoundMB.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_MIN_ROWS_PER_SECOND = $MinimumRowsPerSecond.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_OUTPUT = $result
            $testProject = Join-Path $repoRoot 'tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj'
            Invoke-LoggedProcess 'ColumnarCore' 'dotnet' @(
                'test', $testProject, '-c', 'Release', '--no-build', '--no-restore', '-m:1',
                '--filter', 'FullyQualifiedName=ETL_SQL.Tests.Scale.BillionRowCertificationTests.NativeScanFilterProjectionAndLowCardinalityAggregateStayBounded'
            ) (Join-Path $outRoot 'columnar-core.log') (Join-Path $outRoot 'columnar-core.err.log')
            Set-Content -LiteralPath $resultKey -Value $runKey -Encoding ASCII
        } else {
            Write-Status 'resuming' 'ColumnarCore' 0 "reusing completed result for commit $commit"
        }
    }

    if ($Scenario -eq 'All' -or $Scenario -eq 'TempTableRoundTrip') {
        $tempOut = Join-Path $outRoot 'temp-table-round-trip'
        $result = Join-Path $tempOut 'cert-report.json'
        $resultKey = Join-Path $outRoot 'temp-table-round-trip.key'
        $runKey = "$commit|$Rows|$MemoryBoundMB|$MemoryGrantMB|$TempBatchRows|$MinimumRowsPerSecond"
        $reusable = (Test-Path $result) -and (Test-Path $resultKey) -and
            ((Get-Content -LiteralPath $resultKey -Raw).Trim() -eq $runKey)
        if ($Force -or -not $reusable) {
            $env:CERT_MEMORY_BOUND_MB = $MemoryBoundMB.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:CERT_MEMORY_GRANT_MB = $MemoryGrantMB.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:CERT_BATCH_ROWS = $TempBatchRows.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:CERT_MIN_ROWS_PER_SECOND = $MinimumRowsPerSecond.ToString([Globalization.CultureInfo]::InvariantCulture)
            $scale = $Rows / 50000d
            $pwsh = (Get-Process -Id $PID).Path
            Invoke-LoggedProcess 'TempTableRoundTrip' $pwsh @(
                '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-ScaleCertification.ps1'),
                '-Tier', 'Huge', '-Scenario', 'TempTableSpill',
                '-RowCountScale', $scale.ToString([Globalization.CultureInfo]::InvariantCulture),
                '-OutDir', $tempOut, '-SkipBuild'
            ) (Join-Path $outRoot 'temp-table-round-trip.log') (Join-Path $outRoot 'temp-table-round-trip.err.log')
            Set-Content -LiteralPath $resultKey -Value $runKey -Encoding ASCII
        } else {
            Write-Status 'resuming' 'TempTableRoundTrip' 0 "reusing completed result for commit $commit"
        }
    }

    if ($Scenario -eq 'All' -or $Scenario -eq 'AllocProfile') {
        # v0.15.0 Phase 1: the 1B certification also captures the allocation/GC profile of the
        # #temp round trip and compares it against the checked-in budget for this row count
        # (certification-results/spill-alloc-budgets). Missing budget warns without failing;
        # establish one with Test-SpillAllocProfile.ps1 -Rows <n> -UpdateBudget.
        $allocOut = Join-Path $outRoot 'alloc-profile'
        $result = Join-Path $allocOut ("profile-{0}rows-{1}.json" -f $Rows, (git -C $repoRoot rev-parse --short HEAD).Trim())
        $resultKey = Join-Path $outRoot 'alloc-profile.key'
        $runKey = "$commit|$Rows|$MemoryGrantMB|$TempBatchRows|alloc"
        $reusable = (Test-Path $result) -and (Test-Path $resultKey) -and
            ((Get-Content -LiteralPath $resultKey -Raw).Trim() -eq $runKey)
        if ($Force -or -not $reusable) {
            $pwsh = (Get-Process -Id $PID).Path
            Invoke-LoggedProcess 'AllocProfile' $pwsh @(
                '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-SpillAllocProfile.ps1'),
                '-Rows', $Rows.ToString([Globalization.CultureInfo]::InvariantCulture),
                '-MemoryGrantMB', $MemoryGrantMB.ToString([Globalization.CultureInfo]::InvariantCulture),
                '-BatchRows', $TempBatchRows.ToString([Globalization.CultureInfo]::InvariantCulture),
                '-OutDir', $allocOut, '-SkipBuild'
            ) (Join-Path $outRoot 'alloc-profile.log') (Join-Path $outRoot 'alloc-profile.err.log')
            Set-Content -LiteralPath $resultKey -Value $runKey -Encoding ASCII
        } else {
            Write-Status 'resuming' 'AllocProfile' 0 "reusing completed result for commit $commit"
        }
    }

    if ($Scenario -eq 'ExternalSort') {
        # v0.15.0 Phase 4 candidate: external sort is intentionally opt-in until an operator-run
        # artifact passes and the public matrix moves it from Candidate to Certified.
        $result = Join-Path $outRoot 'external-sort.json'
        $resultKey = Join-Path $outRoot 'external-sort.key'
        $sortChunkRows = 100000
        $runKey = "$commit|$Rows|$MemoryBoundMB|$MemoryGrantMB|$sortChunkRows|$MinimumRowsPerSecond|ExternalSort"
        $reusable = (Test-Path $result) -and (Test-Path $resultKey) -and
            ((Get-Content -LiteralPath $resultKey -Raw).Trim() -eq $runKey)
        if ($Force -or -not $reusable) {
            $env:GATE_F_ROWS = $Rows.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_EXTERNAL_SORT_ROWS = $Rows.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_CERTIFICATION = '1'
            $env:GATE_F_SORT_CHUNK_ROWS = $sortChunkRows.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_MEMORY_BOUND_MB = $MemoryBoundMB.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_MIN_ROWS_PER_SECOND = $MinimumRowsPerSecond.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_EXTERNAL_SORT_OUTPUT = $result
            $testProject = Join-Path $repoRoot 'tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj'
            Invoke-LoggedProcess 'ExternalSort' 'dotnet' @(
                'test', $testProject, '-c', 'Release', '--no-build', '--no-restore', '-m:1',
                '--filter', 'FullyQualifiedName=ETL_SQL.Tests.Scale.BillionRowCertificationTests.ExternalSortMultiKeyCandidateStreamsAndValidatesOrder'
            ) (Join-Path $outRoot 'external-sort.log') (Join-Path $outRoot 'external-sort.err.log')
            Set-Content -LiteralPath $resultKey -Value $runKey -Encoding ASCII
        } else {
            Write-Status 'resuming' 'ExternalSort' 0 "reusing completed result for commit $commit"
        }
    }

    if ($Scenario -eq 'ExternalJoin') {
        # v0.15.0 Phase 4 candidate: external equi-join is opt-in until an operator-run artifact
        # passes and the public matrix moves it from Candidate to Certified.
        $result = Join-Path $outRoot 'external-join.json'
        $resultKey = Join-Path $outRoot 'external-join.key'
        $joinPartitions = 32
        $runKey = "$commit|$Rows|$MemoryBoundMB|$MemoryGrantMB|$joinPartitions|$MinimumRowsPerSecond|ExternalJoin"
        $reusable = (Test-Path $result) -and (Test-Path $resultKey) -and
            ((Get-Content -LiteralPath $resultKey -Raw).Trim() -eq $runKey)
        if ($Force -or -not $reusable) {
            $env:GATE_F_ROWS = $Rows.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_EXTERNAL_JOIN_LEFT_ROWS = $Rows.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_EXTERNAL_JOIN_RIGHT_ROWS = $Rows.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_CERTIFICATION = '1'
            $env:GATE_F_JOIN_PARTITIONS = $joinPartitions.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_MEMORY_BOUND_MB = $MemoryBoundMB.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_MIN_ROWS_PER_SECOND = $MinimumRowsPerSecond.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_EXTERNAL_JOIN_OUTPUT = $result
            $testProject = Join-Path $repoRoot 'tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj'
            Invoke-LoggedProcess 'ExternalJoin' 'dotnet' @(
                'test', $testProject, '-c', 'Release', '--no-build', '--no-restore', '-m:1',
                '--filter', 'FullyQualifiedName=ETL_SQL.Tests.Scale.BillionRowCertificationTests.ExternalEquiJoinCandidateStreamsAndValidatesControlledOverlap'
            ) (Join-Path $outRoot 'external-join.log') (Join-Path $outRoot 'external-join.err.log')
            Set-Content -LiteralPath $resultKey -Value $runKey -Encoding ASCII
        } else {
            Write-Status 'resuming' 'ExternalJoin' 0 "reusing completed result for commit $commit"
        }
    }

    $report = [ordered]@{
        schemaVersion = 2
        generatedAt = (Get-Date).ToString('o')
        capturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        commit = [ordered]@{
            sha = $commit
            isDirty = $false
        }
        commitSha = $commit
        sourceFingerprint = $sourceFingerprint
        configFingerprint = $configFingerprint
        rows = $Rows
        testsPassed = $true
        config = $config
        host = $hostProfile
        run = $runManifest
        scenarioManifests = $scenarioManifests
        admission = $admissionResults
        columnarCore = if (Test-Path (Join-Path $outRoot 'columnar-core.json')) {
            Get-Content (Join-Path $outRoot 'columnar-core.json') -Raw | ConvertFrom-Json
        } else { $null }
        tempTableRoundTrip = if (Test-Path (Join-Path $outRoot 'temp-table-round-trip\cert-report.json')) {
            (Get-Content (Join-Path $outRoot 'temp-table-round-trip\cert-report.json') -Raw | ConvertFrom-Json).scenarios[0]
        } else { $null }
        allocProfile = & {
            $allocReport = Get-ChildItem (Join-Path $outRoot 'alloc-profile') -Filter 'profile-*.json' -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($allocReport) { Get-Content $allocReport.FullName -Raw | ConvertFrom-Json } else { $null }
        }
        externalSort = if (Test-Path (Join-Path $outRoot 'external-sort.json')) {
            Get-Content (Join-Path $outRoot 'external-sort.json') -Raw | ConvertFrom-Json
        } else { $null }
        externalJoin = if (Test-Path (Join-Path $outRoot 'external-join.json')) {
            Get-Content (Join-Path $outRoot 'external-join.json') -Raw | ConvertFrom-Json
        } else { $null }
    }
    $report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $outRoot 'gate-f-report.json') -Encoding UTF8
    Write-Status 'completed' '' 0 'Gate F completed successfully.'
    Write-Host "Gate F completed. Report: $(Join-Path $outRoot 'gate-f-report.json')" -ForegroundColor Green
}
catch {
    Write-Status 'failed' $activeScenario 0 $_.Exception.Message
    $_ | Out-String | Add-Content -LiteralPath $runLog
    throw
}
finally {
    Remove-Item Env:GATE_F_CERTIFICATION,Env:GATE_F_ROWS,Env:GATE_F_BATCH_ROWS,Env:GATE_F_MEMORY_BOUND_MB,Env:GATE_F_MIN_ROWS_PER_SECOND,Env:GATE_F_OUTPUT `
        -ErrorAction SilentlyContinue
    Remove-Item Env:CERT_MEMORY_BOUND_MB,Env:CERT_MEMORY_GRANT_MB,Env:CERT_MIN_ROWS_PER_SECOND `
        -ErrorAction SilentlyContinue
    Remove-Item Env:GATE_F_EXTERNAL_SORT_ROWS,Env:GATE_F_SORT_CHUNK_ROWS,Env:GATE_F_EXTERNAL_SORT_OUTPUT `
        -ErrorAction SilentlyContinue
    Remove-Item Env:GATE_F_EXTERNAL_JOIN_LEFT_ROWS,Env:GATE_F_EXTERNAL_JOIN_RIGHT_ROWS,Env:GATE_F_JOIN_PARTITIONS,Env:GATE_F_EXTERNAL_JOIN_OUTPUT `
        -ErrorAction SilentlyContinue
}
