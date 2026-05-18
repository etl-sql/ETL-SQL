# ETL-SQL Release Automation Script
# Usage: ./publish_release.ps1

$Version = if ($env:ETL_SQL_VERSION) { $env:ETL_SQL_VERSION } else { "0.7.0" }
$ReleaseRoot = Join-Path $PSScriptRoot "..\release"
$SampleSource = Join-Path $PSScriptRoot "..\samples"
$DocsSource = Join-Path $PSScriptRoot "..\Docs"

# Target RIDs
$Platforms = @("win-x64", "linux-x64", "osx-x64")

# Projects to publish
$Projects = @(
    "..\src\ETL-SQL.App\ETL-SQL.App.csproj",
    "..\src\ETL-SQL.TUI\ETL-SQL.TUI.csproj",
    "..\src\ETL-SQL.LanguageServer\ETL-SQL.LanguageServer.csproj",
    "..\src\ETL-SQL.ReportBuilder.CLI\ETL-SQL.ReportBuilder.CLI.csproj",
    "..\src\ETL-SQL.ReportPlayer\ETL-SQL.ReportPlayer.csproj",
    "..\src\ETL-SQL.ReportPortal\ETL-SQL.ReportPortal.csproj",
    "..\src\ETL-SQL.Orchestrator.Service\ETL-SQL.Orchestrator.Service.csproj"
)

# 1. Cleanup
if (Test-Path $ReleaseRoot) {
    Remove-Item $ReleaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $ReleaseRoot | Out-Null

Write-Host "Starting Release Build for v$Version" -ForegroundColor Cyan

foreach ($Platform in $Platforms) {
    Write-Host "`nBuilding for platform: $Platform" -ForegroundColor Yellow
    $PlatformFolder = Join-Path $ReleaseRoot $Platform
    $BinFolder = Join-Path $PlatformFolder "bin"
    $DocFolder = Join-Path $PlatformFolder "docs"
    $SampleFolder = Join-Path $PlatformFolder "samples"
    
    New-Item -ItemType Directory -Path $BinFolder | Out-Null
    New-Item -ItemType Directory -Path $DocFolder | Out-Null
    New-Item -ItemType Directory -Path $SampleFolder | Out-Null

    # 2. Publish Binaries
    foreach ($Proj in $Projects) {
        $ProjPath = Join-Path $PSScriptRoot $Proj
        $ProjName = [System.IO.Path]::GetFileNameWithoutExtension($ProjPath)
        
        Write-Host "  Publishing $ProjName..." -ForegroundColor Gray
        dotnet publish $ProjPath -c Release -r $Platform --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -o $BinFolder --nologo | Out-Null
    }

    # 3. Cleanup redundant files
    Write-Host "  Cleaning up redundant assets..." -ForegroundColor Gray
    Get-ChildItem $BinFolder -Filter "appsettings.Development.json" -Recurse | Remove-Item -Force
    Get-ChildItem $BinFolder -Filter "*.staticwebassets.endpoints.json" -Recurse | Remove-Item -Force

    # 4. Copy Docs
    Copy-Item (Join-Path $DocsSource "QUICKSTART.txt") $DocFolder
    Copy-Item (Join-Path $DocsSource "ReportPortal_Administrators_Guide.md") (Join-Path $DocFolder "ReportPortal_Guide.txt")
    Copy-Item (Join-Path $PSScriptRoot "..\CHANGELOG.md") (Join-Path $DocFolder "CHANGELOG.txt") # Rename to txt for portability
    
    # 5. Copy Curated Samples (Top 15)
    $SampleList = @(
        "sample_Hello.etlsql",
        "sample_variables.etlsql",
        "sample_lineage.etlsql",
        "realworld_01_dw_load.etlsql",
        "realworld_02_secure_sftp_alert.etlsql",
        "realworld_04_incremental_merge.etlsql",
        "realworld_09_directory_watcher.etlsql",
        "sample_docker.etlsql",
        "sample_functions.etlsql",
        "sample_ansi_sql.etlsql",
        "sample_avro.etlsql",
        "sample_parquet.etlsql",
        "sales report.rptsql",
        "slicer_test.rptsql",
        "verify_env.etlsql"
    )

    foreach ($Sample in $SampleList) {
        $Src = Join-Path $SampleSource $Sample
        if (Test-Path $Src) {
            Copy-Item $Src $SampleFolder
        }
    }

    Write-Host "  Packaging VSIX bundle..." -ForegroundColor Gray
    $VsixPath = & (Join-Path $PSScriptRoot "publish_vsix.ps1") -Platform $Platform -BinSourceDir $BinFolder
    if (Test-Path $VsixPath) {
        Move-Item $VsixPath $BinFolder # Put it next to the EXEs
    }

    # 7. Zip for GitHub Release
    Write-Host "  Creating ZIP archive for GitHub..." -ForegroundColor Gray
    $ZipFileName = "ETL-SQL-v$Version-$Platform.zip"
    $ZipDest = Join-Path $ReleaseRoot $ZipFileName
    if (Test-Path $ZipDest) { Remove-Item $ZipDest -Force }
    Compress-Archive -Path (Join-Path $PlatformFolder "*") -DestinationPath $ZipDest -Force

    Write-Host "  Platform $Platform complete. Archive: $ZipFileName" -ForegroundColor Green
}

Write-Host "`nRelease v$Version ready in $ReleaseRoot" -ForegroundColor Cyan
