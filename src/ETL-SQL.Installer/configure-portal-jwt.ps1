# Post-install configuration applied to the appsettings.json next to this script (the install folder).
# Runs as a deferred MSI custom action (LocalSystem) before the services start. Four jobs:
#   1. Generate a plaintext base64 JWT signing secret for the Portal if one is not set, so
#      JwtSecretValidationService passes on first start (idempotent: existing secret is preserved).
#   2. Generate a base64 Orchestrator API key (and mirror it to Portal:Orchestrator:ApiKey) if not set.
#      The Orchestrator binds to a network address and refuses to start without a key, so this must be
#      present for the service to come up. Idempotent: an existing key on either side is preserved/reused.
#   3. Approve the install folder as a Security safe zone so the engine's path-protection guard lets
#      the services write their working data (datasets, snapshots, portal.db, logs) under the install
#      folder, which lives in the otherwise-restricted Program Files tree.
#   4. Write the Portal:Surface runtime flag from SURFACE= in CustomActionData.
#      Values: "All" (both Admin and Report surfaces), "AdminOnly", or "ReportOnly".
#      This is the installer feature decision made once here so the runtime binary
#      activates only the surfaces that were selected during install.
param(
    [string]$CustomData = ''
)

$ErrorActionPreference = 'Stop'

function New-Base64Secret {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return [Convert]::ToBase64String($bytes)
}

