# Installs git hooks to automate dotnet format checks.
$HooksDir = Join-Path $PSScriptRoot "../.git/hooks"

$HookContent = @'
#!/bin/sh
# Git hook to verify C# formatting
echo "Checking code formatting..."
dotnet format --verify-no-changes
if [ $? -ne 0 ]; then
    echo "Formatting checks failed. Run 'dotnet format' to resolve Style errors before committing or pushing."
    exit 1
fi
'@

if (-not (Test-Path $HooksDir)) {
    New-Item -ItemType Directory -Path $HooksDir -Force | Out-Null
}

Set-Content -Path (Join-Path $HooksDir "pre-commit") -Value $HookContent -NoNewline
Set-Content -Path (Join-Path $HooksDir "pre-push") -Value $HookContent -NoNewline
Write-Host "Git hooks (pre-commit, pre-push) installed successfully at $HooksDir" -ForegroundColor Green
