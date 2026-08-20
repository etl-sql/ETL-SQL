using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using Spectre.Console;

namespace ETL_SQL.App
{
    public class CliOrchestrator
    {
        // Define options at class level to avoid null lookups in Dispatch
        private static readonly Option<int> BatchSizeOption = new("--batch-size", new[] { "-b" })
        {
            Description = "The size of data chunks to process in memory.",
            DefaultValueFactory = _ => 10000
        };
        private static readonly Option<bool> PerfOption = new("--perf", new[] { "-p" })
        {
            Description = "Display performance metrics after execution."
        };
        private static readonly Option<bool> VerboseOption = new("--verbose", new[] { "-v" })
        {
            Description = "Print detailed execution tracking."
        };
        private static readonly Option<string?> LogOption = new("--log", new[] { "-l" })
        {
            Description = "Enable logging. Optional: specify path/directory.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<bool> SilentOption = new("--silent", new[] { "-s" })
        {
            Description = "Remove all console messages."
        };
        private static readonly Option<string?> PreviewOption = new("--preview", new[] { "-pr" })
        {
            Description = "Preview top N results (e.g. 20, 100, *)",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<int> EstimateOption = new("--estimate", new[] { "-e" })
        {
            Description = "Estimated total rows for progress UI.",
            DefaultValueFactory = _ => 1000000
        };
        private static readonly Option<string> PassOption = new("--pass", Array.Empty<string>())
        {
            Description = "Master password for encryption."
        };
        private static readonly Option<bool> JsonOption = new("--json", Array.Empty<string>())
        {
            Description = "Output results and messages in structured JSON format."
        };
        private static readonly Option<bool> QualitySummaryOption = new("--quality-summary", Array.Empty<string>())
        {
            Description = "Print a counts-only data-quality summary after execution."
        };
        private static readonly Argument<string?> ScanSourceArg = new("source")
        {
            Description = "Local file/directory or SHARED: connection alias to inspect (default: current directory).",
            Arity = ArgumentArity.ZeroOrOne
        };
        private static readonly Option<bool> ScanPiiOption = new("--pii", Array.Empty<string>())
        {
            Description = "Suggest protected-data tags from schema names and etlsql-policy.json."
        };
        private static readonly Option<string?> ScanTableOption = new("--table", Array.Empty<string>())
        {
            Description = "Database table whose schema should be inspected when source is SHARED:alias.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> OutputJsonOption = new("--output-json", Array.Empty<string>())
        {
            Description = "Write versioned, counts-only run evidence to the specified JSON file.",
            Arity = ArgumentArity.ExactlyOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<bool> PageOption = new("--page", new[] { "-pa" })
        {
            Description = "Pause and page between multiple result sets in the console."
        };
        private static readonly Option<string?> SessionOption = new("--session", Array.Empty<string>())
        {
            Description = "Enable session persistence with the specified session ID.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string[]> VarOption = new("--var", new[] { "-d" })
        {
            Description = "Inject a variable into the script (e.g. @Name=Value).",
            AllowMultipleArgumentsPerToken = true
        };
        private static readonly Option<bool> ProgressOption = new("--progress", new[] { "-g" })
        {
            Description = "Display real-time graphical execution progress."
        };
        private static readonly Option<bool> ResumeOption = new("--resume", Array.Empty<string>())
        {
            Description = "Resume execution of a persistent session from the last successfully completed checkpoint."
        };
        // Identity administration. The client id is an identifier, so a flag is fine; the client
        // SECRET is deliberately absent — it comes only from the environment or a SECRET: reference,
        // because argv is visible to every process and captured by CI logs.
        private static readonly Option<string?> PortalUrlOption = new("--portal-url", Array.Empty<string>())
        {
            Description = "Portal base URL. Defaults to the ETLSQL_PORTAL_URL environment variable.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> PortalClientIdOption = new("--client-id", Array.Empty<string>())
        {
            Description = "Service-account client id. Defaults to ETLSQL_PORTAL_CLIENT_ID.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> AdminFilterOption = new("--filter", Array.Empty<string>())
        {
            Description = "Case-insensitive substring filter on the name.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> AdminRoleOption = new("--role", Array.Empty<string>())
        {
            Description = "Only list users holding this role.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<bool> IncludeInactiveOption = new("--include-inactive", Array.Empty<string>())
        {
            Description = "Include deactivated users."
        };
        private static readonly Option<string?> AdminUsernameOption = new("--username", Array.Empty<string>())
        {
            Description = "Target user name.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> AdminGroupNameOption = new("--name", Array.Empty<string>())
        {
            Description = "Target group name.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> AdminAssignRoleOption = new("--role", Array.Empty<string>())
        {
            Description = "Role to assign to the new user.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> AdminFirstNameOption = new("--first-name", Array.Empty<string>())
        {
            Description = "Given name.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> AdminLastNameOption = new("--last-name", Array.Empty<string>())
        {
            Description = "Family name.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> AdminNewNameOption = new("--new-name", Array.Empty<string>())
        {
            Description = "Replacement group name.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string[]> AdminCapabilityOption = new("--capability", Array.Empty<string>())
        {
            Description = "Studio capability to grant. Repeatable. Replaces the group's whole grant.",
            AllowMultipleArgumentsPerToken = false
        };
        // ── Orchestrator object grants ───────────────────────────────────────
        private static readonly Option<string?> GrantKindOption = new("--kind", Array.Empty<string>())
        {
            Description = "Object kind: JOB, SCHEDULE, or NOTIFICATION.",
            DefaultValueFactory = _ => null
        };

        private static readonly Option<string?> GrantObjectOption = new("--object", Array.Empty<string>())
        {
            Description = "Object name, resolved in your own tenant.",
            DefaultValueFactory = _ => null
        };

        private static readonly Option<string?> GrantPrincipalKindOption = new("--principal-kind", Array.Empty<string>())
        {
            Description = "Principal kind: USER, GROUP, or SERVICE.",
            DefaultValueFactory = _ => null
        };

        private static readonly Option<string?> GrantPrincipalOption = new("--principal", Array.Empty<string>())
        {
            Description = "Principal key. The stable identifier, not a username — a username can be reassigned.",
            DefaultValueFactory = _ => null
        };

        private static readonly Option<string?> GrantPermissionOption = new("--permission", Array.Empty<string>())
        {
            Description = "READ, EXECUTE, OVERRIDE, or MANAGE.",
            DefaultValueFactory = _ => null
        };

        private static readonly Option<string?> ServiceAccountNameOption = new("--name", Array.Empty<string>())
        {
            Description = "Service-account name.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> ServiceAccountOwnerOption = new("--owner", Array.Empty<string>())
        {
            Description = "Portal username that owns the service account.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> ServiceAccountDescriptionOption = new("--description", Array.Empty<string>())
        {
            Description = "Service-account description.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string[]> ServiceAccountScopeOption = new("--scope", Array.Empty<string>())
        {
            Description = "Scope to grant. Repeatable; on update, replaces the whole scope set.",
            AllowMultipleArgumentsPerToken = false
        };
        private static readonly Option<string[]> ServiceAccountRoleOption = new("--role", Array.Empty<string>())
        {
            Description = "Role to grant. Repeatable and accepted only when creating the account.",
            AllowMultipleArgumentsPerToken = false
        };
        private static readonly Option<string[]> ServiceAccountCapabilityOption = new("--capability", Array.Empty<string>())
        {
            Description = "Studio capability to grant. Repeatable; on update, replaces the whole grant.",
            AllowMultipleArgumentsPerToken = false
        };
        private static readonly Option<bool> ServiceAccountClearCapabilitiesOption = new("--clear-capabilities", Array.Empty<string>())
        {
            Description = "Remove every Studio capability. Mutually exclusive with --capability."
        };
        private static readonly Option<string?> ServiceAccountExpiresOption = new("--expires-at", Array.Empty<string>())
        {
            Description = "UTC expiry as an ISO-8601 timestamp.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<bool> ServiceAccountClearExpiryOption = new("--clear-expiry", Array.Empty<string>())
        {
            Description = "Remove the account expiry. Mutually exclusive with --expires-at."
        };
        private static readonly Option<bool> ServiceAccountEnableOption = new("--enable", Array.Empty<string>())
        {
            Description = "Enable the account. Mutually exclusive with --disable."
        };
        private static readonly Option<bool> ServiceAccountDisableOption = new("--disable", Array.Empty<string>())
        {
            Description = "Disable the account without revoking it. Mutually exclusive with --enable."
        };
        private static readonly Option<string?> ServiceAccountSecretOutputOption = new("--secret-out", Array.Empty<string>())
        {
            Description = "New file that receives the one-time secret. The secret is never printed.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> AdminEmailOption = new("--email", Array.Empty<string>())
        {
            Description = "Email address for the new user.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> AdminProviderOption = new("--provider", Array.Empty<string>())
        {
            Description = "Identity provider for the new user (Local or LDAP).",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> AdminDescriptionOption = new("--description", Array.Empty<string>())
        {
            Description = "Description for the new group.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<bool> PasswordStdinOption = new("--password-stdin", Array.Empty<string>())
        {
            Description = "Read the password from standard input. Passwords are never accepted as arguments."
        };
        private static readonly Option<bool> IfNotExistsOption = new("--if-not-exists", Array.Empty<string>())
        {
            Description = "Succeed without changes when the record already exists, so a re-run is a no-op."
        };
        private static readonly Option<bool> IfExistsOption = new("--if-exists", Array.Empty<string>())
        {
            Description = "Succeed without changes when the record is already absent."
        };
        private static readonly Option<long?> IfVersionOption = new("--if-version", Array.Empty<string>())
        {
            Description = "Fail unless the record is still at this version. Guards against a concurrent edit.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<bool> RecordOption = new("--record", Array.Empty<string>())
        {
            Description = "Record this run in the job history and lineage catalog, overriding Engine:AuditAdHocRuns."
        };
        private static readonly Option<bool> NoRecordOption = new("--no-record", Array.Empty<string>())
        {
            Description = "Do not record this run, overriding Engine:AuditAdHocRuns."
        };
        private static readonly Option<string?> JobNameOption = new("--job-name", Array.Empty<string>())
        {
            Description = "Identity to record this run under. Defaults to the script's file name.",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<bool> UpdateJwtOption = new("--update", Array.Empty<string>())
        {
            Description = "Update the local appsettings.json file with the new secret."
        };

        private static readonly Argument<string> RunScriptArg = new("script")
        {
            Description = "The ETL-SQL script to execute."
        };
        private static readonly Argument<string> EncryptValueArg = new("value")
        {
            Description = "The string to encrypt."
        };
        private static readonly Argument<string?> TestValArg = new("target")
        {
            Description = "Test file, directory, or pattern to execute (e.g. tests/, *.test.etlsql).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };

        private static readonly Argument<string?> ServeScriptArg = new("script")
        {
            Description = "The .rptsql file to serve (omit if using --manifest)",
            Arity = ArgumentArity.ZeroOrOne
        };
        private static readonly Option<string?> ServeManifestOption = new("--manifest", new[] { "-m" })
        {
            Description = "Serve multiple reports defined in a JSON manifest",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<int?> ServePortOption = new("--port", new[] { "-p" })
        {
            Description = "Port to listen on (default: auto-assigned ephemeral port)",
            DefaultValueFactory = _ => null
        };
        private static readonly Option<bool> ServeNoBrowserOption = new("--no-browser", Array.Empty<string>())
        {
            Description = "Do not automatically open the browser on start"
        };
        private static readonly Option<bool> DoctorStrictOption = new("--strict", Array.Empty<string>())
        {
            Description = "Exit with code 1 if any check produces a WARN or FAIL result."
        };
        private static readonly Option<string> DoctorProfileOption = new("--profile", Array.Empty<string>())
        {
            Description = "Check depth: 'quick' (fast local checks) or 'full' (adds engine, report, asset, runtime, and configured service probes).",
            DefaultValueFactory = _ => "quick"
        };
        private static readonly Option<bool> PurgeDryRunOption = new("--dry-run", Array.Empty<string>())
        {
            Description = "List the data that would be removed without deleting anything."
        };
        private static readonly Option<bool> PurgeYesOption = new("--yes", new[] { "-y" })
        {
            Description = "Skip the confirmation prompt (for scripts and installers)."
        };
        private static readonly Option<string?> SpecSchemaOption = new("--schema", new[] { "-s" })
        {
            Description = "Path to the input JSON schema specification file.",
            Arity = ArgumentArity.ExactlyOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> SpecOutputOption = new("--output", new[] { "-o" })
        {
            Description = "Destination path for the generated ETL-SQL script.",
            Arity = ArgumentArity.ExactlyOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> ExtractInputOption = new("--input", new[] { "-i" })
        {
            Description = "Path to the input large PDF specification file.",
            Arity = ArgumentArity.ExactlyOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> ExtractOutputOption = new("--output", new[] { "-o" })
        {
            Description = "Destination path for the extracted trimmed PDF file.",
            Arity = ArgumentArity.ExactlyOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> BundleOutputOption = new("--output", new[] { "-o" })
        {
            Description = "Destination path for the support bundle archive (default: timestamped .zip in the working directory).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Argument<string?> InitDirectoryArg = new("directory")
        {
            Description = "Target directory to scaffold into (default: current directory).",
            Arity = ArgumentArity.ZeroOrOne
        };
        private static readonly Option<bool> InitForceOption = new("--force", new[] { "-f" })
        {
            Description = "Overwrite existing files if they are already present."
        };
        private static readonly Option<string?> BackupOutputDirOption = new("--output-dir", new[] { "-o" })
        {
            Description = "Directory to write the backup archives into (default: current directory).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> BackupTenantRootOption = new("--tenant-root", Array.Empty<string>())
        {
            Description = "Host-fixed Managed Dedicated tenant boundary to back up.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> RestoreFromOption = new("--from", Array.Empty<string>())
        {
            Description = "Path to the data backup archive (etl-sql-backup-*.zip).",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> RestoreKeysOption = new("--keys", Array.Empty<string>())
        {
            Description = "Path to the matching keys archive (etl-sql-keys-*.zip).",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> RestoreToOption = new("--to", Array.Empty<string>())
        {
            Description = "Target directory to restore into (required unless --validate).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<bool> RestoreValidateOption = new("--validate", Array.Empty<string>())
        {
            Description = "Verify catalog and key versions and archive integrity without writing any files."
        };
        private static readonly Option<string?> RestoreReportOption = new("--report", Array.Empty<string>())
        {
            Description = "Write a machine-readable JSON recovery report to this path.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> RestoreExpectedTenantOption = new("--expected-tenant", Array.Empty<string>())
        {
            Description = "Required host-fixed tenant identity for a Managed Dedicated archive.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> SecretNameOption = new("--name", new[] { "-n" })
        {
            Description = "Name of the secret (letters, numbers, period, underscore, hyphen).",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> SecretValueOption = new("--value", Array.Empty<string>())
        {
            Description = "Secret value. Omit to enter it at a masked prompt or pipe it via stdin; --value can persist in shell history.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> ConnectionAliasOption = new("--alias", new[] { "-a" })
        {
            Description = "Catalog alias scripts reference as SHARED:alias (letters, numbers, period, underscore, hyphen).",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> ConnectionTypeOption = new("--type", new[] { "-t" })
        {
            Description = "Connector type of the shared connection (MSSQL, POSTGRES, S3, ...).",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> ConnectionTargetOption = new("--target", Array.Empty<string>())
        {
            Description = "Optional connection-string target. Credential fields must reference SECRET:name, never raw values.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string[]> ConnectionOptionOption = new("--option", Array.Empty<string>())
        {
            Description = "Connection option as KEY=VALUE (repeatable). Credential fields must reference SECRET:name.",
            AllowMultipleArgumentsPerToken = true
        };
        private static readonly Option<string[]> ConnectionSensitiveOption = new("--sensitive", Array.Empty<string>())
        {
            Description = "Field name this entry classifies as sensitive (repeatable): masked in displays and SECRET:-resolvable.",
            AllowMultipleArgumentsPerToken = true
        };
        private static readonly Option<string?> ToolNameOption = new("--name", new[] { "-n" })
        {
            Description = "Name of the tool.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> ToolTypeOption = new("--type", new[] { "-t" })
        {
            Description = "Type of the tool (e.g. EXECUTABLE).",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string[]> ToolOptionOption = new("--option", Array.Empty<string>())
        {
            Description = "Tool option as KEY=VALUE (repeatable).",
            AllowMultipleArgumentsPerToken = true
        };
        private static readonly Option<string?> MigrateFromOption = new("--from", Array.Empty<string>())
        {
            Description = "Source database provider (only 'sqlite' is supported).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "sqlite"
        };
        private static readonly Option<string?> MigrateToOption = new("--to", Array.Empty<string>())
        {
            Description = "Target database provider (only 'postgres' is supported).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "postgres"
        };
        private static readonly Option<bool> MigrateDryRunOption = new("--dry-run", Array.Empty<string>())
        {
            Description = "Verify counts and target schema compatibility without writing any data."
        };
        private static readonly Option<string?> PromotionSourceOption = new("--source", new[] { "-s" })
        {
            Description = "Workspace or export root to inventory (default: current directory).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> PromotionFromProfileOption = new("--from-profile", Array.Empty<string>())
        {
            Description = "Source deployment profile: Solo, Team, Enterprise, or SaaS.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> PromotionToProfileOption = new("--to-profile", Array.Empty<string>())
        {
            Description = "Target deployment profile: Solo, Team, Enterprise, or SaaS.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> PromotionOutputOption = new("--output", new[] { "-o" })
        {
            Description = "Destination for the versioned JSON inventory (default: deployment-preflight.json).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> PromotionPackageOption = new("--package", new[] { "-p" })
        {
            Description = "Path to a versioned Orchestrator promotion package.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string[]> PromotionBindingOption = new("--bind", Array.Empty<string>())
        {
            Description = "Target binding in SOURCE=TARGET form (repeatable).",
            AllowMultipleArgumentsPerToken = true
        };
        private static readonly Option<int> PromotionHistoryLimitOption = new("--history-limit", Array.Empty<string>())
        {
            Description = "Maximum quality-history and lineage records to export (default: 10000).",
            DefaultValueFactory = _ => 10_000
        };
        private static readonly Option<string?> TenantBundleOption = new("--bundle")
        {
            Description = "Path to the tenant portability bundle directory.",
        };
        private static readonly Option<string?> TenantOperatorKeyOption = new("--operator-key")
        {
            Description = "Published operator public key used to verify the bundle signature.",
        };
        private static readonly Option<bool> TenantRequireSignatureOption = new("--require-signature")
        {
            Description = "Fail unless the bundle carries a signature that verifies against --operator-key.",
        };
        private static readonly Option<string[]> TenantBindingOption = new("--binding")
        {
            Description = "Target binding as SOURCE=TARGET (repeatable); preflight also accepts a supplied logical id.",
            AllowMultipleArgumentsPerToken = true,
        };
        private static readonly Option<string?> TenantExportIdentityOption = new("--tenant")
        {
            Description = "Stable tenant export identity recorded in the bundle manifest."
        };
        private static readonly Option<string?> TenantSourceProfileOption = new("--source-profile")
        {
            Description = "Source profile: Solo, Team, Enterprise, or SaaS."
        };
        private static readonly Option<string[]> TenantArtifactOption = new("--artifact")
        {
            Description = "Portable source artifact to include (repeatable).",
            AllowMultipleArgumentsPerToken = true,
        };
        private static readonly Option<string?> TenantArtifactRootOption = new("--artifact-root")
        {
            Description = "Root used to preserve relative artifact paths."
        };
        private static readonly Option<string?> TenantOrchestratorPackageOption = new("--orchestrator-package")
        {
            Description = "Optional existing Orchestrator promotion package to include."
        };
        private static readonly Option<string?> TenantOrchestratorAliasOption = new("--orchestrator-alias")
        {
            Description = "Portal Orchestrator alias recorded by configuration export."
        };
        private static readonly Option<string?> TenantRecipientKeyOption = new("--recipient-key")
        {
            Description = "Recipient public key for export or tenant private key for import."
        };
        private static readonly Option<string?> TenantSigningKeyOption = new("--signing-key")
        {
            Description = "Operator private key used to sign an exported bundle."
        };
        private static readonly Option<string?> TenantCollisionOption = new("--collision")
        {
            Description = "Import collision policy: fail (default) or proceed.",
            DefaultValueFactory = _ => "fail"
        };
        private static readonly Option<bool> TenantDryRunOption = new("--dry-run")
        {
            Description = "Compute and print the import plan without changing the target."
        };

        private static readonly Option<string?> SaasTenantOption = new("--tenant", Array.Empty<string>())
        {
            Description = "Tenant assertion; must match the active signed operation authorization.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> SaasSourceProfileOption = new("--source-profile", Array.Empty<string>())
        {
            Description = "Onboarding source profile: Solo or Enterprise.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> SaasPortalBootstrapOption = new("--portal-bootstrap", Array.Empty<string>())
        {
            Description = "Optional secret-free Portal configuration bootstrap to stage for tenant replay.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> SaasOutputRootOption = new("--output-root", Array.Empty<string>())
        {
            Description = "Deployment-plane root under which the isolated tenant boundary is created.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> SaasOidcAuthorityOption = new("--oidc-authority", Array.Empty<string>())
        {
            Description = "Tenant-owned OIDC issuer HTTPS authority. Must be paired with --oidc-client-id."
        };
        private static readonly Option<string?> SaasOidcClientIdOption = new("--oidc-client-id", Array.Empty<string>())
        {
            Description = "Tenant-owned OIDC client id. Its secret is injected at Portal__Identity__Oidc__ClientSecret."
        };
        private static readonly Option<int> SaasMaxConcurrentJobsOption = new("--max-concurrent-jobs", Array.Empty<string>())
        {
            Description = "Tenant concurrent-job limit.",
            DefaultValueFactory = _ => 4
        };
        private static readonly Option<int> SaasMaxStorageMbOption = new("--max-storage-mb", Array.Empty<string>())
        {
            Description = "Tenant storage limit in MiB.",
            DefaultValueFactory = _ => 10_240
        };
        private static readonly Option<int> SaasMaxReportSessionsOption = new("--max-report-sessions", Array.Empty<string>())
        {
            Description = "Tenant concurrent report-session limit.",
            DefaultValueFactory = _ => 20
        };
        private static readonly Option<string?> SaasUpgradeTenantRootOption = new("--tenant-root", Array.Empty<string>())
        {
            Description = "Provisioned Managed Dedicated tenant boundary to upgrade.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> FleetTargetReleaseOption = new("--target-release", Array.Empty<string>())
        {
            Description = "Release every eligible deployment is being rolled to.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<int> FleetWaveSizeOption = new("--wave-size", Array.Empty<string>())
        {
            Description = "Deployments per rollout wave, so a canary wave can be small.",
            DefaultValueFactory = _ => 5
        };
        private static readonly Option<string?> FleetOperatorOption = new("--operator", Array.Empty<string>())
        {
            Description = "Platform person or service enumerating the fleet. Never a tenant user.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> FleetAuthorizationReferenceOption = new("--authorization", Array.Empty<string>())
        {
            Description = "Change record or rollout ticket this enumeration hangs off.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> FleetReasonOption = new("--reason", Array.Empty<string>())
        {
            Description = "Why the fleet is being enumerated, so the access can be reviewed later.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<bool> FleetExecuteOption = new("--execute", Array.Empty<string>())
        {
            Description = "Walk each wave in order, cutting over every deployment the loaded signed authorization names."
        };
        private static readonly Option<string?> FleetRootOption = new("--fleet-root", Array.Empty<string>())
        {
            Description = "Root the deployments were onboarded under; each tenant occupies its own directory.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<int> FleetMaxFailuresOption = new("--max-failures", Array.Empty<string>())
        {
            Description = "Failed cutovers tolerated before the rollout stops opening waves.",
            DefaultValueFactory = _ => 0
        };
        private static readonly Option<string?> SaasUpgradeTargetReleaseOption = new("--target-release", Array.Empty<string>())
        {
            Description = "Release or immutable image digest assertion; must match signed upgrade authorization.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<int?> SaasUpgradeMaxConcurrentJobsOption = new("--max-concurrent-jobs", Array.Empty<string>())
        {
            Description = "Concurrent-job capacity assertion; must match signed upgrade authorization.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<int?> SaasUpgradeMaxStorageMbOption = new("--max-storage-mb", Array.Empty<string>())
        {
            Description = "Storage-capacity assertion in MiB; must match signed upgrade authorization.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<int?> SaasUpgradeMaxReportSessionsOption = new("--max-report-sessions", Array.Empty<string>())
        {
            Description = "Report-session capacity assertion; must match signed upgrade authorization.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<bool> SaasUpgradeExecuteOption = new("--execute", Array.Empty<string>())
        {
            Description = "Fence scheduling, drain durable admissions, and apply the authorized cutover."
        };
        private static readonly Option<string?> SaasDeletionTenantRootOption = new("--tenant-root", Array.Empty<string>())
        {
            Description = "Provisioned Managed Dedicated tenant boundary to delete.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> SaasDeletionReceiptRootOption = new("--receipt-root", Array.Empty<string>())
        {
            Description = "External durable directory for the deletion completion record.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<bool> SaasDeletionExecuteOption = new("--execute", Array.Empty<string>())
        {
            Description = "Perform deletion after signed authorization, retention, and legal-hold checks pass."
        };
        private static readonly Option<string?> HaSoakRunIdOption = new("--run-id", Array.Empty<string>())
        {
            Description = "Stable run identifier for generated HA soak topology artifacts.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> HaSoakRunRootOption = new("--run-root", new[] { "-r" })
        {
            Description = "Path to a generated HA soak topology run root.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> HaSoakOutputRootOption = new("--output-root", Array.Empty<string>())
        {
            Description = "Directory for generated HA soak runs or diagnostics.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> HaSoakOutputPathOption = new("--output", new[] { "-o" })
        {
            Description = "Destination file path for the generated HA soak artifact.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> HaSoakModeOption = new("--mode", Array.Empty<string>())
        {
            Description = "Plan depth: CiSmoke or ManualCertification.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "CiSmoke"
        };
        private static readonly Option<string?> HaSoakRequiredGateOption = new("--required-gate", Array.Empty<string>())
        {
            Description = "Evidence gate to validate: Sustained, LargeJob, FaultInjection, or All.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "Sustained"
        };
        private static readonly Option<string?> HaSoakRequiredCommitOption = new("--required-commit", Array.Empty<string>())
        {
            Description = "Source commit SHA required by topology metadata; defaults to current HEAD.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> HaSoakMarkdownReportOption = new("--markdown-report", Array.Empty<string>())
        {
            Description = "Optional path for the HA soak evidence validation Markdown summary.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> HaSoakSustainedWorkloadOption = new("--workload", Array.Empty<string>())
        {
            Description = "Path to the materialized sustained-workload JSON.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> HaSoakPlanOption = new("--plan", Array.Empty<string>())
        {
            Description = "Existing HA soak plan path to execute.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> HaSoakAdminPasswordOption = new("--admin-password", Array.Empty<string>())
        {
            Description = "Admin password to place in the local workload config; defaults to the generated run-root password.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> HaSoakComposeFileOption = new("--compose-file", Array.Empty<string>())
        {
            Description = "Docker Compose file used by the generated topology.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> HaSoakEnvExampleOption = new("--env-example", Array.Empty<string>())
        {
            Description = "Environment template used by the generated topology.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<string?> HaSoakImageTagOption = new("--image-tag", Array.Empty<string>())
        {
            Description = "Container image tag to use when preparing the topology.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null
        };
        private static readonly Option<int> HaSoakPortalScaleOption = new("--portal-scale", Array.Empty<string>())
        {
            Description = "Portal replica count for the HA soak topology.",
            DefaultValueFactory = _ => 2
        };
        private static readonly Option<int> HaSoakOrchestratorScaleOption = new("--orchestrator-scale", Array.Empty<string>())
        {
            Description = "Orchestrator replica count for the HA soak topology.",
            DefaultValueFactory = _ => 2
        };
        private static readonly Option<int> HaSoakPortalPortOption = new("--portal-port", Array.Empty<string>())
        {
            Description = "Host port for the HA soak load-balanced Portal endpoint.",
            DefaultValueFactory = _ => 5600
        };
        private static readonly Option<int> HaSoakOrchestratorPortOption = new("--orchestrator-port", Array.Empty<string>())
        {
            Description = "Host port for the HA soak Orchestrator endpoint.",
            DefaultValueFactory = _ => 5601
        };
        private static readonly Option<int> HaSoakPostgresPortOption = new("--postgres-port", Array.Empty<string>())
        {
            Description = "Host port for the HA soak PostgreSQL endpoint.",
            DefaultValueFactory = _ => 5632
        };
        private static readonly Option<int> HaSoakLogTailOption = new("--log-tail", Array.Empty<string>())
        {
            Description = "Number of Docker log lines per service to include in diagnostics.",
            DefaultValueFactory = _ => 500
        };
        private static readonly Option<int> HaSoakDurationSecondsOption = new("--duration-seconds", Array.Empty<string>())
        {
            Description = "Override runner duration in seconds for bounded local execution.",
            DefaultValueFactory = _ => 0
        };
        private static readonly Option<bool> HaSoakStartOption = new("--start", Array.Empty<string>())
        {
            Description = "Start the generated Docker topology after writing the environment files."
        };
        private static readonly Option<bool> HaSoakPullOption = new("--pull", Array.Empty<string>())
        {
            Description = "Pull container images before starting the generated topology."
        };
        private static readonly Option<bool> HaSoakValidateOnlyOption = new("--validate-only", Array.Empty<string>())
        {
            Description = "Validate the topology/script contract without writing runtime artifacts."
        };
        private static readonly Option<bool> HaSoakAllowDirtyOption = new("--allow-dirty", Array.Empty<string>())
        {
            Description = "Allow evidence validation while the current worktree has uncommitted changes."
        };
        private static readonly Option<bool> HaSoakNoDockerOption = new("--no-docker", Array.Empty<string>())
        {
            Description = "Skip Docker status/log capture when exporting diagnostics."
        };
        private static readonly Option<bool> HaSoakForceOption = new("--force", new[] { "-f" })
        {
            Description = "Overwrite existing generated HA soak artifacts."
        };
        private static readonly Option<string?> EnterpriseTenantOption = new("--tenant")
        {
            Description = "Enterprise tenant or environment identifier.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> EnterpriseEndpointOption = new("--policy-endpoint")
        {
            Description = "Authoritative HTTPS organization-policy endpoint.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> EnterpriseSigningKeyOption = new("--signing-key")
        {
            Description = "Path to the organization's RSA policy-signing public key in PEM format.",
            Arity = ArgumentArity.ExactlyOne
        };
        private static readonly Option<string?> EnterpriseCertificateOption = new("--client-certificate-thumbprint")
        {
            Description = "Optional SHA-1 or SHA-256 machine/client certificate thumbprint.",
            Arity = ArgumentArity.ZeroOrOne
        };
        private static readonly Option<string?> EnterpriseServiceIdentityOption = new("--service-identity")
        {
            Description = "Optional Windows service identity granted read access to enrollment.",
            Arity = ArgumentArity.ZeroOrOne
        };
        private static readonly Option<int> EnterpriseOfflineHoursOption = new("--max-offline-hours")
        {
            Description = "Maximum age of cached policy before secure startup fails (1-720).",
            DefaultValueFactory = _ => 24
        };
        private static readonly Option<bool> EnterpriseAllowOfflineFailureOption = new("--allow-offline-failure")
        {
            Description = "Record non-fail-closed policy availability behavior for non-production enrollment."
        };
        private static readonly Option<bool> EnterpriseConfirmOption = new("--yes", new[] { "-y" })
        {
            Description = "Confirm the destructive enterprise unenrollment operation."
        };

        private static readonly Option<string?> GatewayPortalOption = new("--portal")
        {
            Description = "Portal URL (e.g. https://portal.company.com)."
        };
        private static readonly Option<string?> GatewayTokenOption = new("--token")
        {
            Description = "One-time enrollment token issued by Portal."
        };
        private static readonly Option<string?> GatewayTenantOption = new("--tenant")
        {
            Description = "Tenant identifier from the Portal enrollment command."
        };
        private static readonly Option<string?> GatewayIdOption = new("--gateway-id")
        {
            Description = "Logical gateway or cluster ID."
        };
        private static readonly Option<string?> GatewayNodeIdOption = new("--node-id")
        {
            Description = "Node machine identifier (default: host machine name)."
        };
        private static readonly Option<bool> GatewayInstallServiceOption = new("--install-service")
        {
            Description = "Register as a background system service (Windows Service / systemd)."
        };
        private static readonly Option<bool> GatewayNonInteractiveOption = new("--non-interactive", new[] { "-y" })
        {
            Description = "Run in non-interactive mode without prompting."
        };
        private static readonly Option<string?> GatewayResourceIdOption = new("--resource-id") { Description = "Stable local resource ID." };
        private static readonly Option<string?> GatewayConnectorTypeOption = new("--connector") { Description = "Registered connector type." };
        private static readonly Option<string?> GatewayLocalTargetOption = new("--target") { Description = "Local connector target; use ${CREDENTIAL} for the resolved credential." };
        private static readonly Option<string?> GatewayCredentialOption = new("--credential-ref") { Description = "Local credential reference in ENV:name form." };
        private static readonly Option<string?> GatewayOperationsOption = new("--operations") { Description = "Comma-separated READ, WRITE, EXECUTE operation classes." };

        public static RootCommand BuildRootCommand(Func<CliContext, Task<int>> handler)
        {
            var rootCommand = new RootCommand("ETL-SQL Engine - Modern Data Integration Tool");

            // 1. RUN Command
            var runCommand = new Command("run", "Execute an ETL-SQL script")
            {
                RunScriptArg,
                BatchSizeOption, PerfOption, VerboseOption, LogOption, SilentOption, PreviewOption,
                JsonOption, QualitySummaryOption, OutputJsonOption, PageOption, SessionOption,
                VarOption, ProgressOption, ResumeOption,
                RecordOption, NoRecordOption, JobNameOption
            };
            runCommand.SetAction(context => Dispatch(context, "run", handler));

            // 2. TEST Command
            var testCommand = new Command("test", "Run native ETL-SQL test suites (*.test.etlsql) and table assertions")
            {
                TestValArg,
                JsonOption, VerboseOption, PerfOption
            };
            testCommand.SetAction(context => Dispatch(context, "test", handler));

            // 4. ENCRYPT Command
            var encryptCommand = new Command("encrypt", "Utility to encrypt a string for secure connections")
            {
                EncryptValueArg,
                PassOption
            };
            encryptCommand.SetAction(context => Dispatch(context, "encrypt", handler));

            var generateCommand = new Command("generate", "Generate mock data for testing projects")
            {
                EstimateOption
            };
            generateCommand.SetAction(context => Dispatch(context, "generate", handler));

            var noticesCommand = new Command("notices", "Show third-party notices and dependency credits");
            noticesCommand.SetAction(context => Dispatch(context, "notices", handler));

            var scanCommand = new Command("scan", "Inspect local or cataloged database schemas for stewardship gaps")
            {
                ScanSourceArg, ScanPiiOption, ScanTableOption, JsonOption
            };
            scanCommand.SetAction(context => Dispatch(context, "scan", handler));

            // 5. SESSION Command
            var sessionCommand = new Command("session", "Manage ad-hoc execution sessions");
            var clearSubcommand = new Command("clear", "Clear a session state")
            {
                new Argument<string>("id") { Description = "The session ID to clear" }
            };
            clearSubcommand.SetAction(context => Dispatch(context, "session-clear", handler));
            sessionCommand.Add(clearSubcommand);

            // 6. UI Command (for REPL and windowed mode)
            var uiCommand = new Command("ui", "Interactive user interface commands");
            var replSubcommand = new Command("repl", "Start the JSON-based REPL protocol for IDE integration")
            {
                BatchSizeOption, PerfOption, VerboseOption, LogOption, JsonOption, SessionOption, VarOption
            };
            replSubcommand.SetAction(context => Dispatch(context, "ui-repl", handler));

            var simpleSubcommand = new Command("simple", "Start the simple interactive menu UI")
            {
                BatchSizeOption, VerboseOption
            };
            simpleSubcommand.SetAction(context => Dispatch(context, "ui-simple", handler));

            var editSubcommand = new Command("edit", "Start the modern windowed Terminal IDE (default)")
            {
                new Argument<string?>("file") { Description = "Optional file to pre-load", Arity = ArgumentArity.ZeroOrOne },
                BatchSizeOption, VerboseOption, SessionOption
            };
            editSubcommand.SetAction(context => Dispatch(context, "ui-edit", handler));

            var oldSubcommand = new Command("old", "Start the legacy Spectre-based console editor")
            {
                new Argument<string?>("file") { Description = "Optional file to pre-load", Arity = ArgumentArity.ZeroOrOne },
                BatchSizeOption, VerboseOption
            };
            oldSubcommand.SetAction(context => Dispatch(context, "ui-old", handler));

            uiCommand.Add(replSubcommand);
            uiCommand.Add(simpleSubcommand);
            uiCommand.Add(editSubcommand);
            uiCommand.Add(oldSubcommand);

            // 7. DOCTOR Command (Health Check)
            var doctorCommand = BuildDoctorCommand(handler);

            // 8. CONFIG Command
            var configCommand = new Command("config", "Manage application configuration");
            var setupJwtSubcommand = new Command("setup-jwt", "Generate a secure JWT secret and update appsettings.json")
            {
                UpdateJwtOption
            };
            setupJwtSubcommand.SetAction(context => Dispatch(context, "config-setup-jwt", handler));
            configCommand.Add(setupJwtSubcommand);

            // 9. SERVE Command — start live preview server for a Report-SQL script
            var serveCommand = new Command("serve", "Start a live preview server for a Report-SQL script")
            {
                ServeScriptArg,
                ServeManifestOption,
                ServePortOption,
                ServeNoBrowserOption,
            };
            serveCommand.SetAction(context => Dispatch(context, "serve", handler));

            // 10. PURGE Command — delete all runtime data (cross-platform "delete all data")
            var purgeCommand = new Command("purge", "Delete all ETL-SQL runtime data (reports, snapshots, databases, logs, sessions)")
            {
                PurgeDryRunOption,
                PurgeYesOption,
            };
            purgeCommand.SetAction(context => Dispatch(context, "purge", handler));

            // 11. GEN-SCRIPT Command — compile specification JSON to ETL-SQL script template
            var genScriptCommand = new Command("gen-script", "Compile a schema JSON specification into a validated ETL-SQL script template")
            {
                SpecSchemaOption,
                SpecOutputOption
            };
            genScriptCommand.SetAction(context => Dispatch(context, "gen-script", handler));

            // 12. EXTRACT-SPEC Command — trim large PDF specifications to data dictionary pages
            var extractSpecCommand = new Command("extract-spec", "Extract data dictionary / schema pages from a large PDF specification")
            {
                ExtractInputOption,
                ExtractOutputOption
            };
            extractSpecCommand.SetAction(context => Dispatch(context, "extract-spec", handler));

            // 13. ADMIN Command group — supported operator workflows (doctor, support-bundle, backup, restore, gateway)
            var adminCommand = new Command("admin", "Operator and administration commands");
            adminCommand.Add(BuildDoctorCommand(handler));
            adminCommand.Add(BuildGatewayCommand(handler));
            var supportBundleCommand = new Command("support-bundle", "Collect a redacted support archive (config, health, logs, database metrics)")
            {
                BundleOutputOption,
            };
            supportBundleCommand.SetAction(context => Dispatch(context, "admin-support-bundle", handler));
            adminCommand.Add(supportBundleCommand);

            var backupCommand = new Command("backup", "Back up portal/orchestrator state into split-custody data and keys archives")
            {
                BackupOutputDirOption,
                BackupTenantRootOption,
            };
            backupCommand.SetAction(context => Dispatch(context, "admin-backup", handler));
            adminCommand.Add(backupCommand);

            var restoreCommand = new Command("restore", "Validate and restore a backup (data + keys archives)")
            {
                RestoreFromOption,
                RestoreKeysOption,
                RestoreToOption,
                RestoreValidateOption,
                RestoreReportOption,
                RestoreExpectedTenantOption,
            };
            restoreCommand.SetAction(context => Dispatch(context, "admin-restore", handler));
            adminCommand.Add(restoreCommand);

            var migrateDbCommand = new Command("migrate-database", "Copy Portal/Orchestrator state from SQLite into the configured PostgreSQL deployment")
            {
                MigrateFromOption,
                MigrateToOption,
                MigrateDryRunOption,
            };
            migrateDbCommand.SetAction(context => Dispatch(context, "admin-migrate-database", handler));
            adminCommand.Add(migrateDbCommand);

            var promotionCommand = new Command("promotion", "Inspect and prepare deployment-profile promotions");
            var promotionPreflightCommand = new Command("preflight", "Create a secret-safe, mutation-free promotion inventory")
            {
                PromotionSourceOption,
                PromotionFromProfileOption,
                PromotionToProfileOption,
                PromotionOutputOption,
            };
            promotionPreflightCommand.SetAction(context => Dispatch(context, "admin-promotion-preflight", handler));
            promotionCommand.Add(promotionPreflightCommand);
            var promotionExportCommand = new Command("export", "Export eligible Orchestrator catalog and governance state")
            {
                PromotionOutputOption,
                PromotionHistoryLimitOption,
            };
            promotionExportCommand.SetAction(context => Dispatch(context, "admin-promotion-export", handler));
            promotionCommand.Add(promotionExportCommand);
            var promotionValidateCommand = new Command("validate", "Validate mappings and collisions without changing the target")
            {
                PromotionPackageOption,
                PromotionBindingOption,
                PromotionOutputOption,
            };
            promotionValidateCommand.SetAction(context => Dispatch(context, "admin-promotion-validate", handler));
            promotionCommand.Add(promotionValidateCommand);
            var promotionImportCommand = new Command("import", "Import an Orchestrator promotion package idempotently")
            {
                PromotionPackageOption,
                PromotionBindingOption,
            };
            promotionImportCommand.SetAction(context => Dispatch(context, "admin-promotion-import", handler));
            promotionCommand.Add(promotionImportCommand);
            var saasOnboardCommand = new Command("saas-onboard", "Create and populate one physically isolated SaaS tenant boundary")
            {
                SaasTenantOption,
                SaasSourceProfileOption,
                PromotionSourceOption,
                PromotionPackageOption,
                SaasPortalBootstrapOption,
                SaasOutputRootOption,
                SaasOidcAuthorityOption,
                SaasOidcClientIdOption,
                PromotionBindingOption,
                SaasMaxConcurrentJobsOption,
                SaasMaxStorageMbOption,
                SaasMaxReportSessionsOption,
            };
            saasOnboardCommand.SetAction(context => Dispatch(context, "admin-promotion-saas-onboard", handler));
            promotionCommand.Add(saasOnboardCommand);
            var saasUpgradeCommand = new Command("saas-upgrade", "Drain and upgrade one Managed Dedicated tenant boundary")
            {
                SaasTenantOption,
                SaasUpgradeTenantRootOption,
                SaasUpgradeTargetReleaseOption,
                SaasUpgradeMaxConcurrentJobsOption,
                SaasUpgradeMaxStorageMbOption,
                SaasUpgradeMaxReportSessionsOption,
                SaasUpgradeExecuteOption,
            };
            saasUpgradeCommand.SetAction(context => Dispatch(context, "admin-promotion-saas-upgrade", handler));
            promotionCommand.Add(saasUpgradeCommand);
            var saasFleetPlanCommand = new Command(
                "saas-fleet-plan",
                "Plan a release rollout across the Managed Dedicated fleet (plans only; never upgrades)")
            {
                FleetTargetReleaseOption,
                FleetWaveSizeOption,
                FleetOperatorOption,
                FleetAuthorizationReferenceOption,
                FleetReasonOption,
                FleetExecuteOption,
                FleetRootOption,
                FleetMaxFailuresOption,
            };
            saasFleetPlanCommand.SetAction(context => Dispatch(context, "admin-promotion-saas-fleet-plan", handler));
            promotionCommand.Add(saasFleetPlanCommand);
            var saasDeleteCommand = new Command("saas-delete", "Delete one Managed Dedicated tenant boundary under signed retention/legal authorization")
            {
                SaasTenantOption,
                SaasDeletionTenantRootOption,
                SaasDeletionReceiptRootOption,
                SaasDeletionExecuteOption,
            };
            saasDeleteCommand.SetAction(context => Dispatch(context, "admin-promotion-saas-delete", handler));
            promotionCommand.Add(saasDeleteCommand);
            adminCommand.Add(promotionCommand);

            var tenantCommand = new Command("tenant", "Export, inspect, and import tenant portability bundles");
            var tenantExportCommand = new Command("export", "Compose a signed, optionally tenant-encrypted portability bundle")
            {
                TenantBundleOption,
                PortalUrlOption,
                PortalClientIdOption,
                TenantExportIdentityOption,
                TenantSourceProfileOption,
                TenantArtifactOption,
                TenantArtifactRootOption,
                TenantOrchestratorPackageOption,
                TenantOrchestratorAliasOption,
                TenantRecipientKeyOption,
                TenantSigningKeyOption,
            };
            tenantExportCommand.SetAction(context => Dispatch(context, "admin-tenant-export", handler));
            tenantCommand.Add(tenantExportCommand);
            var tenantValidateCommand = new Command("validate", "Verify a bundle's integrity and, with --operator-key, its authenticity")
            {
                TenantBundleOption,
                TenantOperatorKeyOption,
                TenantRequireSignatureOption,
            };
            tenantValidateCommand.SetAction(context => Dispatch(context, "admin-tenant-validate", handler));
            tenantCommand.Add(tenantValidateCommand);
            var tenantPreflightCommand = new Command("preflight", "Report what a target must supply before a bundle can be imported")
            {
                TenantBundleOption,
                TenantOperatorKeyOption,
                TenantRequireSignatureOption,
                TenantBindingOption,
            };
            tenantPreflightCommand.SetAction(context => Dispatch(context, "admin-tenant-preflight", handler));
            tenantCommand.Add(tenantPreflightCommand);
            var tenantImportCommand = new Command("import", "Preflight and apply a bundle with workloads disabled")
            {
                TenantBundleOption,
                PortalUrlOption,
                TenantOperatorKeyOption,
                TenantRequireSignatureOption,
                TenantBindingOption,
                TenantRecipientKeyOption,
                TenantCollisionOption,
                TenantDryRunOption,
            };
            tenantImportCommand.SetAction(context => Dispatch(context, "admin-tenant-import", handler));
            tenantCommand.Add(tenantImportCommand);
            adminCommand.Add(tenantCommand);

            var haSoakCommand = new Command("ha-soak", "Prepare and collect PostgreSQL HA soak certification artifacts");
            var haSoakPrepareCommand = new Command("prepare", "Generate an isolated PostgreSQL HA soak topology run root")
            {
                HaSoakRunIdOption,
                HaSoakOutputRootOption,
                HaSoakComposeFileOption,
                HaSoakEnvExampleOption,
                HaSoakPortalScaleOption,
                HaSoakOrchestratorScaleOption,
                HaSoakPortalPortOption,
                HaSoakOrchestratorPortOption,
                HaSoakPostgresPortOption,
                HaSoakImageTagOption,
                HaSoakValidateOnlyOption,
                HaSoakStartOption,
                HaSoakPullOption,
                HaSoakForceOption,
            };
            haSoakPrepareCommand.SetAction(context => Dispatch(context, "admin-ha-soak-prepare", handler));
            haSoakCommand.Add(haSoakPrepareCommand);

            var haSoakWorkloadCommand = new Command("workload", "Materialize the sustained-load workload config for a topology run")
            {
                HaSoakRunRootOption,
                HaSoakOutputPathOption,
                HaSoakAdminPasswordOption,
                HaSoakForceOption,
            };
            haSoakWorkloadCommand.SetAction(context => Dispatch(context, "admin-ha-soak-workload", handler));
            haSoakCommand.Add(haSoakWorkloadCommand);

            var haSoakRunbookCommand = new Command("runbook", "Generate an ordered operator runbook for a topology run")
            {
                HaSoakRunRootOption,
                HaSoakSustainedWorkloadOption,
                HaSoakModeOption,
                HaSoakOutputPathOption,
                HaSoakForceOption,
            };
            haSoakRunbookCommand.SetAction(context => Dispatch(context, "admin-ha-soak-runbook", handler));
            haSoakCommand.Add(haSoakRunbookCommand);

            var haSoakEvidenceCommand = new Command("evidence", "Generate the non-secret HA soak evidence checklist")
            {
                HaSoakRunRootOption,
                HaSoakSustainedWorkloadOption,
                HaSoakOutputPathOption,
                HaSoakForceOption,
            };
            haSoakEvidenceCommand.SetAction(context => Dispatch(context, "admin-ha-soak-evidence", handler));
            haSoakCommand.Add(haSoakEvidenceCommand);

            var haSoakLargeJobCommand = new Command("large-job-plan", "Generate the concurrent large-job soak plan")
            {
                HaSoakRunRootOption,
                HaSoakModeOption,
                HaSoakOutputPathOption,
                HaSoakForceOption,
            };
            haSoakLargeJobCommand.SetAction(context => Dispatch(context, "admin-ha-soak-large-job-plan", handler));
            haSoakCommand.Add(haSoakLargeJobCommand);

            var haSoakLargeJobRunCommand = new Command("large-job-run", "Run the bounded concurrent large-job soak harness")
            {
                HaSoakRunRootOption,
                HaSoakPlanOption,
                HaSoakOutputRootOption,
                HaSoakDurationSecondsOption,
                HaSoakForceOption,
            };
            haSoakLargeJobRunCommand.SetAction(context => Dispatch(context, "admin-ha-soak-large-job-run", handler));
            haSoakCommand.Add(haSoakLargeJobRunCommand);

            var haSoakFaultCommand = new Command("fault-plan", "Generate the HA fault-injection plan")
            {
                HaSoakRunRootOption,
                HaSoakModeOption,
                HaSoakOutputPathOption,
                HaSoakForceOption,
            };
            haSoakFaultCommand.SetAction(context => Dispatch(context, "admin-ha-soak-fault-plan", handler));
            haSoakCommand.Add(haSoakFaultCommand);

            var haSoakFaultRunCommand = new Command("fault-run", "Run the bounded HA fault-injection harness")
            {
                HaSoakRunRootOption,
                HaSoakPlanOption,
                HaSoakOutputRootOption,
                HaSoakForceOption,
            };
            haSoakFaultRunCommand.SetAction(context => Dispatch(context, "admin-ha-soak-fault-run", handler));
            haSoakCommand.Add(haSoakFaultRunCommand);

            var haSoakMetricsCommand = new Command("metrics", "Capture a non-secret PostgreSQL metrics snapshot")
            {
                HaSoakRunRootOption,
                HaSoakOutputPathOption,
                HaSoakValidateOnlyOption,
                HaSoakForceOption,
            };
            haSoakMetricsCommand.SetAction(context => Dispatch(context, "admin-ha-soak-metrics", handler));
            haSoakCommand.Add(haSoakMetricsCommand);

            var haSoakValidateCommand = new Command("validate", "Validate completed HA soak evidence before citing it")
            {
                HaSoakRunRootOption,
                HaSoakRequiredGateOption,
                HaSoakRequiredCommitOption,
                HaSoakAllowDirtyOption,
                HaSoakMarkdownReportOption,
            };
            haSoakValidateCommand.SetAction(context => Dispatch(context, "admin-ha-soak-validate", handler));
            haSoakCommand.Add(haSoakValidateCommand);

            var haSoakDiagnosticsCommand = new Command("diagnostics", "Export a redacted diagnostics bundle for a topology run")
            {
                HaSoakRunRootOption,
                HaSoakOutputRootOption,
                HaSoakLogTailOption,
                HaSoakNoDockerOption,
                HaSoakForceOption,
            };
            haSoakDiagnosticsCommand.SetAction(context => Dispatch(context, "admin-ha-soak-diagnostics", handler));
            haSoakCommand.Add(haSoakDiagnosticsCommand);
            adminCommand.Add(haSoakCommand);

            // Identity administration over the Portal API. Nested under `admin` following the
            // ha-soak and machine-store precedent: the identity family is large enough that flat
            // naming stops scanning cleanly.
            var whoAmICommand = new Command("portal-whoami",
                "Resolve Portal credentials and print the identity, roles, and scopes (never a secret)")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption
            };
            whoAmICommand.SetAction(context => Dispatch(context, "admin-portal-whoami", handler));
            adminCommand.Add(whoAmICommand);

            var userCommand = new Command("user", "Manage Portal users");

            var userListCommand = new Command("list", "List Portal users")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption,
                AdminFilterOption, AdminRoleOption, IncludeInactiveOption
            };
            userListCommand.SetAction(context => Dispatch(context, "admin-user-list", handler));
            userCommand.Add(userListCommand);

            var userShowCommand = new Command("show", "Show one Portal user")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminUsernameOption
            };
            userShowCommand.SetAction(context => Dispatch(context, "admin-user-show", handler));
            userCommand.Add(userShowCommand);

            var userPermissionsCommand = new Command("permissions",
                "Show a user's effective permissions — answers \"why can this person see this\"")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminUsernameOption
            };
            userPermissionsCommand.SetAction(context => Dispatch(context, "admin-user-permissions", handler));
            userCommand.Add(userPermissionsCommand);

            var userCreateCommand = new Command("create", "Create a Portal user")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption,
                AdminUsernameOption, AdminEmailOption, AdminAssignRoleOption, AdminProviderOption,
                PasswordStdinOption, IfNotExistsOption
            };
            userCreateCommand.SetAction(context => Dispatch(context, "admin-user-create", handler));
            userCommand.Add(userCreateCommand);

            var userDeleteCommand = new Command("delete", "Delete a Portal user")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption,
                AdminUsernameOption, IfExistsOption, IfVersionOption
            };
            userDeleteCommand.SetAction(context => Dispatch(context, "admin-user-delete", handler));
            userCommand.Add(userDeleteCommand);

            var userEnableCommand = new Command("enable", "Reactivate a Portal user")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminUsernameOption, IfVersionOption
            };
            userEnableCommand.SetAction(context => Dispatch(context, "admin-user-enable", handler));
            userCommand.Add(userEnableCommand);

            var userDisableCommand = new Command("disable", "Deactivate a Portal user")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminUsernameOption, IfVersionOption
            };
            userDisableCommand.SetAction(context => Dispatch(context, "admin-user-disable", handler));
            userCommand.Add(userDisableCommand);

            var userRevokeCommand = new Command("revoke-tokens", "Revoke a user's issued tokens")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminUsernameOption
            };
            userRevokeCommand.SetAction(context => Dispatch(context, "admin-user-revoke-tokens", handler));
            userCommand.Add(userRevokeCommand);

            var userUpdateCommand = new Command("update", "Update a Portal user's details or role")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminUsernameOption,
                AdminEmailOption, AdminFirstNameOption, AdminLastNameOption, AdminAssignRoleOption,
                IfVersionOption
            };
            userUpdateCommand.SetAction(context => Dispatch(context, "admin-user-update", handler));
            userCommand.Add(userUpdateCommand);

            var userResetPasswordCommand = new Command("reset-password", "Set a user's password, read from stdin")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminUsernameOption,
                PasswordStdinOption, IfVersionOption
            };
            userResetPasswordCommand.SetAction(context => Dispatch(context, "admin-user-reset-password", handler));
            userCommand.Add(userResetPasswordCommand);

            adminCommand.Add(userCommand);

            var groupCommand = new Command("group", "Manage Portal groups and their membership");

            var groupListCommand = new Command("list", "List Portal groups")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminFilterOption
            };
            groupListCommand.SetAction(context => Dispatch(context, "admin-group-list", handler));
            groupCommand.Add(groupListCommand);

            var groupMembersCommand = new Command("members", "List the members of a group")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminGroupNameOption
            };
            groupMembersCommand.SetAction(context => Dispatch(context, "admin-group-members", handler));
            groupCommand.Add(groupMembersCommand);

            var groupCreateCommand = new Command("create", "Create a Portal group")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption,
                AdminGroupNameOption, AdminDescriptionOption, IfNotExistsOption
            };
            groupCreateCommand.SetAction(context => Dispatch(context, "admin-group-create", handler));
            groupCommand.Add(groupCreateCommand);

            var groupDeleteCommand = new Command("delete", "Delete a Portal group")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption,
                AdminGroupNameOption, IfExistsOption, IfVersionOption
            };
            groupDeleteCommand.SetAction(context => Dispatch(context, "admin-group-delete", handler));
            groupCommand.Add(groupDeleteCommand);

            var groupAddMemberCommand = new Command("add-member", "Add a user to a group")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminGroupNameOption, AdminUsernameOption
            };
            groupAddMemberCommand.SetAction(context => Dispatch(context, "admin-group-add-member", handler));
            groupCommand.Add(groupAddMemberCommand);

            var groupRemoveMemberCommand = new Command("remove-member", "Remove a user from a group")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminGroupNameOption, AdminUsernameOption
            };
            groupRemoveMemberCommand.SetAction(context => Dispatch(context, "admin-group-remove-member", handler));
            groupCommand.Add(groupRemoveMemberCommand);

            var groupUpdateCommand = new Command("update", "Rename a group or change its description")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption,
                AdminGroupNameOption, AdminNewNameOption, AdminDescriptionOption, IfVersionOption
            };
            groupUpdateCommand.SetAction(context => Dispatch(context, "admin-group-update", handler));
            groupCommand.Add(groupUpdateCommand);

            var groupCapabilitiesCommand = new Command("capabilities", "Show a group's Studio capabilities")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminGroupNameOption
            };
            groupCapabilitiesCommand.SetAction(context => Dispatch(context, "admin-group-capabilities", handler));
            groupCommand.Add(groupCapabilitiesCommand);

            var groupSetCapabilitiesCommand = new Command("set-capabilities",
                "Replace a group's Studio capabilities with the given set")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminGroupNameOption, AdminCapabilityOption
            };
            groupSetCapabilitiesCommand.SetAction(context => Dispatch(context, "admin-group-set-capabilities", handler));
            groupCommand.Add(groupSetCapabilitiesCommand);

            adminCommand.Add(groupCommand);

            // Distinct from the root `session` command, which manages ad-hoc execution sessions.
            var portalSessionCommand = new Command("session", "Inspect and disconnect Portal sign-in sessions");
            var portalSessionListCommand = new Command("list", "List active Portal sessions")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminFilterOption
            };
            portalSessionListCommand.SetAction(context => Dispatch(context, "admin-session-list", handler));
            portalSessionCommand.Add(portalSessionListCommand);
            var portalSessionDisconnectCommand = new Command("disconnect", "Disconnect a user's Portal sessions")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminUsernameOption
            };
            portalSessionDisconnectCommand.SetAction(context => Dispatch(context, "admin-session-disconnect", handler));
            portalSessionCommand.Add(portalSessionDisconnectCommand);

            adminCommand.Add(portalSessionCommand);

            var serviceAccountCommand = new Command("service-account", "Manage Portal service accounts");
            var serviceAccountListCommand = new Command("list", "List Portal service accounts")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminFilterOption
            };
            serviceAccountListCommand.SetAction(context => Dispatch(context, "admin-service-account-list", handler));
            serviceAccountCommand.Add(serviceAccountListCommand);

            var serviceAccountCreateCommand = new Command("create", "Create a Portal service account")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, ServiceAccountNameOption,
                ServiceAccountOwnerOption, ServiceAccountDescriptionOption, ServiceAccountScopeOption,
                ServiceAccountRoleOption, ServiceAccountCapabilityOption, ServiceAccountExpiresOption,
                ServiceAccountSecretOutputOption, IfNotExistsOption
            };
            serviceAccountCreateCommand.SetAction(context => Dispatch(context, "admin-service-account-create", handler));
            serviceAccountCommand.Add(serviceAccountCreateCommand);

            var serviceAccountUpdateCommand = new Command("update", "Update a Portal service account")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, ServiceAccountNameOption,
                ServiceAccountScopeOption, ServiceAccountCapabilityOption, ServiceAccountExpiresOption,
                ServiceAccountClearCapabilitiesOption, ServiceAccountClearExpiryOption,
                ServiceAccountEnableOption, ServiceAccountDisableOption,
                IfVersionOption
            };
            serviceAccountUpdateCommand.SetAction(context => Dispatch(context, "admin-service-account-update", handler));
            serviceAccountCommand.Add(serviceAccountUpdateCommand);

            var serviceAccountRotateCommand = new Command("rotate-secret", "Rotate a service account secret")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, ServiceAccountNameOption,
                ServiceAccountSecretOutputOption, IfVersionOption
            };
            serviceAccountRotateCommand.SetAction(context => Dispatch(context, "admin-service-account-rotate-secret", handler));
            serviceAccountCommand.Add(serviceAccountRotateCommand);

            var serviceAccountRevokeCommand = new Command("revoke", "Permanently revoke a Portal service account")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, ServiceAccountNameOption, IfVersionOption
            };
            serviceAccountRevokeCommand.SetAction(context => Dispatch(context, "admin-service-account-revoke", handler));
            serviceAccountCommand.Add(serviceAccountRevokeCommand);
            adminCommand.Add(serviceAccountCommand);

            // Grants on orchestrator objects, for headless and scripted provisioning. Routed through
            // the Portal like every other admin command, so the operator needs Portal credentials and
            // never the Orchestrator's signing secret.
            var orchestratorCommand = new Command(
                "orchestrator", "Manage per-object Orchestrator grants and ownership");

            var orchestratorShowCommand = new Command("show", "Show the grants on one Orchestrator object")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, GrantKindOption, GrantObjectOption
            };
            orchestratorShowCommand.SetAction(context => Dispatch(context, "admin-orchestrator-show", handler));
            orchestratorCommand.Add(orchestratorShowCommand);

            var orchestratorGrantCommand = new Command("grant", "Grant a principal a permission on an object")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, GrantKindOption, GrantObjectOption,
                GrantPrincipalKindOption, GrantPrincipalOption, GrantPermissionOption
            };
            orchestratorGrantCommand.SetAction(context => Dispatch(context, "admin-orchestrator-grant", handler));
            orchestratorCommand.Add(orchestratorGrantCommand);

            var orchestratorRevokeCommand = new Command("revoke", "Revoke a principal's grant on an object")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, GrantKindOption, GrantObjectOption,
                GrantPrincipalKindOption, GrantPrincipalOption
            };
            orchestratorRevokeCommand.SetAction(context => Dispatch(context, "admin-orchestrator-revoke", handler));
            orchestratorCommand.Add(orchestratorRevokeCommand);

            // Ownership. Administrator-only in the Orchestrator, because an owner may manage their own
            // object — so handing ownership on would let an owner widen access without anyone
            // administering it.
            var orchestratorSetOwnerCommand = new Command(
                "set-owner", "Reassign an object's owner (administrators only)")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, GrantKindOption, GrantObjectOption,
                GrantPrincipalKindOption, GrantPrincipalOption
            };
            orchestratorSetOwnerCommand.SetAction(context => Dispatch(context, "admin-orchestrator-set-owner", handler));
            orchestratorCommand.Add(orchestratorSetOwnerCommand);

            var orchestratorUnownedCommand = new Command(
                "unowned", "List objects with no recorded owner — reachable only by administrators")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption
            };
            orchestratorUnownedCommand.SetAction(context => Dispatch(context, "admin-orchestrator-unowned", handler));
            orchestratorCommand.Add(orchestratorUnownedCommand);

            // --kind is optional here on purpose: the case this exists for is a solo box that has just
            // attached a Portal and needs an owner for everything it already had.
            var orchestratorAdoptCommand = new Command(
                "adopt", "Assign an owner to every unowned object (administrators only)")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, GrantKindOption,
                GrantPrincipalKindOption, GrantPrincipalOption
            };
            orchestratorAdoptCommand.SetAction(context => Dispatch(context, "admin-orchestrator-adopt", handler));
            orchestratorCommand.Add(orchestratorAdoptCommand);
            adminCommand.Add(orchestratorCommand);

            var accessSimulateCommand = new Command("access-simulate",
                "Simulate what a user can reach — the access question, answered without a browser")
            {
                PortalUrlOption, PortalClientIdOption, JsonOption, AdminUsernameOption
            };
            accessSimulateCommand.SetAction(context => Dispatch(context, "admin-access-simulate", handler));
            adminCommand.Add(accessSimulateCommand);

            // Machine-local governance stores are a distinct namespace from the encrypted,
            // audited Portal catalog. Keeping "machine" in the command path prevents an operator
            // from successfully changing the wrong store.
            var machineCommand = new Command("machine", "Manage machine-local governance stores");
            var machineSecretCommand = new Command("secret", "Manage the machine-local Governance:Secrets provider");
            var setSecretCommand = new Command("set", "Encrypt and store a named machine-local secret")
            {
                SecretNameOption,
                SecretValueOption,
            };
            setSecretCommand.SetAction(context => Dispatch(context, "admin-machine-secret-set", handler));
            machineSecretCommand.Add(setSecretCommand);

            var listSecretsCommand = new Command("list", "List names and status from the machine-local secret store");
            listSecretsCommand.SetAction(context => Dispatch(context, "admin-machine-secret-list", handler));
            machineSecretCommand.Add(listSecretsCommand);

            var verifySecretCommand = new Command("verify", "Resolve a machine-local secret without printing the value")
            {
                SecretNameOption,
            };
            verifySecretCommand.SetAction(context => Dispatch(context, "admin-machine-secret-verify", handler));
            machineSecretCommand.Add(verifySecretCommand);

            var rotateSecretCommand = new Command("rotate", "Replace an existing machine-local secret")
            {
                SecretNameOption,
                SecretValueOption,
            };
            rotateSecretCommand.SetAction(context => Dispatch(context, "admin-machine-secret-rotate", handler));
            machineSecretCommand.Add(rotateSecretCommand);

            var disableSecretCommand = new Command("disable", "Disable a machine-local secret")
            {
                SecretNameOption,
            };
            disableSecretCommand.SetAction(context => Dispatch(context, "admin-machine-secret-disable", handler));
            machineSecretCommand.Add(disableSecretCommand);

            var enableSecretCommand = new Command("enable", "Re-enable a disabled machine-local secret")
            {
                SecretNameOption,
            };
            enableSecretCommand.SetAction(context => Dispatch(context, "admin-machine-secret-enable", handler));
            machineSecretCommand.Add(enableSecretCommand);

            var deleteSecretCommand = new Command("delete", "Permanently remove a machine-local secret")
            {
                SecretNameOption,
            };
            deleteSecretCommand.SetAction(context => Dispatch(context, "admin-machine-secret-delete", handler));
            machineSecretCommand.Add(deleteSecretCommand);
            machineCommand.Add(machineSecretCommand);

            var machineConnectionCommand = new Command("connection", "Manage the machine-local shared connection catalog");
            var setConnectionCommand = new Command("set", "Store a machine-local SHARED: connection")
            {
                ConnectionAliasOption,
                ConnectionTypeOption,
                ConnectionTargetOption,
                ConnectionOptionOption,
                ConnectionSensitiveOption,
            };
            setConnectionCommand.SetAction(context => Dispatch(context, "admin-machine-connection-set", handler));
            machineConnectionCommand.Add(setConnectionCommand);

            var listConnectionsCommand = new Command("list", "List machine-local shared connections and status");
            listConnectionsCommand.SetAction(context => Dispatch(context, "admin-machine-connection-list", handler));
            machineConnectionCommand.Add(listConnectionsCommand);

            var verifyConnectionCommand = new Command("verify", "Verify a machine-local shared connection without printing values")
            {
                ConnectionAliasOption,
            };
            verifyConnectionCommand.SetAction(context => Dispatch(context, "admin-machine-connection-verify", handler));
            machineConnectionCommand.Add(verifyConnectionCommand);

            var disableConnectionCommand = new Command("disable", "Disable a machine-local shared connection")
            {
                ConnectionAliasOption,
            };
            disableConnectionCommand.SetAction(context => Dispatch(context, "admin-machine-connection-disable", handler));
            machineConnectionCommand.Add(disableConnectionCommand);

            var enableConnectionCommand = new Command("enable", "Re-enable a machine-local shared connection")
            {
                ConnectionAliasOption,
            };
            enableConnectionCommand.SetAction(context => Dispatch(context, "admin-machine-connection-enable", handler));
            machineConnectionCommand.Add(enableConnectionCommand);

            var deleteConnectionCommand = new Command("delete", "Permanently remove a machine-local shared connection")
            {
                ConnectionAliasOption,
            };
            deleteConnectionCommand.SetAction(context => Dispatch(context, "admin-machine-connection-delete", handler));
            machineConnectionCommand.Add(deleteConnectionCommand);
            machineCommand.Add(machineConnectionCommand);

            var machineToolCommand = new Command("tool", "Manage the machine-local tool catalog");
            var setToolCommand = new Command("set", "Store a machine-local tool")
            {
                ToolNameOption,
                ToolTypeOption,
                ToolOptionOption,
            };
            setToolCommand.SetAction(context => Dispatch(context, "admin-machine-tool-set", handler));
            machineToolCommand.Add(setToolCommand);

            var listToolsCommand = new Command("list", "List machine-local tools");
            listToolsCommand.SetAction(context => Dispatch(context, "admin-machine-tool-list", handler));
            machineToolCommand.Add(listToolsCommand);

            var deleteToolCommand = new Command("delete", "Permanently remove a machine-local tool")
            {
                ToolNameOption,
            };
            deleteToolCommand.SetAction(context => Dispatch(context, "admin-machine-tool-delete", handler));
            machineToolCommand.Add(deleteToolCommand);
            machineCommand.Add(machineToolCommand);

            adminCommand.Add(machineCommand);

            var enterpriseCommand = new Command("enterprise", "Manage machine-level enterprise policy enrollment");
            var enterpriseEnrollCommand = new Command("enroll", "Enroll this machine in authoritative enterprise policy")
            {
                EnterpriseTenantOption,
                EnterpriseEndpointOption,
                EnterpriseSigningKeyOption,
                EnterpriseCertificateOption,
                EnterpriseServiceIdentityOption,
                EnterpriseOfflineHoursOption,
                EnterpriseAllowOfflineFailureOption
            };
            enterpriseEnrollCommand.SetAction(context => Dispatch(context, "enterprise-enroll", handler));
            enterpriseCommand.Add(enterpriseEnrollCommand);
            var enterpriseStatusCommand = new Command("status", "Inspect machine enterprise enrollment");
            enterpriseStatusCommand.SetAction(context => Dispatch(context, "enterprise-status", handler));
            enterpriseCommand.Add(enterpriseStatusCommand);
            var enterpriseUnenrollCommand = new Command("unenroll", "Remove machine enterprise enrollment")
            {
                EnterpriseConfirmOption
            };
            enterpriseUnenrollCommand.SetAction(context => Dispatch(context, "enterprise-unenroll", handler));
            enterpriseCommand.Add(enterpriseUnenrollCommand);

            // 14. INIT Command — scaffold a starter configuration and first script for CLI-first onboarding
            var initCommand = new Command("init", "Scaffold a starter configuration and first ETL-SQL script for new users")
            {
                InitDirectoryArg,
                InitForceOption,
            };
            initCommand.SetAction(context => Dispatch(context, "init", handler));

            rootCommand.Add(runCommand);
            rootCommand.Add(testCommand);
            rootCommand.Add(encryptCommand);
            rootCommand.Add(generateCommand);
            rootCommand.Add(noticesCommand);
            rootCommand.Add(scanCommand);
            rootCommand.Add(sessionCommand);
            rootCommand.Add(uiCommand);
            rootCommand.Add(doctorCommand);
            rootCommand.Add(configCommand);
            rootCommand.Add(serveCommand);
            rootCommand.Add(purgeCommand);
            rootCommand.Add(genScriptCommand);
            rootCommand.Add(extractSpecCommand);
            rootCommand.Add(adminCommand);
            rootCommand.Add(enterpriseCommand);
            rootCommand.Add(initCommand);
            rootCommand.Add(BuildGatewayCommand(handler));

            return rootCommand;
        }

        // Builds a fresh Gateway command instance.
        private static Command BuildGatewayCommand(Func<CliContext, Task<int>> handler)
        {
            var gatewayCommand = new Command("gateway", "On-premises Data Gateway administration and setup");
            var setupCommand = new Command("setup", "Configure and enroll this machine as an on-premises Data Gateway node")
            {
                GatewayPortalOption,
                GatewayTokenOption,
                GatewayTenantOption,
                GatewayIdOption,
                GatewayNodeIdOption,
                GatewayInstallServiceOption,
                GatewayNonInteractiveOption
            };
            setupCommand.SetAction(context => Dispatch(context, "gateway-setup", handler));
            gatewayCommand.Add(setupCommand);
            var startCommand = new Command("start", "Run the enrolled Gateway daemon in the foreground");
            startCommand.SetAction(context => Dispatch(context, "gateway-start", handler));
            gatewayCommand.Add(startCommand);
            var resourceCommand = new Command("resource", "Administer the protected Gateway-local resource registry")
            {
                GatewayResourceIdOption, GatewayConnectorTypeOption, GatewayLocalTargetOption,
                GatewayCredentialOption, GatewayOperationsOption
            };
            var proposeCommand = new Command("propose", "Propose a local connector resource");
            proposeCommand.SetAction(context => Dispatch(context, "gateway-resource-propose", handler));
            resourceCommand.Add(proposeCommand);
            foreach (var action in new[] { "approve", "disable" })
            {
                var command = new Command(action, $"{action} a local Gateway resource");
                command.SetAction(context => Dispatch(context, $"gateway-resource-{action}", handler));
                resourceCommand.Add(command);
            }
            var listCommand = new Command("list", "List local Gateway resources without revealing targets or credentials");
            listCommand.SetAction(context => Dispatch(context, "gateway-resource-list", handler));
            resourceCommand.Add(listCommand);
            gatewayCommand.Add(resourceCommand);
            return gatewayCommand;
        }

        // Builds a fresh Doctor command instance. A System.CommandLine Command cannot be attached to
        // two parents, so we mint a new one for both the top-level alias and the `admin` group.
        private static Command BuildDoctorCommand(Func<CliContext, Task<int>> handler)
        {
            var doctorCommand = new Command("doctor", "Perform a system health check to verify the environment")
            {
                DoctorStrictOption,
                DoctorProfileOption,
                JsonOption,
            };
            doctorCommand.SetAction(context => Dispatch(context, "doctor", handler));
            return doctorCommand;
        }

        private static string? TryGetString(ParseResult res, Option<string?> option) =>
            res.GetResult(option)?.GetValueOrDefault<string?>();

        private static int TryGetInt(ParseResult res, Option<int> option, int defaultValue) =>
            res.GetResult(option) == null ? defaultValue : res.GetValue(option);

        private static bool TryGetBool(ParseResult res, Option<bool> option) =>
            res.GetResult(option) != null && res.GetValue(option);

        private static async Task<int> Dispatch(ParseResult res, string commandName, Func<CliContext, Task<int>> handler)
        {
            var cliContext = new CliContext
            {
                Command = commandName,
                BatchSize = res.GetValue(BatchSizeOption),
                IsPerfMode = res.GetValue(PerfOption),
                IsVerbose = res.GetValue(VerboseOption),
                IsSilentMode = res.GetValue(SilentOption),
                EstimatedRows = res.GetValue(EstimateOption),
                PreviewVal = res.GetValue(PreviewOption),
                Password = res.GetValue(PassOption) ?? Environment.GetEnvironmentVariable("ETL_SQL_MASTER_PASSWORD"),
                LogPath = res.GetResult(LogOption)?.GetValueOrDefault<string?>() ?? "logs/",
                IsLogMode = res.GetResult(LogOption) != null,
                IsJsonMode = res.GetValue(JsonOption),
                QualitySummary = res.GetValue(QualitySummaryOption),
                OutputJsonPath = res.GetValue(OutputJsonOption),
                EnablePaging = res.GetValue(PageOption),
                DisplayProgress = res.GetValue(ProgressOption)
            };

            if (commandName.StartsWith("admin-user-", StringComparison.Ordinal)
                || commandName.StartsWith("admin-group-", StringComparison.Ordinal)
                || commandName.StartsWith("admin-session-", StringComparison.Ordinal)
                || commandName.StartsWith("admin-service-account-", StringComparison.Ordinal)
                || commandName == "admin-portal-whoami"
                || commandName == "admin-access-simulate")
            {
                cliContext.PortalUrl = TryGetString(res, PortalUrlOption);
                cliContext.PortalClientId = TryGetString(res, PortalClientIdOption);
                cliContext.AdminFilter = TryGetString(res, AdminFilterOption);
                cliContext.AdminRole = TryGetString(res, AdminRoleOption) ?? TryGetString(res, AdminAssignRoleOption);
                cliContext.IncludeInactive = TryGetBool(res, IncludeInactiveOption);
                cliContext.AdminUsername = TryGetString(res, AdminUsernameOption);
                cliContext.AdminGroupName = TryGetString(res, AdminGroupNameOption);
                cliContext.AdminEmail = TryGetString(res, AdminEmailOption);
                cliContext.AdminProvider = TryGetString(res, AdminProviderOption);
                cliContext.AdminDescription = TryGetString(res, AdminDescriptionOption);
                cliContext.PasswordStdin = TryGetBool(res, PasswordStdinOption);
                cliContext.IfNotExists = TryGetBool(res, IfNotExistsOption);
                cliContext.IfExists = TryGetBool(res, IfExistsOption);
                cliContext.IfVersion = res.GetResult(IfVersionOption) is null ? null : res.GetValue(IfVersionOption);
                cliContext.AdminFirstName = TryGetString(res, AdminFirstNameOption);
                cliContext.AdminLastName = TryGetString(res, AdminLastNameOption);
                cliContext.AdminNewName = TryGetString(res, AdminNewNameOption);
                cliContext.AdminCapabilities = res.GetResult(AdminCapabilityOption) is null
                    ? null
                    : [.. res.GetValue(AdminCapabilityOption) ?? []];

                if (commandName.StartsWith("admin-orchestrator-", StringComparison.Ordinal))
                {
                    cliContext.GrantObjectKind = TryGetString(res, GrantKindOption);
                    cliContext.GrantObjectName = TryGetString(res, GrantObjectOption);
                    cliContext.GrantPrincipalKind = TryGetString(res, GrantPrincipalKindOption);
                    cliContext.GrantPrincipalId = TryGetString(res, GrantPrincipalOption);
                    cliContext.GrantPermission = TryGetString(res, GrantPermissionOption);
                }

                if (commandName.StartsWith("admin-service-account-", StringComparison.Ordinal))
                {
                    cliContext.ServiceAccountName = TryGetString(res, ServiceAccountNameOption);
                    cliContext.ServiceAccountOwner = TryGetString(res, ServiceAccountOwnerOption);
                    cliContext.ServiceAccountDescription = TryGetString(res, ServiceAccountDescriptionOption);
                    cliContext.ServiceAccountScopes = res.GetResult(ServiceAccountScopeOption) is null
                        ? null : [.. res.GetValue(ServiceAccountScopeOption) ?? []];
                    cliContext.ServiceAccountRoles = res.GetResult(ServiceAccountRoleOption) is null
                        ? null : [.. res.GetValue(ServiceAccountRoleOption) ?? []];
                    cliContext.ServiceAccountCapabilities = res.GetResult(ServiceAccountCapabilityOption) is null
                        ? null : [.. res.GetValue(ServiceAccountCapabilityOption) ?? []];
                    cliContext.ServiceAccountClearCapabilities = TryGetBool(res, ServiceAccountClearCapabilitiesOption);
                    cliContext.ServiceAccountExpiresAt = TryGetString(res, ServiceAccountExpiresOption);
                    cliContext.ServiceAccountClearExpiry = TryGetBool(res, ServiceAccountClearExpiryOption);
                    cliContext.ServiceAccountEnable = TryGetBool(res, ServiceAccountEnableOption);
                    cliContext.ServiceAccountDisable = TryGetBool(res, ServiceAccountDisableOption);
                    cliContext.ServiceAccountSecretOutput = TryGetString(res, ServiceAccountSecretOutputOption);
                }
            }

            if (commandName.StartsWith("admin-tenant-", StringComparison.Ordinal))
            {
                cliContext.TenantBundleRoot = TryGetString(res, TenantBundleOption);
                cliContext.TenantOperatorKey = TryGetString(res, TenantOperatorKeyOption);
                cliContext.TenantRequireSignature = TryGetBool(res, TenantRequireSignatureOption);
                cliContext.TenantBindings = res.GetResult(TenantBindingOption) is null
                    ? null : [.. res.GetValue(TenantBindingOption) ?? []];
                cliContext.TenantExportIdentity = TryGetString(res, TenantExportIdentityOption);
                cliContext.TenantSourceProfile = TryGetString(res, TenantSourceProfileOption);
                cliContext.TenantArtifactFiles = res.GetResult(TenantArtifactOption) is null
                    ? null : [.. res.GetValue(TenantArtifactOption) ?? []];
                cliContext.TenantArtifactRoot = TryGetString(res, TenantArtifactRootOption);
                cliContext.TenantOrchestratorPackage = TryGetString(res, TenantOrchestratorPackageOption);
                cliContext.TenantOrchestratorAlias = TryGetString(res, TenantOrchestratorAliasOption);
                cliContext.TenantRecipientKey = TryGetString(res, TenantRecipientKeyOption);
                cliContext.TenantSigningKey = TryGetString(res, TenantSigningKeyOption);
                cliContext.TenantCollisionPolicy = TryGetString(res, TenantCollisionOption);
                cliContext.TenantDryRun = TryGetBool(res, TenantDryRunOption);
            }

            if (commandName == "run")
            {
                var input = res.GetValue(RunScriptArg);
                cliContext.ScriptFile = string.IsNullOrWhiteSpace(input) ? null : new FileInfo(input.Trim('"', '\'', ' '));

                // Only an explicitly supplied flag overrides configuration; absent means "leave
                // Engine:AuditAdHocRuns in charge". --no-record wins if both are given, because the
                // safe reading of a contradictory command line is to record less, not more.
                bool record = TryGetBool(res, RecordOption);
                bool noRecord = TryGetBool(res, NoRecordOption);
                cliContext.RecordRun = noRecord ? false : record ? true : null;

                var jobName = TryGetString(res, JobNameOption);
                cliContext.JobName = string.IsNullOrWhiteSpace(jobName) ? null : jobName.Trim();
            }
            else if (commandName == "encrypt")
            {
                cliContext.EncryptValue = res.GetValue(EncryptValueArg);
            }
            else if (commandName == "test")
            {
                cliContext.TestVal = res.GetValue(TestValArg);
            }
            else if (commandName == "scan")
            {
                cliContext.ScanSource = res.GetValue(ScanSourceArg);
                cliContext.ScanPii = res.GetValue(ScanPiiOption);
                cliContext.ScanTable = res.GetValue(ScanTableOption);
            }
            else if (commandName == "session-clear")
            {
                var idArg = res.CommandResult.Children.OfType<ArgumentResult>().FirstOrDefault();
                var sid = idArg?.GetValueOrDefault<string>();
                if (sid != null) cliContext.SessionId = sid;
            }
            else if (commandName.StartsWith("ui-"))
            {
                cliContext.UiMode = commandName.Substring(3); // "repl", "simple", "edit", "old"
                // Check if there was a positional file argument
                var fileResult = res.CommandResult.Children.OfType<ArgumentResult>().FirstOrDefault(a => a.Argument.Name == "file");
                if (fileResult != null)
                {
                    var input = fileResult.GetValueOrDefault<string?>();
                    cliContext.ScriptFile = string.IsNullOrWhiteSpace(input) ? null : new FileInfo(input.Trim('"', '\'', ' '));
                }
            }
            else if (commandName == "config-setup-jwt")
            {
                cliContext.UpdateConfig = res.GetValue(UpdateJwtOption);
            }
            else if (commandName == "serve")
            {
                var scriptInput = res.GetValue(ServeScriptArg);
                if (!string.IsNullOrWhiteSpace(scriptInput))
                    cliContext.ScriptFile = new FileInfo(scriptInput.Trim('"', '\'', ' '));
                cliContext.ServeManifest = res.GetValue(ServeManifestOption);
                cliContext.ServePort = res.GetValue(ServePortOption);
                cliContext.ServeNoBrowser = res.GetValue(ServeNoBrowserOption);
            }
            else if (commandName == "doctor")
            {
                cliContext.DoctorStrict = res.GetValue(DoctorStrictOption);
                cliContext.DoctorProfile = res.GetValue(DoctorProfileOption) ?? "quick";
            }
            else if (commandName == "purge")
            {
                cliContext.PurgeDryRun = res.GetValue(PurgeDryRunOption);
                cliContext.PurgeYes = res.GetValue(PurgeYesOption);
            }
            else if (commandName == "gen-script")
            {
                cliContext.SpecSchema = res.GetValue(SpecSchemaOption);
                cliContext.SpecOutput = res.GetValue(SpecOutputOption);
            }
            else if (commandName == "extract-spec")
            {
                cliContext.ExtractInput = res.GetValue(ExtractInputOption);
                cliContext.ExtractOutput = res.GetValue(ExtractOutputOption);
            }
            else if (commandName == "admin-support-bundle")
            {
                cliContext.BundleOutput = res.GetValue(BundleOutputOption);
            }
            else if (commandName == "admin-backup")
            {
                cliContext.BackupOutputDir = res.GetValue(BackupOutputDirOption);
                cliContext.BackupTenantRoot = res.GetValue(BackupTenantRootOption);
            }
            else if (commandName == "admin-restore")
            {
                cliContext.RestoreFrom = res.GetValue(RestoreFromOption);
                cliContext.RestoreKeys = res.GetValue(RestoreKeysOption);
                cliContext.RestoreTo = res.GetValue(RestoreToOption);
                cliContext.RestoreValidateOnly = res.GetValue(RestoreValidateOption);
                cliContext.RestoreReport = res.GetValue(RestoreReportOption);
                cliContext.RestoreExpectedTenant = res.GetValue(RestoreExpectedTenantOption);
            }
            else if (commandName == "admin-migrate-database")
            {
                cliContext.MigrateFrom = res.GetValue(MigrateFromOption);
                cliContext.MigrateTo = res.GetValue(MigrateToOption);
                cliContext.MigrateDryRun = res.GetValue(MigrateDryRunOption);
            }
            else if (commandName == "admin-promotion-preflight")
            {
                cliContext.PromotionSource = res.GetValue(PromotionSourceOption);
                cliContext.PromotionFromProfile = res.GetValue(PromotionFromProfileOption);
                cliContext.PromotionToProfile = res.GetValue(PromotionToProfileOption);
                cliContext.PromotionOutput = res.GetValue(PromotionOutputOption);
            }
            else if (commandName == "admin-promotion-export")
            {
                cliContext.PromotionOutput = res.GetValue(PromotionOutputOption);
                cliContext.PromotionHistoryLimit = res.GetValue(PromotionHistoryLimitOption);
            }
            else if (commandName is "admin-promotion-validate" or "admin-promotion-import")
            {
                cliContext.PromotionPackage = res.GetValue(PromotionPackageOption);
                cliContext.PromotionBindings = res.GetValue(PromotionBindingOption);
                if (commandName == "admin-promotion-validate")
                    cliContext.PromotionOutput = res.GetValue(PromotionOutputOption);
            }
            else if (commandName == "admin-promotion-saas-onboard")
            {
                cliContext.SaasTenantId = res.GetValue(SaasTenantOption);
                cliContext.SaasSourceProfile = res.GetValue(SaasSourceProfileOption);
                cliContext.PromotionSource = res.GetValue(PromotionSourceOption);
                cliContext.PromotionPackage = res.GetValue(PromotionPackageOption);
                cliContext.SaasPortalBootstrap = res.GetValue(SaasPortalBootstrapOption);
                cliContext.SaasOutputRoot = res.GetValue(SaasOutputRootOption);
                cliContext.SaasOidcAuthority = res.GetValue(SaasOidcAuthorityOption);
                cliContext.SaasOidcClientId = res.GetValue(SaasOidcClientIdOption);
                cliContext.PromotionBindings = res.GetValue(PromotionBindingOption);
                cliContext.SaasMaxConcurrentJobs = res.GetValue(SaasMaxConcurrentJobsOption);
                cliContext.SaasMaxStorageMb = res.GetValue(SaasMaxStorageMbOption);
                cliContext.SaasMaxReportSessions = res.GetValue(SaasMaxReportSessionsOption);
            }
            else if (commandName == "admin-promotion-saas-fleet-plan")
            {
                cliContext.FleetTargetRelease = res.GetValue(FleetTargetReleaseOption);
                cliContext.FleetWaveSize = res.GetValue(FleetWaveSizeOption);
                cliContext.FleetOperator = res.GetValue(FleetOperatorOption);
                cliContext.FleetAuthorizationReference = res.GetValue(FleetAuthorizationReferenceOption);
                cliContext.FleetReason = res.GetValue(FleetReasonOption);
                cliContext.FleetExecute = res.GetValue(FleetExecuteOption);
                cliContext.FleetRoot = res.GetValue(FleetRootOption);
                cliContext.FleetMaxFailures = res.GetValue(FleetMaxFailuresOption);
            }
            else if (commandName == "admin-promotion-saas-delete")
            {
                cliContext.SaasTenantId = res.GetValue(SaasTenantOption);
                cliContext.SaasDeletionTenantRoot = res.GetValue(SaasDeletionTenantRootOption);
                cliContext.SaasDeletionReceiptRoot = res.GetValue(SaasDeletionReceiptRootOption);
                cliContext.SaasDeletionExecute = res.GetValue(SaasDeletionExecuteOption);
            }
            else if (commandName == "admin-promotion-saas-upgrade")
            {
                cliContext.SaasTenantId = res.GetValue(SaasTenantOption);
                cliContext.SaasUpgradeTenantRoot = res.GetValue(SaasUpgradeTenantRootOption);
                cliContext.SaasUpgradeTargetRelease = res.GetValue(SaasUpgradeTargetReleaseOption);
                cliContext.SaasUpgradeMaxConcurrentJobs = res.GetValue(SaasUpgradeMaxConcurrentJobsOption);
                cliContext.SaasUpgradeMaxStorageMb = res.GetValue(SaasUpgradeMaxStorageMbOption);
                cliContext.SaasUpgradeMaxReportSessions = res.GetValue(SaasUpgradeMaxReportSessionsOption);
                cliContext.SaasUpgradeExecute = res.GetValue(SaasUpgradeExecuteOption);
            }
            else if (commandName.StartsWith("admin-ha-soak-", StringComparison.Ordinal))
            {
                cliContext.HaSoakRunId = TryGetString(res, HaSoakRunIdOption);
                cliContext.HaSoakRunRoot = TryGetString(res, HaSoakRunRootOption);
                cliContext.HaSoakOutputRoot = TryGetString(res, HaSoakOutputRootOption);
                cliContext.HaSoakOutputPath = TryGetString(res, HaSoakOutputPathOption);
                cliContext.HaSoakMode = TryGetString(res, HaSoakModeOption);
                cliContext.HaSoakRequiredGate = TryGetString(res, HaSoakRequiredGateOption);
                cliContext.HaSoakRequiredCommit = TryGetString(res, HaSoakRequiredCommitOption);
                cliContext.HaSoakMarkdownReport = TryGetString(res, HaSoakMarkdownReportOption);
                cliContext.HaSoakSustainedWorkloadPath = TryGetString(res, HaSoakSustainedWorkloadOption);
                cliContext.HaSoakPlanPath = TryGetString(res, HaSoakPlanOption);
                cliContext.HaSoakAdminPassword = TryGetString(res, HaSoakAdminPasswordOption);
                cliContext.HaSoakComposeFile = TryGetString(res, HaSoakComposeFileOption);
                cliContext.HaSoakEnvExample = TryGetString(res, HaSoakEnvExampleOption);
                cliContext.HaSoakImageTag = TryGetString(res, HaSoakImageTagOption);
                cliContext.HaSoakPortalScale = TryGetInt(res, HaSoakPortalScaleOption, 2);
                cliContext.HaSoakOrchestratorScale = TryGetInt(res, HaSoakOrchestratorScaleOption, 2);
                cliContext.HaSoakPortalPort = TryGetInt(res, HaSoakPortalPortOption, 5600);
                cliContext.HaSoakOrchestratorPort = TryGetInt(res, HaSoakOrchestratorPortOption, 5601);
                cliContext.HaSoakPostgresPort = TryGetInt(res, HaSoakPostgresPortOption, 5632);
                cliContext.HaSoakLogTail = TryGetInt(res, HaSoakLogTailOption, 500);
                cliContext.HaSoakDurationSeconds = TryGetInt(res, HaSoakDurationSecondsOption, 0);
                cliContext.HaSoakStart = TryGetBool(res, HaSoakStartOption);
                cliContext.HaSoakPull = TryGetBool(res, HaSoakPullOption);
                cliContext.HaSoakValidateOnly = TryGetBool(res, HaSoakValidateOnlyOption);
                cliContext.HaSoakAllowDirty = TryGetBool(res, HaSoakAllowDirtyOption);
                cliContext.HaSoakNoDocker = TryGetBool(res, HaSoakNoDockerOption);
                cliContext.HaSoakForce = TryGetBool(res, HaSoakForceOption);
            }
            else if (commandName is "admin-machine-secret-set" or "admin-machine-secret-rotate")
            {
                cliContext.SecretName = res.GetValue(SecretNameOption);
                cliContext.SecretValue = res.GetValue(SecretValueOption);
            }
            else if (commandName is "admin-machine-secret-verify" or "admin-machine-secret-disable"
                     or "admin-machine-secret-enable" or "admin-machine-secret-delete")
            {
                cliContext.SecretName = res.GetValue(SecretNameOption);
            }
            else if (commandName == "admin-machine-connection-set")
            {
                cliContext.ConnectionAlias = res.GetValue(ConnectionAliasOption);
                cliContext.ConnectionType = res.GetValue(ConnectionTypeOption);
                cliContext.ConnectionTarget = res.GetValue(ConnectionTargetOption);
                cliContext.ConnectionOptions = res.GetValue(ConnectionOptionOption);
                cliContext.ConnectionSensitiveFields = res.GetValue(ConnectionSensitiveOption);
            }
            else if (commandName is "admin-machine-connection-verify" or "admin-machine-connection-disable"
                     or "admin-machine-connection-enable" or "admin-machine-connection-delete")
            {
                cliContext.ConnectionAlias = res.GetValue(ConnectionAliasOption);
            }
            else if (commandName == "admin-machine-tool-set")
            {
                cliContext.ToolName = res.GetValue(ToolNameOption);
                cliContext.ToolType = res.GetValue(ToolTypeOption);
                cliContext.ToolOptions = res.GetValue(ToolOptionOption);
            }
            else if (commandName == "admin-machine-tool-delete")
            {
                cliContext.ToolName = res.GetValue(ToolNameOption);
            }
            else if (commandName == "enterprise-enroll")
            {
                cliContext.EnterpriseTenant = res.GetValue(EnterpriseTenantOption);
                cliContext.EnterprisePolicyEndpoint = res.GetValue(EnterpriseEndpointOption);
                cliContext.EnterpriseSigningKeyPath = res.GetValue(EnterpriseSigningKeyOption);
                cliContext.EnterpriseClientCertificateThumbprint = res.GetValue(EnterpriseCertificateOption);
                cliContext.EnterpriseServiceIdentity = res.GetValue(EnterpriseServiceIdentityOption);
                cliContext.EnterpriseMaxOfflineHours = res.GetValue(EnterpriseOfflineHoursOption);
                cliContext.EnterpriseAllowOfflineFailure = res.GetValue(EnterpriseAllowOfflineFailureOption);
            }
            else if (commandName == "enterprise-unenroll")
            {
                cliContext.EnterpriseConfirm = res.GetValue(EnterpriseConfirmOption);
            }
            else if (commandName is "gateway-setup" or "admin-gateway-setup")
            {
                cliContext.PortalUrl = res.GetValue(GatewayPortalOption);
                cliContext.GatewayToken = res.GetValue(GatewayTokenOption);
                cliContext.GatewayTenantId = res.GetValue(GatewayTenantOption);
                cliContext.GatewayId = res.GetValue(GatewayIdOption);
                cliContext.GatewayNodeId = res.GetValue(GatewayNodeIdOption);
                cliContext.GatewayInstallService = res.GetValue(GatewayInstallServiceOption);
                cliContext.GatewayNonInteractive = res.GetValue(GatewayNonInteractiveOption);
            }
            else if (commandName.StartsWith("gateway-resource-", StringComparison.Ordinal))
            {
                cliContext.GatewayResourceId = TryGetString(res, GatewayResourceIdOption);
                cliContext.GatewayConnectorType = TryGetString(res, GatewayConnectorTypeOption);
                cliContext.GatewayLocalTarget = TryGetString(res, GatewayLocalTargetOption);
                cliContext.GatewayCredentialReference = TryGetString(res, GatewayCredentialOption);
                cliContext.GatewayOperations = TryGetString(res, GatewayOperationsOption);
            }
            else if (commandName == "init")
            {
                var dir = res.GetValue(InitDirectoryArg);
                cliContext.InitDirectory = string.IsNullOrWhiteSpace(dir) ? null : dir.Trim('"', '\'', ' ');
                cliContext.InitForce = res.GetValue(InitForceOption);
            }

            var sessionOptVal = res.GetValue(SessionOption);
            if (sessionOptVal != null) cliContext.SessionId = sessionOptVal;

            if (res.GetResult(ResumeOption) != null)
            {
                cliContext.Resume = res.GetValue(ResumeOption);
            }

            if (res.GetResult(VarOption) != null)
            {
                var varArgs = res.GetValue(VarOption);
                foreach (var arg in varArgs ?? Array.Empty<string>())
                {
                    var parts = arg.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].StartsWith("@") ? parts[0] : "@" + parts[0];
                        cliContext.Variables[key] = ETL_SQL.Core.Common.VariableOverrideValueParser.Parse(parts[1]);
                    }
                }
            }

            return await handler(cliContext);
        }

        public static void ShowAdvancedHelp()
        {
            AnsiConsole.Write(new FigletText("ETL-SQL").Centered().Color(Color.DeepSkyBlue1));
            AnsiConsole.Write(new Rule("[yellow]ETL-SQL Engine CLI Subcommands[/]").RuleStyle("grey"));
            Console.WriteLine();

            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
            table.AddColumn("[bold yellow]Command[/]");
            table.AddColumn("[bold white]Description[/]");
            table.AddRow($"run [blue]{Markup.Escape("<script>")}[/]", "Execute an ETL script with options like --perf, --log, --batch-size.");
            table.AddRow($"serve [blue]{Markup.Escape("<script.rptsql>")}[/]", "Start a live preview server for a Report-SQL script (opens browser automatically).");
            table.AddRow($"test [blue]{Markup.Escape("<category>")}[/]", "Run unit or integration tests (e.g., unit).");
            table.AddRow($"encrypt [blue]{Markup.Escape("<string>")}[/]", "Securely encrypt connection strings.");
            table.AddRow("generate", "Generate large scale mock data for performance validation.");
            table.AddRow("notices", "Show third-party notices and dependency credits.");
            table.AddRow($"init [blue]{Markup.Escape("[directory]")}[/]", "Scaffold a starter appsettings.json and first ETL-SQL script (CLI-first onboarding).");
            table.AddRow("admin doctor", "System health check (alias of top-level 'doctor'). Use --profile full for deep checks.");
            table.AddRow("admin support-bundle", "Collect a redacted support archive (config, health, logs, DB metrics).");
            table.AddRow("admin backup", "Back up portal/orchestrator state into split-custody data + keys archives.");
            table.AddRow("admin restore", "Validate (--validate) and restore a backup (--from <data> --keys <keys> --to <dir>).");
            table.AddRow("admin migrate-database", "Copy SQLite Portal/Orchestrator state into the configured PostgreSQL (--dry-run to verify only).");
            table.AddRow("admin ha-soak", "Prepare HA soak topology artifacts, runbooks, metrics, and diagnostics through the admin CLI.");
            table.AddRow("enterprise enroll|status|unenroll", "Manage protected machine-level enterprise policy enrollment.");
            table.AddRow("config setup-jwt", "Generate a secure 256-bit JWT secret.");
            table.AddRow("purge", "Delete all runtime data (reports, snapshots, DBs, logs, sessions). Use --dry-run to preview.");
            table.AddRow($"gen-script [blue]-s <json> -o <etlsql>[/]", "Compile a schema JSON specification into an ETL-SQL script template.");
            table.AddRow($"extract-spec [blue]-i <pdf> -o <pdf>[/]", "Extract data dictionary / schema pages from a large PDF specification.");
            table.AddRow("ui repl", "Start the JSON-based REPL protocol.");
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\nUse [cyan]ETL-SQL {Markup.Escape("<command>")} --help[/] for details on specific options.");
        }
    }

    // CliContext moved to ETL-SQL.Core/CliContext.cs — available via global using ETL_SQL.Core
}
