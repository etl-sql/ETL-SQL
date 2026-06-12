<#
.SYNOPSIS
    CI gate: fails when any NuGet package (direct or transitive) has a known vulnerability.

.DESCRIPTION
    Runs `dotnet list package --vulnerable --include-transitive` across the solution using the shared
    dependency-audit helpers in scripts/lib/DependencyAudit.ps1 (solution-level audit with per-project
    fallback for the .NET 10.0.300 SDK + CPM NullReferenceException). The dependency graph must already
    be restored (`dotnet restore ETL-SQL.slnx`) — the audit runs with --no-restore.

    This is the fast vulnerable-only gate for every CI run; the full three-mode audit
    (outdated/deprecated/vulnerable) still runs in Test-PreRelease.ps1.

    Exit code 0 = no known-vulnerable packages; non-zero = vulnerable packages found, or no
    authoritative audit could run (never silently skipped). See SECURITY.md
    ("Dependency Vulnerability Management") for the response procedure when this gate blocks a build.
#>
[CmdletBinding()]
param(
    [string]$Solution = "ETL-SQL.slnx"
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = (Resolve-Path (Join-Path $ScriptRoot "..")).Path
. (Join-Path $ScriptRoot "lib/DependencyAudit.ps1")

try {
    $projectFallback = Get-SolutionProjectPaths -RepoRoot $RepoRoot
    $audit = Invoke-NuGetPackageAudit -Mode "--vulnerable" -Solution $Solution -ProjectFallback $projectFallback
    $findings = @(Get-NuGetAuditFindings -AuditResult $audit -Kind "vulnerable")
}
catch {
    Write-Host "FAIL  $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ("Vulnerable NuGet packages (direct + transitive): {0}" -f $findings.Count)
foreach ($finding in $findings) {
    Write-Host ("  - {0}" -f (Format-NuGetFinding -Finding $finding -RepoRoot $RepoRoot)) -ForegroundColor Red
    foreach ($vulnerability in @($finding.vulnerabilities)) {
        if ($vulnerability.advisoryurl) {
            Write-Host ("      {0} {1}" -f $vulnerability.severity, $vulnerability.advisoryurl)
        }
    }
}

if ($findings.Count -gt 0) {
    Write-Host "Known-vulnerable packages block the build. Follow the response procedure in SECURITY.md ('Dependency Vulnerability Management')." -ForegroundColor Red
    exit 1
}

Write-Host "No known-vulnerable NuGet packages." -ForegroundColor Green
exit 0
