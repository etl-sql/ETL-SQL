# Post-install configuration applied to the appsettings.json next to this script (the install folder).
# Runs as a deferred MSI custom action (LocalSystem) before the services start. Two jobs:
#   1. Generate a plaintext base64 JWT signing secret for the Report Portal if one is not set, so
#      JwtSecretValidationService passes on first start (idempotent: existing secret is preserved).
#   2. Approve the install folder as a Security safe zone so the engine's path-protection guard lets
#      the services write their working data (datasets, snapshots, portal.db, logs) under the install
#      folder, which lives in the otherwise-restricted Program Files tree.
$ErrorActionPreference = 'Stop'
try {
    $cfgPath = Join-Path $PSScriptRoot 'appsettings.json'
    if (-not (Test-Path -LiteralPath $cfgPath)) { return }

    $json = Get-Content -LiteralPath $cfgPath -Raw | ConvertFrom-Json
    $changed = $false

    if ($null -ne $json.Portal -and $null -ne $json.Portal.Jwt -and `
        [string]::IsNullOrWhiteSpace([string]$json.Portal.Jwt.Secret)) {
        $bytes = New-Object byte[] 32
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        $json.Portal.Jwt.Secret = [Convert]::ToBase64String($bytes)
        $changed = $true
    }

    $installDir = $PSScriptRoot.TrimEnd('\')
    if ($null -ne $json.Security) {
        # Force an array shape so the value binds to string[] regardless of element count.
        $json.Security.ApprovedSafeZones = @($installDir)
        $changed = $true
    }

    if ($changed) {
        ($json | ConvertTo-Json -Depth 64) | Set-Content -LiteralPath $cfgPath -Encoding UTF8
    }
} catch {
    # Non-fatal: a genuinely missing secret or safe zone surfaces when the service runs, rather
    # than aborting the whole installation here.
}
