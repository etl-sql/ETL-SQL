<#
.SYNOPSIS
    Checks in seconds that this machine can run a local validation tier, before the tier starts.

.DESCRIPTION
    A missing tool otherwise shows up minutes into a run as an unrelated-looking failure: a fixture
    that needs Node.js 22, an integration test that cannot reach Docker, a timing test measured on a
    busy machine. Every problem found is listed, then the script exits 1.

.PARAMETER For
    PrePush      Node.js 22, PowerShell 7, the pinned .NET SDK, the repository hooks and free memory.
    Integration  PrePush, plus a reachable Docker daemon and the SQL Server ODBC driver.
    Timing       PrePush, plus an idle processor and enough free memory for a measurement to mean
                 something. A timing tier must not start on a busy machine.

.EXAMPLE
    pwsh -File scripts\Test-Environment.ps1 -For Integration
#>
[CmdletBinding()]
param(
    [ValidateSet('PrePush', 'Integration', 'Timing')]
    [string]$For = 'PrePush',

    # Thresholds are parameters so the self-test can force each failure.
    [double]$MinimumFreeMemoryGB = 2,
    [double]$TimingMinimumFreeMemoryGB = 8,
    [ValidateRange(1, 100)]
    [double]$TimingMaximumCpuPercent = 25
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$problems = [System.Collections.Generic.List[string]]::new()

function Get-ToolOutput([string]$Tool, [string[]]$Arguments) {
    if (-not (Get-Command $Tool -CommandType Application -ErrorAction SilentlyContinue)) { return $null }
    try {
        $output = & $Tool @Arguments 2>$null
        if ($LASTEXITCODE -ne 0) { return $null }
        return (@($output) -join "`n").Trim()
    } catch { return $null }
}

function Get-FreeMemoryGB {
    if ($IsWindows) {
        $os = Get-CimInstance Win32_OperatingSystem
        return [math]::Round($os.FreePhysicalMemory / 1MB, 1)   # FreePhysicalMemory is in KB.
    }
    if ($IsLinux) {
        $line = Get-Content /proc/meminfo | Where-Object { $_ -match '^MemAvailable:\s+(\d+)' } | Select-Object -First 1
        if ($line -match '(\d+)') { return [math]::Round([long]$Matches[1] / 1MB, 1) }
    }
    return $null   # macOS reports free pages in several classes; no single honest number.
}

function Get-CpuPercent {
    # Two readings one second apart of the whole machine, not this process.
    if ($IsWindows) {
        # Get-Counter fails now and then with "A counter with a negative denominator value was
        # detected" (3 in 8 calls on 2026-11-13), which ended this script with no list of problems.
        # Retry; if it still fails, the load is unknown and the caller says so.
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                $samples = Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 1 -MaxSamples 2 -ErrorAction Stop
                return [math]::Round(($samples.CounterSamples | Measure-Object CookedValue -Average).Average, 1)
            } catch {
                if ($attempt -eq 3) { return $null }
            }
        }
    }
    if ($IsLinux) {
        $read = { $f = (Get-Content /proc/stat -TotalCount 1) -split '\s+' | Select-Object -Skip 1 | ForEach-Object { [long]$_ }
            @{ Idle = $f[3] + $f[4]; Total = ($f | Measure-Object -Sum).Sum } }
        $a = & $read; Start-Sleep -Seconds 1; $b = & $read
        $total = $b.Total - $a.Total
        if ($total -le 0) { return $null }
        return [math]::Round(100.0 * (1 - ($b.Idle - $a.Idle) / $total), 1)
    }
    return $null
}

# --- Every tier -------------------------------------------------------------------------------
$node = Get-ToolOutput 'node' @('--version')
if ($node -notmatch '^v22\.') {
    $found = if ($node) { $node } else { 'none' }
    $problems.Add("Node.js 22 is required on PATH (found: $found). The browser toolchain and several fixtures fail on other versions.")
}

if ($PSVersionTable.PSVersion.Major -lt 7) {
    $problems.Add("PowerShell 7 or later is required (running $($PSVersionTable.PSVersion)).")
}

$pinned = (Get-Content -LiteralPath (Join-Path $RepoRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$sdk = Get-ToolOutput 'dotnet' @('--version')
if (-not $sdk) {
    $problems.Add("The .NET SDK is not on PATH, or none satisfies global.json ($pinned).")
} elseif (($sdk -split '\.')[0] -ne ($pinned -split '\.')[0]) {
    $problems.Add("The .NET SDK is $sdk; global.json pins $pinned.")
}

$hooks = Get-ToolOutput 'git' @('-C', $RepoRoot, 'config', '--get', 'core.hooksPath')
if ($hooks -ne 'scripts') {
    $problems.Add("Repository hooks are not enabled. Run: git config core.hooksPath scripts")
}

$freeGB = Get-FreeMemoryGB
$requiredGB = if ($For -eq 'Timing') { $TimingMinimumFreeMemoryGB } else { $MinimumFreeMemoryGB }
if ($null -ne $freeGB -and $freeGB -lt $requiredGB) {
    $problems.Add("Only $freeGB GB of memory is free; this tier needs $requiredGB GB. Close other work first.")
}

# --- Integration ------------------------------------------------------------------------------
if ($For -eq 'Integration') {
    if ($null -eq (Get-ToolOutput 'docker' @('info', '--format', '{{.ServerVersion}}'))) {
        $problems.Add('The Docker daemon is not reachable. Start Docker; the integration lane runs its databases in containers.')
    }
    $driver = if ($IsWindows) {
        @(Get-OdbcDriver -ErrorAction SilentlyContinue | Where-Object Name -Like 'ODBC Driver 1* for SQL Server').Count -gt 0
    } else {
        (Get-ToolOutput 'odbcinst' @('-q', '-d')) -match 'ODBC Driver 1\d for SQL Server'
    }
    if (-not $driver) {
        $problems.Add('The Microsoft ODBC Driver for SQL Server (17 or 18) is not installed; the ODBC integration tests need it.')
    }
}

# --- Timing -----------------------------------------------------------------------------------
if ($For -eq 'Timing') {
    $cpu = Get-CpuPercent
    if ($null -eq $cpu) {
        Write-Host 'Processor load could not be read; the timing tier runs without that check.' -ForegroundColor Yellow
    } elseif ($cpu -gt $TimingMaximumCpuPercent) {
        $problems.Add("The processor is $cpu% busy; a timing tier needs it under $TimingMaximumCpuPercent%. Wait for other work to finish.")
    }
}

if ($problems.Count -gt 0) {
    Write-Host "Environment is not ready for the $For tier:" -ForegroundColor Red
    foreach ($problem in $problems) { Write-Host "  - $problem" -ForegroundColor Red }
    exit 1
}
Write-Host "Environment ready for the $For tier." -ForegroundColor Green
exit 0
