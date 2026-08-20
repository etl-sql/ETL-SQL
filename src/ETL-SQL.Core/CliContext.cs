using System.Collections.Generic;
using System.IO;

namespace ETL_SQL.Core;
/// <summary>
/// Carries the parsed CLI arguments and settings for the current command invocation.
/// Lives in Core so both App (headless executor) and TUI can share the same type
/// without creating a circular project dependency.
/// </summary>
public class CliContext
{
    /// <summary>
    /// A shallow copy, for callers that drive one command repeatedly with different arguments — a
    /// fleet rollout applying the same cutover to each tenant in turn. Copying rather than mutating
    /// the caller's context keeps one deployment's arguments from leaking into the next.
    /// </summary>
    public CliContext Clone() => (CliContext)MemberwiseClone();

    public string Command { get; set; } = "run";
    public FileInfo? ScriptFile { get; set; }
    public bool IsPerfMode { get; set; }
    public int BatchSize { get; set; }
    public bool IsGenerateMode => Command == "generate";
    public bool IsLogMode { get; set; }
    public string? LogPath { get; set; }
    public bool IsSilentMode { get; set; }
    public string? UiMode { get; set; }
    public int EstimatedRows { get; set; }
    public bool IsVerbose { get; set; }
    public string? TestVal { get; set; }
    public bool IsTestMode => Command == "test";
    public string? PreviewVal { get; set; }
    public string? DocsVal { get; set; }
    public string? Password { get; set; }
    public string? EncryptValue { get; set; }
    public bool IsJsonMode { get; set; }
    public bool EnablePaging { get; set; }
    public bool DisplayProgress { get; set; }
    public bool QualitySummary { get; set; }
    public string? OutputJsonPath { get; set; }
    public string? ScanSource { get; set; }
    public string? ScanTable { get; set; }
    public bool ScanPii { get; set; }
    public string SessionId { get; set; } = System.Guid.NewGuid().ToString("N");
    public bool Resume { get; set; }
    public bool UpdateConfig { get; set; }

    /// <summary>
    /// Per-invocation override for whether this run is written to the job history and lineage
    /// catalog. <c>Engine:AuditAdHocRuns</c> is machine-wide, but one install serves both
    /// interactive development and a scheduled task — recording the 02:00 job should not mean
    /// recording every exploratory run. Null leaves the configured setting in charge.
    /// </summary>
    public bool? RecordRun { get; set; }

    // ── admin identity verbs ────────────────────────────────────────────────────
    /// <summary>Portal base URL for the admin CLI; falls back to ETLSQL_PORTAL_URL.</summary>
    public string? PortalUrl { get; set; }

    /// <summary>Service-account client id. An identifier, not a secret, so a flag is acceptable.</summary>
    public string? PortalClientId { get; set; }

    /// <summary>Substring filter for list verbs.</summary>
    public string? AdminFilter { get; set; }

    /// <summary>Role filter for <c>admin user list</c>.</summary>
    public string? AdminRole { get; set; }

    /// <summary>Include deactivated users in list output.</summary>
    public bool IncludeInactive { get; set; }

    /// <summary>Target user name for the verbs that take one.</summary>
    public string? AdminUsername { get; set; }

    /// <summary>Target group name for the verbs that take one.</summary>
    public string? AdminGroupName { get; set; }

    /// <summary>Email for <c>admin user create</c>.</summary>
    public string? AdminEmail { get; set; }

    /// <summary>Identity provider for <c>admin user create</c> (Local, LDAP).</summary>
    public string? AdminProvider { get; set; }

    /// <summary>Description for <c>admin group create</c>.</summary>
    public string? AdminDescription { get; set; }

    /// <summary>Read the new password from standard input. The only accepted source — never argv.</summary>
    public bool PasswordStdin { get; set; }

    /// <summary>Make a create a no-op when the record already exists, so a runbook re-run is safe.</summary>
    public bool IfNotExists { get; set; }

    /// <summary>Make a delete a no-op when the record is already gone.</summary>
    public bool IfExists { get; set; }

    /// <summary>Fail the write unless the record is still at this version.</summary>
    public long? IfVersion { get; set; }

    /// <summary>Given name for <c>admin user update</c>.</summary>
    public string? AdminFirstName { get; set; }

    /// <summary>Family name for <c>admin user update</c>.</summary>
    public string? AdminLastName { get; set; }

    /// <summary>Replacement name for <c>admin group update</c>, kept apart from the lookup name.</summary>
    public string? AdminNewName { get; set; }

