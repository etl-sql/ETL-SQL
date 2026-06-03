# ETL-SQL Release Automation Script
# Usage: ./publish_release.ps1 [-Platforms win-x64,linux-x64]

param(
    [string[]]$Platforms = @("win-x64", "linux-x64", "osx-x64", "osx-arm64"),
    [switch]$SkipVsix
)

$Version = if ($env:ETL_SQL_VERSION) { $env:ETL_SQL_VERSION } else { "0.9.0" }
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ReleaseRoot = Join-Path $RepoRoot "release"
$SampleSource = Join-Path $RepoRoot "samples"
$DocsSource = Join-Path $RepoRoot "Docs"

function Join-PathSegments {
    param([string[]]$Segments)

    $Path = $Segments[0]
    for ($i = 1; $i -lt $Segments.Count; $i++) {
        $Path = Join-Path $Path $Segments[$i]
    }

    return $Path
}

function Assert-NativeCommandSucceeded {
    param([Parameter(Mandatory = $true)][string]$Description)

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE"
    }
}

function Assert-ReleaseBinaries {
    param(
        [Parameter(Mandatory = $true)][string]$Platform,
        [Parameter(Mandatory = $true)][string]$BinFolder
    )

    $suffix = if ($Platform -eq "win-x64") { ".exe" } else { "" }
    $required = @(
        "ETL-SQL$suffix",
        "ETL-SQL-TUI$suffix",
        "ETL-SQL-LSP$suffix",
        "ETL-SQL-Report$suffix",
        "ETL-SQL-Player$suffix",
        "ETL-SQL-Portal$suffix",
        "ETL-SQL-Service$suffix"
    )

    $missing = @()
    foreach ($file in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $BinFolder $file))) {
            $missing += $file
        }
    }

    if ($missing.Count -gt 0) {
        throw "Release publish for $Platform is missing required binaries: $($missing -join ', ')"
    }
}

function New-VerifiedArchive {
    param(
        [Parameter(Mandatory = $true)][string]$SourceGlob,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $tempPath = "$DestinationPath.tmp.zip"
    if (Test-Path -LiteralPath $DestinationPath) { Remove-Item -LiteralPath $DestinationPath -Force }
    if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force }

    Compress-Archive -Path $SourceGlob -DestinationPath $tempPath -Force

    $archive = Get-Item -LiteralPath $tempPath
    if ($archive.Length -le 0) {
        Remove-Item -LiteralPath $tempPath -Force
        throw "Archive creation produced an empty file: $DestinationPath"
    }

    Move-Item -LiteralPath $tempPath -Destination $DestinationPath -Force
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
        Assert-NativeCommandSucceeded "dotnet publish $ProjName for $Platform"
    }

    Assert-ReleaseBinaries -Platform $Platform -BinFolder $BinFolder

    # 3. Cleanup redundant files
    Write-Host "  Cleaning up redundant assets..." -ForegroundColor Gray
    Get-ChildItem $BinFolder -Filter "appsettings.Development.json" -Recurse | Remove-Item -Force
    Get-ChildItem $BinFolder -Filter "*.staticwebassets.endpoints.json" -Recurse | Remove-Item -Force

    # 4. Copy Docs
    Copy-Item (Join-Path $DocsSource "QUICKSTART.txt") $DocFolder
    Copy-Item (Join-Path $DocsSource "ReportPortal_Administrators_Guide.md") (Join-Path $DocFolder "ReportPortal_Guide.txt")
    Copy-Item (Join-Path $RepoRoot "CHANGELOG.md") (Join-Path $DocFolder "CHANGELOG.txt") # Rename to txt for portability
    
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
        # Fail loudly instead of silently shipping a release with no extension. publish_vsix.ps1
        # exits non-zero on failure; that leaves $VsixPath empty/invalid here.
        if (-not $VsixPath -or -not (Test-Path $VsixPath)) {
            throw "VSIX packaging failed for $Platform (no .vsix produced). Re-run with -SkipVsix to bypass."
        }
        # Publish the VSIX as a standalone GitHub release asset (discoverable for VS Code users)
        # rather than burying it inside the platform ZIP.
        $VsixDest = Join-Path $ReleaseRoot ([System.IO.Path]::GetFileName($VsixPath))
        Move-Item $VsixPath $VsixDest -Force
        Write-Host "  VSIX asset: $VsixDest" -ForegroundColor Green
    }

    # 7. Zip for GitHub Release
    Write-Host "  Creating ZIP archive for GitHub..." -ForegroundColor Gray
    $ZipFileName = "ETL-SQL-v$Version-$Platform.zip"
    $ZipDest = Join-Path $ReleaseRoot $ZipFileName
    New-VerifiedArchive -SourceGlob (Join-Path $PlatformFolder "*") -DestinationPath $ZipDest

    Write-Host "  Platform $Platform complete. Archive: $ZipFileName" -ForegroundColor Green
}

Write-Host "`nRelease v$Version ready in $ReleaseRoot" -ForegroundColor Cyan
