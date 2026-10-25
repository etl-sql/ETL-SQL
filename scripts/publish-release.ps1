# ETL-SQL Release Automation Script
# Usage: ./publish-release.ps1 [-Platforms win-x64,linux-x64]

param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string[]]$Platforms = @("win-x64", "linux-x64", "osx-x64", "osx-arm64"),
    [switch]$SkipVsix,
    [string]$CertificationEvidenceRoot,
    [psobject]$OutputTransaction
)

$ErrorActionPreference = 'Stop'
# COMPAT_BREAK: 0.22 — reject unsafe packaging inputs before release output cleanup.
$Platforms = @($Platforms | ForEach-Object { $_.ToLowerInvariant() })
if ($Platforms.Count -eq 0 -or @($Platforms | Select-Object -Unique).Count -ne $Platforms.Count) {
    throw 'Choose at least one supported platform, with no duplicates.'
}
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Version = if ($env:ETL_SQL_VERSION) { $env:ETL_SQL_VERSION } else {
    [xml]$versionProps = Get-Content -LiteralPath (Join-Path $RepoRoot 'Directory.Build.props')
    [string]$versionProps.Project.PropertyGroup.VersionPrefix
}
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw 'Release version must be a file-safe three-part version.'
}
$ReleaseRoot = Join-Path $RepoRoot "release"
$SampleSource = Join-Path $RepoRoot "samples"
$RuntimeStatePatterns = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'lib/package-runtime-state.patterns'))
. (Join-Path $PSScriptRoot 'ReleaseEvidence.Helpers.ps1')
$explicitEvidenceRoot = -not [string]::IsNullOrWhiteSpace($CertificationEvidenceRoot)
if (-not $explicitEvidenceRoot) {
    $CertificationEvidenceRoot = Join-Path $RepoRoot "artifacts/release-evidence/$Version/deployment-profiles"
}
$hasCertificationEvidence = Test-Path -LiteralPath $CertificationEvidenceRoot -PathType Container
if ($explicitEvidenceRoot -or $hasCertificationEvidence) {
    $CandidateCommit = & git -C $RepoRoot rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the candidate commit.' }
    $dirty = & git -C $RepoRoot status --porcelain
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Certification packaging requires the clean candidate worktree.' }
    # Validate before deleting an earlier release output or spending time publishing binaries.
    $null = @(Get-ReleaseCertificationInputs -EvidenceRoot $CertificationEvidenceRoot -ReleaseVersion $Version -CandidateCommit $CandidateCommit -RequireCompleteClaimIndex -RequireValidatedExecutionPlan)
}

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
        $binary = Get-Item -LiteralPath (Join-Path $BinFolder $file) -Force -ErrorAction SilentlyContinue
        if (-not $binary -or $binary.PSIsContainer -or $binary.Length -eq 0 -or
            ($binary.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            $missing += $file
        }
    }

    if ($missing.Count -gt 0) {
        throw "Release publish for $Platform has missing or invalid required binaries: $($missing -join ', ')"
    }
}

