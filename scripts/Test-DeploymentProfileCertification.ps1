<#
.SYNOPSIS
    Certifies ETL-SQL deployment profiles and supported transitions.

.DESCRIPTION
    Composes existing focused suites into profile/transition journeys and retains commit-bound JSON
    and Markdown evidence. A lane fails closed: a skipped or failed required phase makes the lane red.

.EXAMPLE
    .\scripts\Test-DeploymentProfileCertification.ps1 -Profile Solo

.EXAMPLE
    .\scripts\Test-DeploymentProfileCertification.ps1 -Transition SoloToTeam

.EXAMPLE
    .\scripts\Test-DeploymentProfileCertification.ps1 -Profile All -NoBuild
#>
[CmdletBinding(DefaultParameterSetName = "Profile")]
param(
    [Parameter(ParameterSetName = "Profile")]
    [ValidateSet("Solo", "Team", "Enterprise", "SaaS", "SharedSaaS", "All")]
    [string]$Profile = "All",

    [Parameter(Mandatory = $true, ParameterSetName = "Transition")]
    [ValidateSet("SoloToTeam", "TeamToEnterprise", "EnterpriseToSaaS", "SoloToSaaS", "SaaSToEnterpriseExit", "Upgrade", "All")]
    [string]$Transition,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$OutputRoot = "certification-results/deployment-profiles",

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ReleaseVersion,

    [switch]$NoBuild,
    [switch]$Explain
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
. (Join-Path $ScriptRoot 'Release.Helpers.ps1')
. (Join-Path $ScriptRoot 'DeploymentCertification.Plan.ps1')
. (Join-Path $ScriptRoot 'DeploymentTestEvidence.Helpers.ps1')
$RepoRoot = (Resolve-Path (Join-Path $ScriptRoot "..")).Path

function Get-JourneyCoverage {
    param(
        [object]$Fixture,
        [string]$Kind,
        [string[]]$Names,
        [bool]$PhasesPassed
    )
    $selector = if ($Kind -eq "Profile") { "profiles" } else { "transitions" }
    $coverage = New-Object System.Collections.Generic.List[object]
    foreach ($name in $Names) {
        $journeys = @($Fixture.journeys | Where-Object { @($_.$selector) -contains $name })
        $continuity = @($journeys | ForEach-Object { $_.continuity } | Select-Object -Unique)
        $hostOwned = @($journeys | ForEach-Object { $_.hostOwnedState } | Select-Object -Unique)
        $negative = @($journeys | ForEach-Object {
            [ordered]@{
                journey = $_.name
                proof = $_.negativeProof
                result = if ($PhasesPassed) { "CoveredByPassingRequiredPhases" } else { "NotProven" }
            }
        })
        $coverage.Add([ordered]@{
            lane = $name
            topology = Get-TopologyClaim $name
            fixtureHash = $script:journeyFixtureHash
            journeys = @($journeys.name)
            mappingDecisions = @($hostOwned | ForEach-Object {
                [ordered]@{ resource = $_; disposition = "TargetOwnedBindingRequired" }
            })
            continuity = [ordered]@{ identifierCount = $continuity.Count; identifiers = $continuity }
            negativeIsolation = $negative
            rollback = if ($Kind -eq "Transition") {
                [ordered]@{ required = $true; outcome = "See concrete scenario evidence" }
            } else {
                [ordered]@{ required = $false; outcome = "NotApplicableToProfileContract" }
            }
        })
    }
    $coverage.ToArray()
}

function Update-ReleaseClaimsIndex {
    param(
        [string]$Root,
        [string]$Version,
        [string]$Run,
        [string]$Commit,
        [string]$Kind,
        [object[]]$Claims,
        [string]$Result,
        [bool]$ReleaseEligible
    )
    $indexPath = Join-Path $Root "claims-index.json"
    $existing = if (Test-Path -LiteralPath $indexPath) {
        @(Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json | Select-Object -ExpandProperty claims)
    } else { @() }
    $selectedNames = @($Claims | ForEach-Object { $_.lane })
    $retained = @($existing | Where-Object { $_.kind -ne $Kind -or $_.lane -notin $selectedNames })
    $updated = @($retained) + @($Claims | ForEach-Object {
        [ordered]@{
            lane = $_.lane
            kind = $Kind
            topology = $_.topology
            claim = $_.claim
            claimScope = if ($null -eq $_.claimScope) { "ProfileAndTransitionContracts" } else { $_.claimScope }
            sharedSaaS = if ($null -eq $_.sharedSaaS) { "N/A" } else { $_.sharedSaaS }
            uncovered = if ($null -eq $_.uncovered) { @() } else { @($_.uncovered) }
            commit = $Commit
            result = $Result
            releaseEligible = $ReleaseEligible
            evidence = "$Run/certification.json"
        }
    })
    $sortedClaims = @($updated | ForEach-Object { [pscustomobject]$_ } | Sort-Object Kind, Lane)
    $index = [ordered]@{
        schemaVersion = "etl-sql.deployment-profile-release-claims/v1"
        releaseVersion = $Version
        generatedUtc = ([DateTimeOffset]::UtcNow).ToString("O")
        claims = $sortedClaims
    }
    $index | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $indexPath -Encoding utf8

    $markdown = New-Object System.Collections.Generic.List[string]
    $markdown.Add("# Deployment-profile release claims — v$Version")
    $markdown.Add("")
    $markdown.Add("| Kind | Lane | Topology | Claim | Scope | Shared SaaS | Uncovered | Result | Release eligible | Evidence |")
    $markdown.Add("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |")
    foreach ($item in $index.claims) {
        $markdown.Add("| $($item.kind) | $($item.lane) | $($item.topology) | $($item.claim) | $($item.claimScope) | $($item.sharedSaaS) | $($item.uncovered -join ' ') | $($item.result) | $($item.releaseEligible) | [$($item.evidence)]($($item.evidence)) |")
    }
    $markdown.Add("")
    $markdown.Add('Only rows with `releaseEligible = True` support the stated claim and scope. Application/policy contracts and Managed Dedicated evidence never certify Shared SaaS runtime containment or hosted production.')
    $markdown | Set-Content -LiteralPath (Join-Path $Root "claims-index.md") -Encoding utf8
}

$laneNames = if ($PSCmdlet.ParameterSetName -eq "Transition") {
    if ($Transition -eq "All") { @(Get-DeploymentCertificationLanes -Kind Transition) } else { @($Transition) }
} else {
    if ($Profile -eq "All") { @(Get-DeploymentCertificationLanes -Kind Profile) } else { @($Profile) }
}
$laneKind = if ($PSCmdlet.ParameterSetName -eq "Transition") { "Transition" } else { "Profile" }
$phases = @(Get-DeploymentCertificationPlan -Kind $laneKind -Names $laneNames)

if ($Explain) {
    Write-Host "Deployment-profile certification plan ($laneKind):" -ForegroundColor Cyan
    foreach ($phase in $phases) {
        Write-Host ("[{0}] {1}" -f $phase.lane, $phase.name) -ForegroundColor White
        if ($phase.faultProfile) {
            Write-Host ("  ./scripts/Test-ProviderNeutralFaultCertification.ps1 -Profile {0}" -f $phase.faultProfile) -ForegroundColor DarkGray
        } else {
            Write-Host ("  dotnet test {0} --filter {1}" -f $phase.project, $phase.filter) -ForegroundColor DarkGray
        }
        Write-Host ("  {0}" -f $phase.proof)
    }
    exit 0
}

Push-Location $RepoRoot
try {
    $source = Get-ReleaseCertificationSource -RepoRoot $RepoRoot
    $commit = $source.commit
    $dirtyLines = $source.dirtyPaths
    $runId = Get-Date -Format "yyyyMMdd-HHmmss"
    $resolvedOutputRoot = if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
        [System.IO.Path]::GetFullPath($OutputRoot)
    } else {
        $effectiveOutputRoot = if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion) -and
            -not $PSBoundParameters.ContainsKey("OutputRoot")) {
            "artifacts/release-evidence/$ReleaseVersion/deployment-profiles"
        } else { $OutputRoot }
        [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $effectiveOutputRoot))
    }
    $runRoot = Join-Path $resolvedOutputRoot $runId
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $scenarioRoot = Join-Path $runRoot "scenario-evidence"
    New-Item -ItemType Directory -Path $scenarioRoot -Force | Out-Null
    $results = New-Object System.Collections.Generic.List[object]
    $journeyFixturePath = Join-Path $RepoRoot "tests/fixtures/deployment-profile-journeys.json"
    $journeyFixture = Get-Content -LiteralPath $journeyFixturePath -Raw | ConvertFrom-Json
    $script:journeyFixtureHash = (Get-FileHash -LiteralPath $journeyFixturePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $priorScenarioEvidenceDirectory = [Environment]::GetEnvironmentVariable("ETLSQL_DEPLOYMENT_CERT_EVIDENCE_DIR")
    [Environment]::SetEnvironmentVariable("ETLSQL_DEPLOYMENT_CERT_EVIDENCE_DIR", $scenarioRoot)

    try {
        foreach ($phase in $phases) {
            $started = [DateTimeOffset]::UtcNow
            $logName = Get-DeploymentPhaseLogName $phase.lane $phase.name
            $logPath = Join-Path $runRoot $logName
            $testEvidence = $null
            Write-Host ("[{0}] {1}" -f $phase.lane, $phase.name) -ForegroundColor Cyan
            if ($phase.faultProfile) {
                $faultOutputRoot = Join-Path $runRoot "provider-neutral-faults"
                $arguments = @(
                    "-NoProfile", "-File", (Join-Path $ScriptRoot "Test-ProviderNeutralFaultCertification.ps1"),
                    "-Profile", $phase.faultProfile,
                    "-Configuration", $Configuration,
                    "-OutputRoot", $faultOutputRoot)
                if ($NoBuild) { $arguments += "-NoBuild" }
                $output = & pwsh @arguments 2>&1
                $executable = 'pwsh'
                $commandText = "pwsh " + ($arguments -join " ")
            } else {
                $trxName = [IO.Path]::ChangeExtension($logName, '.trx')
                if (Test-Path -LiteralPath (Join-Path $runRoot $trxName)) { Remove-Item -LiteralPath (Join-Path $runRoot $trxName) -Force }
                $arguments = @("test", $phase.project, "--configuration", $Configuration, "--filter", $phase.filter, "--logger", "console;verbosity=normal", '--logger', "trx;LogFileName=$trxName", '--results-directory', $runRoot)
                if ($NoBuild) { $arguments += @("--no-build", "--no-restore") }
                $output = & dotnet @arguments 2>&1
                $executable = 'dotnet'
                $commandText = "dotnet " + ($arguments -join " ")
            }
            $exitCode = $LASTEXITCODE
            if (-not $phase.faultProfile -and $exitCode -eq 0) {
                try {
                    $testEvidence = Get-DeploymentTestEvidence -Path (Join-Path $runRoot $trxName) -Filter $phase.filter
                    $testEvidence.path = $trxName
                } catch {
                    $exitCode = 1
                    $output += 'Required TRX evidence is missing, invalid, incomplete or does not cover the planned test filter.'
                }
            }
            if (-not $phase.faultProfile -and $exitCode -eq 0 -and
                -not (Test-ReleaseTestOutputExecuted -Output ($output -join "`n"))) {
                $exitCode = 1
                $output += "Certification phase '$($phase.name)' produced no passed test summary."
            }
            $output | Set-Content -LiteralPath $logPath -Encoding utf8
            $results.Add([ordered]@{
                lane = $phase.lane
                phase = $phase.name
                prerequisite = $phase.prerequisite
                proof = $phase.proof
                command = $commandText
                invocation = [ordered]@{ executable = $executable; arguments = @($arguments) }
                testEvidence = $testEvidence
                startedUtc = $started.ToString("O")
                completedUtc = ([DateTimeOffset]::UtcNow).ToString("O")
                exitCode = $exitCode
                status = if ($exitCode -eq 0) { "Passed" } else { "Failed" }
                log = $logName
            })
            if ($exitCode -ne 0) { break }
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable("ETLSQL_DEPLOYMENT_CERT_EVIDENCE_DIR", $priorScenarioEvidenceDirectory)
    }

    $phasesPassed = $results.Count -eq $phases.Count -and @($results | Where-Object { $_.status -ne "Passed" }).Count -eq 0
    $scenarioEvidence = @(
        Get-ChildItem -LiteralPath $scenarioRoot -Filter "*.json" -File |
            Sort-Object Name |
            ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json }
    )
    $expectedScenarioIds = @(Get-ExpectedScenarioIds $laneNames)
    $actualScenarioIds = @($scenarioEvidence | ForEach-Object { $_.scenarioId })
    $missingScenarioIds = @($expectedScenarioIds | Where-Object { $_ -notin $actualScenarioIds })
    $scenarioSchemaFailures = @($scenarioEvidence | Where-Object {
        $_.schemaVersion -ne "etl-sql.deployment-scenario-evidence/v1" -or
        [string]::IsNullOrWhiteSpace($_.scenarioId) -or
        $null -eq $_.topology -or $null -eq $_.artifactHashes -or
        $null -eq $_.mappingDecisions -or $null -eq $_.continuity -or
        $null -eq $_.negativeIsolation -or $null -eq $_.rollback
    })
    # Progressive SaaS Phase A: the Enterprise lane must prove all eight hosted prerequisites in this
    # one run. A prerequisite whose phases never ran — because an earlier phase failed and the loop
    # broke — is reported by name rather than inferred from the lane summary, so the operator learns
    # which prerequisite is unproven instead of only that the lane went red.
    $hostedPrerequisites = @()
    if ($laneKind -eq "Profile" -and $laneNames -contains "Enterprise") {
        $hostedPrerequisites = @(foreach ($prerequisite in $EnterpriseHostedPrerequisites) {
            $covering = @($results | Where-Object { $_.lane -eq "Enterprise" -and $_.prerequisite -eq $prerequisite })
            [ordered]@{
                prerequisite = $prerequisite
                proven = ($covering.Count -gt 0 -and @($covering | Where-Object { $_.status -ne "Passed" }).Count -eq 0)
                phases = @($covering | ForEach-Object { $_.phase })
            }
        })
    }
    $unprovenPrerequisites = @($hostedPrerequisites | Where-Object { -not $_.proven })
    $passed = $phasesPassed -and $missingScenarioIds.Count -eq 0 -and $scenarioSchemaFailures.Count -eq 0 -and
        $unprovenPrerequisites.Count -eq 0
    $releaseEligible = $passed -and $dirtyLines.Count -eq 0 -and $Configuration -eq 'Release' -and -not $NoBuild
    $uncovered = New-Object System.Collections.Generic.List[string]
    foreach ($missing in $missingScenarioIds) { $uncovered.Add("Missing required scenario evidence: $missing") }
    foreach ($invalid in $scenarioSchemaFailures) { $uncovered.Add("Invalid scenario evidence contract: $($invalid.scenarioId)") }
    foreach ($unproven in $unprovenPrerequisites) { $uncovered.Add("Enterprise hosted prerequisite not proven: $($unproven.prerequisite)") }
    if ($laneNames -contains "SaaS" -or $laneNames -contains "EnterpriseToSaaS" -or
        $laneNames -contains "SoloToSaaS" -or $laneNames -contains "Upgrade") {
        $uncovered.Add("Managed Dedicated profile and transition evidence does not certify Shared SaaS.")
    }
    foreach ($claim in @($laneNames | ForEach-Object { Get-TopologyClaim $_ })) {
        foreach ($gap in $claim.uncovered) { $uncovered.Add($gap) }
    }
    if ($dirtyLines.Count -gt 0) {
        $uncovered.Add("The worktree was dirty at start; this run is development evidence and cannot support a release claim.")
    }
    if ($Configuration -ne 'Release' -or $NoBuild) { $uncovered.Add('Debug or NoBuild results are development evidence; matching candidate build provenance is required for reuse.') }
    $journeyCoverage = @(Get-JourneyCoverage $journeyFixture $laneKind $laneNames $phasesPassed)
    Assert-ReleaseCertificationSourceUnchanged -RepoRoot $RepoRoot -Initial $source
    $evidence = [ordered]@{
        schemaVersion = "etl-sql.deployment-profile-certification/v1"
        runId = $runId
        commit = $commit
        sourceFingerprint = $source.fingerprint
        dirty = $dirtyLines.Count -gt 0
        dirtyPaths = @($dirtyLines)
        kind = $laneKind
        lanes = @($laneNames)
        configuration = $Configuration
        noBuild = [bool]$NoBuild
        repositoryRoot = $RepoRoot
        runDirectory = $runRoot
        startedBy = [Environment]::UserName
        machine = [Environment]::MachineName
        os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        result = if ($passed) { "Passed" } else { "Failed" }
        releaseEligible = $releaseEligible
        topologyClaims = @($laneNames | ForEach-Object { Get-TopologyClaim $_ })
        journeyFixture = [ordered]@{ path = "tests/fixtures/deployment-profile-journeys.json"; sha256 = $script:journeyFixtureHash }
        journeyCoverage = $journeyCoverage
        hostedPrerequisites = $hostedPrerequisites
        scenarioEvidence = $scenarioEvidence
        scenarioEvidenceFiles = @(Get-ChildItem -LiteralPath $scenarioRoot -Filter '*.json' -File | ForEach-Object {
            $item = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
            [ordered]@{ scenarioId = $item.scenarioId; path = "scenario-evidence/$($_.Name)"; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
        phases = $results.ToArray()
        uncovered = $uncovered.ToArray()
    }
    $jsonPath = Join-Path $runRoot "certification.json"
    $evidence | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $jsonPath -Encoding utf8

    $markdown = New-Object System.Collections.Generic.List[string]
    $markdown.Add("# Deployment-profile certification")
    $markdown.Add("")
    $markdown.Add("- Commit: ``$commit``")
    $markdown.Add("- Worktree dirty: ``$($evidence.dirty)``")
    $markdown.Add("- Lanes: $($laneNames -join ', ')")
    $markdown.Add("- Result: **$($evidence.result)**")
    $markdown.Add("- Release eligible: **$($evidence.releaseEligible)**")
    $markdown.Add("")
    $markdown.Add("| Lane | Phase | Result | Evidence |")
    $markdown.Add("| :--- | :--- | :--- | :--- |")
    foreach ($result in $results) {
        $markdown.Add("| $($result.lane) | $($result.phase) | $($result.status) | [$($result.log)]($($result.log)) |")
    }
    $markdown.Add("")
    $markdown.Add("## Topology claims")
    $markdown.Add("")
    $markdown.Add("| Lane | Tested topology | Claim | Scope | Shared SaaS |")
    $markdown.Add("| :--- | :--- | :--- | :--- | :--- |")
    foreach ($claim in $evidence.topologyClaims) {
        $shared = if ($null -eq $claim.sharedSaaS) { "N/A" } else { $claim.sharedSaaS }
        $scope = if ($null -eq $claim.claimScope) { "ProfileAndTransitionContracts" } else { $claim.claimScope }
        $markdown.Add("| $($claim.lane) | $($claim.topology) | $($claim.claim) | $scope | $shared |")
    }
    if ($hostedPrerequisites.Count -gt 0) {
        $markdown.Add("")
        $markdown.Add("## Enterprise hosted prerequisites (Progressive SaaS Phase A)")
        $markdown.Add("")
        $markdown.Add("A Managed Dedicated SaaS claim builds on all eight. This table is the joined proof for")
        $markdown.Add("this commit; it is not a substitute for each domain's own topology evidence.")
        $markdown.Add("")
        $markdown.Add("| Prerequisite | Proven | Proving phases |")
        $markdown.Add("| :--- | :---: | :--- |")
        foreach ($entry in $hostedPrerequisites) {
            $phaseList = if ($entry.phases.Count -eq 0) { "_not run_" } else { $entry.phases -join "; " }
            $markdown.Add("| $($entry.prerequisite) | $($entry.proven) | $phaseList |")
        }
    }
    $markdown.Add("")
    $markdown.Add("## Concrete scenario evidence")
    $markdown.Add("")
    if ($scenarioEvidence.Count -eq 0) {
        $markdown.Add("No concrete lifecycle scenario is required for the selected profile contract; phase and journey coverage above are the evidence.")
    } else {
        $markdown.Add("| Scenario | Kind | Source | Target | Topology |")
        $markdown.Add("| :--- | :--- | :--- | :--- | :--- |")
        foreach ($scenario in $scenarioEvidence) {
            $markdown.Add("| $($scenario.scenarioId) | $($scenario.kind) | $($scenario.sourceProfile) | $($scenario.targetProfile) | $($scenario.topology) |")
        }
    }
    $markdown.Add("")
    $markdown.Add("## Uncovered")
    $markdown.Add("")
    if ($evidence.uncovered.Count -eq 0) {
        $markdown.Add("None for the selected lane contract.")
    } else {
        foreach ($item in $evidence.uncovered) { $markdown.Add("- $item") }
    }
    $markdown | Set-Content -LiteralPath (Join-Path $runRoot "certification.md") -Encoding utf8

    if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion)) {
        Update-ReleaseClaimsIndex -Root $resolvedOutputRoot -Version $ReleaseVersion -Run $runId `
            -Commit $commit -Kind $laneKind -Claims $evidence.topologyClaims -Result $evidence.result `
            -ReleaseEligible $evidence.releaseEligible
    }

    Write-Host "Evidence: $runRoot" -ForegroundColor Cyan
    if (-not $passed -or
        (-not [string]::IsNullOrWhiteSpace($ReleaseVersion) -and -not $releaseEligible)) { exit 1 }
}
finally {
    Pop-Location
}
