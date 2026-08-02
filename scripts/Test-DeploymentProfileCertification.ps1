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
    [ValidateSet("Solo", "Team", "Enterprise", "SaaS", "All")]
    [string]$Profile = "All",

    [Parameter(Mandatory = $true, ParameterSetName = "Transition")]
    [ValidateSet("SoloToTeam", "TeamToEnterprise", "EnterpriseToSaaS", "SoloToSaaS", "Upgrade", "All")]
    [string]$Transition,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$OutputRoot = "certification-results/deployment-profiles",

    [switch]$NoBuild,
    [switch]$Explain
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = (Resolve-Path (Join-Path $ScriptRoot "..")).Path
$CoreTests = "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj"
$PortalTests = "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj"

function New-Phase {
    param([string]$Name, [string]$Project, [string]$Filter, [string]$Proof)
    [ordered]@{ name = $Name; project = $Project; filter = $Filter; proof = $Proof }
}

function Get-ProfilePhases {
    param([string]$Name)
    switch ($Name) {
        "Solo" {
            @(
                New-Phase "Solo contract and local policy" $CoreTests "FullyQualifiedName~DeploymentProfileContractTests|FullyQualifiedName~WorkspacePolicyDocumentTests" "Portable profile contract and source-controlled policy load locally."
                New-Phase "Solo quality gate and evidence" $CoreTests "FullyQualifiedName~QualityRunReporterTests|FullyQualifiedName~AssertJobRuntimeTests|FullyQualifiedName~WorkspacePolicyRequiredTagsRuleTests|FullyQualifiedName~WorkstationAutomation" "Local metadata and quality failures return non-zero gate semantics with versioned evidence."
                New-Phase "Solo schema stewardship" $CoreTests "FullyQualifiedName~PiiSchemaScannerTests|FullyQualifiedName~StewardshipScoringTests" "Schema-only PII/stewardship works without Portal or remote services."
            )
        }
        "Team" {
            @(
                New-Phase "Team durable quality and catalog" $CoreTests "FullyQualifiedName~DataQualityMetricsPersistenceTests|FullyQualifiedName~OrchestratorPromotionPackageTests" "SQLite retains quality history, jobs, schedules, ownership, lineage, and tags."
                New-Phase "Team notification dispatch" $CoreTests "FullyQualifiedName~TeamSqliteBaselines_TriggerWebhookAndSmtpNotificationsWithoutPortal|FullyQualifiedName~SchedulerRetryTests|FullyQualifiedName~WebhookConnectorTests|FullyQualifiedName~JobFailureDigestTemplateTests" "SQLite quality baselines drive SMTP/Webhook notification paths and retries without Portal."
                New-Phase "Team Portal configuration" $PortalTests "FullyQualifiedName~ConfigurationRoundTripTests|FullyQualifiedName~ConfigurationPromotionValidationTests" "Optional single-node Portal configuration converges and collisions fail safely."
            )
        }
        "Enterprise" {
            @(
                New-Phase "Enterprise policy and enrollment" $CoreTests "FullyQualifiedName~EnterprisePolicyRuntimeTests|FullyQualifiedName~OrganizationPolicySchemaTests|FullyQualifiedName~PolicyAuthorityServiceTests" "Typed organization policy and authority boundaries are enforced."
                New-Phase "Enterprise OIDC and publishing policy" $PortalTests "FullyQualifiedName~OidcAuthTests|FullyQualifiedName~ReportPublishingPolicyTests|FullyQualifiedName~PortalInteractiveRunPolicyTests|FullyQualifiedName~PolicyDistributionApiTests" "Federated identity and signed organization metadata policy guard Portal execution and publishing."
                New-Phase "Enterprise HA fencing" $CoreTests "FullyQualifiedName~FencingTokenTests|FullyQualifiedName~WriteEpochFencingTests" "Database leases and write epochs fence stale owners."
            )
        }
        "SaaS" {
            @(
                New-Phase "SaaS tenant onboarding and isolation" $CoreTests "FullyQualifiedName~SaasTenantOnboardingTests" "Host-fixed tenant boundaries isolate stores, artifacts, keys, caches, queues, namespaces, and limits."
                New-Phase "SaaS import safety" $CoreTests "FullyQualifiedName~OrchestratorPromotionPackageTests|FullyQualifiedName~DeploymentPromotionPreflightTests" "Portable state imports idempotently without resolved secret material."
                New-Phase "SaaS Portal runtime isolation" $PortalTests "FullyQualifiedName~SaasTenantRuntimeIsolationTests|FullyQualifiedName~ConfigurationPromotionValidationTests|FullyQualifiedName~ConfigurationExportSecretExclusionTests" "Host-fixed Portal databases, audit outboxes, and security caches reject cross-tenant state while bootstrap validation excludes secrets."
            )
        }
    }
}

