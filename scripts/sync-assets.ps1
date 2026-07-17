# ETL-SQL Shared Assets Sync Script
# Source of Truth: src/ETL-SQL.ReportRuntime/Resources/Shared/
param(
    [switch]$Check
)

$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$SharedDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.ReportRuntime\Resources\Shared"

# Targets
$VsCodeMedia = Join-Path $PSScriptRoot "..\src\etl-sql-vscode\media"
$PlayerWwwRoot = Join-Path $PSScriptRoot "..\src\ETL-SQL.ReportPlayer\wwwroot"
$PortalJsDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.Portal\wwwroot\js"
$PortalCssDir = Join-Path $PSScriptRoot "..\src\ETL-SQL.Portal\wwwroot\css"
$Mode = if ($Check) { "Check" } else { "Sync" }

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host " Shared Report Assets $Mode" -ForegroundColor Cyan
Write-Host " Source: $SharedDir" -ForegroundColor Gray
Write-Host "=======================================================`n" -ForegroundColor Cyan

if (!(Test-Path $SharedDir)) {
    Write-Error "Shared source directory not found: $SharedDir"
    exit 1
}

$Files = Get-ChildItem $SharedDir -File -Recurse
$Drift = New-Object System.Collections.Generic.List[string]
$Failures = New-Object System.Collections.Generic.List[string]

function Get-AssetRelativePath {
    param(
        [string]$Path
    )

    $basePath = [System.IO.Path]::GetFullPath($SharedDir).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($basePath, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($basePath.Length)
    }

    return Split-Path -Leaf $Path
}

function Get-ExpectedContent {
    param(
        [System.IO.FileInfo]$File
    )

    $content = [System.IO.File]::ReadAllText($File.FullName)
    if ($File.Extension -eq ".js" -or $File.Extension -eq ".css") {
        $relativePath = (Get-AssetRelativePath -Path $File.FullName).Replace('\', '/')

        # Vendored third-party bundles (e.g. designer/codemirror/) are committed
        # pre-built and must not have a generated-file banner prepended.
        if ($relativePath -like "designer/codemirror/*") {
            return $content
        }

        $sourcePath = "src/ETL-SQL.ReportRuntime/Resources/Shared/$relativePath"
        $banner = @"
/* GENERATED FILE - DO NOT EDIT.
 * Source: $sourcePath
 * Edit the canonical source, then run: .\scripts\sync-assets.ps1
 */

"@
        return $banner + $content
    }

    return $content
}

function Sync-Or-Check {
    param(
        [System.IO.FileInfo]$File,
        [string]$TargetDir,
        [string]$Label
    )

    if (!(Test-Path $TargetDir)) {
        return
    }

    $relativePath = Get-AssetRelativePath -Path $File.FullName
    $targetPath = Join-Path $TargetDir $relativePath
    if ($Check) {
        if (!(Test-Path $targetPath)) {
            $Drift.Add("$Label missing $($File.Name)")
            return
        }

        $expectedContent = Get-ExpectedContent -File $File
        $targetContent = [System.IO.File]::ReadAllText($targetPath)
        if ($expectedContent -ne $targetContent) {
            $Drift.Add("$Label drifted: $($File.Name)")
        }
    } else {
        $expectedContent = Get-ExpectedContent -File $File
        if ((Test-Path $targetPath) -and ([System.IO.File]::ReadAllText($targetPath) -eq $expectedContent)) {
            Write-Host "    -> $Label OK" -ForegroundColor Gray
            return
        }

        $targetParent = Split-Path -Parent $targetPath
        if (!(Test-Path $targetParent)) {
            New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
        }

        try {
            if (Test-Path $targetPath) {
                Set-ItemProperty -Path $targetPath -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
            }

            [System.IO.File]::WriteAllText($targetPath, $expectedContent, [System.Text.UTF8Encoding]::new($false))
            Write-Host "    -> $Label OK" -ForegroundColor Gray
        } catch {
            $Failures.Add("$Label failed to write $relativePath`: $($_.Exception.Message)")
            Write-Host "    -> $Label FAILED" -ForegroundColor Red
        }
    }
}

foreach ($File in $Files) {
    $Verb = if ($Check) { "Checking" } else { "Syncing" }
    Write-Host "  $Verb $($File.Name)..." -ForegroundColor Yellow
    
    # 1. VS Code Media
    Sync-Or-Check -File $File -TargetDir $VsCodeMedia -Label "VS Code"

    # 2. ReportPlayer (Static Web Files)
    Sync-Or-Check -File $File -TargetDir $PlayerWwwRoot -Label "ReportPlayer"

    # 3. Portal (Categorized JS/CSS/maps/designer)
    if ((Test-Path $PortalJsDir) -and (Test-Path $PortalCssDir)) {
        $relativePath = Get-AssetRelativePath -Path $File.FullName
        $portalRoot = Join-Path $PSScriptRoot "..\src\ETL-SQL.Portal\wwwroot"
        if ($relativePath -like "maps\*") {
            # Maps preserve their subdirectory under wwwroot/maps/
            Sync-Or-Check -File $File -TargetDir $portalRoot -Label "Portal (Maps)"
        } elseif ($relativePath -like "designer\*") {
            # Designer files preserve their full subdirectory under wwwroot/designer/
            Sync-Or-Check -File $File -TargetDir $portalRoot -Label "Portal (Designer)"
        } elseif ($File.Extension -eq ".js") {
            Sync-Or-Check -File $File -TargetDir $PortalJsDir -Label "Portal (JS)"
        } elseif ($File.Extension -eq ".css") {
            Sync-Or-Check -File $File -TargetDir $PortalCssDir -Label "Portal (CSS)"
        } else {
            Sync-Or-Check -File $File -TargetDir $PortalJsDir -Label "Portal (Misc)"
        }
    }
}

if ($Check -and $Drift.Count -gt 0) {
    Write-Host "`nShared report assets have drifted from src/ETL-SQL.ReportRuntime/Resources/Shared:" -ForegroundColor Red
    foreach ($item in $Drift) {
        Write-Host "  - $item" -ForegroundColor Red
    }
    Write-Host "`nRun .\scripts\sync-assets.ps1 to refresh host copies." -ForegroundColor Yellow
    exit 1
}

if (!$Check -and $Failures.Count -gt 0) {
    Write-Host "`nShared report asset sync failed:" -ForegroundColor Red
    foreach ($item in $Failures) {
        Write-Host "  - $item" -ForegroundColor Red
    }
    exit 1
}

Write-Host "`n$Mode Complete." -ForegroundColor Green