function New-VerifiedArchive {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    if (Test-Path -LiteralPath $DestinationPath) {
        $destination = Get-Item -LiteralPath $DestinationPath -Force
        if ($destination.PSIsContainer -or ($destination.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Accepted archive destination must be a regular file.'
        }
    }
    $expected = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($item in Get-ChildItem -LiteralPath $SourceDirectory -Recurse -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Archive input must not contain links.' }
        if (-not $item.PSIsContainer) {
            $relative = [IO.Path]::GetRelativePath($SourceDirectory, $item.FullName).Replace('\', '/')
            $expected.Add($relative, (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash)
        }
    }
    if ($expected.Count -eq 0) { throw 'Cannot package an empty release directory.' }
    $tempPath = "$DestinationPath.$([Guid]::NewGuid().ToString('N')).tmp.zip"
    try {
        [IO.Compression.ZipFile]::CreateFromDirectory($SourceDirectory, $tempPath)
        $archive = [IO.Compression.ZipFile]::OpenRead($tempPath)
        try {
            $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($entry in $archive.Entries) {
                if ($entry.FullName.EndsWith('/')) { continue }
                if (-not $expected.ContainsKey($entry.FullName) -or -not $seen.Add($entry.FullName)) {
                    throw 'Archive contains an unexpected or duplicate file.'
                }
                $stream = $entry.Open()
                try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
                finally { $stream.Dispose() }
                if ($hash -ne $expected[$entry.FullName]) { throw 'Archived file does not match its release input.' }
            }
            if ($seen.Count -ne $expected.Count) { throw 'Archive is missing release files.' }
        } finally { $archive.Dispose() }
        Move-Item -LiteralPath $tempPath -Destination $DestinationPath -Force
    } finally {
        if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force }
    }
}

# Projects to publish
$Projects = @(
    ,@("..", "src", "ETL-SQL.App", "ETL-SQL.App.csproj")
    ,@("..", "src", "ETL-SQL.TUI", "ETL-SQL.TUI.csproj")
    ,@("..", "src", "ETL-SQL.LanguageServer", "ETL-SQL.LanguageServer.csproj")
    ,@("..", "src", "ETL-SQL.ReportBuilder.CLI", "ETL-SQL.ReportBuilder.CLI.csproj")
    ,@("..", "src", "ETL-SQL.ReportPlayer", "ETL-SQL.ReportPlayer.csproj")
    # Workstation editor ships with the CLI/workstation set, not the Portal set: it runs on
    # loopback with no IIS, Docker, PostgreSQL or Portal database.
    ,@("..", "src", "ETL-SQL.WorkstationEditor", "ETL-SQL.WorkstationEditor.csproj")
    ,@("..", "src", "ETL-SQL.Portal", "ETL-SQL.Portal.csproj")
    ,@("..", "src", "ETL-SQL.Orchestrator.Service", "ETL-SQL.Orchestrator.Service.csproj")
)

# COMPAT_BREAK: 0.22 — retain accepted releases throughout a failed complete rebuild.
. (Join-Path $PSScriptRoot 'ReleaseOutput.Helpers.ps1')
$ownsTransaction = $null -eq $OutputTransaction
if ($ownsTransaction) { $transaction = New-ReleaseOutputTransaction -RepoRoot $RepoRoot }
else {
    Assert-ActiveReleaseOutputTransaction -RepoRoot $RepoRoot -Transaction $OutputTransaction
    $transaction = $OutputTransaction
}
$ReleaseRoot = $transaction.Output
try {
    Write-Host "Release attempt retained at $($transaction.Attempt)" -ForegroundColor Gray
    & node (Join-Path $RepoRoot 'scripts/sync-assets.js')
    if ($LASTEXITCODE -ne 0) { throw 'Browser asset preparation failed. Run npm ci --prefix scripts/typecheck first.' }

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
        foreach ($item in Get-ChildItem -LiteralPath $BinFolder -Force) {
            foreach ($pattern in $RuntimeStatePatterns) {
                if ($item.Name -like $pattern) { throw 'Runtime state in the publish folder. Publish again into a clean folder.' }
            }
        }

        # 3. Cleanup redundant files
        Write-Host "  Cleaning up redundant assets..." -ForegroundColor Gray
        Get-ChildItem -LiteralPath $BinFolder -Filter "appsettings.Development.json" -Recurse | Remove-Item -Force
        Get-ChildItem -LiteralPath $BinFolder -Filter "*.staticwebassets.endpoints.json" -Recurse | Remove-Item -Force

        # 4. Docs pointer + compliance files.
        # Full guides/reference live on GitHub and evolve between releases, so we ship a
        # version-pinned pointer here instead of bundling drift-prone doc copies. License,
        # notice, third-party notices, and the changelog travel with the binaries.
        $DocsUrl = "https://github.com/etl-sql/ETL-SQL/tree/v$Version/docs"
        @(
            "ETL-SQL v$Version - Documentation",
            "",
            "Full documentation is maintained online and versioned with each release:",
            "",
            "  Docs:            $DocsUrl",
            "  Getting started: https://github.com/etl-sql/ETL-SQL/blob/v$Version/docs/guides/onboarding/getting-started.md",
            "  Repository:      https://github.com/etl-sql/ETL-SQL",
            "  Issues:          https://github.com/etl-sql/ETL-SQL/issues",
            "",
            "This release bundles LICENSE, NOTICE, third-party notices, and the changelog (below)."
        ) | Set-Content -LiteralPath (Join-Path $DocFolder "DOCS.txt") -Encoding utf8
        Copy-Item -LiteralPath (Join-Path $RepoRoot "CHANGELOG.md") -Destination (Join-Path $DocFolder "CHANGELOG.txt") # Rename to txt for portability
        Copy-Item -LiteralPath (Join-Path $RepoRoot "LICENSE.md") -Destination (Join-Path $DocFolder "LICENSE.md")
        Copy-Item -LiteralPath (Join-Path $RepoRoot "NOTICE.md") -Destination (Join-Path $DocFolder "NOTICE.md")
        Copy-Item -LiteralPath (Join-Path $RepoRoot "THIRD-PARTY-NOTICES.md") -Destination (Join-Path $DocFolder "THIRD-PARTY-NOTICES.md")

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
            ,@("08_Reporting", "sales_report.rptsql")
            ,@("08_Reporting", "slicer_test.rptsql")
            ,@("99_Experimental", "verify_env.etlsql")
        )

        foreach ($Sample in $SampleList) {
            $RelativeSample = Join-PathSegments $Sample
            $Src = Join-Path $SampleSource $RelativeSample
            if (-not (Test-Path -LiteralPath $Src)) {
                throw "Curated sample is missing: $RelativeSample"
            }

            $Dest = Join-Path $SampleFolder $RelativeSample
            $DestDir = Split-Path $Dest -Parent
            if (-not (Test-Path -LiteralPath $DestDir)) {
                New-Item -ItemType Directory -Path $DestDir | Out-Null
            }
            Copy-Item -LiteralPath $Src -Destination $Dest
        }

        if (-not $SkipVsix) {
            Write-Host "  Packaging VSIX bundle..." -ForegroundColor Gray
            $VsixPath = & (Join-Path $PSScriptRoot "publish-vsix.ps1") -Platform $Platform -BinSourceDir $BinFolder -OutputDir (Join-Path $ReleaseRoot 'vsix')
            Assert-NativeCommandSucceeded -Description 'VSIX packaging'
            # Fail loudly instead of silently shipping a release with no extension. publish-vsix.ps1
            # exits non-zero on failure; that leaves $VsixPath empty/invalid here.
            $vsixFile = if ($VsixPath) { Get-Item -LiteralPath $VsixPath -Force -ErrorAction SilentlyContinue } else { $null }
            if (-not $vsixFile -or $vsixFile.PSIsContainer -or $vsixFile.Length -eq 0 -or
                ($vsixFile.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "VSIX packaging failed for $Platform (no .vsix produced). Re-run with -SkipVsix to bypass."
            }
            # Publish the VSIX as a standalone GitHub release asset (discoverable for VS Code users)
            # rather than burying it inside the platform ZIP.
            $VsixDest = Join-Path $ReleaseRoot ([System.IO.Path]::GetFileName($VsixPath))
            Move-Item -LiteralPath $VsixPath -Destination $VsixDest -Force
            Write-Host "  VSIX asset: $VsixDest" -ForegroundColor Green
        }

        # 7. Zip for GitHub Release
        Write-Host "  Creating ZIP archive for GitHub..." -ForegroundColor Gray
        $ZipFileName = "ETL-SQL-v$Version-$Platform.zip"
        $ZipDest = Join-Path $ReleaseRoot $ZipFileName
        New-VerifiedArchive -SourceDirectory $PlatformFolder -DestinationPath $ZipDest

        Write-Host "  Platform $Platform complete. Archive: $ZipFileName" -ForegroundColor Green
    }

    # 7.5 Retain test and certification reports in release assets
    Write-Host "`nPackaging test and certification reports..." -ForegroundColor Yellow
    if ($hasCertificationEvidence) {
        New-ReleaseCertificationArchive -EvidenceRoot $CertificationEvidenceRoot -ReleaseVersion $Version -CandidateCommit $CandidateCommit `
            -DestinationPath (Join-Path $ReleaseRoot "ETL-SQL-v$Version-certification-results.zip") -RequireCompleteClaimIndex -RequireValidatedExecutionPlan
        Write-Host "  Packaged verified release-specific certification bundles." -ForegroundColor Green
    } else {
        Write-Host "  No release-specific certification evidence present; no certification ZIP produced." -ForegroundColor Yellow
        Write-Host "  Release publication still requires the accepted evidence bundles from the checklist." -ForegroundColor Yellow
    }

    # 8. Generate SBOM
    Write-Host "`nGenerating Software Bill of Materials (SBOM)..." -ForegroundColor Yellow
    node (Join-Path $PSScriptRoot "generate-sbom.js") --output-dir $ReleaseRoot
    Assert-NativeCommandSucceeded -Description 'SBOM generation'
    $sbom = Get-Content -LiteralPath (Join-Path $ReleaseRoot 'sbom.json') -Raw | ConvertFrom-Json
    if ($sbom.metadata.component.version -ne $Version) { throw 'SBOM component version must match the release version.' }

    # 9. Generate Checksums
    Write-Host "`nGenerating SHA-256 checksums..." -ForegroundColor Yellow
    Write-ReleaseChecksums -OutputDirectory $ReleaseRoot
    Write-Host "  Checksums file generated: sha256sums.txt" -ForegroundColor Green

    if ($ownsTransaction) {
        Complete-ReleaseOutputTransaction $transaction
        Write-Host "`nRelease v$Version ready in $($transaction.Accepted)" -ForegroundColor Cyan
    } else { Write-Host "Portable bundles staged in $ReleaseRoot" -ForegroundColor Cyan }
} finally {
    if ($ownsTransaction) { Close-ReleaseOutputTransaction $transaction }
}
