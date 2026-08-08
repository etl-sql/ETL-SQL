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
| [`etl-sql admin group`](admin-group.md) | Inspect Portal groups |
| [`etl-sql admin group add-member`](admin-group-add-member.md) | Add a user to a group |
| [`etl-sql admin group create`](admin-group-create.md) | Create a Portal group |
| [`etl-sql admin group delete`](admin-group-delete.md) | Delete a Portal group |
| [`etl-sql admin group list`](admin-group-list.md) | List Portal groups |
| [`etl-sql admin group members`](admin-group-members.md) | List the members of a group |
| [`etl-sql admin group remove-member`](admin-group-remove-member.md) | Remove a user from a group |
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
| [`etl-sql admin portal-whoami`](admin-portal-whoami.md) | Resolve Portal credentials and print the identity, roles, and scopes (never a secret) |
| [`etl-sql admin promotion`](admin-promotion.md) | Inspect and prepare deployment-profile promotions |
| [`etl-sql admin promotion export`](admin-promotion-export.md) | Export eligible Orchestrator catalog and governance state |
| [`etl-sql admin promotion import`](admin-promotion-import.md) | Import an Orchestrator promotion package idempotently |
| [`etl-sql admin promotion preflight`](admin-promotion-preflight.md) | Create a secret-safe, mutation-free promotion inventory |
| [`etl-sql admin promotion saas-onboard`](admin-promotion-saas-onboard.md) | Create and populate one physically isolated SaaS tenant boundary |
| [`etl-sql admin promotion validate`](admin-promotion-validate.md) | Validate mappings and collisions without changing the target |
| [`etl-sql admin restore`](admin-restore.md) | Validate and restore a backup (data + keys archives) |
| [`etl-sql admin rotate-secret`](admin-rotate-secret.md) | Replace the value of an existing named secret |
| [`etl-sql admin session`](admin-session.md) | Inspect Portal sign-in sessions |
| [`etl-sql admin session disconnect`](admin-session-disconnect.md) | Disconnect a user's Portal sessions |
| [`etl-sql admin session list`](admin-session-list.md) | List active Portal sessions |
| [`etl-sql admin set-connection`](admin-set-connection.md) | Store a shared connection in the catalog for scripts to use as SHARED:alias |
| [`etl-sql admin set-secret`](admin-set-secret.md) | Encrypt and store a named secret in the configured secret store (machine scope) |
| [`etl-sql admin support-bundle`](admin-support-bundle.md) | Collect a redacted support archive (config, health, logs, database metrics) |
| [`etl-sql admin user`](admin-user.md) | Inspect Portal users |
| [`etl-sql admin user create`](admin-user-create.md) | Create a Portal user |
| [`etl-sql admin user delete`](admin-user-delete.md) | Delete a Portal user |
| [`etl-sql admin user disable`](admin-user-disable.md) | Deactivate a Portal user |
| [`etl-sql admin user enable`](admin-user-enable.md) | Reactivate a Portal user |
| [`etl-sql admin user list`](admin-user-list.md) | List Portal users |
| [`etl-sql admin user permissions`](admin-user-permissions.md) | Show a user's effective permissions — answers "why can this person see this" |
| [`etl-sql admin user revoke-tokens`](admin-user-revoke-tokens.md) | Revoke a user's issued tokens |
| [`etl-sql admin user show`](admin-user-show.md) | Show one Portal user |
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
