#requires -Version 7.0
<#
.SYNOPSIS
    Installs an isolated ETL-SQL environment (Portal + Orchestrator) as environment-specific Windows
    services with their own data root, config, logs, Data Protection key ring, keys, and ports.

.DESCRIPTION
    Creates services named ETL-SQL-Portal-<Environment> and ETL-SQL-Orchestrator-<Environment>, each
    running under the supplied service account, isolated under <InstallRoot>\<Environment>. Per-service
    configuration is injected via the service's own Environment registry value, so no environment
    shares config with another. The data root is ACL'd to the service account only (inheritance
    removed), so one environment's identity cannot read another environment's files.

    Run from an elevated PowerShell 7 prompt. After install, verify isolation with
    deploy\verify\Test-Isolation.ps1.

.EXAMPLE
    ./Install-Environment.ps1 -Environment finance -BinPath 'C:\Program Files\ETL-SQL\bin' `
        -ServiceAccount 'CORP\svc-etlsql-finance' -PortBase 5010 `
        -JwtSecret (Read-Host 'JWT') -DatasetKey (Read-Host 'DatasetKey') -OrchestratorApiKey (Read-Host 'OrchKey')
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{0,30}$')][string]$Environment,
    [Parameter(Mandatory)][string]$BinPath,
    [string]$InstallRoot = 'C:\ETL-SQL',
    [Parameter(Mandatory)][string]$ServiceAccount,
    [SecureString]$ServiceAccountPassword,
    [int]$PortBase = 5000,
    [Parameter(Mandatory)][string]$JwtSecret,
    [Parameter(Mandatory)][string]$DatasetKey,
    [Parameter(Mandatory)][string]$OrchestratorApiKey,
    # HA: supply both to use PostgreSQL instead of per-environment SQLite files.
    [string]$PortalDbConnectionString,
    [string]$OrchestratorDbConnectionString
)

$ErrorActionPreference = 'Stop'

$portalExe = Join-Path $BinPath 'ETL-SQL-Portal.exe'
$orchExe   = Join-Path $BinPath 'ETL-SQL-Service.exe'
foreach ($exe in @($portalExe, $orchExe)) {
    if (-not (Test-Path $exe)) { throw "Service executable not found: $exe (check -BinPath)." }
}

$envRoot   = Join-Path $InstallRoot $Environment
$dataDir   = Join-Path $envRoot 'data'
$keyRing   = Join-Path $dataDir '.portal-keys'
$reports   = Join-Path $envRoot 'Reports'
$snapshots = Join-Path $envRoot 'Snapshots'
$datasets  = Join-Path $envRoot 'datasets'
$maps      = Join-Path $envRoot 'maps'
$logs      = Join-Path $envRoot 'logs'

$portalSvc = "ETL-SQL-Portal-$Environment"
$orchSvc   = "ETL-SQL-Orchestrator-$Environment"

$portPortal = $PortBase + 0
$portOrch   = $PortBase + 1

Write-Host "Installing environment '$Environment' under $envRoot (ports $portPortal/$portOrch)..."

# 1. Per-environment directory tree.
foreach ($d in @($envRoot, $dataDir, $keyRing, $reports, $snapshots, $datasets, $maps, $logs)) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

# 2. Lock the data root to the service account only (remove inherited broad access). This is what
#    enforces that another environment's identity cannot read or mutate this environment's files.
$acl = Get-Acl $envRoot
$acl.SetAccessRuleProtection($true, $false)   # disable inheritance, drop inherited rules
$acl.Access | ForEach-Object { [void]$acl.RemoveAccessRule($_) }
foreach ($principal in @($ServiceAccount, 'BUILTIN\Administrators', 'NT AUTHORITY\SYSTEM')) {
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $principal, 'FullControl',
        'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
}
Set-Acl -Path $envRoot -AclObject $acl