function Get-TransitionPhases {
    param([string]$Name)
    switch ($Name) {
        "SoloToTeam" {
            @(
                New-Phase "Solo to Team lifecycle" $CoreTests "FullyQualifiedName~DeploymentTransitionLifecycleTests.SoloToTeam" "Backup/export, scheduler fencing, cutover continuity, and a scheduler-safe rollback point execute end to end."
                New-Phase "Solo to Team inventory and import" $CoreTests "FullyQualifiedName~DeploymentPromotionPreflightTests|FullyQualifiedName~OrchestratorPromotionPackageTests" "Artifacts and eligible history rebind and converge with jobs fenced."
                New-Phase "Solo to Team Portal replay" $PortalTests "FullyQualifiedName~ConfigurationPromotionTests" "Logical identity, ownership, secrets, roots, and replay idempotence are preserved."
            )
        }
        "TeamToEnterprise" {
            @(
                New-Phase "Team to Enterprise lifecycle" $CoreTests "FullyQualifiedName~DeploymentTransitionLifecycleTests.TeamToEnterprise" "Backup/export, scheduler fencing, cutover continuity, and a scheduler-safe rollback point execute end to end."
                New-Phase "Team to Enterprise state migration" $CoreTests "FullyQualifiedName~DatabaseMigrationServiceTests" "SQLite state migrates transactionally to PostgreSQL and fails closed."
                New-Phase "Team to Enterprise configuration promotion" $PortalTests "FullyQualifiedName~ConfigurationPromotionTests|FullyQualifiedName~ConfigurationPromotionValidationTests" "Target bindings and catalog ownership survive promotion."
            )
        }
        "EnterpriseToSaaS" {
            @(
                New-Phase "Enterprise to SaaS lifecycle" $CoreTests "FullyQualifiedName~DeploymentTransitionLifecycleTests.EnterpriseToSaas" "Backup/export, scheduler fencing, cutover continuity, and a scheduler-safe rollback point execute end to end."
                New-Phase "Enterprise to SaaS onboarding" $CoreTests "FullyQualifiedName~SaasTenantOnboardingTests|FullyQualifiedName~OrchestratorPromotionPackageTests" "Portable enterprise state enters a disabled, isolated tenant boundary."
                New-Phase "Enterprise to SaaS Portal validation" $PortalTests "FullyQualifiedName~SaasTenantRuntimeIsolationTests|FullyQualifiedName~ConfigurationPromotionValidationTests|FullyQualifiedName~ConfigurationExportSecretExclusionTests" "Bootstrap collisions, secret leakage, and cross-tenant Portal runtime state fail before activation."
            )
        }
        "SoloToSaaS" {
            @(
                New-Phase "Solo to SaaS lifecycle" $CoreTests "FullyQualifiedName~DeploymentTransitionLifecycleTests.SoloToSaas" "Backup/export, scheduler fencing, cutover continuity, and a scheduler-safe rollback point execute end to end."
                New-Phase "Solo to SaaS onboarding" $CoreTests "FullyQualifiedName~SaasTenantOnboardingTests|FullyQualifiedName~DeploymentPromotionPreflightTests" "Direct portable onboarding creates the same isolated boundary without profile rewriting."
            )
        }
        "Upgrade" {
            @(
                New-Phase "All-profile upgrade lifecycle" $CoreTests "FullyQualifiedName~DeploymentProfileUpgradeLifecycleTests" "Solo, Team, Enterprise, and SaaS execute backup/export, scheduler fencing, cutover proof, and scheduler-safe rollback."
                New-Phase "Profile contract upgrade invariants" $CoreTests "FullyQualifiedName~DeploymentProfileContractTests|FullyQualifiedName~SaasTenantOnboardingTests" "Portable/profile manifests remain readable and tenant boundaries remain isolated."
                New-Phase "Portal N to N+1 migration and restore" $PortalTests "FullyQualifiedName~UpgradePathDrillTests|FullyQualifiedName~MigrationConvergenceTests|FullyQualifiedName~BackupRestoreDrillTests" "Release-N Portal/Orchestrator state migrates forward, schema converges, and coordinated restore remains viable."
            )
        }
    }
}

$laneNames = if ($PSCmdlet.ParameterSetName -eq "Transition") {
    if ($Transition -eq "All") { @("SoloToTeam", "TeamToEnterprise", "EnterpriseToSaaS", "SoloToSaaS", "Upgrade") } else { @($Transition) }
} else {
    if ($Profile -eq "All") { @("Solo", "Team", "Enterprise", "SaaS") } else { @($Profile) }
}
$laneKind = if ($PSCmdlet.ParameterSetName -eq "Transition") { "Transition" } else { "Profile" }
$phases = New-Object System.Collections.Generic.List[object]
$journeyContract = New-Phase "Deployment journey fixture contract" $CoreTests "FullyQualifiedName~DeploymentProfileJourneyFixtureTests" "Required journeys declare positive, negative, portability, host-ownership, and continuity proof."
$journeyContract["lane"] = "Contract"
$phases.Add($journeyContract)
foreach ($lane in $laneNames) {
    $items = if ($laneKind -eq "Profile") { Get-ProfilePhases $lane } else { Get-TransitionPhases $lane }
    foreach ($item in $items) {
        $item["lane"] = $lane
        $phases.Add($item)
    }
}

