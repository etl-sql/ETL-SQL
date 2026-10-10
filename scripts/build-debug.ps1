param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$ext  = Join-Path $root "src\etl-sql-vscode"
$ui   = Join-Path $ext  "ui"

$failedSteps = [System.Collections.Generic.List[string]]::new()

function Step($label, [scriptblock]$action) {
    Write-Host "`n==> $label" -ForegroundColor Cyan
    try {
        & $action
        if ($LASTEXITCODE -ne 0) { throw "Command exited with code $LASTEXITCODE." }
        Write-Host "OK: $label" -ForegroundColor Green
    } catch {
        Write-Host "FAILED: $label — $($_.Exception.Message)" -ForegroundColor Red
        $failedSteps.Add($label)
    }
}
Step "Prepare browser assets" { node (Join-Path $root 'scripts/sync-assets.js') }
if ($failedSteps.Count -gt 0) { exit 1 }
Step ".NET build" {
    dotnet build "$root\ETL-SQL.slnx" --nologo -v minimal
}

Step "UI build  (vite)" {
    npm --prefix $ui run build --silent
}

Step "Extension compile  (tsc)" {
    npm --prefix $ext run compile --silent
}

if (-not $SkipTests) {
    Step "Extension unit tests  (vitest)" {
        npm --prefix $ext run test:unit
    }
}

Write-Host ""
if ($failedSteps.Count -eq 0) {
    Write-Host "All steps passed. Ready to debug." -ForegroundColor Green
} else {
    Write-Host "Failed steps: $($failedSteps -join ', ')" -ForegroundColor Red
    exit 1
}
