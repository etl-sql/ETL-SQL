# Verify-ReplFeatures.ps1
# This script automates the verification of the ETL-SQL REPL Protocol.

$projectName = "src\ETL-SQL.App\ETL-SQL.App.csproj"
$exportPath = Join-Path (Get-Location) "repl_verify.csv"

Write-Host "--- Automated REPL Verification ---" -ForegroundColor Cyan

# Ensure the app is built
Write-Host "Building project..." -ForegroundColor Gray
dotnet build src\ETL-SQL.App -v q
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; exit 1 }

# Cleanup old test files
if (Test-Path $exportPath) { Remove-Item $exportPath }

# Construct JSON commands with absolute escaped paths
$escapedExportPath = $exportPath.Replace("\", "\\")
$commands = @(
    '{"action": "run", "script": "DECLARE @testVal = ''REPL_VERIFIED''; SELECT @testVal AS Status;"}',
    '{"action": "export", "path": "' + $escapedExportPath + '", "format": "csv"}',
    '{"action": "exit"}'
)

Write-Host "Launching REPL and sending commands..." -ForegroundColor Cyan
$process = New-Object System.Diagnostics.Process
$process.StartInfo.FileName = "dotnet"
$process.StartInfo.Arguments = "run --project $projectName -- ui repl"
$process.StartInfo.UseShellExecute = $false
$process.StartInfo.RedirectStandardInput = $true
$process.StartInfo.RedirectStandardOutput = $true
$process.StartInfo.CreateNoWindow = $true

$process.Start() | Out-Null

$sw = $process.StandardInput
foreach ($cmd in $commands) {
    Write-Host "Sending: $cmd" -ForegroundColor Gray
    $sw.WriteLine($cmd)
}
$sw.Close()

$output = $process.StandardOutput.ReadToEnd()
$process.WaitForExit()

Write-Host "`n--- REPL Response Analysis ---" -ForegroundColor Yellow
$outputLines = $output -split "`r?`n"
$foundVars = $false
$foundExport = $false

foreach ($line in $outputLines) {
    if ($line -like "*variables*") { $foundVars = $true }
    if ($line -like "*Successfully exported*") { $foundExport = $true }
}

if ($foundVars) { Write-Host "[OK] Variable Explorer JSON found." -ForegroundColor Green }
else { Write-Host "[FAIL] Variable Explorer JSON missing." -ForegroundColor Red }

if ($foundExport) { Write-Host "[OK] Export confirmation JSON found." -ForegroundColor Green }
else { Write-Host "[FAIL] Export confirmation JSON missing." -ForegroundColor Red }

# Final Validation
if (Test-Path $exportPath) {
    Write-Host "`nSUCCESS: CSV Export file created at $exportPath" -ForegroundColor Green
    $content = Get-Content $exportPath
    Write-Host "CSV Content:" -ForegroundColor Gray
    Write-Host $content
} else {
    Write-Host "`nFAILURE: CSV Export file was not created." -ForegroundColor Red
    exit 1
}

Write-Host "`nREPL Verification Complete." -ForegroundColor Cyan
