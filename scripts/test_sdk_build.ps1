# Verification Script for ETL-SQL SDK Build
# This script verifies that the project configurations produce self-contained single-file executables.

$ErrorActionPreference = "Stop"
$OutputDir = Join-Path $PSScriptRoot "..\sdk_test_output"
$RID = "win-x64" # Testing for current platform

if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir | Out-Null

$Projects = @(
    @{ Path = "..\src\ETL-SQL.App\ETL-SQL.App.csproj"; Name = "ETL-SQL.exe" },
    @{ Path = "..\src\ETL-SQL.LanguageServer\ETL-SQL.LanguageServer.csproj"; Name = "ETL-SQL-LSP.exe" },
    @{ Path = "..\src\ETL-SQL.ReportBuilder.CLI\ETL-SQL.ReportBuilder.CLI.csproj"; Name = "ETL-SQL-Report.exe" }
)

Write-Host "--- SDK Build Verification ---" -ForegroundColor Cyan

foreach ($Proj in $Projects) {
    $ProjPath = Join-Path $PSScriptRoot $Proj.Path
    $ProjName = $Proj.Name
    
    Write-Host "Publishing $($ProjName)..." -ForegroundColor Gray
    dotnet publish $ProjPath -c Release -r $RID --self-contained true -o $OutputDir /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true | Out-Null
    
    $ExePath = Join-Path $OutputDir $ProjName
    if (Test-Path $ExePath) {
        $Size = (Get-Item $ExePath).Length / 1MB
        Write-Host "[SUCCESS] $ProjName created ($($Size.ToString("N2")) MB)" -ForegroundColor Green
        
    # Basic Smoke Test
    Write-Host "Running smoke test for $ProjName..." -ForegroundColor Gray
    try {
        if ($ProjName -eq "ETL-SQL-LSP.exe") {
            # LSP waits for stdin, just verify it exists and has size
            Write-Host "[SUCCESS] $ProjName exists and is self-contained. Skipping execution test (LSP)." -ForegroundColor Green
        } elseif ($ProjName -eq "ETL-SQL.exe") {
            $version = & $ExePath --version
            Write-Host "[SUCCESS] $ProjName smoke test passed: $version" -ForegroundColor Green
        } else {
            # Report builder prints usage when no args
            & $ExePath | Out-Null
            Write-Host "[SUCCESS] $ProjName smoke test passed." -ForegroundColor Green
        }
    } catch {
        Write-Host "[FAILURE] $ProjName failed smoke test: $($_.Exception.Message)" -ForegroundColor Red
    }
    } else {
        Write-Host "[FAILURE] $ProjName NOT found at $ExePath" -ForegroundColor Red
    }
}

Write-Host "`nVerification complete. Artifacts are in $OutputDir" -ForegroundColor Cyan
