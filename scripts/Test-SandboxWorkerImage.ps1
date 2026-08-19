[CmdletBinding()]
param(
    # A stable tag lets the Docker Desktop / runc lifecycle tests find the image they need.
    [string] $Tag,

    # Keep the built image instead of removing the temporary tag. Implied by -Tag.
    [switch] $Keep
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$keepImage = $Keep.IsPresent -or -not [string]::IsNullOrWhiteSpace($Tag)
$tag = if ([string]::IsNullOrWhiteSpace($Tag)) {
    "etlsql-sandbox-worker-test:$([guid]::NewGuid().ToString('N').Substring(0, 8))"
} else {
    $Tag
}

Write-Host "==> Building sandbox worker image with temporary tag: $tag"
$buildSuccess = $false
for ($attempt = 1; $attempt -le 3; $attempt++) {
    & docker build -t $tag -f "$repoRoot/src/ETL-SQL.App/Dockerfile.sandbox" $repoRoot
    if ($LASTEXITCODE -eq 0) {
        $buildSuccess = $true
        break
    }
    if ($attempt -lt 3) {
        Write-Warning "Docker build attempt $attempt failed; retrying in $($attempt * 2) seconds..."
        Start-Sleep -Seconds ($attempt * 2)
    }
}
if (-not $buildSuccess) {
    Write-Error "Docker build failed after 3 attempts."
    exit 1
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

if ($keepImage) {
    Write-Host "==> Keeping tag: $tag"
} else {
    Write-Host "==> Cleaning up temporary tag..."
    & docker rmi $tag | Out-Null
}

Write-Host ""
Write-Host "SUCCESS: Sandbox worker image is viable."
Write-Host "--------------------------------------------------------"
Write-Host "Local Image ID: $imageId"
Write-Host "--------------------------------------------------------"
Write-Host "Note: This is a local image ID, NOT a registry RepoDigest."
Write-Host "A local image ID is accepted only by the Standard provider mode. Hardened and Dedicated"
Write-Host "configurations still require a digest-pinned registry reference."
