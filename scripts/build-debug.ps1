param(
    [switch]$SkipTests
)

$root = Join-Path $PSScriptRoot ".."
$ext  = Join-Path $root "src\etl-sql-vscode"
$ui   = Join-Path $ext  "ui"

$errors = @()

function Step($label, [scriptblock]$action) {
    Write-Host "`n==> $label" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $label" -ForegroundColor Red
        $script:errors += $label
    } else {
        Write-Host "OK: $label" -ForegroundColor Green
    }
}

Step ".NET build" {
    dotnet build "$root\ETL-SQL.slnx" --nologo -v minimal
}

Step "UI build  (vite)" {
    Push-Location $ui
    npm run build --silent
    Pop-Location
}

Step "Extension compile  (tsc)" {
    Push-Location $ext
    npm run compile --silent
    Pop-Location
}

if (-not $SkipTests) {
    Step "Extension unit tests  (vitest)" {
        Push-Location $ext
        npm run test:unit
        Pop-Location
    }
}

Write-Host ""
if ($errors.Count -eq 0) {
    Write-Host "All steps passed. Ready to debug." -ForegroundColor Green
} else {
    Write-Host "Failed steps: $($errors -join ', ')" -ForegroundColor Red
    exit 1
}
