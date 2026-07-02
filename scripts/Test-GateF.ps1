<#
.SYNOPSIS
    Runs the resumable Gate F billion-row certification matrix.

.DESCRIPTION
    Runs the bounded native operator core and spill-backed #temp round-trip in isolated Release hosts.
    Each scenario has durable stdout/stderr logs and a result JSON. status.json is refreshed while a
    child is active, so another session can inspect progress without attaching to this console.
#>
param(
    [ValidateSet('All', 'ColumnarCore', 'TempTableRoundTrip')]
    [string]$Scenario = 'All',
    [ValidateRange(1000, 1000000000)]
    [long]$Rows = 1000000000,
    [string]$OutDir = '.\certification-results\gate-f-1b',
    [double]$MemoryBoundMB = 16384,
    [int]$MemoryGrantMB = 8192,
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
        detail = $detail
        outputDirectory = $outRoot
    }
    $temp = "$statusPath.tmp"
    $status | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $statusPath -Force
}

function Invoke-LoggedProcess(
    [string]$name,
    [string]$filePath,
    [string[]]$arguments,
    [string]$stdoutPath,
    [string]$stderrPath) {
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
    if (($Scenario -eq 'All' -or $Scenario -eq 'TempTableRoundTrip') -and $freeDiskGB -lt $MinimumFreeDiskGB) {
        throw ("Gate F requires at least {0:N1} GB free on spill drive {1}; only {2:N1} GB is available. " +
            "Free disk space or override -MinimumFreeDiskGB only with a justified measured estimate." -f `
            $MinimumFreeDiskGB, $tempDrive.Root, $freeDiskGB)
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
        "Gate F started: $($startedAt.ToString('o'))",
        "Commit: $commit",
        "Rows: $Rows",
        "Scenario: $Scenario",
        "Memory bound MB: $MemoryBoundMB",
        "Memory grant MB: $MemoryGrantMB",
        "Minimum rows/s: $MinimumRowsPerSecond",
        ("Spill drive free GB: {0:N1}" -f $freeDiskGB)
    ) | Set-Content -LiteralPath $runLog -Encoding UTF8
    Write-Status 'preparing' '' 0 "commit $commit"

    if (-not $SkipBuild) {
        & dotnet build (Join-Path $repoRoot 'ETL-SQL.slnx') -c Release --no-restore -v quiet *>&1 |
            Tee-Object -FilePath $runLog -Append
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    }

    if ($Scenario -eq 'All' -or $Scenario -eq 'ColumnarCore') {
        $result = Join-Path $outRoot 'columnar-core.json'
        if ($Force -or -not (Test-Path $result)) {
            $env:GATE_F_ROWS = $Rows.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_BATCH_ROWS = '100000'
            $env:GATE_F_MEMORY_BOUND_MB = $MemoryBoundMB.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_MIN_ROWS_PER_SECOND = $MinimumRowsPerSecond.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:GATE_F_OUTPUT = $result
            $testProject = Join-Path $repoRoot 'tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj'
            Invoke-LoggedProcess 'ColumnarCore' 'dotnet' @(
                'test', $testProject, '-c', 'Release', '--no-build', '--no-restore', '-m:1',
                '--filter', 'FullyQualifiedName=ETL_SQL.Tests.Scale.BillionRowCertificationTests.NativeScanFilterProjectionAndLowCardinalityAggregateStayBounded'
            ) (Join-Path $outRoot 'columnar-core.log') (Join-Path $outRoot 'columnar-core.err.log')
        }
    }

    if ($Scenario -eq 'All' -or $Scenario -eq 'TempTableRoundTrip') {
        $tempOut = Join-Path $outRoot 'temp-table-round-trip'
        $result = Join-Path $tempOut 'cert-report.json'
        if ($Force -or -not (Test-Path $result)) {
            $env:CERT_MEMORY_BOUND_MB = $MemoryBoundMB.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:CERT_MEMORY_GRANT_MB = $MemoryGrantMB.ToString([Globalization.CultureInfo]::InvariantCulture)
            $env:CERT_MIN_ROWS_PER_SECOND = $MinimumRowsPerSecond.ToString([Globalization.CultureInfo]::InvariantCulture)
            $scale = $Rows / 50000d
            $pwsh = (Get-Process -Id $PID).Path
            Invoke-LoggedProcess 'TempTableRoundTrip' $pwsh @(
                '-NoProfile', '-File', (Join-Path $PSScriptRoot 'Test-ScaleCertification.ps1'),
                '-Tier', 'Huge', '-Scenario', 'TempTableSpill',
                '-RowCountScale', $scale.ToString([Globalization.CultureInfo]::InvariantCulture),
                '-OutDir', $tempOut, '-SkipBuild'
            ) (Join-Path $outRoot 'temp-table-round-trip.log') (Join-Path $outRoot 'temp-table-round-trip.err.log')
        }
    }

    $report = [ordered]@{
        generatedAt = (Get-Date).ToString('o')
        commit = $commit
        rows = $Rows
        testsPassed = $true
        columnarCore = if (Test-Path (Join-Path $outRoot 'columnar-core.json')) {
            Get-Content (Join-Path $outRoot 'columnar-core.json') -Raw | ConvertFrom-Json
        } else { $null }
        tempTableRoundTrip = if (Test-Path (Join-Path $outRoot 'temp-table-round-trip\cert-report.json')) {
            (Get-Content (Join-Path $outRoot 'temp-table-round-trip\cert-report.json') -Raw | ConvertFrom-Json).scenarios[0]
        } else { $null }
    }
    $report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $outRoot 'gate-f-report.json') -Encoding UTF8
    Write-Status 'completed' '' 0 'Gate F completed successfully.'
    Write-Host "Gate F completed. Report: $(Join-Path $outRoot 'gate-f-report.json')" -ForegroundColor Green
}
catch {
    Write-Status 'failed' '' 0 $_.Exception.Message
    $_ | Out-String | Add-Content -LiteralPath $runLog
    throw
}
finally {
    Remove-Item Env:GATE_F_ROWS,Env:GATE_F_BATCH_ROWS,Env:GATE_F_MEMORY_BOUND_MB,Env:GATE_F_MIN_ROWS_PER_SECOND,Env:GATE_F_OUTPUT `
        -ErrorAction SilentlyContinue
    Remove-Item Env:CERT_MEMORY_BOUND_MB,Env:CERT_MEMORY_GRANT_MB,Env:CERT_MIN_ROWS_PER_SECOND `
        -ErrorAction SilentlyContinue
}
