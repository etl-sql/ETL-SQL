[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$tag = "etlsql-sandbox-worker-test:$([guid]::NewGuid().ToString('N').Substring(0, 8))"

Write-Host "==> Building sandbox worker image with temporary tag: $tag"
& docker build -t $tag -f "$repoRoot/src/ETL-SQL.App/Dockerfile.sandbox" $repoRoot
if ($LASTEXITCODE -ne 0) {
    Write-Error "Docker build failed."
    exit $LASTEXITCODE
}

Write-Host "==> Obtaining content image ID..."
$imageId = & docker inspect --format '{{.Id}}' $tag
if (-not $imageId) {
    Write-Error "Failed to obtain image ID."
    exit 1
}

Write-Host "==> Verifying default user..."
$user = & docker inspect --format '{{.Config.User}}' $tag
if ([string]::IsNullOrWhiteSpace($user) -or $user -eq "root" -or $user -eq "0" -or $user -eq "0:0") {
    Write-Error "Configured user is blank or root ($user). The sandbox worker must run as a numeric non-root user by default."
    exit 1
}
Write-Host "    User is configured securely: $user"

Write-Host "==> Verifying entrypoint execution..."
# The ENTRYPOINT is ["/app/etl-sql"], so we just pass --version
$versionOutput = & docker run --rm $tag --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Execution of --version failed: $versionOutput"
    exit $LASTEXITCODE
}
Write-Host "    Execution succeeded."
Write-Host "    Version output: $versionOutput"

Write-Host "==> Cleaning up temporary tag..."
& docker rmi $tag | Out-Null

Write-Host ""
Write-Host "SUCCESS: Sandbox worker image is viable."
Write-Host "--------------------------------------------------------"
Write-Host "Local Image ID: $imageId"
Write-Host "--------------------------------------------------------"
Write-Host "Note: This is a local image ID, NOT a registry RepoDigest."
Write-Host "Use this precise Local Image ID in subsequent Hardened/sandbox configurations."
