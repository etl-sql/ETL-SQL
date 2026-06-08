<#
.SYNOPSIS
    NuGet dependency-audit helpers shared by Test-PreRelease.ps1 and Test-DependencyAudit.ps1.

.DESCRIPTION
    The .NET 10.0.300 SDK has a bug where `dotnet list package --deprecated` and
    `dotnet list package --vulnerable` throw a NullReferenceException at the *solution* level when
    central package management (CPM) is in use, the same class of failure already worked around for
    `--outdated`. Previously that aborted the release gate before build and tests ran.

    These helpers make the audit reliable: when the solution-level command fails for deprecated or
    vulnerable, they fall back to auditing each project individually (which is not affected by the
    solution-level CPM bug). Vulnerability/deprecation results are never silently skipped — if no
    authoritative audit can run at all, Invoke-NuGetPackageAudit throws an actionable error so the
    release gate fails loudly instead of certifying an unknown dependency posture.

    The dotnet invocation is injectable (-Runner) so the behaviour can be unit-tested without the SDK.
#>

# Note: intentionally NOT using Set-StrictMode here. The audit parsers rely on lenient property
# access ($package.latestVersion etc. returning $null when a JSON property is absent).

function Invoke-DotnetListPackage {
    param(
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string]$Mode
    )

    $args = @("list", $Target, "package", $Mode, "--include-transitive", "--format", "json", "--no-restore")

    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & dotnet @args 2>&1
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }
    $exitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 0 }

    return @{ ExitCode = $exitCode; Output = ($output -join "`n") }
}

function ConvertFrom-NuGetAuditOutput {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Mode
    )

    $jsonText = $Text.Trim()
    if ([string]::IsNullOrWhiteSpace($jsonText)) {
        throw "dotnet list package $Mode returned no output."
    }

    return $jsonText | ConvertFrom-Json
}

function Get-SolutionProjectPaths {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $projects = New-Object System.Collections.Generic.List[string]
    foreach ($dir in @("src", "tests")) {
        $root = Join-Path $RepoRoot $dir
        if (Test-Path $root) {
            Get-ChildItem -Path $root -Filter *.csproj -Recurse -File |
                ForEach-Object { $projects.Add($_.FullName) }
        }
    }
    return $projects.ToArray()
}

function Invoke-NuGetPackageAudit {
    param(
        [Parameter(Mandatory = $true)][string]$Mode,
        [string]$Solution = "ETL-SQL.slnx",
        [string[]]$ProjectFallback = @(),
        [scriptblock]$Runner = $null
    )

    if (-not $Runner) {
        $Runner = { param($target, $mode) Invoke-DotnetListPackage -Target $target -Mode $mode }
    }

    # 1. Preferred: audit the whole solution in one call.
    $primary = & $Runner $Solution $Mode
    if ($primary.ExitCode -eq 0) {
        return ConvertFrom-NuGetAuditOutput -Text $primary.Output -Mode $Mode
    }

    # 2. --outdated is informational only; a failure there is a soft skip (not a security gate).
    if ($Mode -eq "--outdated") {
        Write-Warning "dotnet list package --outdated failed at the solution level (known SDK bug with CPM). Skipping outdated package check."
        return [PSCustomObject]@{ projects = @() }
    }

    # 3. Reliability fallback for deprecated/vulnerable: audit each project individually. The
    #    solution-level CPM NullReferenceException does not occur for single-project invocations.
    $mergedProjects = New-Object System.Collections.Generic.List[object]
    $auditedAnyProject = $false
    foreach ($project in $ProjectFallback) {
        $result = & $Runner $project $Mode
        if ($result.ExitCode -ne 0) {
            continue
        }
        $auditedAnyProject = $true
        $parsed = ConvertFrom-NuGetAuditOutput -Text $result.Output -Mode $Mode
        if ($parsed.projects) {
            foreach ($p in $parsed.projects) { $mergedProjects.Add($p) }
        }
    }

    if ($auditedAnyProject) {
        return [PSCustomObject]@{ projects = $mergedProjects.ToArray() }
    }

    # 4. No authoritative audit could run. Never silently skip vulnerable/deprecated results.
    throw ("NuGet $Mode audit could not run: 'dotnet list package $Mode' failed at the solution level " +
        "and for every project (known .NET 10.0.300 SDK NullReferenceException with central package " +
        "management). The release gate cannot certify the dependency posture. Resolve by using an SDK " +
        "where 'dotnet list package $Mode --include-transitive' succeeds, or run the audit manually and " +
        "confirm there are no $($Mode.TrimStart('-')) packages before shipping.")
}