    /// <summary>Studio capabilities for <c>admin group set-capabilities</c>. Replaces the grant wholesale.</summary>
    public List<string>? AdminCapabilities { get; set; }

    // ── admin service-account lifecycle ─────────────────────────────────────────
    public string? ServiceAccountName { get; set; }

    // ── Orchestrator object grants ───────────────────────────────────────────
    /// <summary>JOB, SCHEDULE, or NOTIFICATION.</summary>
    public string? GrantObjectKind { get; set; }
    /// <summary>The object's name, resolved in the caller's own tenant by the Orchestrator.</summary>
    public string? GrantObjectName { get; set; }
    /// <summary>USER, GROUP, or SERVICE.</summary>
    public string? GrantPrincipalKind { get; set; }
    /// <summary>The principal's stable key — not a username, which can be reassigned.</summary>
    public string? GrantPrincipalId { get; set; }
    /// <summary>READ, EXECUTE, OVERRIDE, or MANAGE.</summary>
    public string? GrantPermission { get; set; }
    public string? ServiceAccountOwner { get; set; }
    public string? ServiceAccountDescription { get; set; }
    public List<string>? ServiceAccountScopes { get; set; }
    public List<string>? ServiceAccountRoles { get; set; }
    public List<string>? ServiceAccountCapabilities { get; set; }
    public bool ServiceAccountClearCapabilities { get; set; }
    public string? ServiceAccountExpiresAt { get; set; }
    public bool ServiceAccountClearExpiry { get; set; }
    public bool ServiceAccountEnable { get; set; }
    public bool ServiceAccountDisable { get; set; }
    public string? ServiceAccountSecretOutput { get; set; }

    /// <summary>
    /// Stable identity for an unattended run. Without it the job name is the script's file name, so
    /// the same script under two schedules — or same-named scripts in different folders — collapse
    /// into one history identity that triage cannot tell apart. Null keeps the file-name default.
    /// </summary>
    public string? JobName { get; set; }
    public Dictionary<string, object?> Variables { get; } = new(System.StringComparer.OrdinalIgnoreCase);

    // serve command
    public string? ServeManifest { get; set; }
    public int? ServePort { get; set; }
    public bool ServeNoBrowser { get; set; }

    // doctor command
    public bool DoctorStrict { get; set; }
    public string DoctorProfile { get; set; } = "quick";

    // purge command
    public bool PurgeDryRun { get; set; }
    public bool PurgeYes { get; set; }

    // gen-script command
    public string? SpecSchema { get; set; }
    public string? SpecOutput { get; set; }

    // extract-spec command
    public string? ExtractInput { get; set; }
    public string? ExtractOutput { get; set; }

    // admin support-bundle command
    public string? BundleOutput { get; set; }

    // init command
    public string? InitDirectory { get; set; }
    public bool InitForce { get; set; }

    // admin backup command
    public string? BackupOutputDir { get; set; }
    public string? BackupTenantRoot { get; set; }

    // admin restore command
    public string? RestoreFrom { get; set; }
    public string? RestoreKeys { get; set; }
    public string? RestoreTo { get; set; }
    public string? RestoreReport { get; set; }
    public string? RestoreExpectedTenant { get; set; }
    public bool RestoreValidateOnly { get; set; }

    // admin migrate-database command
    public string? MigrateFrom { get; set; }
    public string? MigrateTo { get; set; }
    public bool MigrateDryRun { get; set; }

    // admin promotion preflight command
    public string? PromotionSource { get; set; }
    public string? PromotionFromProfile { get; set; }
    public string? PromotionToProfile { get; set; }
    public string? PromotionOutput { get; set; }
    public string? PromotionPackage { get; set; }
    public string[]? PromotionBindings { get; set; }
    public int PromotionHistoryLimit { get; set; } = 10_000;

