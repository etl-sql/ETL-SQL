# CLI Reference

Command-line interface for the ETL-SQL engine. One page per command, generated from
the command definitions so they stay in sync with the code.

## Commands

| Command | Description |
| :--- | :--- |
| [`etl-sql admin`](admin.md) | Operator and administration commands |
| [`etl-sql admin access-simulate`](admin-access-simulate.md) | Simulate what a user can reach — the access question, answered without a browser |
| [`etl-sql admin backup`](admin-backup.md) | Back up portal/orchestrator state into split-custody data and keys archives |
| [`etl-sql admin doctor`](admin-doctor.md) | Perform a system health check to verify the environment |
| [`etl-sql admin group`](admin-group.md) | Manage Portal groups and their membership |
| [`etl-sql admin group add-member`](admin-group-add-member.md) | Add a user to a group |
| [`etl-sql admin group capabilities`](admin-group-capabilities.md) | Show a group's Studio capabilities |
| [`etl-sql admin group create`](admin-group-create.md) | Create a Portal group |
| [`etl-sql admin group delete`](admin-group-delete.md) | Delete a Portal group |
| [`etl-sql admin group list`](admin-group-list.md) | List Portal groups |
| [`etl-sql admin group members`](admin-group-members.md) | List the members of a group |
| [`etl-sql admin group remove-member`](admin-group-remove-member.md) | Remove a user from a group |
| [`etl-sql admin group set-capabilities`](admin-group-set-capabilities.md) | Replace a group's Studio capabilities with the given set |
| [`etl-sql admin group update`](admin-group-update.md) | Rename a group or change its description |
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
| [`etl-sql admin machine`](admin-machine.md) | Manage machine-local governance stores |
| [`etl-sql admin machine connection`](admin-machine-connection.md) | Manage the machine-local shared connection catalog |
| [`etl-sql admin machine connection delete`](admin-machine-connection-delete.md) | Permanently remove a machine-local shared connection |
| [`etl-sql admin machine connection disable`](admin-machine-connection-disable.md) | Disable a machine-local shared connection |
| [`etl-sql admin machine connection enable`](admin-machine-connection-enable.md) | Re-enable a machine-local shared connection |
| [`etl-sql admin machine connection list`](admin-machine-connection-list.md) | List machine-local shared connections and status |
| [`etl-sql admin machine connection set`](admin-machine-connection-set.md) | Store a machine-local SHARED: connection |
| [`etl-sql admin machine connection verify`](admin-machine-connection-verify.md) | Verify a machine-local shared connection without printing values |
| [`etl-sql admin machine secret`](admin-machine-secret.md) | Manage the machine-local Governance:Secrets provider |
| [`etl-sql admin machine secret delete`](admin-machine-secret-delete.md) | Permanently remove a machine-local secret |
| [`etl-sql admin machine secret disable`](admin-machine-secret-disable.md) | Disable a machine-local secret |
| [`etl-sql admin machine secret enable`](admin-machine-secret-enable.md) | Re-enable a disabled machine-local secret |
| [`etl-sql admin machine secret list`](admin-machine-secret-list.md) | List names and status from the machine-local secret store |
| [`etl-sql admin machine secret rotate`](admin-machine-secret-rotate.md) | Replace an existing machine-local secret |
| [`etl-sql admin machine secret set`](admin-machine-secret-set.md) | Encrypt and store a named machine-local secret |
| [`etl-sql admin machine secret verify`](admin-machine-secret-verify.md) | Resolve a machine-local secret without printing the value |
| [`etl-sql admin machine tool`](admin-machine-tool.md) | Manage the machine-local tool catalog |
| [`etl-sql admin machine tool delete`](admin-machine-tool-delete.md) | Permanently remove a machine-local tool |
| [`etl-sql admin machine tool list`](admin-machine-tool-list.md) | List machine-local tools |
| [`etl-sql admin machine tool set`](admin-machine-tool-set.md) | Store a machine-local tool |
| [`etl-sql admin migrate-database`](admin-migrate-database.md) | Copy Portal/Orchestrator state from SQLite into the configured PostgreSQL deployment |
| [`etl-sql admin orchestrator`](admin-orchestrator.md) | Manage per-object Orchestrator grants |
| [`etl-sql admin orchestrator grant`](admin-orchestrator-grant.md) | Grant a principal a permission on an object |
| [`etl-sql admin orchestrator revoke`](admin-orchestrator-revoke.md) | Revoke a principal's grant on an object |
| [`etl-sql admin orchestrator show`](admin-orchestrator-show.md) | Show the grants on one Orchestrator object |
| [`etl-sql admin portal-whoami`](admin-portal-whoami.md) | Resolve Portal credentials and print the identity, roles, and scopes (never a secret) |
| [`etl-sql admin promotion`](admin-promotion.md) | Inspect and prepare deployment-profile promotions |
| [`etl-sql admin promotion export`](admin-promotion-export.md) | Export eligible Orchestrator catalog and governance state |
| [`etl-sql admin promotion import`](admin-promotion-import.md) | Import an Orchestrator promotion package idempotently |
| [`etl-sql admin promotion preflight`](admin-promotion-preflight.md) | Create a secret-safe, mutation-free promotion inventory |
| [`etl-sql admin promotion saas-delete`](admin-promotion-saas-delete.md) | Delete one Managed Dedicated tenant boundary under signed retention/legal authorization |
| [`etl-sql admin promotion saas-onboard`](admin-promotion-saas-onboard.md) | Create and populate one physically isolated SaaS tenant boundary |
| [`etl-sql admin promotion saas-upgrade`](admin-promotion-saas-upgrade.md) | Drain and upgrade one Managed Dedicated tenant boundary |
| [`etl-sql admin promotion validate`](admin-promotion-validate.md) | Validate mappings and collisions without changing the target |
| [`etl-sql admin restore`](admin-restore.md) | Validate and restore a backup (data + keys archives) |
| [`etl-sql admin service-account`](admin-service-account.md) | Manage Portal service accounts |
| [`etl-sql admin service-account create`](admin-service-account-create.md) | Create a Portal service account |
| [`etl-sql admin service-account list`](admin-service-account-list.md) | List Portal service accounts |
| [`etl-sql admin service-account revoke`](admin-service-account-revoke.md) | Permanently revoke a Portal service account |
| [`etl-sql admin service-account rotate-secret`](admin-service-account-rotate-secret.md) | Rotate a service account secret |
| [`etl-sql admin service-account update`](admin-service-account-update.md) | Update a Portal service account |
| [`etl-sql admin session`](admin-session.md) | Inspect and disconnect Portal sign-in sessions |
| [`etl-sql admin session disconnect`](admin-session-disconnect.md) | Disconnect a user's Portal sessions |
| [`etl-sql admin session list`](admin-session-list.md) | List active Portal sessions |
| [`etl-sql admin support-bundle`](admin-support-bundle.md) | Collect a redacted support archive (config, health, logs, database metrics) |
| [`etl-sql admin tenant`](admin-tenant.md) | Export, inspect, and import tenant portability bundles |
| [`etl-sql admin tenant export`](admin-tenant-export.md) | Compose a signed, optionally tenant-encrypted portability bundle |
| [`etl-sql admin tenant import`](admin-tenant-import.md) | Preflight and apply a bundle with workloads disabled |
| [`etl-sql admin tenant preflight`](admin-tenant-preflight.md) | Report what a target must supply before a bundle can be imported |
| [`etl-sql admin tenant validate`](admin-tenant-validate.md) | Verify a bundle's integrity and, with --operator-key, its authenticity |
| [`etl-sql admin user`](admin-user.md) | Manage Portal users |
| [`etl-sql admin user create`](admin-user-create.md) | Create a Portal user |
| [`etl-sql admin user delete`](admin-user-delete.md) | Delete a Portal user |
| [`etl-sql admin user disable`](admin-user-disable.md) | Deactivate a Portal user |
| [`etl-sql admin user enable`](admin-user-enable.md) | Reactivate a Portal user |
| [`etl-sql admin user list`](admin-user-list.md) | List Portal users |
| [`etl-sql admin user permissions`](admin-user-permissions.md) | Show a user's effective permissions — answers "why can this person see this" |
| [`etl-sql admin user reset-password`](admin-user-reset-password.md) | Set a user's password, read from stdin |
| [`etl-sql admin user revoke-tokens`](admin-user-revoke-tokens.md) | Revoke a user's issued tokens |
| [`etl-sql admin user show`](admin-user-show.md) | Show one Portal user |
| [`etl-sql admin user update`](admin-user-update.md) | Update a Portal user's details or role |
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
| [`etl-sql test`](test.md) | Run native ETL-SQL test suites (*.test.etlsql) and table assertions |
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