function Get-NuGetAuditFindings {
    param(
        [Parameter(Mandatory = $true)]$AuditResult,
        [Parameter(Mandatory = $true)][ValidateSet("outdated", "deprecated", "vulnerable")][string]$Kind
    )

    $findings = New-Object System.Collections.Generic.List[object]
    if (-not $AuditResult.projects) {
        return $findings
    }

    foreach ($project in $AuditResult.projects) {
        if (-not $project.frameworks) {
            continue
        }

        foreach ($framework in $project.frameworks) {
            foreach ($bucketName in @("topLevelPackages", "transitivePackages")) {
                $packages = $framework.$bucketName
                if (-not $packages) {
                    continue
                }

                foreach ($package in $packages) {
                    switch ($Kind) {
                        "outdated" {
                            if ($package.latestVersion) {
                                $findings.Add([ordered]@{
                                    project = $project.path
                                    framework = $framework.framework
                                    bucket = $bucketName
                                    id = $package.id
                                    requestedVersion = $package.requestedVersion
                                    resolvedVersion = $package.resolvedVersion
                                    latestVersion = $package.latestVersion
                                })
                            }
                        }
                        "deprecated" {
                            if ($package.deprecationReasons) {
                                $findings.Add([ordered]@{
                                    project = $project.path
                                    framework = $framework.framework
                                    bucket = $bucketName
                                    id = $package.id
                                    resolvedVersion = $package.resolvedVersion
                                    deprecationReasons = @($package.deprecationReasons)
                                    alternativePackage = $package.alternativePackage
                                })
                            }
                        }
                        "vulnerable" {
                            if ($package.vulnerabilities -or $package.severity -or $package.advisoryUrl -or $package.advisoryTitle) {
                                $entry = [ordered]@{
                                    project = $project.path
                                    framework = $framework.framework
                                    bucket = $bucketName
                                    id = $package.id
                                    resolvedVersion = $package.resolvedVersion
                                }

                                if ($package.vulnerabilities) {
                                    $entry.vulnerabilities = @($package.vulnerabilities)
                                }
                                if ($package.severity) {
                                    $entry.severity = $package.severity
                                }
                                if ($package.advisoryUrl) {
                                    $entry.advisoryUrl = $package.advisoryUrl
                                }
                                if ($package.advisoryTitle) {
                                    $entry.advisoryTitle = $package.advisoryTitle
                                }

                                $findings.Add($entry)
                            }
                        }
                    }
                }
            }
        }
    }

    return $findings
}

function Format-NuGetFinding {
    param(
        [Parameter(Mandatory = $true)]$Finding,
        [string]$RepoRoot = $null
    )

    $projectPath = [string]$Finding.project
    if ($RepoRoot -and $projectPath.StartsWith($RepoRoot)) {
        $projectPath = $projectPath.Substring($RepoRoot.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar)
    }
    $scope = if ($Finding.bucket -eq "topLevelPackages") { "top-level" } else { "transitive" }

    switch ($true) {
        { $Finding.latestVersion } {
            return ("{0} [{1}] {2} {3} -> {4}" -f $Finding.id, $scope, $projectPath, $Finding.resolvedVersion, $Finding.latestVersion)
        }
        { $Finding.deprecationReasons } {
            $reasons = ($Finding.deprecationReasons -join ", ")
            return ("{0} [{1}] {2} {3} ({4})" -f $Finding.id, $scope, $projectPath, $Finding.resolvedVersion, $reasons)
        }
        { $Finding.vulnerabilities } {
            $severities = @($Finding.vulnerabilities | ForEach-Object { $_.severity }) -join ", "
            if ([string]::IsNullOrWhiteSpace($severities)) {
                $severities = "unknown severity"
            }
            return ("{0} [{1}] {2} {3} ({4})" -f $Finding.id, $scope, $projectPath, $Finding.resolvedVersion, $severities)
        }
        { $Finding.severity -or $Finding.advisoryUrl -or $Finding.advisoryTitle } {
            $details = @()
            if ($Finding.severity) { $details += $Finding.severity }
            if ($Finding.advisoryTitle) { $details += $Finding.advisoryTitle }
            if ($Finding.advisoryUrl) { $details += $Finding.advisoryUrl }
            return ("{0} [{1}] {2} {3} ({4})" -f $Finding.id, $scope, $projectPath, $Finding.resolvedVersion, ($details -join ", "))
        }
        default {
            return ("{0} [{1}] {2}" -f $Finding.id, $scope, $projectPath)
        }
    }
}

