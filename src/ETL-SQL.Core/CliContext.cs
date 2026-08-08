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

    // admin restore command
    public string? RestoreFrom { get; set; }
    public string? RestoreKeys { get; set; }
    public string? RestoreTo { get; set; }
    public string? RestoreReport { get; set; }
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
    public string? SaasTenantId { get; set; }
    public string? SaasSourceProfile { get; set; }
    public string? SaasPortalBootstrap { get; set; }
    public string? SaasOutputRoot { get; set; }
    public int SaasMaxConcurrentJobs { get; set; } = 4;
    public int SaasMaxStorageMb { get; set; } = 10_240;
    public int SaasMaxReportSessions { get; set; } = 20;

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

    // enterprise enrollment commands
    public string? EnterpriseTenant { get; set; }
    public string? EnterprisePolicyEndpoint { get; set; }
    public string? EnterpriseSigningKeyPath { get; set; }
    public string? EnterpriseClientCertificateThumbprint { get; set; }
    public string? EnterpriseServiceIdentity { get; set; }
    public int EnterpriseMaxOfflineHours { get; set; } = 24;
    public bool EnterpriseAllowOfflineFailure { get; set; }
    public bool EnterpriseConfirm { get; set; }
}
