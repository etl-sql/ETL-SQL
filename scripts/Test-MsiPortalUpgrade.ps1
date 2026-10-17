<#
.SYNOPSIS
    Certifies the MSI's Portal profile: a fresh install, an in-place upgrade from the previous
    release, and rollback to it.

.DESCRIPTION
    Test-MsiUpgrade.ps1 certifies the workstation-only profile. This installs the ENTERPRISE profile,
    whose Portal and Orchestrator run as Windows services, and checks what a person relies on:

      0. A fresh install of the candidate starts both services with no operator configuration,
         writes a one-time sign-in readable only by Administrators and SYSTEM, and serves the
         prepared browser assets.
      1. The previous release, configured as its documentation says, records what a browser would
         cache. Its databases and configuration are backed up with the services stopped.
      2. The upgrade restarts both services on the new executables, keeps every secret and the
         operator's own setting, serves the candidate's assets, and a cached browser refetches what
         changed (no stale 304).
      3. Rollback: uninstall the candidate (data kept), install the previous release, restore the
         backup. v0.21.0 renames the database schema, so a previous release needs its backup.

    Every check is reported; the script exits non-zero when any failed. Mutates the machine:
    elevated, on a disposable runner with no ETL-SQL installed. Node 22 must be on PATH.

.EXAMPLE
    .\scripts\Test-MsiPortalUpgrade.ps1 -PreviousMsi .\ETL-SQL-v0.20.0-x64-Setup.msi -CurrentMsi .\ETL-SQL-v0.21.0.msi
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$PreviousMsi,
    [Parameter(Mandatory)] [string]$CurrentMsi,
    [string]$OutDir = './release-validation/msi-portal-upgrade'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MsiUpgrade.Helpers.ps1')

if (-not $IsWindows) { throw 'MSI certification runs on Windows only.' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'MSI certification requires an elevated Administrator session.'
}
if (@(Get-EtlSqlUninstallEntries).Count -ne 0) { throw 'ETL-SQL is already installed. Use a clean runner.' }

$repoRoot = Split-Path -Parent $PSScriptRoot
$previousPath = (Resolve-Path -LiteralPath $PreviousMsi).Path
$currentPath = (Resolve-Path -LiteralPath $CurrentMsi).Path
$contract = Test-MsiUpgradeContract -PreviousMsi $previousPath -CurrentMsi $currentPath
$out = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $out | Out-Null
$summary = Join-Path $out 'summary.txt'
Set-Content -LiteralPath $summary -Value "previous $($contract.PreviousVersion) current $($contract.CurrentVersion)"
$script:failed = 0
$url = 'http://localhost:5002'
$checker = Join-Path $repoRoot 'scripts/test-installed-portal-assets.mjs'
$installDir = 'C:\Program Files\ETL-SQL'
$systemData = Join-Path $env:SystemRoot 'System32\config\systemprofile\AppData\Local\ETL-SQL'

function Write-Line([string]$Text) {
    Write-Host $Text
    Add-Content -LiteralPath $summary -Value $Text
    if ($Text.StartsWith('== ')) { Write-ResourceSnapshot }
}

# The first CI run lost its runner with no log. Record what was running at each stage, so a
# starved or crash-looping service shows up in the evidence.
function Write-ResourceSnapshot {
    $os = Get-CimInstance Win32_OperatingSystem
    Add-Content -LiteralPath $summary -Value ("   free memory {0:N0} MB of {1:N0} MB" -f ($os.FreePhysicalMemory / 1KB), ($os.TotalVisibleMemorySize / 1KB))
    Get-Process | Sort-Object CPU -Descending | Select-Object -First 6 | ForEach-Object {
        Add-Content -LiteralPath $summary -Value ("   {0,-28} cpu {1,8:N0}s  ws {2,6:N0} MB" -f $_.ProcessName, $_.CPU, ($_.WorkingSet64 / 1MB))
    }
    foreach ($name in 'ETL-SQL-Orchestrator', 'ETL-SQL-Portal') {
        $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
        if ($svc) { Add-Content -LiteralPath $summary -Value "   service $name $($svc.Status)" }
    }
}

function Test-Step([string]$Name, [scriptblock]$Body) {
    try {
        $result = & $Body
        if ($result -eq $false) { throw 'returned false' }
        Write-Line "PASS $Name"
    } catch {
        Write-Line "FAIL $Name — $($_.Exception.Message)"
        $script:failed = 1
    }
}

