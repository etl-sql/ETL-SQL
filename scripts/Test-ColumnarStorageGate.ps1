param(
    [ValidateSet(100000, 10000000, 50000000)]
    [int]$Rows = 10000000,
    [double]$MaximumRatio = 0.5,
    [string]$OutFile = '.\certification-results\columnar-storage-gate.json',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutFile))
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null

if (-not $SkipBuild) {
    dotnet build (Join-Path $repoRoot 'ETL-SQL.slnx') -c Release --no-restore -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}

$previousRows = $env:COLUMNAR_STORAGE_GATE_ROWS
$previousRatio = $env:COLUMNAR_STORAGE_GATE_MAX_RATIO
$previousOutput = $env:COLUMNAR_STORAGE_GATE_OUTPUT
try {
    $env:COLUMNAR_STORAGE_GATE_ROWS = $Rows.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:COLUMNAR_STORAGE_GATE_MAX_RATIO = $MaximumRatio.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:COLUMNAR_STORAGE_GATE_OUTPUT = $outputPath
    $testProject = Join-Path $repoRoot 'tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj'
    dotnet test $testProject -c Release --no-build --no-restore -m:1 `
        --filter 'FullyQualifiedName=ETL_SQL.Tests.Scale.ColumnarStorageAssessmentTests.NativeStoreUsesMateriallyLessResidentCapacityAndScansTypedBuffers'
    if ($LASTEXITCODE -ne 0) { throw 'Columnar storage gate failed.' }
}
finally {
    $env:COLUMNAR_STORAGE_GATE_ROWS = $previousRows
    $env:COLUMNAR_STORAGE_GATE_MAX_RATIO = $previousRatio
    $env:COLUMNAR_STORAGE_GATE_OUTPUT = $previousOutput
}

$metric = Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json
Write-Host ("Columnar storage gate: {0:N0} rows, {1:N2}% row-heap ratio, {2:N0} rows/s, {3} segments" -f `
    $metric.rowCount, ($metric.ratio * 100), $metric.rowsPerSecond, $metric.segments) -ForegroundColor Green