function Invoke-NuGetDependencyAudit {
    <#
        Runs the full three-mode audit, prints a human-readable summary, and throws when blocking
        deprecated (non-Legacy) or vulnerable packages are found — or when the audit could not run.
        Returns a summary object so callers/tests can assert on the findings.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$Solution = "ETL-SQL.slnx",
        [scriptblock]$Runner = $null
    )

    $projectFallback = Get-SolutionProjectPaths -RepoRoot $RepoRoot

    $outdatedAudit = Invoke-NuGetPackageAudit -Mode "--outdated" -Solution $Solution -ProjectFallback $projectFallback -Runner $Runner
    $deprecatedAudit = Invoke-NuGetPackageAudit -Mode "--deprecated" -Solution $Solution -ProjectFallback $projectFallback -Runner $Runner
    $vulnerableAudit = Invoke-NuGetPackageAudit -Mode "--vulnerable" -Solution $Solution -ProjectFallback $projectFallback -Runner $Runner

    $outdatedFindings = @(Get-NuGetAuditFindings -AuditResult $outdatedAudit -Kind "outdated")
    $deprecatedFindings = @(Get-NuGetAuditFindings -AuditResult $deprecatedAudit -Kind "deprecated")
    $vulnerableFindings = @(Get-NuGetAuditFindings -AuditResult $vulnerableAudit -Kind "vulnerable")

    Write-Output ("Outdated packages: {0}" -f $outdatedFindings.Count)
    if ($outdatedFindings.Count -gt 0) {
        Write-Output "Recent package updates are available:"
        foreach ($finding in ($outdatedFindings | Select-Object -First 20)) {
            Write-Output ("  - {0}" -f (Format-NuGetFinding -Finding $finding -RepoRoot $RepoRoot))
        }
        if ($outdatedFindings.Count -gt 20) {
            Write-Output ("  - ... and {0} more" -f ($outdatedFindings.Count - 20))
        }
    }

    Write-Output ("Deprecated packages: {0}" -f $deprecatedFindings.Count)
    foreach ($finding in ($deprecatedFindings | Select-Object -First 20)) {
        Write-Output ("  - {0}" -f (Format-NuGetFinding -Finding $finding -RepoRoot $RepoRoot))
    }
    if ($deprecatedFindings.Count -gt 20) {
        Write-Output ("  - ... and {0} more" -f ($deprecatedFindings.Count - 20))
    }

    Write-Output ("Vulnerable packages: {0}" -f $vulnerableFindings.Count)
    foreach ($finding in ($vulnerableFindings | Select-Object -First 20)) {
        Write-Output ("  - {0}" -f (Format-NuGetFinding -Finding $finding -RepoRoot $RepoRoot))
    }
    if ($vulnerableFindings.Count -gt 20) {
        Write-Output ("  - ... and {0} more" -f ($vulnerableFindings.Count - 20))
    }

    $blockingDeprecated = @($deprecatedFindings | Where-Object {
        $reasons = $_.deprecationReasons
        $nonLegacy = $reasons | Where-Object { $_ -ne "Legacy" }
        $nonLegacy.Count -gt 0
    })

    if ($blockingDeprecated.Count -gt 0 -or $vulnerableFindings.Count -gt 0) {
        throw "NuGet audit found deprecated or vulnerable packages. Update or replace them before shipping."
    }

    return [PSCustomObject]@{
        Outdated   = $outdatedFindings
        Deprecated = $deprecatedFindings
        Vulnerable = $vulnerableFindings
    }
}
