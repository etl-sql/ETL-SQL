. (Join-Path $PSScriptRoot 'DeploymentCertification.Contracts.ps1')

$CoreTests = "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj"
$PortalTests = "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj"

function New-Phase {
    param([string]$Name, [string]$Project, [string]$Filter, [string]$Proof, [string]$Prerequisite)
    $phase = [ordered]@{ name = $Name; project = $Project; filter = $Filter; proof = $Proof }
    if (-not [string]::IsNullOrWhiteSpace($Prerequisite)) { $phase["prerequisite"] = $Prerequisite }
    $phase
}

function New-FaultPhase {
    param([string]$ProfileName)
    [ordered]@{
        name = "Provider-neutral fault matrix"
        faultProfile = $ProfileName
        proof = "Repeated provider-neutral faults prove fencing, visible outcomes, exactly-once committed results, and checkpoint-bounded recovery for this profile."
    }
}

# The eight Enterprise prerequisites a hosted (SaaS) deployment builds on, per the Progressive SaaS
# Phase A gate in TODO.md. The Enterprise profile lane must prove all of them in one run against one
# commit: a hosted claim assembled by correlating three lanes by hand is an inferred claim, which is
# exactly what this framework refuses elsewhere.
$EnterpriseHostedPrerequisites = @(
    "verifiable-caller-identity",
    "per-object-authorization",
    "shared-state-and-artifact-providers",
    "scoped-secret-and-policy-authority",
    "durable-audit",
    "high-availability",
    "backup-and-restore",
    "upgrade-and-promotion-evidence"
)

