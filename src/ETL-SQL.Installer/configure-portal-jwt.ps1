# Post-install configuration applied to the appsettings.json next to this script (the install folder).
# Runs as a deferred MSI custom action (LocalSystem) before the services start. Three jobs:
#   1. Generate a plaintext base64 JWT signing secret for the Portal if one is not set, so
#      JwtSecretValidationService passes on first start (idempotent: existing secret is preserved).
#   2. Generate a base64 Orchestrator API key (and mirror it to Portal:Orchestrator:ApiKey) if not set.
#      The Orchestrator binds to a network address and refuses to start without a key, so this must be
#      present for the service to come up. Idempotent: an existing key on either side is preserved/reused.
#   3. Approve the install folder as a Security safe zone so the engine's path-protection guard lets
#      the services write their working data (datasets, snapshots, portal.db, logs) under the install
#      folder, which lives in the otherwise-restricted Program Files tree.
param(
    [string]$CustomData = ''
)

$ErrorActionPreference = 'Stop'

function New-Base64Secret {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return [Convert]::ToBase64String($bytes)
}

try {
    $parts = $CustomData.Split('|')
    $installDir = if ($parts.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($parts[0])) { $parts[0] } else { $PSScriptRoot }
    $cfgPath = Join-Path $installDir 'appsettings.json'
    if (-not (Test-Path -LiteralPath $cfgPath)) {
        $cfgPath = Join-Path $PSScriptRoot 'appsettings.json'
    }
    if (-not (Test-Path -LiteralPath $cfgPath)) { return }

    $moduleOpts = @{}
    foreach ($part in $parts) {
        if ($part -match '^([^=]+)=([^=]*)$') {
            $moduleOpts[$Matches[1].ToUpperInvariant()] = $Matches[2].Trim()
        }
    }

    $json = Get-Content -LiteralPath $cfgPath -Raw | ConvertFrom-Json
    $changed = $false

    if ($null -ne $json.Portal -and $null -ne $json.Portal.Jwt -and `
        [string]::IsNullOrWhiteSpace([string]$json.Portal.Jwt.Secret)) {
        $json.Portal.Jwt.Secret = New-Base64Secret
        $changed = $true
    }

    # Portal Module Sub-choices (Subject Enablement)
    if ($null -ne $json.Portal) {
        if ($null -eq $json.Portal.Modules) {
            $json.Portal | Add-Member -MemberType NoteProperty -Name "Modules" -Value (New-Object PSObject) -Force
        }

        if ($moduleOpts.ContainsKey('REPORTING')) {
            $json.Portal.Modules | Add-Member -MemberType NoteProperty -Name "Reporting" -Value ($moduleOpts['REPORTING'] -eq '1') -Force
            $changed = $true
        }
        if ($moduleOpts.ContainsKey('DESIGNER')) {
            $json.Portal.Modules | Add-Member -MemberType NoteProperty -Name "Designer" -Value ($moduleOpts['DESIGNER'] -eq '1') -Force
            $changed = $true
        }
        if ($moduleOpts.ContainsKey('SCHEDULING')) {
            $json.Portal.Modules | Add-Member -MemberType NoteProperty -Name "Scheduling" -Value ($moduleOpts['SCHEDULING'] -eq '1') -Force
            $changed = $true
        }
        if ($moduleOpts.ContainsKey('OPERATIONS')) {
            $json.Portal.Modules | Add-Member -MemberType NoteProperty -Name "Operations" -Value ($moduleOpts['OPERATIONS'] -eq '1') -Force
            $changed = $true
        }
    }

    # Orchestrator API key — keep the service key and the Portal's client key in sync.
    $orch = $json.Orchestrator
    $portalOrch = if ($null -ne $json.Portal) { $json.Portal.Orchestrator } else { $null }
    $apiKey = $null
    if ($null -ne $orch -and -not [string]::IsNullOrWhiteSpace([string]$orch.ApiKey)) {
        $apiKey = [string]$orch.ApiKey
    } elseif ($null -ne $portalOrch -and -not [string]::IsNullOrWhiteSpace([string]$portalOrch.ApiKey)) {
        $apiKey = [string]$portalOrch.ApiKey
    }
    if ($null -eq $apiKey) { $apiKey = New-Base64Secret }
    if ($null -ne $orch -and [string]::IsNullOrWhiteSpace([string]$orch.ApiKey)) {
        $orch.ApiKey = $apiKey
        $changed = $true
    }
    if ($null -ne $portalOrch -and [string]::IsNullOrWhiteSpace([string]$portalOrch.ApiKey)) {
        $portalOrch.ApiKey = $apiKey
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