function Wait-Portal([string]$Stage) {
    $deadline = (Get-Date).AddMinutes(4)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri "$url/health" -UseBasicParsing -TimeoutSec 5 -SkipHttpErrorCheck
            if ($response.StatusCode -eq 200) { return }
        } catch { }
        Start-Sleep -Seconds 2
    }
    Get-ChildItem -LiteralPath (Join-Path $installDir 'logs') -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime | Select-Object -Last 2 |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $out "$Stage-$($_.Name)") }
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddMinutes(-6) } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -match 'ETL-SQL|\.NET Runtime|Application Error' } |
        Select-Object -First 20 TimeCreated, ProviderName, Message | Format-List | Out-File (Join-Path $out "$Stage-events.txt")
    throw "Portal did not answer /health within 4 minutes."
}

# A service runs the installed executable of the expected version.
function Assert-ServiceRunsVersion([string]$Service, [string]$Version) {
    $svc = Get-CimInstance Win32_Service -Filter "Name='$Service'"
    if ($null -eq $svc -or $svc.State -ne 'Running' -or $svc.ProcessId -eq 0) { throw "$Service is not running ($($svc.State))." }
    $path = (Get-Process -Id $svc.ProcessId).Path
    $product = (Get-Item -LiteralPath $path).VersionInfo.ProductVersion
    if (-not $path.StartsWith($installDir, [StringComparison]::OrdinalIgnoreCase)) { throw "$Service runs $path." }
    if ($product -notmatch [regex]::Escape(([version]$Version).ToString(3))) { throw "$Service runs $product, expected $Version." }
}

# The effective value, as the services read it: the installed secrets file overrides appsettings.json.
function Get-Setting([string]$Path) {
    $value = ''
    foreach ($file in 'appsettings.json', 'appsettings.secrets.json') {
        $full = Join-Path $installDir $file
        if (-not (Test-Path -LiteralPath $full)) { continue }
        $node = Get-Content -LiteralPath $full -Raw | ConvertFrom-Json
        foreach ($part in $Path.Split(':')) { if ($null -eq $node) { break }; $node = $node.$part }
        if ($node -is [string] -and $node) { $value = $node }
    }
    return $value
}

$generatedSecrets = 'Portal:Jwt:Secret', 'Portal:Dataset:AtRestKey', 'Orchestrator:ApiKey', 'Orchestrator:IdentitySigningSecret', 'Portal:Orchestrator:ApiKey', 'Portal:Orchestrator:IdentitySigningSecret'

# appsettings.json is readable by every local user; the secrets file only by Administrators and SYSTEM.
function Assert-SecretsProtected {
    $secretsFile = Join-Path $installDir 'appsettings.secrets.json'
    $grantees = (Get-Acl -LiteralPath $secretsFile).Access | ForEach-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } | Sort-Object -Unique
    if (($grantees -join ',') -ne 'S-1-5-18,S-1-5-32-544') { throw "Secrets file grantees: $($grantees -join ', ')" }
    $shared = Get-Content -LiteralPath (Join-Path $installDir 'appsettings.json') -Raw | ConvertFrom-Json
    foreach ($path in $generatedSecrets) {
        $node = $shared
        foreach ($part in $path.Split(':')) { if ($null -eq $node) { break }; $node = $node.$part }
        if ($node -is [string] -and $node) { throw "appsettings.json still holds $path." }
    }
}

function Assert-Setting([string]$Path, [string]$Expected) {
    $value = Get-Setting $Path
    if ([string]::IsNullOrWhiteSpace($value) -or $value -ne $Expected) {
        throw "$Path changed (was $($Expected.Length) chars, now $($value.Length))."
    }
}

function Invoke-Assets([string[]]$Arguments) {
    & node $checker --url $url --install $installDir --dump (Join-Path $out 'served-mismatch') @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Served-asset check failed (exit $LASTEXITCODE)." }
}

function Install-Msi([string]$Msi, [string]$Log) {
    Invoke-MsiExec @('/i', $Msi, 'DEPLOY_PROFILE=ENTERPRISE') (Join-Path $out $Log) "Install $Log"
}