if ($Explain) {
    Write-Host "Deployment-profile certification plan ($laneKind):" -ForegroundColor Cyan
    foreach ($phase in $phases) {
        Write-Host ("[{0}] {1}" -f $phase.lane, $phase.name) -ForegroundColor White
        Write-Host ("  dotnet test {0} --filter {1}" -f $phase.project, $phase.filter) -ForegroundColor DarkGray
        Write-Host ("  {0}" -f $phase.proof)
    }
    exit 0
}

Push-Location $RepoRoot
try {
    $commit = ((& git rev-parse HEAD) -join "").Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) { throw "Could not resolve git commit." }
    $dirtyLines = @(& git status --short)
    $runId = Get-Date -Format "yyyyMMdd-HHmmss"
    $resolvedOutputRoot = if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
        [System.IO.Path]::GetFullPath($OutputRoot)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $OutputRoot))
    }
    $runRoot = Join-Path $resolvedOutputRoot $runId
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $results = New-Object System.Collections.Generic.List[object]

    foreach ($phase in $phases) {
        $started = [DateTimeOffset]::UtcNow
        $logName = (($phase.lane + "-" + $phase.name) -replace '[^A-Za-z0-9._-]', '-').ToLowerInvariant() + ".log"
        $logPath = Join-Path $runRoot $logName
        $arguments = @("test", $phase.project, "--configuration", $Configuration, "--filter", $phase.filter, "--logger", "console;verbosity=normal")
        if ($NoBuild) { $arguments += @("--no-build", "--no-restore") }
        Write-Host ("[{0}] {1}" -f $phase.lane, $phase.name) -ForegroundColor Cyan
        $output = & dotnet @arguments 2>&1
        $exitCode = $LASTEXITCODE
        $output | Set-Content -LiteralPath $logPath -Encoding utf8
        $results.Add([ordered]@{
            lane = $phase.lane
            phase = $phase.name
            proof = $phase.proof
            command = "dotnet " + ($arguments -join " ")
            startedUtc = $started.ToString("O")
            completedUtc = ([DateTimeOffset]::UtcNow).ToString("O")
            exitCode = $exitCode
            status = if ($exitCode -eq 0) { "Passed" } else { "Failed" }
            log = $logName
        })
        if ($exitCode -ne 0) { break }
    }

    $passed = $results.Count -eq $phases.Count -and @($results | Where-Object { $_.status -ne "Passed" }).Count -eq 0
    $evidence = [ordered]@{
        schemaVersion = "etl-sql.deployment-profile-certification/v1"
        runId = $runId
        commit = $commit
        dirty = $dirtyLines.Count -gt 0
        dirtyPaths = @($dirtyLines)
        kind = $laneKind
        lanes = @($laneNames)
        configuration = $Configuration
        startedBy = [Environment]::UserName
        machine = [Environment]::MachineName
        os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        result = if ($passed) { "Passed" } else { "Failed" }
        phases = $results.ToArray()
        uncovered = @()
    }
    $jsonPath = Join-Path $runRoot "certification.json"
    $evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding utf8

    $markdown = New-Object System.Collections.Generic.List[string]
    $markdown.Add("# Deployment-profile certification")
    $markdown.Add("")
    $markdown.Add("- Commit: ``$commit``")
    $markdown.Add("- Worktree dirty: ``$($evidence.dirty)``")
    $markdown.Add("- Lanes: $($laneNames -join ', ')")
    $markdown.Add("- Result: **$($evidence.result)**")
    $markdown.Add("")
    $markdown.Add("| Lane | Phase | Result | Evidence |")
    $markdown.Add("| :--- | :--- | :--- | :--- |")
    foreach ($result in $results) {
        $markdown.Add("| $($result.lane) | $($result.phase) | $($result.status) | [$($result.log)]($($result.log)) |")
    }
    $markdown.Add("")
    $markdown.Add("## Uncovered")
    $markdown.Add("")
    $uncoveredText = if ($evidence.uncovered.Count -eq 0) { "None for the selected lane contract." } else { $evidence.uncovered -join "`n" }
    $markdown.Add($uncoveredText)
    $markdown | Set-Content -LiteralPath (Join-Path $runRoot "certification.md") -Encoding utf8

    Write-Host "Evidence: $runRoot" -ForegroundColor Cyan
    if (-not $passed) { exit 1 }
}
finally {
    Pop-Location
}
