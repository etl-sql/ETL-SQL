<#
.SYNOPSIS
    Runs the ordinary engine lane with coverage and enforces the release threshold.

.DESCRIPTION
    This is the single source of truth for coverage collection, assembly filtering, report
    generation, and the minimum line-coverage percentage. CI and Test-PreRelease.ps1 both call it.
    Missing or unparseable coverage fails closed.

.EXAMPLE
    .\scripts\Test-CoverageGate.ps1 -RunEngineLane -Configuration Release -NoRestore -NoBuild

.EXAMPLE
    .\scripts\Test-CoverageGate.ps1 -CoverageDirectory coverage
#>
[CmdletBinding()]
param(
    [string]$CoverageDirectory = "coverage",
    [string]$ReportDirectory = "",
    [ValidateRange(0, 100)]
    [double]$MinimumLineCoverage = 70.0,
    [switch]$RunEngineLane,
    [string]$Configuration = "Release",
    [switch]$NoRestore,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Resolve-RepoPath {
    param([string]$Path)
    if ([IO.Path]::IsPathFullyQualified($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $RepoRoot $Path))
}

function Test-IsWithinDirectory {
    param(
        [string]$Candidate,
        [string]$Directory,
        [switch]$AllowDirectoryItself
    )

    if ($AllowDirectoryItself -and $Candidate.Equals($Directory, [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }
    $prefix = $Directory.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $Candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-ReportGeneratorPath {
    $manifestPath = Join-Path $RepoRoot ".config/dotnet-tools.json"
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $package = $manifest.tools.'dotnet-reportgenerator-globaltool'
    if (-not $package) { throw "ReportGenerator is not pinned in $manifestPath." }

    $packagesRoot = if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        $userRoot = if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
            $env:USERPROFILE
        } elseif (-not [string]::IsNullOrWhiteSpace($env:HOME)) {
            $env:HOME
        } else {
            [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        }
        Join-Path $userRoot ".nuget/packages"
    } else {
        $env:NUGET_PACKAGES
    }
    $toolRoot = Join-Path $packagesRoot "dotnet-reportgenerator-globaltool/$($package.version)/tools"
    $preferredTfm = "net$([Environment]::Version.Major).0"
    $preferred = Join-Path $toolRoot "$preferredTfm/any/ReportGenerator.dll"
    if (Test-Path -LiteralPath $preferred) { return $preferred }

    $fallback = Get-ChildItem -LiteralPath $toolRoot -Recurse -File -Filter "ReportGenerator.dll" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $fallback) { throw "ReportGenerator is not restored. Run 'dotnet tool restore'." }
    return $fallback.FullName
}

$coverageRoot = Resolve-RepoPath $CoverageDirectory
$reportRoot = if ([string]::IsNullOrWhiteSpace($ReportDirectory)) {
    Join-Path $coverageRoot "report"
} else {
    Resolve-RepoPath $ReportDirectory
}

if ($RunEngineLane) {
    $coveragePrefix = [IO.Path]::GetFullPath((Join-Path $RepoRoot "coverage"))
    $releasePrefix = [IO.Path]::GetFullPath((Join-Path $RepoRoot "release-validation"))
    $canClean = (Test-IsWithinDirectory $coverageRoot $coveragePrefix -AllowDirectoryItself) `
        -or (Test-IsWithinDirectory $coverageRoot $releasePrefix)
    if ((Test-Path -LiteralPath $coverageRoot) -and -not $canClean) {
        throw "Coverage collection directory must be under coverage/ or release-validation/: $coverageRoot"
    }
    if (Test-Path -LiteralPath $coverageRoot) {
        Remove-Item -LiteralPath $coverageRoot -Recurse -Force
    }

    $laneArgs = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $PSScriptRoot "test-lane.ps1"),
        "-Lane", "engine", "-Configuration", $Configuration,
        "-CollectCoverage", "-ResultsDirectory", $CoverageDirectory
    )
    if ($NoRestore) { $laneArgs += "-NoRestore" }
    if ($NoBuild) { $laneArgs += "-NoBuild" }
    & (Get-Process -Id $PID).Path @laneArgs
    if ($LASTEXITCODE -ne 0) { throw "Engine coverage lane failed with exit code $LASTEXITCODE." }
}

$coverageFiles = @(Get-ChildItem -LiteralPath $coverageRoot -Recurse -File -Filter "coverage.cobertura.xml" -ErrorAction SilentlyContinue)
if ($coverageFiles.Count -eq 0) {
    throw "No coverage.cobertura.xml files were found under $coverageRoot."
}

New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$reportPattern = Join-Path $coverageRoot "**/coverage.cobertura.xml"
$assemblyFilters = "+ETL-SQL;+ETL-SQL.*;-ETL-SQL.Tests;-ETL-SQL.PerfTests;-ETL-SQL.Benchmarks;-ETL-SQL.LintTests;-ETL-SQL.Portal.Migrations.*"
$reportGeneratorPath = Resolve-ReportGeneratorPath
$processInfo = [Diagnostics.ProcessStartInfo]::new()
$processInfo.FileName = "dotnet"
$processInfo.UseShellExecute = $false
foreach ($argument in @(
    $reportGeneratorPath,
    "-reports:$reportPattern",
    "-targetdir:$reportRoot",
    "-reporttypes:Html;Cobertura;TextSummary",
    "-assemblyfilters:$assemblyFilters"
)) {
    $null = $processInfo.ArgumentList.Add($argument)
}
$reportProcess = [Diagnostics.Process]::Start($processInfo)
$reportProcess.WaitForExit()
if ($reportProcess.ExitCode -ne 0) { throw "ReportGenerator failed with exit code $($reportProcess.ExitCode)." }

$summaryPath = Join-Path $reportRoot "Summary.txt"
if (-not (Test-Path -LiteralPath $summaryPath)) {
    throw "Coverage summary was not generated at $summaryPath."
}
$summary = Get-Content -LiteralPath $summaryPath -Raw
if ($summary -notmatch "Line coverage:\s+([\d.]+)%") {
    throw "Could not parse line coverage from $summaryPath."
}
$lineCoverage = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
$passed = $lineCoverage -ge $MinimumLineCoverage
$evidence = [ordered]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    commit = (& git -C $RepoRoot rev-parse HEAD 2>$null) -join ""
    minimumLineCoverage = $MinimumLineCoverage
    lineCoverage = $lineCoverage
    passed = $passed
    coverageFiles = @($coverageFiles | ForEach-Object { $_.FullName })
    summaryPath = $summaryPath
}
$evidencePath = Join-Path $reportRoot "coverage-gate.json"
$evidence | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $evidencePath -Encoding UTF8

Write-Host ("Line coverage: {0:N1}% (minimum {1:N1}%)" -f $lineCoverage, $MinimumLineCoverage)
Write-Host "Coverage evidence: $evidencePath"
if (-not $passed) {
    throw ("Line coverage {0:N1}% is below the required {1:N1}%." -f $lineCoverage, $MinimumLineCoverage)
}
