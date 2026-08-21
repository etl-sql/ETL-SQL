<#
.SYNOPSIS
    Measures and generates reproducible Phase 2 reporting baselines, bundle sizes, and capability matrix.

.DESCRIPTION
    Executes the reporting baseline harness to measure:
    - Shared browser runtime asset bundle sizes (raw, gzip, brotli)
    - Cold-start compile latencies for representative visual fixtures
    - Multi-format export throughput (Markdown, CSV, SVG)
    - Memory allocation per fixture
    - Comprehensive 36-visual-type capability matrix

.OUTPUTS
    - docs/benchmarks/reporting-phase2-baselines.md
    - docs/benchmarks/reporting-phase2-baselines.json
#>

[CmdletBinding()]
param(
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$outputDir = Join-Path $repoRoot "docs\benchmarks"

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Write-Host "Executing Reporting Phase 2 Baseline Measurement Suite..." -ForegroundColor Cyan

# Run the xUnit baseline test suite to ensure fixtures and matrix validate
$testOutput = dotnet test "$repoRoot\tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" --filter "FullyQualifiedName~ReportingBaselineTests" --verbosity normal

if ($LASTEXITCODE -ne 0) {
    Write-Error "Baseline tests failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# Run a dedicated lightweight C# runner to extract the exact measurements to JSON & Markdown
$runnerCode = @"
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Reporting.Baselines;
using ETL_SQL.Tests.Reporting.Baselines;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var repoRoot = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        var report = await ReportingBaselineMeasurementHarness.RunFullBaselineAsync(repoRoot);
        
        var mdPath = Path.Combine(repoRoot, "docs", "benchmarks", "reporting-phase2-baselines.md");
        var jsonPath = Path.Combine(repoRoot, "docs", "benchmarks", "reporting-phase2-baselines.json");
        
        Directory.CreateDirectory(Path.GetDirectoryName(mdPath)!);
        
        var mdContent = ReportingBaselineMeasurementHarness.FormatMarkdownReport(report);
        await File.WriteAllTextAsync(mdPath, mdContent);
        
        var jsonContent = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(jsonPath, jsonContent);
        
        Console.WriteLine($"Generated baseline report: {mdPath}");
        Console.WriteLine($"Generated baseline JSON:   {jsonPath}");
    }
}
"@

# Execute harness extraction test
$extractTest = dotnet test "$repoRoot\tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj" --filter "FullyQualifiedName~FullBaselineHarness_RunsAndGeneratesMarkdownAndJsonReports"

# Run inline execution script using the compiled test assembly
$pwshScript = @"
Add-Type -Path '$repoRoot\tests\ETL-SQL.Tests\bin\Debug\net10.0\ETL-SQL.Core.dll'
Add-Type -Path '$repoRoot\tests\ETL-SQL.Tests\bin\Debug\net10.0\ETL-SQL.Reporting.dll'
Add-Type -Path '$repoRoot\tests\ETL-SQL.Tests\bin\Debug\net10.0\ETL-SQL.Engine.dll'
Add-Type -Path '$repoRoot\tests\ETL-SQL.Tests\bin\Debug\net10.0\ETL-SQL.Tests.dll'

`$task = [ETL_SQL.Tests.Reporting.Baselines.ReportingBaselineMeasurementHarness]::RunFullBaselineAsync('$repoRoot')
`$task.Wait()
`$report = `$task.Result

`$md = [ETL_SQL.Tests.Reporting.Baselines.ReportingBaselineMeasurementHarness]::FormatMarkdownReport(`$report)
`$mdPath = Join-Path '$outputDir' 'reporting-phase2-baselines.md'
[System.IO.File]::WriteAllText(`$mdPath, `$md)

`$jsonOpts = [System.Text.Json.JsonSerializerOptions]::new()
`$jsonOpts.WriteIndented = `$true
`$json = [System.Text.Json.JsonSerializer]::Serialize(`$report, `$jsonOpts)
`$jsonPath = Join-Path '$outputDir' 'reporting-phase2-baselines.json'
[System.IO.File]::WriteAllText(`$jsonPath, `$json)

Write-Host "Generated: `$mdPath" -ForegroundColor Green
Write-Host "Generated: `$jsonPath" -ForegroundColor Green
"@

Invoke-Expression $pwshScript

Write-Host "Reporting Phase 2 Baseline measurements generated successfully." -ForegroundColor Green
