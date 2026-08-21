$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
Add-Type -Path "$repoRoot\tests\ETL-SQL.Tests\bin\Debug\net10.0\Apache.Arrow.dll"
Add-Type -Path "$repoRoot\tests\ETL-SQL.Tests\bin\Debug\net10.0\ETL-SQL.Reporting.Contracts.dll"
Add-Type -Path "$repoRoot\tests\ETL-SQL.Tests\bin\Debug\net10.0\ETL-SQL.Core.dll"
Add-Type -Path "$repoRoot\tests\ETL-SQL.Tests\bin\Debug\net10.0\ETL-SQL.Reporting.dll"
Add-Type -Path "$repoRoot\tests\ETL-SQL.Tests\bin\Debug\net10.0\ETL-SQL.Tests.dll"

$outputDir = Join-Path $repoRoot "docs\benchmarks"
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$report = [ETL_SQL.Tests.Reporting.AdvancedAuthoring.AdvancedAuthoringSemanticReadinessHarness]::GenerateReadinessReport()
$md = [ETL_SQL.Tests.Reporting.AdvancedAuthoring.AdvancedAuthoringSemanticReadinessHarness]::FormatMarkdownReport($report)
$json = [ETL_SQL.Tests.Reporting.AdvancedAuthoring.AdvancedAuthoringSemanticReadinessHarness]::FormatJsonReport($report)

$mdPath = Join-Path $outputDir "reporting-phase7-semantic-readiness.md"
$jsonPath = Join-Path $outputDir "reporting-phase7-semantic-readiness.json"

[System.IO.File]::WriteAllText($mdPath, $md)
[System.IO.File]::WriteAllText($jsonPath, $json)

Write-Host "Generated: $mdPath" -ForegroundColor Green
Write-Host "Generated: $jsonPath" -ForegroundColor Green
