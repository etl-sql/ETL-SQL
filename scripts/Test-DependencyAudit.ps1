<#
.SYNOPSIS
    Script-level tests for the NuGet dependency-audit helpers (scripts/lib/DependencyAudit.ps1).

.DESCRIPTION
    Proves the dependency-audit phase completes and behaves correctly without requiring the .NET SDK,
    by injecting a fake `dotnet list package` runner. Covers the SDK 10.0.300 + CPM failure path:
    solution-level failure for --deprecated/--vulnerable must fall back to per-project auditing, and if
    no authoritative audit can run the helper must throw (never silently skip vulnerable results).

    Exit code 0 = all checks passed; non-zero = a check failed (suitable for CI).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptRoot "lib/DependencyAudit.ps1")

$script:Passed = 0
$script:Failed = 0

function Assert-True {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Name)
    if ($Condition) {
        Write-Host "PASS  $Name" -ForegroundColor Green
        $script:Passed++
    }
    else {
        Write-Host "FAIL  $Name" -ForegroundColor Red
        $script:Failed++
    }
}

function Assert-Throws {
    param([Parameter(Mandatory)][scriptblock]$Action, [string]$MatchMessage, [Parameter(Mandatory)][string]$Name)
    try {
        & $Action | Out-Null
        Assert-True $false "$Name (expected throw, none occurred)"
    }
    catch {
        if ($MatchMessage -and $_.Exception.Message -notlike "*$MatchMessage*") {
            Assert-True $false "$Name (threw, but message did not contain '$MatchMessage': $($_.Exception.Message))"
        }
        else {
            Assert-True $true $Name
        }
    }
}

$cleanJson = '{ "version": 1, "projects": [] }'
$vulnJson = @'
{ "version": 1, "projects": [ { "path": "Sample.csproj", "frameworks": [
  { "framework": "net10.0", "topLevelPackages": [
    { "id": "Bad.Package", "resolvedVersion": "1.0.0", "vulnerabilities": [ { "severity": "High", "advisoryurl": "https://example/advisory" } ] }
  ] } ] } ] }
'@

# A runner factory: succeed/fail per (target,mode) according to a rules table.
function New-FakeRunner {
    param([scriptblock]$Logic)
    return $Logic
}

# 1. Solution-level success returns parsed results.
$okRunner = New-FakeRunner { param($target, $mode) @{ ExitCode = 0; Output = $cleanJson } }
$result1 = Invoke-NuGetPackageAudit -Mode "--vulnerable" -Solution "S.slnx" -Runner $okRunner
Assert-True ($null -ne $result1 -and (@($result1.projects)).Count -eq 0) "Solution-level success returns empty project set"

# 2. Solution-level failure + per-project success => merged result, no throw.
$fallbackOkRunner = New-FakeRunner {
    param($target, $mode)
    if ($target -eq "S.slnx") { return @{ ExitCode = 1; Output = "Unhandled exception: NullReferenceException" } }
    return @{ ExitCode = 0; Output = $cleanJson }
}
$result2 = Invoke-NuGetPackageAudit -Mode "--vulnerable" -Solution "S.slnx" -ProjectFallback @("A.csproj", "B.csproj") -Runner $fallbackOkRunner
Assert-True ($null -ne $result2) "Per-project fallback succeeds when solution-level fails"

# 3. Solution-level failure + ALL projects fail => throw (never silently skip vulnerable).
$allFailRunner = New-FakeRunner { param($target, $mode) @{ ExitCode = 1; Output = "Unhandled exception: NullReferenceException" } }
Assert-Throws { Invoke-NuGetPackageAudit -Mode "--vulnerable" -Solution "S.slnx" -ProjectFallback @("A.csproj") -Runner $allFailRunner } `
    -MatchMessage "could not run" -Name "Un-auditable vulnerable check throws actionable error"

# 4. --outdated solution failure is a soft skip (informational only).
$outdatedFail = New-FakeRunner {
    param($target, $mode)
    if ($mode -eq "--outdated") { return @{ ExitCode = 1; Output = "Unhandled exception" } }
    return @{ ExitCode = 0; Output = $cleanJson }
}
$result4 = Invoke-NuGetPackageAudit -Mode "--outdated" -Solution "S.slnx" -ProjectFallback @("A.csproj") -Runner $outdatedFail
Assert-True ((@($result4.projects)).Count -eq 0) "--outdated solution failure soft-skips"

# 5. Full audit with a vulnerable package present => blocking throw.
$vulnRunner = New-FakeRunner {
    param($target, $mode)
    if ($mode -eq "--vulnerable") { return @{ ExitCode = 0; Output = $vulnJson } }
    return @{ ExitCode = 0; Output = $cleanJson }
}
Assert-Throws { Invoke-NuGetDependencyAudit -RepoRoot (Resolve-Path (Join-Path $ScriptRoot "..")) -Solution "S.slnx" -Runner $vulnRunner } `
    -MatchMessage "vulnerable" -Name "Vulnerable package fails the audit phase"

# 6. Full audit with everything clean => completes and returns a summary.
# The function also writes diagnostic lines to the output stream (so they land in the phase log),
# so the summary object is the last emitted item.
$cleanRunner = New-FakeRunner { param($target, $mode) @{ ExitCode = 0; Output = $cleanJson } }
$summary = (Invoke-NuGetDependencyAudit -RepoRoot (Resolve-Path (Join-Path $ScriptRoot "..")) -Solution "S.slnx" -Runner $cleanRunner) | Select-Object -Last 1
Assert-True ($null -ne $summary -and (@($summary.Vulnerable)).Count -eq 0) "Clean audit phase completes with empty findings"

Write-Host ""
Write-Host ("Dependency-audit tests: {0} passed, {1} failed" -f $script:Passed, $script:Failed) `
    -ForegroundColor $(if ($script:Failed -eq 0) { "Green" } else { "Red" })
exit ($(if ($script:Failed -eq 0) { 0 } else { 1 }))
