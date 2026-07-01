param(
    [ValidateSet(100000, 10000000, 50000000)]
    [int]$Rows = 10000000,
    [double]$MinimumSpeedup = 1.5,
    [string]$OutFile = '.\certification-results\columnar-operator-gate.json',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutFile))
$outputDir = Split-Path -Parent $outputPath
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

if (-not $SkipBuild) {
    dotnet build (Join-Path $repoRoot 'ETL-SQL.slnx') -c Release --no-restore -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}

$previousRows = $env:COLUMNAR_GATE_ROWS
$previousSpeedup = $env:COLUMNAR_GATE_MIN_SPEEDUP
$previousOutput = $env:COLUMNAR_GATE_OUTPUT
try {
    $env:COLUMNAR_GATE_ROWS = $Rows.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:COLUMNAR_GATE_MIN_SPEEDUP = $MinimumSpeedup.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:COLUMNAR_GATE_OUTPUT = $outputPath
    $testProject = Join-Path $repoRoot 'tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj'
    dotnet test $testProject -c Release --no-build --no-restore -m:1 `
        --filter 'FullyQualifiedName=ETL_SQL.Tests.Scale.ColumnarOperatorGateTests.NativeFilterProjectionAndGroupMatchRowsAndReportThroughput'
    if ($LASTEXITCODE -ne 0) { throw 'Columnar operator gate failed.' }
}
finally {
    $env:COLUMNAR_GATE_ROWS = $previousRows
    $env:COLUMNAR_GATE_MIN_SPEEDUP = $previousSpeedup
    $env:COLUMNAR_GATE_OUTPUT = $previousOutput
}

$metric = Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json
Write-Host ("Columnar operator gate: {0:N0} rows, native {1:N0} rows/s, row {2:N0} rows/s, speedup {3:N2}x" -f `
    $metric.rowCount, $metric.nativeRowsPerSecond, $metric.rowRowsPerSecond, $metric.speedup) -ForegroundColor Green
