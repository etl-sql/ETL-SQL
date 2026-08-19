# Installs git hooks to automate formatting and pre-push validation.
$HooksDir = Join-Path $PSScriptRoot "../.git/hooks"

$PreCommitContent = @'
#!/bin/sh
# Git hook to verify C# formatting before committing
echo "Checking code formatting..."
dotnet format --verify-no-changes
if [ $? -ne 0 ]; then
    echo "Formatting checks failed. Run 'dotnet format' to resolve Style errors before committing."
    exit 1
fi
'@

$PrePushContent = @'
#!/bin/sh
# Git hook to run fast pre-push validation (format, assets, syntax index, flaky delays, test inventory, contract tests)
echo "Running fast pre-push validation..."
if command -v pwsh >/dev/null 2>&1; then
    pwsh -NoProfile -File ./scripts/Test-PrePush.ps1
else
    powershell -NoProfile -File ./scripts/Test-PrePush.ps1
fi
if [ $? -ne 0 ]; then
    echo "Pre-push validation failed. Run '.\scripts\Test-PrePush.ps1' locally to diagnose."
    exit 1
fi
'@

if (-not (Test-Path $HooksDir)) {
    New-Item -ItemType Directory -Path $HooksDir -Force | Out-Null
}

Set-Content -Path (Join-Path $HooksDir "pre-commit") -Value $PreCommitContent -NoNewline
Set-Content -Path (Join-Path $HooksDir "pre-push") -Value $PrePushContent -NoNewline
Write-Host "Git hooks (pre-commit, pre-push) installed successfully at $HooksDir" -ForegroundColor Green

