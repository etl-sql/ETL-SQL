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

function Write-ProtectedFile {
    param([string]$Path, [byte[]]$Bytes)
    $acl = New-Object System.Security.AccessControl.FileSecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sid in 'S-1-5-18', 'S-1-5-32-544') {
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
            (New-Object System.Security.Principal.SecurityIdentifier $sid), 'FullControl', 'Allow')))
    }
    # The invoking installer must be able to rename its staged file after closing the handle.
    # MSI runs as SYSTEM (already present); local fixtures/manual elevated runs may own the file.
    $workingAcl = New-Object System.Security.AccessControl.FileSecurity
    $workingAcl.SetSecurityDescriptorBinaryForm($acl.GetSecurityDescriptorBinaryForm(), [System.Security.AccessControl.AccessControlSections]::Access)
    $workingAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
        ([System.Security.Principal.WindowsIdentity]::GetCurrent().User), 'FullControl', 'Allow')))
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $backup = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.recovery'
    try {
        # Stage with access limited to SYSTEM, Administrators and this installer identity.
        $stream = [System.IO.File]::Create($temporary, 4096, [System.IO.FileOptions]::None, $workingAcl)
        try {
            $stream.Write($Bytes, 0, $Bytes.Length)
            $stream.Flush($true)
        } finally { $stream.Dispose() }
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            # ReplaceFile retains the destination DACL, so restrict it before replacement.
            [System.IO.File]::SetAccessControl($Path, $workingAcl)
            [System.IO.File]::Replace($temporary, $Path, $backup)
            Remove-Item -LiteralPath $backup -Force
        } else {
            [System.IO.File]::Move($temporary, $Path)
        }
    } finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        }
        foreach ($published in $Path, $backup) {
            if (Test-Path -LiteralPath $published -PathType Leaf) {
                [System.IO.File]::SetAccessControl($published, $acl)
            }
        }
        # A failed replacement can leave the original under .recovery. Never delete that copy.
    }
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

function Get-JsonParent {
    param($Root, [string]$Dotted)
    $node = $Root
    foreach ($part in $Dotted.Split(':') | Select-Object -SkipLast 1) {
        if ($null -eq $node -or $null -eq $node.PSObject.Properties[$part]) { return $null }
        $node = $node.$part
    }
    return $node
}

function Test-EmptySetting($Value) {
    if ($null -eq $Value) { return $true }
    if ($Value -is [string]) { return [string]::IsNullOrWhiteSpace($Value) }
    if ($Value -is [array]) { return $Value.Count -eq 0 }
    if ($Value -is [System.Management.Automation.PSCustomObject]) { return @($Value.PSObject.Properties).Count -eq 0 }
    return $false
}

