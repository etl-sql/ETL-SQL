# Installs a git pre-commit hook to automate dotnet format checks.
$HookPath = Join-Path $PSScriptRoot "../.git/hooks/pre-commit"

$HookContent = @'
#!/bin/sh
# Git pre-commit hook to verify C# formatting
echo "Checking code formatting..."
dotnet format --verify-no-changes
if [ $? -ne 0 ]; then
    echo "Formatting checks failed. Run 'dotnet format' to resolve Style errors before committing."
    exit 1
fi
'@

if (-not (Test-Path (Split-Path $HookPath))) {
    New-Item -ItemType Directory -Path (Split-Path $HookPath) -Force | Out-Null
}

Set-Content -Path $HookPath -Value $HookContent -NoNewline
Write-Host "Git pre-commit hook installed successfully at $HookPath" -ForegroundColor Green
