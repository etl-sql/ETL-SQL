using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
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
        private static readonly Argument<string> TestValArg = new("testVal")
        {
            Description = "Test category: unit, integration, etc.",
            DefaultValueFactory = _ => "unit"
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

        public static RootCommand BuildRootCommand(Func<CliContext, Task<int>> handler)
        {
            var rootCommand = new RootCommand("ETL-SQL Engine - Modern Data Integration Tool");

            // 1. RUN Command
            var runCommand = new Command("run", "Execute an ETL-SQL script")
            {
                RunScriptArg,
                BatchSizeOption, PerfOption, VerboseOption, LogOption, SilentOption, PreviewOption, JsonOption, PageOption, SessionOption, VarOption, ProgressOption, ResumeOption
            };
            runCommand.SetAction(context => Dispatch(context, "run", handler));

            // 2. TEST Command
            var testCommand = new Command("test", "Run internal diagnostics or unit tests")
            {
                TestValArg
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

            // 13. ADMIN Command group — supported operator workflows (doctor, support-bundle, backup, restore)
            var adminCommand = new Command("admin", "Operator and administration commands");
            adminCommand.Add(BuildDoctorCommand(handler));
            var supportBundleCommand = new Command("support-bundle", "Collect a redacted support archive (config, health, logs, database metrics)")
            {
                BundleOutputOption,
            };
            supportBundleCommand.SetAction(context => Dispatch(context, "admin-support-bundle", handler));
            adminCommand.Add(supportBundleCommand);

            var backupCommand = new Command("backup", "Back up portal/orchestrator state into split-custody data and keys archives")
            {
                BackupOutputDirOption,
            };
            backupCommand.SetAction(context => Dispatch(context, "admin-backup", handler));
            adminCommand.Add(backupCommand);

            var restoreCommand = new Command("restore", "Validate and restore a backup (data + keys archives)")
            {
                RestoreFromOption,
                RestoreKeysOption,
                RestoreToOption,
                RestoreValidateOption,
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

            var setSecretCommand = new Command("set-secret", "Encrypt and store a named secret in the configured secret store (machine scope)")
            {
                SecretNameOption,
                SecretValueOption,
            };
            setSecretCommand.SetAction(context => Dispatch(context, "admin-set-secret", handler));
            adminCommand.Add(setSecretCommand);

            var verifySecretCommand = new Command("verify-secret", "Resolve a named secret to prove it is readable, without printing the value")
            {
                SecretNameOption,
            };
            verifySecretCommand.SetAction(context => Dispatch(context, "admin-verify-secret", handler));
            adminCommand.Add(verifySecretCommand);

            var rotateSecretCommand = new Command("rotate-secret", "Replace the value of an existing named secret")
            {
                SecretNameOption,
                SecretValueOption,
            };
            rotateSecretCommand.SetAction(context => Dispatch(context, "admin-rotate-secret", handler));
            adminCommand.Add(rotateSecretCommand);

            var disableSecretCommand = new Command("disable-secret", "Disable a named secret so resolution fails until it is re-enabled")
            {
                SecretNameOption,
            };
            disableSecretCommand.SetAction(context => Dispatch(context, "admin-disable-secret", handler));
            adminCommand.Add(disableSecretCommand);

            var enableSecretCommand = new Command("enable-secret", "Re-enable a disabled secret; the stored value resolves again")
            {
                SecretNameOption,
            };
            enableSecretCommand.SetAction(context => Dispatch(context, "admin-enable-secret", handler));
            adminCommand.Add(enableSecretCommand);

            var deleteSecretCommand = new Command("delete-secret", "Permanently remove a named secret from the secret store")
            {
                SecretNameOption,
            };
            deleteSecretCommand.SetAction(context => Dispatch(context, "admin-delete-secret", handler));
            adminCommand.Add(deleteSecretCommand);

            var setConnectionCommand = new Command("set-connection", "Store a shared connection in the catalog for scripts to use as SHARED:alias")
            {
                ConnectionAliasOption,
                ConnectionTypeOption,
                ConnectionTargetOption,
                ConnectionOptionOption,
                ConnectionSensitiveOption,
            };
            setConnectionCommand.SetAction(context => Dispatch(context, "admin-set-connection", handler));
            adminCommand.Add(setConnectionCommand);

            var listConnectionsCommand = new Command("list-connections", "List shared connection catalog entries and their status");
            listConnectionsCommand.SetAction(context => Dispatch(context, "admin-list-connections", handler));
            adminCommand.Add(listConnectionsCommand);

            var verifyConnectionCommand = new Command("verify-connection", "Prove a shared connection's definition and secret references resolve, without printing values")
            {
                ConnectionAliasOption,
            };
            verifyConnectionCommand.SetAction(context => Dispatch(context, "admin-verify-connection", handler));
            adminCommand.Add(verifyConnectionCommand);

            var disableConnectionCommand = new Command("disable-connection", "Disable a shared connection so SHARED:alias fails until it is re-enabled")
            {
                ConnectionAliasOption,
            };
            disableConnectionCommand.SetAction(context => Dispatch(context, "admin-disable-connection", handler));
            adminCommand.Add(disableConnectionCommand);

            var enableConnectionCommand = new Command("enable-connection", "Re-enable a disabled shared connection; its stored definition is retained")
            {
                ConnectionAliasOption,
            };
            enableConnectionCommand.SetAction(context => Dispatch(context, "admin-enable-connection", handler));
            adminCommand.Add(enableConnectionCommand);

            var deleteConnectionCommand = new Command("delete-connection", "Permanently remove a shared connection from the catalog")
            {
                ConnectionAliasOption,
            };
            deleteConnectionCommand.SetAction(context => Dispatch(context, "admin-delete-connection", handler));
            adminCommand.Add(deleteConnectionCommand);

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

            return rootCommand;
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
                EnablePaging = res.GetValue(PageOption),
                DisplayProgress = res.GetValue(ProgressOption)
            };

            if (commandName == "run")
            {
                var input = res.GetValue(RunScriptArg);
                cliContext.ScriptFile = string.IsNullOrWhiteSpace(input) ? null : new FileInfo(input.Trim('"', '\'', ' '));
            }
            else if (commandName == "encrypt")
            {
                cliContext.EncryptValue = res.GetValue(EncryptValueArg);
            }
            else if (commandName == "test")
            {
                cliContext.TestVal = res.GetValue(TestValArg);
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
            }
            else if (commandName == "admin-restore")
            {
                cliContext.RestoreFrom = res.GetValue(RestoreFromOption);
                cliContext.RestoreKeys = res.GetValue(RestoreKeysOption);
                cliContext.RestoreTo = res.GetValue(RestoreToOption);
                cliContext.RestoreValidateOnly = res.GetValue(RestoreValidateOption);
            }
            else if (commandName == "admin-migrate-database")
            {
                cliContext.MigrateFrom = res.GetValue(MigrateFromOption);
                cliContext.MigrateTo = res.GetValue(MigrateToOption);
                cliContext.MigrateDryRun = res.GetValue(MigrateDryRunOption);
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
            else if (commandName is "admin-set-secret" or "admin-rotate-secret")
            {
                cliContext.SecretName = res.GetValue(SecretNameOption);
                cliContext.SecretValue = res.GetValue(SecretValueOption);
            }
            else if (commandName is "admin-verify-secret" or "admin-disable-secret" or "admin-enable-secret" or "admin-delete-secret")
            {
                cliContext.SecretName = res.GetValue(SecretNameOption);
            }
            else if (commandName == "admin-set-connection")
            {
                cliContext.ConnectionAlias = res.GetValue(ConnectionAliasOption);
                cliContext.ConnectionType = res.GetValue(ConnectionTypeOption);
                cliContext.ConnectionTarget = res.GetValue(ConnectionTargetOption);
                cliContext.ConnectionOptions = res.GetValue(ConnectionOptionOption);
                cliContext.ConnectionSensitiveFields = res.GetValue(ConnectionSensitiveOption);
            }
            else if (commandName is "admin-verify-connection" or "admin-disable-connection" or "admin-enable-connection" or "admin-delete-connection")
            {
                cliContext.ConnectionAlias = res.GetValue(ConnectionAliasOption);
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
                        cliContext.Variables[key] = ParseValue(parts[1]);
                    }
                }
            }

            return await handler(cliContext);
        }

        private static object? ParseValue(string raw)
        {
            if (int.TryParse(raw, out var i)) return i;
            if (double.TryParse(raw, out var d)) return d;
            if (bool.TryParse(raw, out var b)) return b;
            if (DateTime.TryParse(raw, out var dt)) return dt;
            return raw.Trim('\'', '\"');
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