try {
    $parts = $CustomData.Split('|')
    $installDir = if ($parts.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($parts[0])) { $parts[0] } else { $PSScriptRoot }
    $cfgPath = Join-Path $installDir 'appsettings.json'
    if (-not (Test-Path -LiteralPath $cfgPath -PathType Leaf)) { throw 'Installer settings are missing.' }
    if (@(Get-ChildItem -LiteralPath $installDir -Filter 'appsettings.secrets.json.*.recovery' -File -Force).Count -gt 0) {
        throw 'Protected installer recovery requires administrator review before retrying.'
    }

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

    # Generated service secrets live in a file only Administrators and SYSTEM can read, because
    # appsettings.json is shared with the CLI and readable by every local user. Its values are the
    # ones in use, so they win over anything in appsettings.json and nothing below regenerates them.
    # Keep in sync with build-linux-packages.sh (InstallerConfigurationScriptTests compares them).
    $secretPaths = @('Portal:Jwt:Secret', 'Portal:Jwt:PreviousSecrets', 'Portal:Dataset:AtRestKey', 'Portal:Dataset:PreviousAtRestKeys', 'Portal:FirstRun:AdminPassword', 'Orchestrator:ApiKey', 'Orchestrator:PreviousApiKeys', 'Orchestrator:IdentitySigningSecret', 'Portal:Orchestrator:ApiKey', 'Portal:Orchestrator:IdentitySigningSecret')
    $secretsPath = Join-Path $installDir 'appsettings.secrets.json'
    if (Test-Path -LiteralPath $secretsPath -PathType Leaf) {
        $existingSecrets = Get-Content -LiteralPath $secretsPath -Raw | ConvertFrom-Json
        foreach ($dotted in $secretPaths) {
            $source = Get-JsonParent -Root $existingSecrets -Dotted $dotted
            $target = Get-JsonParent -Root $json -Dotted $dotted
            $key = $dotted.Split(':')[-1]
            if ($null -ne $source -and $null -ne $target -and -not (Test-EmptySetting $source.$key)) {
                $target | Add-Member -MemberType NoteProperty -Name $key -Value $source.$key -Force
                $changed = $true
            }
        }
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
        $passwordBytes = (New-Object System.Text.ASCIIEncoding).GetBytes($user + "`r`n" + $password + "`r`n")
        Write-ProtectedFile -Path $firstRunPath -Bytes $passwordBytes
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

    # Record the job-history file the Orchestrator service writes: its default, in the service
    # account's local app data. `admin backup` and `migrate-database` run as the operator and would
    # otherwise look beside the binaries. The services run as LocalSystem (Installer.wxs); this step
    # resolved the installing user's folder in CI, so the LocalSystem profile is named, not looked up.
    # An operator's path is kept.
    if ($null -ne $portalOrch -and [string]::IsNullOrWhiteSpace([string]$portalOrch.DatabasePath)) {
        $orchDb = Join-Path $env:SystemRoot 'System32\config\systemprofile\AppData\Local\etlsql-data\etlsql.db'
        $portalOrch | Add-Member -MemberType NoteProperty -Name 'DatabasePath' -Value $orchDb -Force
        $changed = $true
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

    # Move every generated secret out of the shared file, leaving its empty default behind.
    $secrets = [pscustomobject]@{}
    foreach ($dotted in $secretPaths) {
        $node = Get-JsonParent -Root $json -Dotted $dotted
        $key = $dotted.Split(':')[-1]
        if ($null -eq $node -or $null -eq $node.PSObject.Properties[$key]) { continue }
        $value = $node.$key
        if (-not (Test-EmptySetting $value)) {
            $target = $secrets
            foreach ($part in $dotted.Split(':') | Select-Object -SkipLast 1) {
                if ($null -eq $target.PSObject.Properties[$part]) {
                    $target | Add-Member -MemberType NoteProperty -Name $part -Value ([pscustomobject]@{})
                }
                $target = $target.$part
            }
            $target | Add-Member -MemberType NoteProperty -Name $key -Value $value -Force
        }
        # Assigned per branch: an if expression would unroll an empty array to $null.
        if ($value -is [array]) {
            $node | Add-Member -MemberType NoteProperty -Name $key -Value @() -Force
        } elseif ($value -is [System.Management.Automation.PSCustomObject]) {
            $node | Add-Member -MemberType NoteProperty -Name $key -Value ([pscustomobject]@{}) -Force
        } else {
            $node | Add-Member -MemberType NoteProperty -Name $key -Value '' -Force
        }
        $changed = $true
    }

    # Written through a handle opened with the final ACL (SYSTEM and Administrators, nothing
    # inherited from Program Files), so the secrets are never readable by another account.
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes(($secrets | ConvertTo-Json -Depth 64))
    Write-ProtectedFile -Path $secretsPath -Bytes $bytes

    if ($changed) {
        ($json | ConvertTo-Json -Depth 64) | Set-Content -LiteralPath $cfgPath -Encoding UTF8
    }
    if (Test-Path -LiteralPath $savedPath) { Remove-Item -LiteralPath $savedPath -Force }
} catch {
    # COMPAT_BREAK: 0.22 — failed configuration must stop installation before services start.
    # Provider/file exceptions can include secrets or settings; report only a fixed diagnostic.
    Write-Error 'ETL-SQL installer configuration failed.' -ErrorAction Continue
    exit 1
}
