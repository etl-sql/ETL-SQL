#requires -Version 7.0
<#
.SYNOPSIS
    Proves a set of ETL-SQL environments are isolated: no two environments share a database target,
    artifact root, Data Protection key ring, port, service account, or encryption key.

.DESCRIPTION
    Reads one descriptor file per environment (KEY=VALUE; the Windows/systemd installers emit one as
    <root>/<env>.env, and Docker env files are accepted too). Environments are grouped by ETLSQL_ENV,
    so multiple HA-node descriptors for the same environment are not flagged against each other —
    only sharing across DIFFERENT environments is a violation.

    Exit code 0 = isolated, 1 = overlap(s) found, 2 = usage error. With -CheckAcls (Windows, elevated),
    it also verifies one environment's service account is not granted access to another's data root.

.EXAMPLE
    pwsh -File Test-Isolation.ps1 C:\ETL-SQL\*\*.env
.EXAMPLE
    pwsh -File Test-Isolation.ps1 ./finance.env ./hr.env -CheckAcls
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Path,
    [switch]$CheckAcls
)
$ErrorActionPreference = 'Stop'

# Values that must be unique across environments. (ETLSQL_ENV is the grouping key, not checked.)
$uniqueKeys = @(
    'COMPOSE_PROJECT_NAME', 'ENV_DATA_ROOT', 'KEY_RING_PATH', 'SERVICE_ACCOUNT',
    'PORT_PORTAL', 'PORT_ORCH', 'PORT_PG',
    'PORTAL_JWT_SECRET', 'PORTAL_DATASET_KEY', 'ORCH_API_KEY', 'PORTAL_DB', 'ORCH_DB'
)

$files = $Path |
    ForEach-Object { Get-ChildItem -Path $_ -File -ErrorAction SilentlyContinue } |
    Select-Object -ExpandProperty FullName -Unique
if (-not $files) { Write-Error "No descriptor files matched: $($Path -join ', ')"; exit 2 }

function Read-Descriptor([string]$file) {
    $map = @{}
    foreach ($line in Get-Content -LiteralPath $file) {
        $t = $line.Trim()
        if ($t -eq '' -or $t.StartsWith('#')) { continue }
        $i = $t.IndexOf('=')
        if ($i -lt 1) { continue }
        $map[$t.Substring(0, $i).Trim()] = $t.Substring($i + 1).Trim()
    }
    return $map
}

function Test-IdentityMatch([string]$ruleIdentity, [string]$account) {
    if ($ruleIdentity -ieq $account) { return $true }
    $leaf = ($account -split '\\')[-1]
    return ($leaf -ne '' -and ($ruleIdentity -split '\\')[-1] -ieq $leaf)
}

# Best-effort (Windows): confirm one environment's service account is not granted access to another's
# data root. Run elevated for accurate results.
function Test-CrossAccountAcl($environments) {
    $out = [System.Collections.Generic.List[string]]::new()
    $distinct = $environments | Group-Object Id | ForEach-Object { $_.Group[0] }
    foreach ($a in $distinct) {
        $acct = $a.Map['SERVICE_ACCOUNT']
        if (-not $acct) { continue }
        foreach ($b in $distinct) {
            if ($b.Id -eq $a.Id) { continue }
            $root = $b.Map['ENV_DATA_ROOT']
            if (-not $root -or -not (Test-Path -LiteralPath $root)) { continue }
            try {
                $acl = Get-Acl -LiteralPath $root
                $granted = $acl.Access | Where-Object {
                    $_.AccessControlType -eq 'Allow' -and (Test-IdentityMatch $_.IdentityReference.Value $acct)
                }
                if ($granted) {
                    $out.Add("Service account '$acct' (env $($a.Id)) can access env $($b.Id) data root $root")
                }
            } catch { }
        }
    }
    return $out
}

$envs = foreach ($f in $files) {
    $m = Read-Descriptor $f
    $id = if ($m.ContainsKey('ETLSQL_ENV') -and $m['ETLSQL_ENV']) { $m['ETLSQL_ENV'] }
          else { [IO.Path]::GetFileNameWithoutExtension($f) }
    [pscustomobject]@{ Id = $id; File = $f; Map = $m }
}

Write-Host "Checking isolation across $(( $envs.Id | Select-Object -Unique).Count) environment(s) from $($files.Count) descriptor(s)..."

$findings = [System.Collections.Generic.List[string]]::new()
foreach ($key in $uniqueKeys) {
    $byValue = @{}
    foreach ($e in $envs) {
        if ($e.Map.ContainsKey($key) -and $e.Map[$key]) {
            $v = $e.Map[$key]
            if (-not $byValue.ContainsKey($v)) { $byValue[$v] = [System.Collections.Generic.HashSet[string]]::new() }
            [void]$byValue[$v].Add($e.Id)
        }
    }
    foreach ($v in $byValue.Keys) {
        if ($byValue[$v].Count -gt 1) {
            $masked = if ($key -in 'PORTAL_JWT_SECRET', 'PORTAL_DATASET_KEY', 'ORCH_API_KEY') { '<shared value>' } else { $v }
            $findings.Add("Shared ${key} ($masked) across environments: $(($byValue[$v]) -join ', ')")
        }
    }
}

if ($CheckAcls) {
    $findings.AddRange([string[]](Test-CrossAccountAcl $envs))
}

if ($findings.Count -gt 0) {
    Write-Host "ISOLATION VIOLATIONS:" -ForegroundColor Red
    $findings | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "OK: environments are isolated (no shared databases, roots, key rings, ports, accounts, or keys)." -ForegroundColor Green
exit 0
