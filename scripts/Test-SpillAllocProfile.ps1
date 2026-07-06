<#
.SYNOPSIS
    Runs the v0.15.0 Phase 1 spill allocation/GC/I-O profile of the Gate F #temp round trip.

.DESCRIPTION
    Executes tests/ETL-SQL.Tests/Scale/SpillAllocationProfilingTests in an isolated Release host at
    the requested row count and writes the JSON report (cumulative allocation + rate + per-type
    sampled attribution, retained-bytes delta, GC counts/pause, CPU, process I/O, spill bytes) under
    -OutDir. Publish the report BEFORE changing the spill implementation; later runs diff against it.

    Call-site attribution needs stacks, which the in-process sampler cannot see. For that drill-down
    run the profile once, note the test-host PID printed by dotnet, and in a second terminal:
        dotnet-trace collect -p <pid> --profile gc-verbose

.EXAMPLE
    .\scripts\Test-SpillAllocProfile.ps1                    # 10M-row baseline
    .\scripts\Test-SpillAllocProfile.ps1 -Rows 50000000     # 50M
#>
param(
    [ValidateRange(50000, 1000000000)]
    [long]$Rows = 10000000,
    [int]$MemoryGrantMB = 2048,
    [ValidateRange(1000, 1000000)]
    [int]$BatchRows = 25000,
    [string]$OutDir = '.\certification-results\spill-alloc-profile',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outRoot = if ([System.IO.Path]::IsPathRooted($OutDir)) { $OutDir }
           else { Join-Path $repoRoot $OutDir }
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
$commit = (& git -C $repoRoot rev-parse --short HEAD).Trim()
$result = Join-Path $outRoot ("profile-{0}rows-{1}.json" -f $Rows, $commit)

if (-not $SkipBuild) {
    & dotnet build (Join-Path $repoRoot 'ETL-SQL.slnx') -c Release --no-restore -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}

$env:SPILL_PROFILE_ROWS = $Rows.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:SPILL_PROFILE_OUTPUT = $result
$env:CERT_MEMORY_GRANT_MB = $MemoryGrantMB.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:CERT_BATCH_ROWS = $BatchRows.ToString([Globalization.CultureInfo]::InvariantCulture)
try {
    & dotnet test (Join-Path $repoRoot 'tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj') `
        -c Release --no-build --no-restore -m:1 `
        --filter 'FullyQualifiedName~SpillAllocationProfilingTests'
    if ($LASTEXITCODE -ne 0) { throw "Profiling run failed (exit $LASTEXITCODE)." }
}
finally {
    Remove-Item Env:SPILL_PROFILE_ROWS, Env:SPILL_PROFILE_OUTPUT, Env:CERT_MEMORY_GRANT_MB, Env:CERT_BATCH_ROWS -ErrorAction SilentlyContinue
}

if (-not (Test-Path $result)) { throw "Profile report was not written to $result." }
$report = Get-Content $result -Raw | ConvertFrom-Json
Write-Host "`nSpill allocation profile — $($report.rows) rows in $($report.elapsedMs) ms ($($report.rowsPerSecond) rows/s)" -ForegroundColor Cyan
Write-Host ("  allocated {0:N1} MB ({1:N1} B/row), retained delta {2:N1} KB, GC {3}/{4}/{5} pause {6} ms, CPU {7} ms" -f `
    ($report.allocation.cumulativeBytes / 1MB), $report.allocation.bytesPerRow, `
    ($report.allocation.retainedDeltaBytes / 1KB), $report.gc.gen0, $report.gc.gen1, $report.gc.gen2, `
    $report.gc.pauseMs, $report.cpu.timeMs)
Write-Host ("  spill {0:N1} MB; process I/O read {1:N1} MB write {2:N1} MB" -f `
    ($report.spillBytes / 1MB), ($report.io.readBytes / 1MB), ($report.io.writeBytes / 1MB))
Write-Host "  top allocated types (sampled):" -ForegroundColor Cyan
$report.topAllocations | Select-Object -First 10 | ForEach-Object {
    Write-Host ("    {0,6:N1}%  {1,12:N0} B  {2}" -f $_.sharePercent, $_.sampledBytes, $_.type)
}
Write-Host "`nReport: $result" -ForegroundColor Green