# 3. Per-service environment variables (the isolation-bearing config). Distinct paths + keys per env.
$portalEnv = @(
    "ASPNETCORE_URLS=http://+:$portPortal",
    "Portal__ScriptRootPath=$reports",
    "Portal__SnapshotDirectory=$snapshots",
    "Portal__DatasetRootPath=$datasets",
    "Portal__MapRootPath=$maps",
    "Portal__Storage__KeyRingPath=$keyRing",
    "Portal__Jwt__Secret=$JwtSecret",
    "Portal__Dataset__AtRestKey=$DatasetKey",
    "Portal__Orchestrator__ApiUrl=http://localhost:$portOrch",
    "Portal__Orchestrator__ApiKey=$OrchestratorApiKey"
)
$orchEnv = @(
    "ASPNETCORE_URLS=http://+:$portOrch",
    "Orchestrator__ApiKey=$OrchestratorApiKey"
)
if ($PortalDbConnectionString -and $OrchestratorDbConnectionString) {
    $portalEnv += @("Portal__Database__Provider=Postgres", "Portal__Database__ConnectionString=$PortalDbConnectionString")
    $orchEnv   += @("Orchestrator__Database__Provider=Postgres", "Orchestrator__Database__ConnectionString=$OrchestratorDbConnectionString")
} else {
    $portalEnv += "Portal__DatabasePath=$([IO.Path]::Combine($dataDir,'portal.db'))"
    $orchEnv   += "Orchestrator__Database__Provider=Sqlite"
    $orchEnv   += "Orchestrator__DatabasePath=$([IO.Path]::Combine($dataDir,'etlsql.db'))"
}

# 4. Create (or recreate) the services under the environment's own account.
function New-EnvService([string]$Name, [string]$Display, [string]$Exe, [string[]]$EnvBlock) {
    if (Get-Service -Name $Name -ErrorAction SilentlyContinue) {
        Write-Host "  Removing existing service $Name"
        Stop-Service -Name $Name -Force -ErrorAction SilentlyContinue
        sc.exe delete $Name | Out-Null
        Start-Sleep -Seconds 1
    }
    $params = @{
        Name           = $Name
        BinaryPathName = "`"$Exe`""
        DisplayName    = $Display
        StartupType    = 'Automatic'
    }
    if ($ServiceAccountPassword) {
        $params.Credential = [pscredential]::new($ServiceAccount, $ServiceAccountPassword)
    }
    New-Service @params | Out-Null
    if (-not $ServiceAccountPassword) {
        # gMSA / virtual account (no password): set the logon account via sc.exe.
        sc.exe config $Name obj= $ServiceAccount | Out-Null
    }
    # Per-service environment (REG_MULTI_SZ) — the SCM injects this into the service process only.
    $key = "HKLM:\SYSTEM\CurrentControlSet\Services\$Name"
    New-ItemProperty -Path $key -Name 'Environment' -PropertyType MultiString -Value $EnvBlock -Force | Out-Null
    Write-Host "  Installed $Name"
}

New-EnvService -Name $orchSvc   -Display "ETL-SQL Orchestrator ($Environment)" -Exe $orchExe   -EnvBlock $orchEnv
New-EnvService -Name $portalSvc -Display "ETL-SQL Report Portal ($Environment)" -Exe $portalExe -EnvBlock $portalEnv

# 5. Emit the environment descriptor used by the isolation verifier and the runbook.
$descriptor = @"
ETLSQL_ENV=$Environment
COMPOSE_PROJECT_NAME=etlsql-$Environment
SERVICE_ACCOUNT=$ServiceAccount
ENV_DATA_ROOT=$envRoot
KEY_RING_PATH=$keyRing
PORT_PORTAL=$portPortal
PORT_ORCH=$portOrch
PORTAL_JWT_SECRET=$JwtSecret
PORTAL_DATASET_KEY=$DatasetKey
ORCH_API_KEY=$OrchestratorApiKey
PORTAL_DB=$($PortalDbConnectionString    ? $PortalDbConnectionString    : [IO.Path]::Combine($dataDir,'portal.db'))
ORCH_DB=$($OrchestratorDbConnectionString ? $OrchestratorDbConnectionString : [IO.Path]::Combine($dataDir,'etlsql.db'))
"@
$descriptorPath = Join-Path $envRoot "$Environment.env"
Set-Content -Path $descriptorPath -Value $descriptor -Encoding UTF8

Write-Host ""
Write-Host "Environment '$Environment' installed. Descriptor: $descriptorPath"
Write-Host "Start with:  Start-Service $orchSvc; Start-Service $portalSvc"
Write-Host "Verify isolation against other environments with deploy\verify\Test-Isolation.ps1"