    // Tenant portability bundle verbs (admin tenant ...). Key paths, never key material.
    public string? TenantBundleRoot { get; set; }
    public string? TenantOperatorKey { get; set; }
    public bool TenantRequireSignature { get; set; }
    public string[]? TenantBindings { get; set; }
    public string? TenantExportIdentity { get; set; }
    public string? TenantSourceProfile { get; set; }
    public string[]? TenantArtifactFiles { get; set; }
    public string? TenantArtifactRoot { get; set; }
    public string? TenantOrchestratorPackage { get; set; }
    public string? TenantOrchestratorAlias { get; set; }
    public string? TenantRecipientKey { get; set; }
    public string? TenantSigningKey { get; set; }
    public string? TenantCollisionPolicy { get; set; }
    public bool TenantDryRun { get; set; }
    public string? SaasTenantId { get; set; }
    public string? SaasSourceProfile { get; set; }
    public string? SaasPortalBootstrap { get; set; }
    public string? SaasOutputRoot { get; set; }
    public string? SaasOidcAuthority { get; set; }
    public string? SaasOidcClientId { get; set; }
    public int SaasMaxConcurrentJobs { get; set; } = 4;
    public int SaasMaxStorageMb { get; set; } = 10_240;
    public int SaasMaxReportSessions { get; set; } = 20;
    public string? SaasUpgradeTenantRoot { get; set; }
    public string? SaasUpgradeTargetRelease { get; set; }
    public int? SaasUpgradeMaxConcurrentJobs { get; set; }
    public int? SaasUpgradeMaxStorageMb { get; set; }
    public int? SaasUpgradeMaxReportSessions { get; set; }
    public bool SaasUpgradeExecute { get; set; }
    // admin promotion saas-fleet-plan
    public string? FleetTargetRelease { get; set; }
    public int FleetWaveSize { get; set; } = 5;
    public int FleetMaxFailures { get; set; }
    public string? FleetOperator { get; set; }
    public string? FleetAuthorizationReference { get; set; }
    public string? FleetReason { get; set; }
    public bool FleetExecute { get; set; }
    public string? FleetRoot { get; set; }
    public string? SaasDeletionTenantRoot { get; set; }
    public string? SaasDeletionReceiptRoot { get; set; }
    public bool SaasDeletionExecute { get; set; }

    // admin ha-soak commands
    public string? HaSoakRunId { get; set; }
    public string? HaSoakRunRoot { get; set; }
    public string? HaSoakOutputRoot { get; set; }
    public string? HaSoakOutputPath { get; set; }
    public string? HaSoakMode { get; set; }
    public string? HaSoakRequiredGate { get; set; }
    public string? HaSoakRequiredCommit { get; set; }
    public string? HaSoakMarkdownReport { get; set; }
    public string? HaSoakSustainedWorkloadPath { get; set; }
    public string? HaSoakPlanPath { get; set; }
    public string? HaSoakAdminPassword { get; set; }
    public string? HaSoakComposeFile { get; set; }
    public string? HaSoakEnvExample { get; set; }
    public string? HaSoakImageTag { get; set; }
    public int HaSoakPortalScale { get; set; } = 2;
    public int HaSoakOrchestratorScale { get; set; } = 2;
    public int HaSoakPortalPort { get; set; } = 5600;
    public int HaSoakOrchestratorPort { get; set; } = 5601;
    public int HaSoakPostgresPort { get; set; } = 5632;
    public int HaSoakLogTail { get; set; } = 500;
    public int HaSoakDurationSeconds { get; set; }
    public bool HaSoakStart { get; set; }
    public bool HaSoakPull { get; set; }
    public bool HaSoakValidateOnly { get; set; }
    public bool HaSoakAllowDirty { get; set; }
    public bool HaSoakNoDocker { get; set; }
    public bool HaSoakForce { get; set; }

    // admin secret lifecycle commands
    public string? SecretName { get; set; }
    public string? SecretValue { get; set; }

    // admin connection catalog commands
    public string? ConnectionAlias { get; set; }
    public string? ConnectionType { get; set; }
    public string? ConnectionTarget { get; set; }
    public string[]? ConnectionOptions { get; set; }
    public string[]? ConnectionSensitiveFields { get; set; }

    // admin tool catalog commands
    public string? ToolName { get; set; }
    public string? ToolType { get; set; }
    public string[]? ToolOptions { get; set; }

    // enterprise enrollment commands
    public string? EnterpriseTenant { get; set; }
    public string? EnterprisePolicyEndpoint { get; set; }
    public string? EnterpriseSigningKeyPath { get; set; }
    public string? EnterpriseClientCertificateThumbprint { get; set; }
    public string? EnterpriseServiceIdentity { get; set; }
    public int EnterpriseMaxOfflineHours { get; set; } = 24;
    public bool EnterpriseAllowOfflineFailure { get; set; }
    public bool EnterpriseConfirm { get; set; }

    // gateway commands
    public string? GatewayToken { get; set; }
    public string? GatewayTenantId { get; set; }
    public string? GatewayId { get; set; }
    public string? GatewayNodeId { get; set; }
    public bool GatewayInstallService { get; set; }
    public bool GatewayNonInteractive { get; set; }
    public string? GatewayResourceId { get; set; }
    public string? GatewayConnectorType { get; set; }
    public string? GatewayLocalTarget { get; set; }
    public string? GatewayCredentialReference { get; set; }
    public string? GatewayOperations { get; set; }
}