# Installed values win; objects merge so keys only the new defaults have are kept.
function Merge-InstalledSettings {
    param($Target, $Source)
    foreach ($property in $Source.PSObject.Properties) {
        $existing = $Target.PSObject.Properties[$property.Name]
        if ($null -ne $existing -and
            $existing.Value -is [System.Management.Automation.PSCustomObject] -and
            $property.Value -is [System.Management.Automation.PSCustomObject]) {
            Merge-InstalledSettings -Target $existing.Value -Source $property.Value
        } else {
            $Target | Add-Member -MemberType NoteProperty -Name $property.Name -Value $property.Value -Force
        }
    }
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

    # A major upgrade removed the installed appsettings.json and laid down the package's defaults;
    # BackupSettings kept the installed one. Its values win (the operator's settings and the generated
    # secrets) and keys new in this release are added. Losing the secrets would sign everyone out and
    # make cached datasets unreadable.
    $savedPath = Join-Path $installDir 'appsettings.pre-upgrade.json'
    if (Test-Path -LiteralPath $savedPath -PathType Leaf) {
        $installed = Get-Content -LiteralPath $savedPath -Raw | ConvertFrom-Json
        Merge-InstalledSettings -Target $json -Source $installed
        $changed = $true
    }

    if ($null -ne $json.Portal -and $null -ne $json.Portal.Jwt -and `
        [string]::IsNullOrWhiteSpace([string]$json.Portal.Jwt.Secret)) {
        $json.Portal.Jwt.Secret = New-Base64Secret
        $changed = $true
    }

    # The Portal refuses to start without a dataset at-rest key. It must survive upgrades (above):
    # a new key makes every cached dataset unreadable.
    if ($null -ne $json.Portal -and $null -ne $json.Portal.Dataset -and `
        [string]::IsNullOrWhiteSpace([string]$json.Portal.Dataset.AtRestKey)) {
        $json.Portal.Dataset.AtRestKey = New-Base64Secret
        $changed = $true
    }

    # A fresh install has no administrator, and the Portal will not create one without a bootstrap
    # password. Generate a one-time password readable only by Administrators and SYSTEM; the Portal
    # forces a change at first sign-in.
    $firstRunPath = Join-Path $installDir 'first-run-password.txt'
    if ([string]::IsNullOrWhiteSpace($moduleOpts['UPGRADE']) -and $null -ne $json.Portal -and `
        $null -ne $json.Portal.FirstRun -and `
        [string]::IsNullOrWhiteSpace([string]$json.Portal.FirstRun.AdminPassword) -and `
        -not (Test-Path -LiteralPath (Join-Path $installDir 'portal.db')) -and `
        -not (Test-Path -LiteralPath $firstRunPath)) {
        $password = (New-Base64Secret).Substring(0, 24).Replace('/', 'x').Replace('+', 'y') + '9!'
        $json.Portal.FirstRun | Add-Member -MemberType NoteProperty -Name 'AdminPassword' -Value $password -Force
        $user = if ([string]::IsNullOrWhiteSpace([string]$json.Portal.FirstRun.AdminUsername)) { 'admin' } else { [string]$json.Portal.FirstRun.AdminUsername }
        # Created with its final ACL (SYSTEM and Administrators, nothing inherited) and written through
        # the creating handle, so no other account can ever read it.
        $acl = New-Object System.Security.AccessControl.FileSecurity
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($sid in 'S-1-5-18', 'S-1-5-32-544') {
            $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
                (New-Object System.Security.Principal.SecurityIdentifier $sid), 'FullControl', 'Allow')))
        }
        $stream = [System.IO.File]::Create($firstRunPath, 4096, [System.IO.FileOptions]::None, $acl)
        try {
            $writer = New-Object System.IO.StreamWriter($stream, (New-Object System.Text.ASCIIEncoding))
            $writer.WriteLine($user)
            $writer.WriteLine($password)
            $writer.Flush()
        } finally {
            $stream.Dispose()
        }
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

    # Portal:Surface — derived from the four granular selectors passed by the installer.
    # This is the single place the feature selections are translated into a surface name
    # so the runtime binary knows which nav sections and routes to activate.
    #
    #   ORCH_SURFACE    (1/0) — Orchestrator job management tab
    #   STEWARD_SURFACE (1/0) — Data Steward / quality queue tab
    #   REPORT_PORTAL   (1/0) — Report catalog and designer tab
    #
    # Truth table (7 meaningful combinations; 0/0/0 = Portal not installed, skipped):
    #   Orch  Steward  Report  → Surface
    #   1     1        1       → All
    #   1     1        0       → OrchestratorAndSteward
    #   1     0        1       → OrchestratorAndReports
    #   0     1        1       → StewardAndReports
    #   1     0        0       → OrchestratorOnly
    #   0     1        0       → DataStewardOnly
    #   0     0        1       → ReportOnly
    if ($null -ne $json.Portal -and
        ($moduleOpts.ContainsKey('ORCH_SURFACE') -or
         $moduleOpts.ContainsKey('STEWARD_SURFACE') -or
         $moduleOpts.ContainsKey('REPORT_PORTAL'))) {

        $hasOrch    = ($moduleOpts['ORCH_SURFACE']    -eq '1')
        $hasSteward = ($moduleOpts['STEWARD_SURFACE'] -eq '1')
        $hasReport  = ($moduleOpts['REPORT_PORTAL']   -eq '1')

        $surface = switch ($true) {
            ($hasOrch -and $hasSteward -and $hasReport)  { 'All';                      break }
            ($hasOrch -and $hasSteward -and !$hasReport) { 'OrchestratorAndSteward';   break }
            ($hasOrch -and !$hasSteward -and $hasReport) { 'OrchestratorAndReports';   break }
            (!$hasOrch -and $hasSteward -and $hasReport) { 'StewardAndReports';         break }
            ($hasOrch -and !$hasSteward -and !$hasReport){ 'OrchestratorOnly';          break }
            (!$hasOrch -and $hasSteward -and !$hasReport){ 'DataStewardOnly';           break }
            (!$hasOrch -and !$hasSteward -and $hasReport){ 'ReportOnly';                break }
            default                                       { 'All' }
        }

        $json.Portal | Add-Member -MemberType NoteProperty -Name 'Surface' -Value $surface -Force
        $changed = $true
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

    # Federated identity: the Portal signs caller assertions that the Orchestrator verifies with the
    # same secret. The Orchestrator refuses to start without it.
    if ($null -ne $orch -and $null -ne $portalOrch) {
        $signing = [string]$orch.IdentitySigningSecret
        if ([string]::IsNullOrWhiteSpace($signing)) { $signing = [string]$portalOrch.IdentitySigningSecret }
        if ([string]::IsNullOrWhiteSpace($signing)) { $signing = New-Base64Secret }
        foreach ($side in $orch, $portalOrch) {
            if ([string]::IsNullOrWhiteSpace([string]$side.IdentitySigningSecret)) {
                $side | Add-Member -MemberType NoteProperty -Name 'IdentitySigningSecret' -Value $signing -Force
                $changed = $true
            }
        }
    }

    $installRoot = $PSScriptRoot.TrimEnd('\')
    if ($null -ne $json.Security) {
        # The install folder must be a safe zone; zones the operator added stay. Force an array shape
        # so the value binds to string[] regardless of element count.
        $zones = @($json.Security.ApprovedSafeZones | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
        if ($zones -notcontains $installRoot) {
            $json.Security.ApprovedSafeZones = @($zones + $installRoot)
            $changed = $true
        }
    }

    if ($changed) {
        ($json | ConvertTo-Json -Depth 64) | Set-Content -LiteralPath $cfgPath -Encoding UTF8
    }
    if (Test-Path -LiteralPath $savedPath) { Remove-Item -LiteralPath $savedPath -Force }
} catch {
    # Non-fatal: a genuinely missing secret or safe zone surfaces when the service runs, rather
    # than aborting the whole installation here.
}