function Uninstall-All([string]$Log) {
    foreach ($entry in @(Get-EtlSqlUninstallEntries)) {
        Invoke-MsiExec @('/x', $entry.PSChildName) (Join-Path $out $Log) "Uninstall $Log"
    }
}

function Get-DatabaseFiles {
    @($installDir, $systemData) | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object {
        Get-ChildItem -LiteralPath $_ -File | Where-Object Name -match '\.db(-wal|-shm)?$'
    }
}

$services = 'ETL-SQL-Orchestrator', 'ETL-SQL-Portal'
try {
    # 0. Fresh install of the candidate, with no operator configuration at all.
    Write-Line "== fresh install of $($contract.CurrentVersion)"
    Test-Step 'fresh: msiexec install' { Install-Msi $currentPath '00-fresh.log' }
    Test-Step 'fresh: Portal healthy' { Wait-Portal 'fresh' }
    foreach ($service in $services) {
        Test-Step "fresh: $service runs the installed executable" { Assert-ServiceRunsVersion $service $contract.CurrentVersion }
    }
    Test-Step 'fresh: one-time sign-in for Administrators and SYSTEM only' {
        $signIn = Join-Path $installDir 'first-run-password.txt'
        $grantees = (Get-Acl -LiteralPath $signIn).Access | ForEach-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } | Sort-Object -Unique
        if (($grantees -join ',') -ne 'S-1-5-18,S-1-5-32-544') { throw "Grantees: $($grantees -join ', ')" }
    }
    Test-Step 'fresh: dataset at-rest key generated' { -not [string]::IsNullOrWhiteSpace((Get-Setting 'Portal:Dataset:AtRestKey')) }
    Test-Step 'fresh: secrets only in the Administrators and SYSTEM file' { Assert-SecretsProtected }
    Test-Step 'fresh: served assets match the candidate' { Invoke-Assets @('--inventory-root', $repoRoot, '--out', (Join-Path $out 'fresh-assets.json')) }
    Uninstall-All '00-fresh-uninstall.log'
    Remove-Item -LiteralPath $installDir, $systemData -Recurse -Force -ErrorAction SilentlyContinue

    # 1. The previous release, configured as its documentation says.
    Write-Line "== install previous $($contract.PreviousVersion)"
    Test-Step 'previous: msiexec install' { Install-Msi $previousPath '01-previous.log' }
    $cfgPath = Join-Path $installDir 'appsettings.json'
    $cfg = Get-Content -LiteralPath $cfgPath -Raw | ConvertFrom-Json
    $random = { $bytes = [byte[]]::new(32); [Security.Cryptography.RandomNumberGenerator]::Fill($bytes); [Convert]::ToBase64String($bytes) }
    $cfg.Portal.FirstRun | Add-Member NoteProperty AdminPassword ('Upgrade-' + (& $random).Substring(0, 12).Replace('/', 'x') + '9!') -Force
    $cfg.Portal.Dataset.AtRestKey = & $random
    $signing = & $random
    $cfg.Orchestrator | Add-Member NoteProperty IdentitySigningSecret $signing -Force
    $cfg.Portal.Orchestrator | Add-Member NoteProperty IdentitySigningSecret $signing -Force
    $cfg | Add-Member NoteProperty UpgradeSentinel ([pscustomobject]@{ Value = 'kept' }) -Force
    ($cfg | ConvertTo-Json -Depth 64) | Set-Content -LiteralPath $cfgPath -Encoding UTF8
    Restart-Service -Name $services -Force
    Test-Step 'previous: Portal healthy' { Wait-Portal 'previous' }
    $secret = Get-Setting 'Portal:Jwt:Secret'; $apiKey = Get-Setting 'Orchestrator:ApiKey'
    $atRest = Get-Setting 'Portal:Dataset:AtRestKey'
    $cachePrevious = Join-Path $out 'cache-previous.json'
    Test-Step 'previous: served assets recorded as a browser cache' { Invoke-Assets @('--record', $cachePrevious, '--out', (Join-Path $out 'previous-assets.json')) }
    $backup = Join-Path $out 'pre-upgrade-backup'
    Stop-Service -Name $services -Force
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    $saved = @(Get-DatabaseFiles | ForEach-Object {
        $target = Join-Path $backup ("{0}__{1}" -f ($_.DirectoryName -eq $installDir ? 'install' : 'system'), $_.Name)
        Copy-Item -LiteralPath $_.FullName -Destination $target; $target })
    Copy-Item -LiteralPath $cfgPath -Destination (Join-Path $backup 'appsettings.json')
    Write-Line "NOTE previous: backed up $($saved.Count) database file(s) and the configuration"
    Start-Service -Name $services

    # 2. Upgrade in place.
    Write-Line "== upgrade to $($contract.CurrentVersion)"
    Test-Step 'upgrade: msiexec install' { Install-Msi $currentPath '02-upgrade.log' }
    Test-Step 'upgrade: one installed product' { [void](Assert-EntryCount 1 'Upgrade') }
    Test-Step 'upgrade: Portal healthy' { Wait-Portal 'upgrade' }
    foreach ($service in $services) {
        Test-Step "upgrade: $service runs the upgraded executable" { Assert-ServiceRunsVersion $service $contract.CurrentVersion }
    }
    Test-Step 'upgrade: Portal JWT secret preserved' { Assert-Setting 'Portal:Jwt:Secret' $secret }
    Test-Step 'upgrade: Orchestrator API key preserved' { Assert-Setting 'Orchestrator:ApiKey' $apiKey }
    Test-Step 'upgrade: dataset at-rest key preserved' { Assert-Setting 'Portal:Dataset:AtRestKey' $atRest }
    Test-Step 'upgrade: identity signing secret preserved' { Assert-Setting 'Orchestrator:IdentitySigningSecret' $signing }
    Test-Step 'upgrade: operator setting preserved' { Assert-Setting 'UpgradeSentinel:Value' 'kept' }
    Test-Step 'upgrade: secrets moved to the Administrators and SYSTEM file' { Assert-SecretsProtected }
    Test-Step 'upgrade: no one-time sign-in for an existing Portal' { -not (Test-Path -LiteralPath (Join-Path $installDir 'first-run-password.txt')) }
    Test-Step 'upgrade: no configuration backup left behind' { -not (Test-Path -LiteralPath (Join-Path $installDir 'appsettings.pre-upgrade.json')) }
    $cacheCurrent = Join-Path $out 'cache-current.json'
    Test-Step 'upgrade: served assets match the candidate; cached browser refetches changes' {
        Invoke-Assets @('--inventory-root', $repoRoot, '--revalidate', $cachePrevious, '--record', $cacheCurrent, '--out', (Join-Path $out 'upgrade-assets.json'))
    }

    # 3. Roll back: the MSI refuses a downgrade, so uninstall the candidate (its data is kept),
    #    install the previous release, and restore the pre-upgrade backup.
    Write-Line "== roll back to $($contract.PreviousVersion)"
    Test-Step 'rollback: uninstall the candidate' { Uninstall-All '03-rollback-uninstall.log' }
    Test-Step 'rollback: msiexec install previous' { Install-Msi $previousPath '03-rollback-install.log' }
    Stop-Service -Name $services -Force -ErrorAction SilentlyContinue
    Get-DatabaseFiles | Remove-Item -Force
    foreach ($file in Get-ChildItem -LiteralPath $backup -File -Filter '*__*') {
        $scope, $name = $file.Name -split '__', 2
        $dir = $scope -eq 'install' ? $installDir : $systemData
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $dir $name)
    }
    Copy-Item -LiteralPath (Join-Path $backup 'appsettings.json') -Destination $cfgPath -Force
    Start-Service -Name $services
    Test-Step 'rollback: Portal healthy on the restored database' { Wait-Portal 'rollback' }
    Test-Step 'rollback: ETL-SQL-Portal runs the previous executable' { Assert-ServiceRunsVersion 'ETL-SQL-Portal' $contract.PreviousVersion }
    Test-Step 'rollback: served assets are the previous release''s; cached browser refetches changes' {
        Invoke-Assets @('--revalidate', $cacheCurrent, '--out', (Join-Path $out 'rollback-assets.json'))
    }
} finally {
    Uninstall-All '04-cleanup-uninstall.log'
    Test-Step 'cleanup: no product remains' { [void](Assert-EntryCount 0 'Cleanup') }
}

Write-Line ($script:failed -eq 0 ? 'RESULT PASS' : 'RESULT FAIL')
exit $script:failed