function Get-ProfilePhases {
    param([string]$Name)
    switch ($Name) {
        "Solo" {
            @(
                New-Phase "Solo contract and local policy" $CoreTests "FullyQualifiedName~DeploymentProfileContractTests|FullyQualifiedName~WorkspacePolicyDocumentTests" "Portable profile contract and source-controlled policy load locally."
                New-Phase "Solo quality gate and evidence" $CoreTests "FullyQualifiedName~QualityRunReporterTests|FullyQualifiedName~AssertJobRuntimeTests|FullyQualifiedName~WorkspacePolicyRequiredTagsRuleTests|FullyQualifiedName~WorkstationAutomation" "Local metadata and quality failures return non-zero gate semantics with versioned evidence."
                New-Phase "Solo schema stewardship" $CoreTests "FullyQualifiedName~PiiSchemaScannerTests|FullyQualifiedName~StewardshipScoringTests" "Schema-only PII/stewardship works without Portal or remote services."
                New-FaultPhase "Solo"
            )
        }
        "Team" {
            @(
                New-Phase "Team durable quality and catalog" $CoreTests "FullyQualifiedName~DataQualityMetricsPersistenceTests|FullyQualifiedName~OrchestratorPromotionPackageTests" "SQLite retains quality history, jobs, schedules, ownership, lineage, and tags."
                New-Phase "Team notification dispatch" $CoreTests "FullyQualifiedName~TeamSqliteBaselines_TriggerWebhookAndSmtpNotificationsWithoutPortal|FullyQualifiedName~SchedulerRetryTests|FullyQualifiedName~WebhookConnectorTests|FullyQualifiedName~JobFailureDigestTemplateTests" "SQLite quality baselines drive SMTP/Webhook notification paths and retries without Portal."
                New-Phase "Team Portal configuration" $PortalTests "FullyQualifiedName~ConfigurationRoundTripTests|FullyQualifiedName~ConfigurationPromotionValidationTests" "Optional single-node Portal configuration converges and collisions fail safely."
                New-Phase "Team scheduling and flight recorder" $PortalTests "FullyQualifiedName~JobSchedulingIntegrationTests|FullyQualifiedName~OperationsTriageTests" "The single-node Team providers schedule jobs and expose bounded operator triage without a profile-specific runtime."
                New-Phase "Team common implementation boundary" $CoreTests "FullyQualifiedName~TeamProfileImplementationBoundaryTests|FullyQualifiedName~StatementMetricsPayloadTests" "Team uses the common parser, evaluator, connectors, catalog, UI, checkpoint, promotion, and statement-metrics implementations through single-node provider configuration."
                New-FaultPhase "Team"
            )
        }
        "Enterprise" {
            @(
                New-Phase "Enterprise verifiable caller identity" $PortalTests "FullyQualifiedName~OidcAuthTests|FullyQualifiedName~OrchestratorIdentityAssertionTests|FullyQualifiedName~OrchestratorJobChannelIdentityTests" "Federated OIDC identity and short-lived signed Orchestrator assertions carry a verifiable principal across the service boundary; an unsigned actor header carries no authority, and the Portal's own outbound calls carry a signed caller rather than the service key alone." "verifiable-caller-identity"
                New-Phase "Enterprise per-object authorization" $PortalTests "FullyQualifiedName~OrchestratorPerObjectAuthorizationIntegrationTests|FullyQualifiedName~OrchestratorAuditParityTests|FullyQualifiedName~OrchestratorScopeCeilingTests" "Reaching an Orchestrator does not confer authority over another principal's job, schedule, or notification, CREATE OR ALTER cannot silently take over a shared name, every mutation verb leaves an audit record naming the acting principal rather than the service that carried the call, and a narrowly scoped service token is narrow at both the HTTP and the statement door." "per-object-authorization"
                New-Phase "Enterprise shared Portal state provider" $PortalTests "FullyQualifiedName~PortalPostgresProviderTests|FullyQualifiedName~PortalMultiProcessPostgresTests" "Portal state resolves against shared PostgreSQL across multiple processes rather than node-local storage." "shared-state-and-artifact-providers"
                New-Phase "Enterprise shared Orchestrator store and artifact roots" $CoreTests "FullyQualifiedName~OrchestratorPostgresStoreTests|FullyQualifiedName~FileSystemArtifactStorageTests|FullyQualifiedName~GuardedArtifactStorageTests" "Orchestrator durable state uses the shared PostgreSQL dialect, and artifact storage honours its guarded contract on a shared root." "shared-state-and-artifact-providers"
                New-Phase "Enterprise policy authority" $CoreTests "FullyQualifiedName~EnterprisePolicyRuntimeTests|FullyQualifiedName~OrganizationPolicySchemaTests|FullyQualifiedName~PolicyAuthorityServiceTests" "Typed organization policy and authority boundaries are enforced." "scoped-secret-and-policy-authority"
                New-Phase "Enterprise secret authority and publishing policy" $PortalTests "FullyQualifiedName~PortalSecretStoreServiceTests|FullyQualifiedName~PortalSecretsApiTests|FullyQualifiedName~KeyAndCredentialPostureTests|FullyQualifiedName~ReportPublishingPolicyTests|FullyQualifiedName~PortalInteractiveRunPolicyTests|FullyQualifiedName~PolicyDistributionApiTests" "The encrypted, audited, RBAC'd catalog secret store and signed organization metadata policy guard Portal execution and publishing." "scoped-secret-and-policy-authority"
                New-Phase "Enterprise durable audit" $PortalTests "FullyQualifiedName~AuditOutboxTransportTests|FullyQualifiedName~AuditRedactionTests|FullyQualifiedName~AuditCollectorHealthTests|FullyQualifiedName~GovernanceRecoveryCertificationTests" "The durable remote audit outbox retains, redacts, and recovers mutation records rather than dropping them when the collector is unreachable." "durable-audit"
                New-Phase "Enterprise HA fencing" $CoreTests "FullyQualifiedName~FencingTokenTests|FullyQualifiedName~WriteEpochFencingTests" "Database leases and write epochs fence stale owners." "high-availability"
                New-Phase "Enterprise Portal backup and restore" $PortalTests "FullyQualifiedName~BackupRestoreDrillTests" "A coordinated Portal backup restores to a usable state rather than a partially converged one." "backup-and-restore"
                New-Phase "Enterprise engine backup and restore" $CoreTests "FullyQualifiedName~BackupRestoreServiceTests" "Engine-side backup and restore round-trips durable state and validates before claiming success." "backup-and-restore"
                New-Phase "Enterprise upgrade lifecycle" $CoreTests "FullyQualifiedName~DeploymentProfileUpgradeLifecycleTests.Enterprise" "The Enterprise profile executes backup/export, scheduler fencing, cutover proof, and a scheduler-safe rollback point across N to N+1." "upgrade-and-promotion-evidence"
                New-Phase "Enterprise configuration promotion" $PortalTests "FullyQualifiedName~ConfigurationPromotionTests|FullyQualifiedName~ConfigurationPromotionValidationTests" "Target bindings and catalog ownership survive promotion, and collisions fail before mutation." "upgrade-and-promotion-evidence"
                New-FaultPhase "Enterprise"
            )
        }
        "SaaS" {
            @(
                New-Phase "SaaS tenant onboarding and isolation" $CoreTests "FullyQualifiedName~SaasTenantOnboardingTests" "Host-fixed tenant boundaries isolate stores, artifacts, keys, caches, queues, namespaces, and limits."
                New-Phase "SaaS import safety" $CoreTests "FullyQualifiedName~OrchestratorPromotionPackageTests|FullyQualifiedName~DeploymentPromotionPreflightTests" "Portable state imports idempotently without resolved secret material."
                New-Phase "SaaS Portal runtime isolation" $PortalTests "FullyQualifiedName~SaasTenantRuntimeIsolationTests|FullyQualifiedName~ConfigurationPromotionValidationTests|FullyQualifiedName~ConfigurationExportSecretExclusionTests" "Host-fixed Portal databases, audit outboxes, and security caches reject cross-tenant state while bootstrap validation excludes secrets."
                New-FaultPhase "SaaS"
            )
        }
        "SharedSaaS" {
            @(
                New-Phase "Shared tenant database, artifact, cache, and queue isolation" $PortalTests "FullyQualifiedName~SharedTenantStoreIsolationTests|FullyQualifiedName~SharedDatasetTenantIsolationTests|FullyQualifiedName~PortalTenantArtifactStorageTests|FullyQualifiedName~SharedTenantLifecycleEndpointTests" "Hostile tenant identifiers cannot cross shared durable-state, dataset, artifact, or admission boundaries."
                New-Phase "Shared audit, PII, lineage, and quality isolation" $PortalTests "FullyQualifiedName~SharedAuditTenantIsolationTests|FullyQualifiedName~SharedLineageTenantIsolationTests|FullyQualifiedName~SharedStewardshipTenantIsolationTests|FullyQualifiedName~SharedLineageEndpointTests" "Audit, PII stewardship, lineage, and quality evidence remain tenant-scoped in shared stores and APIs."
                New-Phase "Shared path, key, and checkpoint isolation" $CoreTests "FullyQualifiedName~TenantStorageCapabilityTests|FullyQualifiedName~KeyMaterialContractTests|FullyQualifiedName~NamedCheckpointResumeTests|FullyQualifiedName~TenantScopedArtifactStorageTests" "Server-derived tenant capabilities fence paths, key namespaces, artifacts, and resumable checkpoints."
                New-Phase "Shared Gateway and network authority" $CoreTests "FullyQualifiedName~GatewayEnrollmentAndAuthorityTests|FullyQualifiedName~GatewayBindingTests|FullyQualifiedName~GatewayOperationProtocolTests|FullyQualifiedName~GatewayTransportTests" "Gateway identity, binding, grants, typed operations, replay decisions, and outbound transport reject cross-tenant authority."
                New-Phase "Shared sandbox and resource-exhaustion policy contracts" $CoreTests "FullyQualifiedName~SandboxExecutionCoordinatorTests|FullyQualifiedName~SandboxWorkloadPolicyResolverTests|FullyQualifiedName~SandboxAdmissionControllerTests|FullyQualifiedName~SaasFleetRolloutSequencerTests" "Deterministic adapters prove hardened placement policy, tenant admission, resource bounds, and fleet drain contracts. Live hardened-runtime containment requires separate evidence."
                New-Phase "Shared quota, telemetry, and metering isolation" $PortalTests "FullyQualifiedName~TenantExecutionQuotaTests|FullyQualifiedName~TenantMeteringLedgerTests|FullyQualifiedName~TenantUsageStoreTests|FullyQualifiedName~SharedTenantMeteringIntegrationTests" "Noisy-neighbor admission and tenant-attributed telemetry remain bounded and partitioned."
                New-Phase "Shared support and restore isolation" $PortalTests "FullyQualifiedName~SupportAccessApprovalServiceTests|FullyQualifiedName~SupportBundleTests|FullyQualifiedName~SharedBackupAndRecoveryTests|FullyQualifiedName~SharedBackupSurfaceInventoryTests" "Support access is approved and audited, while backup/restore inventories retain every shared tenant surface."
                New-Phase "Shared identity boundary" $PortalTests "FullyQualifiedName~SharedTenantHttpBoundaryTests|FullyQualifiedName~SharedTenantCredentialBindingTests|FullyQualifiedName~SharedOidcAuthTests|FullyQualifiedName~SharedIdentityPartitionStoreTests" "Only verified credentials derive tenant context and shared identity state remains partitioned."
                New-FaultPhase "SharedSaaS"
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
        "SaaSToEnterpriseExit" {
            @(
                New-Phase "SaaS to Enterprise exit journey" $CoreTests "FullyQualifiedName~TenantExitJourneyTests" "A tenant leaves SaaS with a signed, tenant-encrypted bundle that verifies and decrypts with no contact with the source operator, and target preflight states every binding the target owes before anything mutates."
                New-Phase "Portability bundle contract" $CoreTests "FullyQualifiedName~TenantBundleTests|FullyQualifiedName~TenantBundleComposerTests|FullyQualifiedName~TenantPortabilityInspectorTests|FullyQualifiedName~TenantBundleImporterTests" "Bundle format, composition, non-mutating preflight, and import hold: tamper, truncation, path-escape, unencrypted-SaaS refusal, collision refusal, and import leaving executing objects disabled."
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


function Get-ExpectedScenarioIds {
    param([string[]]$Names)
    $expected = New-Object System.Collections.Generic.List[string]
    foreach ($name in $Names) {
        switch ($name) {
            # The Enterprise lane runs the Enterprise upgrade lifecycle for its
            # upgrade-and-promotion-evidence prerequisite, so it must produce that concrete scenario
            # evidence rather than only a green phase.
            "Enterprise" { $expected.Add("EnterpriseUpgrade") }
            "SaaS" { $expected.Add("SaaSManagedDedicatedIsolation") }
            "SoloToTeam" { $expected.Add("SoloToTeam") }
            "TeamToEnterprise" { $expected.Add("TeamToEnterprise") }
            "EnterpriseToSaaS" { $expected.Add("EnterpriseToSaaS") }
            "SoloToSaaS" { $expected.Add("SoloToSaaS") }
            "SaaSToEnterpriseExit" { $expected.Add("SaaSToEnterpriseExit") }
            "Upgrade" {
                @("SoloUpgrade", "TeamUpgrade", "EnterpriseUpgrade", "SaaSUpgrade") |
                    ForEach-Object { $expected.Add($_) }
            }
        }
    }
    @($expected | Select-Object -Unique)
}

function Get-DeploymentCertificationPlan {
    param([Parameter(Mandatory)][ValidateSet('Profile', 'Transition')][string]$Kind, [Parameter(Mandatory)][string[]]$Names)
    $phases = New-Object System.Collections.Generic.List[object]
    $journeyContract = New-Phase "Deployment journey fixture contract" $CoreTests "FullyQualifiedName~DeploymentProfileJourneyFixtureTests" "Required journeys declare positive, negative, portability, host-ownership, and continuity proof."
    $journeyContract["lane"] = "Contract"
    $phases.Add($journeyContract)
    foreach ($lane in $Names) {
        $items = if ($Kind -eq "Profile") { Get-ProfilePhases $lane } else { Get-TransitionPhases $lane }
        foreach ($item in $items) {
            $item["lane"] = $lane
            $phases.Add($item)
        }
    }
    return $phases.ToArray()
}

function Get-DeploymentPhaseLogName {
    param([string]$Lane, [string]$Name)
    return (($Lane + '-' + $Name) -replace '[^A-Za-z0-9._-]', '-').ToLowerInvariant() + '.log'
}

