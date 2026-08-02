# CLI Reference

Command-line interface for the ETL-SQL engine. One page per command, generated from
the command definitions so they stay in sync with the code.

## Commands

| Command | Description |
| :--- | :--- |
| [`etl-sql admin`](admin.md) | Operator and administration commands |
| [`etl-sql admin backup`](admin-backup.md) | Back up portal/orchestrator state into split-custody data and keys archives |
| [`etl-sql admin delete-connection`](admin-delete-connection.md) | Permanently remove a shared connection from the catalog |
| [`etl-sql admin delete-secret`](admin-delete-secret.md) | Permanently remove a named secret from the secret store |
| [`etl-sql admin disable-connection`](admin-disable-connection.md) | Disable a shared connection so SHARED:alias fails until it is re-enabled |
| [`etl-sql admin disable-secret`](admin-disable-secret.md) | Disable a named secret so resolution fails until it is re-enabled |
| [`etl-sql admin doctor`](admin-doctor.md) | Perform a system health check to verify the environment |
| [`etl-sql admin enable-connection`](admin-enable-connection.md) | Re-enable a disabled shared connection; its stored definition is retained |
| [`etl-sql admin enable-secret`](admin-enable-secret.md) | Re-enable a disabled secret; the stored value resolves again |
| [`etl-sql admin ha-soak`](admin-ha-soak.md) | Prepare and collect PostgreSQL HA soak certification artifacts |
| [`etl-sql admin ha-soak diagnostics`](admin-ha-soak-diagnostics.md) | Export a redacted diagnostics bundle for a topology run |
| [`etl-sql admin ha-soak evidence`](admin-ha-soak-evidence.md) | Generate the non-secret HA soak evidence checklist |
| [`etl-sql admin ha-soak fault-plan`](admin-ha-soak-fault-plan.md) | Generate the HA fault-injection plan |
| [`etl-sql admin ha-soak fault-run`](admin-ha-soak-fault-run.md) | Run the bounded HA fault-injection harness |
| [`etl-sql admin ha-soak large-job-plan`](admin-ha-soak-large-job-plan.md) | Generate the concurrent large-job soak plan |
| [`etl-sql admin ha-soak large-job-run`](admin-ha-soak-large-job-run.md) | Run the bounded concurrent large-job soak harness |
| [`etl-sql admin ha-soak metrics`](admin-ha-soak-metrics.md) | Capture a non-secret PostgreSQL metrics snapshot |
| [`etl-sql admin ha-soak prepare`](admin-ha-soak-prepare.md) | Generate an isolated PostgreSQL HA soak topology run root |
| [`etl-sql admin ha-soak runbook`](admin-ha-soak-runbook.md) | Generate an ordered operator runbook for a topology run |
| [`etl-sql admin ha-soak validate`](admin-ha-soak-validate.md) | Validate completed HA soak evidence before citing it |
| [`etl-sql admin ha-soak workload`](admin-ha-soak-workload.md) | Materialize the sustained-load workload config for a topology run |
| [`etl-sql admin list-connections`](admin-list-connections.md) | List shared connection catalog entries and their status |
| [`etl-sql admin migrate-database`](admin-migrate-database.md) | Copy Portal/Orchestrator state from SQLite into the configured PostgreSQL deployment |
| [`etl-sql admin restore`](admin-restore.md) | Validate and restore a backup (data + keys archives) |
| [`etl-sql admin rotate-secret`](admin-rotate-secret.md) | Replace the value of an existing named secret |
| [`etl-sql admin set-connection`](admin-set-connection.md) | Store a shared connection in the catalog for scripts to use as SHARED:alias |
| [`etl-sql admin set-secret`](admin-set-secret.md) | Encrypt and store a named secret in the configured secret store (machine scope) |
| [`etl-sql admin support-bundle`](admin-support-bundle.md) | Collect a redacted support archive (config, health, logs, database metrics) |
| [`etl-sql admin verify-connection`](admin-verify-connection.md) | Prove a shared connection's definition and secret references resolve, without printing values |
| [`etl-sql admin verify-secret`](admin-verify-secret.md) | Resolve a named secret to prove it is readable, without printing the value |
| [`etl-sql config`](config.md) | Manage application configuration |
| [`etl-sql config setup-jwt`](config-setup-jwt.md) | Generate a secure JWT secret and update appsettings.json |
| [`etl-sql doctor`](doctor.md) | Perform a system health check to verify the environment |
| [`etl-sql encrypt`](encrypt.md) | Utility to encrypt a string for secure connections |
| [`etl-sql enterprise`](enterprise.md) | Manage machine-level enterprise policy enrollment |
| [`etl-sql enterprise enroll`](enterprise-enroll.md) | Enroll this machine in authoritative enterprise policy |
| [`etl-sql enterprise status`](enterprise-status.md) | Inspect machine enterprise enrollment |
| [`etl-sql enterprise unenroll`](enterprise-unenroll.md) | Remove machine enterprise enrollment |
| [`etl-sql extract-spec`](extract-spec.md) | Extract data dictionary / schema pages from a large PDF specification |
| [`etl-sql gen-script`](gen-script.md) | Compile a schema JSON specification into a validated ETL-SQL script template |
| [`etl-sql generate`](generate.md) | Generate mock data for testing projects |
| [`etl-sql init`](init.md) | Scaffold a starter configuration and first ETL-SQL script for new users |
| [`etl-sql notices`](notices.md) | Show third-party notices and dependency credits |
| [`etl-sql purge`](purge.md) | Delete all ETL-SQL runtime data (reports, snapshots, databases, logs, sessions) |
| [`etl-sql run`](run.md) | Execute an ETL-SQL script |
| [`etl-sql scan`](scan.md) | Inspect local or cataloged database schemas for stewardship gaps |
| [`etl-sql serve`](serve.md) | Start a live preview server for a Report-SQL script |
| [`etl-sql session`](session.md) | Manage ad-hoc execution sessions |
| [`etl-sql session clear`](session-clear.md) | Clear a session state |
| [`etl-sql test`](test.md) | Run internal diagnostics or unit tests |
| [`etl-sql ui`](ui.md) | Interactive user interface commands |
| [`etl-sql ui edit`](ui-edit.md) | Start the modern windowed Terminal IDE (default) |
| [`etl-sql ui old`](ui-old.md) | Start the legacy Spectre-based console editor |
| [`etl-sql ui repl`](ui-repl.md) | Start the JSON-based REPL protocol for IDE integration |
| [`etl-sql ui simple`](ui-simple.md) | Start the simple interactive menu UI |

## Exit Codes

| Code | Meaning |
| :--- | :--- |
| `0` | Script completed successfully |
| `1` | Parse error, lint error, or runtime exception |

Exit codes are suitable for use in CI/CD pipeline gating.

---

<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.
     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->
