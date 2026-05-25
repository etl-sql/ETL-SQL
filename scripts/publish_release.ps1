# ETL-SQL Release Automation Script
# Usage: ./publish_release.ps1 [-Platforms win-x64,linux-x64]

param(
    [string[]]$Platforms = @("win-x64", "linux-x64", "osx-x64"),
    [switch]$SkipVsix
)

$Version = if ($env:ETL_SQL_VERSION) { $env:ETL_SQL_VERSION } else { "0.8.0" }
$ReleaseRoot = Join-Path $PSScriptRoot "..\release"
$SampleSource = Join-Path $PSScriptRoot "..\samples"
$DocsSource = Join-Path $PSScriptRoot "..\Docs"

function Join-PathSegments {
    param([string[]]$Segments)

    $Path = $Segments[0]
    for ($i = 1; $i -lt $Segments.Count; $i++) {
        $Path = Join-Path $Path $Segments[$i]
    }

    return $Path
}

# Projects to publish
$Projects = @(
    ,@("..", "src", "ETL-SQL.App", "ETL-SQL.App.csproj")
    ,@("..", "src", "ETL-SQL.TUI", "ETL-SQL.TUI.csproj")
    ,@("..", "src", "ETL-SQL.LanguageServer", "ETL-SQL.LanguageServer.csproj")
    ,@("..", "src", "ETL-SQL.ReportBuilder.CLI", "ETL-SQL.ReportBuilder.CLI.csproj")
    ,@("..", "src", "ETL-SQL.ReportPlayer", "ETL-SQL.ReportPlayer.csproj")
    ,@("..", "src", "ETL-SQL.ReportPortal", "ETL-SQL.ReportPortal.csproj")
    ,@("..", "src", "ETL-SQL.Orchestrator.Service", "ETL-SQL.Orchestrator.Service.csproj")
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
        $ProjPath = Join-Path $PSScriptRoot (Join-PathSegments $Proj)
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
        ,@("00_QuickStart", "sample_Hello.etlsql")
        ,@("01_Basics", "Variables_and_State.etlsql")
        ,@("04_Orchestration", "Data_Lineage.etlsql")
        ,@("07_Real_World", "realworld_01_dw_load.etlsql")
        ,@("07_Real_World", "realworld_02_secure_sftp_alert.etlsql")
        ,@("07_Real_World", "realworld_04_incremental_merge.etlsql")
        ,@("07_Real_World", "realworld_09_directory_watcher.etlsql")
        ,@("03_SQL_Engines", "Docker_Orchestration.etlsql")
        ,@("01_Basics", "Function_Library.etlsql")
        ,@("03_SQL_Engines", "ANSI_SQL_Extensions.etlsql")
        ,@("02_Data_Movement", "Avro_Read_Write.etlsql")
        ,@("02_Data_Movement", "Parquet_Read_Write.etlsql")
        ,@("08_Reporting", "sales report.rptsql")
        ,@("08_Reporting", "slicer_test.rptsql")
        ,@("99_Experimental", "verify_env.etlsql")
    )

    foreach ($Sample in $SampleList) {
        $RelativeSample = Join-PathSegments $Sample
        $Src = Join-Path $SampleSource $RelativeSample
        if (-not (Test-Path $Src)) {
            throw "Curated sample is missing: $RelativeSample"
        }

        $Dest = Join-Path $SampleFolder $RelativeSample
        $DestDir = Split-Path $Dest -Parent
        if (-not (Test-Path $DestDir)) {
            New-Item -ItemType Directory -Path $DestDir | Out-Null
        }
        Copy-Item $Src $Dest
    }

    if (-not $SkipVsix) {
        Write-Host "  Packaging VSIX bundle..." -ForegroundColor Gray
        $VsixPath = & (Join-Path $PSScriptRoot "publish_vsix.ps1") -Platform $Platform -BinSourceDir $BinFolder
        if (Test-Path $VsixPath) {
            Move-Item $VsixPath $BinFolder # Put it next to the EXEs
        }
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
