# ETL-SQL Syntax Index

This document indexes every keyword, command, function, and configuration option available in the ETL-SQL language. Use it as a central map to find focused reference pages, examples, and help documentation.

> [!NOTE]
> This is a cross-reference inventory, not the primary explanation of the language. Focused reference pages are the source of truth for syntax and examples. The `Help File` column intentionally points at source-tree help assets and may use local/file links until this index is generated or normalized for release packaging.

---

## 1. Keywords & Commands (Statements)

Statements are the top-level actions in an ETL-SQL script.

| Command | Category | Documentation | Help File |
| :--- | :--- | :--- | :--- |
| `SELECT` | DML / Query | [Statement Reference](reference/statements/README.md) | [SELECT.md](reference/statements/dml/select.md) |
| `INSERT` | DML | [Statement Reference](reference/statements/README.md) | [INSERT.md](reference/statements/dml/insert.md) |
| `UPDATE` | DML | [Statement Reference](reference/statements/README.md) | [UPDATE.md](reference/statements/dml/update.md) |
| `DELETE` | DML | [Statement Reference](reference/statements/README.md) | [DELETE.md](reference/statements/dml/delete.md) |
| `MERGE` | DML | [Statement Reference](reference/statements/README.md) | [MERGE.md](reference/statements/dml/merge.md) |
| `TRUNCATE` | DML | [Statement Reference](reference/statements/README.md) | [TRUNCATE.md](reference/statements/dml/truncate.md) |
| `GENERATE CALENDAR` | Data Prep | [Data Prep Helpers](reference/statements/data-prep.md) | [data-prep.md#generate-calendar](reference/statements/data-prep.md#generate-calendar) |
| `TRANSFORM` | Data Prep | [Data Prep Helpers](reference/statements/data-prep.md) | [data-prep.md#transform](reference/statements/data-prep.md#transform) |
| `DATA QUALITY RULES` | DML | [Statement Reference](reference/statements/README.md) | [DATA QUALITY RULES](reference/statements/dml/data-quality-rules.md) |
| `TRANSFORM` | DML | [Statement Reference](reference/statements/README.md) | [TRANSFORM](reference/statements/dml/transform.md) |
| `DEDUPLICATE` | Transform | [Transform](reference/transform/deduplicate.md) | [deduplicate.md](reference/transform/deduplicate.md) |
| `FILL_DATES` | Transform | [Transform](reference/transform/fill_dates.md) | [fill_dates.md](reference/transform/fill_dates.md) |
| `INTERPOLATE` | Transform | [Transform](reference/transform/interpolate.md) | [interpolate.md](reference/transform/interpolate.md) |
| `NORMALIZE` | Transform | [Transform](reference/transform/normalize.md) | [normalize.md](reference/transform/normalize.md) |
| `PERIOD_COMPARISON` | Transform | [Transform](reference/transform/period_comparison.md) | [period_comparison.md](reference/transform/period_comparison.md) |
| `PIVOT` | Transform | [Transform](reference/transform/pivot.md) | [pivot.md](reference/transform/pivot.md) |
| `ROLLING_AGGREGATE` | Transform | [Transform](reference/transform/rolling_aggregate.md) | [rolling_aggregate.md](reference/transform/rolling_aggregate.md) |
| `SHARE_OF_TOTAL` | Transform | [Transform](reference/transform/share_of_total.md) | [share_of_total.md](reference/transform/share_of_total.md) |
| `TOP_N_OTHERS` | Transform | [Transform](reference/transform/top_n_others.md) | [top_n_others.md](reference/transform/top_n_others.md) |
| `COMPARE DATASETS` | Data Prep / CDC | [Data Prep Helpers](reference/statements/data-prep.md) | [data-prep.md#compare-datasets](reference/statements/data-prep.md#compare-datasets) |
| `CREATE CONNECTION` | DDL / Conn | [Statement Reference](reference/statements/README.md) | [CREATE.md](reference/statements/ddl/create.md) |
| `ALTER CONNECTION` | DDL / Conn | [Statement Reference](reference/statements/README.md) | [ALTER.md](reference/statements/ddl/alter.md) |
| `DROP CONNECTION` | DDL / Conn | [Statement Reference](reference/statements/README.md) | [DROP.md](reference/statements/ddl/drop.md) |
| `CREATE TABLE` | DDL | [Statement Reference](reference/statements/README.md) | [CREATE.md](reference/statements/ddl/create.md) |
| `ALTER TABLE` | DDL | [Statement Reference](reference/statements/README.md) | [ALTER.md](reference/statements/ddl/alter.md) |
| `DROP TABLE` | DDL | [Statement Reference](reference/statements/README.md) | [DROP.md](reference/statements/ddl/drop.md) |
| `DECLARE` | Variables | [Statement Reference](reference/statements/README.md) | [DECLARE.md](reference/variables-parameters/declare.md) |
| `SET @var` | Variables | [Statement Reference](reference/statements/README.md) | [SET.md](reference/set-commands/set.md) |
| `IF / ELSE` | Flow Control | [Statement Reference](reference/statements/README.md) | [IF.md](reference/control-flow/if.md) |
| `WHILE` | Flow Control | [Statement Reference](reference/statements/README.md) | [WHILE.md](reference/control-flow/while.md) |
| `FOR` | Flow Control | [Statement Reference](reference/statements/README.md) | [FOR.md](reference/control-flow/for.md) |
| `FOREACH` | Flow Control | [Statement Reference](reference/statements/README.md) | [FOREACH.md](reference/control-flow/foreach.md) |
| `TRY / CATCH` | Flow Control | [Statement Reference](reference/statements/README.md) | [TRY.md](reference/control-flow/try-catch.md) |
| `WAITFOR` | Flow Control | [Statement Reference](reference/statements/README.md) | [WAITFOR.md](reference/control-flow/waitfor.md) |
| `WAIT UNTIL` | Flow Control | [Statement Reference](reference/statements/README.md) | [WAIT UNTIL.md](reference/control-flow/wait-until.md) |
| `BREAK` | Flow Control | [Statement Reference](reference/statements/README.md) | [BREAK.md](reference/control-flow/break.md) |
| `CONTINUE` | Flow Control | [Statement Reference](reference/statements/README.md) | [CONTINUE.md](reference/control-flow/continue.md) |
| `RETURN` | Flow Control | [Statement Reference](reference/statements/README.md) | [RETURN.md](reference/control-flow/return.md) |
| `THROW` | Flow Control | [Statement Reference](reference/statements/README.md) | [THROW.md](reference/control-flow/throw.md) |
| `BEGIN TRANSACTION` | Session | [Statement Reference](reference/statements/README.md) | [TRANSACTION.md](reference/statements/session-control/transaction.md) |
| `COMMIT` | Session | [Statement Reference](reference/statements/README.md) | [TRANSACTION.md](reference/statements/session-control/transaction.md) |
| `ROLLBACK` | Session | [Statement Reference](reference/statements/README.md) | [TRANSACTION.md](reference/statements/session-control/transaction.md) |
| `PRINT` | IO | [Statement Reference](reference/statements/README.md) | [PRINT.md](reference/statements/session-control/print.md) |
| `EXECUTE` | Orchestration | [Statement Reference](reference/statements/README.md) | [EXECUTE.md](reference/control-flow/execute.md) |
| `RUN SCRIPT` | Orchestration | [Statement Reference](reference/statements/README.md) | [RUN.md](reference/control-flow/run.md) |
| `PUBLISH BUNDLE` | Orchestration | [Statement Reference](reference/statements/README.md) | [PUBLISH.md](reference/orchestrator-jobs/publish.md) |
| `VALIDATE BUNDLE` | Orchestration | [Statement Reference](reference/statements/README.md) | [VALIDATE.md](reference/orchestrator-jobs/validate.md) |
| `EXPORT SCRIPT` | Orchestration | [Statement Reference](reference/statements/README.md) | [EXPORT.md](reference/orchestrator-jobs/export.md) |
| `EXPORT LINEAGE` | Lineage | [Lineage](reference/statements/session-control/lineage.md) | [EXPORT LINEAGE.md](reference/statements/session-control/export-lineage.md) |
| `IMPORT LINEAGE` | Lineage | [Lineage](reference/statements/session-control/lineage.md) | [IMPORT LINEAGE.md](reference/statements/session-control/import-lineage.md) |
| `INSERT LINEAGE` | Lineage | [Lineage](reference/statements/session-control/lineage.md) | [IMPORT LINEAGE.md](reference/statements/session-control/import-lineage.md) |
| `DELETE LINEAGE` | Lineage | [Lineage](reference/statements/session-control/lineage.md) | [IMPORT LINEAGE.md](reference/statements/session-control/import-lineage.md) |
| `TAGS` | Governance | [Lineage](reference/statements/session-control/lineage.md) | [Governance Tags](reference/statements/session-control/governance-tags.md) |
| `PARALLEL` | Orchestration | [Statement Reference](reference/statements/README.md) | [PARALLEL.md](reference/control-flow/parallel.md) |
| `GO` | Scripting | [Statement Reference](reference/statements/README.md) | [GO.md](reference/control-flow/go.md) |
| `ASSERT` | Validation | [Statement Reference](reference/statements/README.md) | [ASSERT.md](reference/statements/session-control/assert.md) |
| `ASSERT JOB` | Validation | [Statement Reference](reference/statements/README.md) | [ASSERT JOB](reference/statements/session-control/assert-job.md) |
| `ASSERT TABLE` | Validation | [Statement Reference](reference/statements/README.md) | [ASSERT TABLE](reference/statements/session-control/assert-table.md) |
| `CONFIG` | Session | [Statement Reference](reference/statements/README.md) | [CONFIG](reference/statements/session-control/config.md) |
| `EXPECT SCHEMA` | Validation | [Statement Reference](reference/statements/README.md) | [EXPECT SCHEMA](reference/statements/ddl/expect-schema.md) |
| `LINT` | Validation | [Statement Reference](reference/statements/README.md) | [LINT.md](reference/statements/session-control/lint.md) |
| `EXPLAIN` | Diagnostics | [Statement Reference](reference/statements/README.md) | [EXPLAIN.md](reference/statements/session-control/explain.md) |
| `eng.profile` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.profile](reference/eng/profile.md) |
| `eng.variables` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.variables](reference/eng/variables.md) |
| `eng.connection_config` | Diagnostics| [Engine Catalog](reference/eng/README.md) | [eng.connection_config](reference/eng/connection-config.md) |
| `eng.connections` | Diagnostics| [Engine Catalog](reference/eng/README.md) | [eng.connections](reference/eng/connections.md) |
| `eng.locks` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.locks](reference/eng/locks.md) |
| `CLEAR SESSION` | Session | [Statement Reference](reference/statements/README.md) | [CLEAR.md](reference/statements/session-control/clear.md) |
| `USE PASSWORD` | Session / Security | [Statement Reference](reference/statements/README.md) | [USE.md](reference/variables-parameters/use.md) |
| `USE SETS` | Session | [Statement Reference](reference/statements/README.md) | [USE.md](reference/variables-parameters/use.md) |
| `CREATE SETS` | Session | [Variables & Parameters](reference/variables-parameters/README.md) | [CREATE SETS](reference/variables-parameters/create-sets.md) |
| `DROP SETS` | Session | [Statement Reference](reference/statements/README.md) | [DROP.md](reference/statements/ddl/drop.md) |
| `REQUIRE VERSION` | Session | [Statement Reference](reference/statements/README.md) | [REQUIRE.md](reference/statements/session-control/require.md) |
| `BULK INSERT` | File IO | [Statement Reference](reference/statements/README.md) | [BULK.INSERT.md](reference/file-operations/bulk-insert.md) |
| `COPY FILE` | File IO | [File Operations](reference/file-operations/README.md) | [COPY.md](reference/file-operations/copy-file.md) |
| `MOVE FILE` | File IO | [File Operations](reference/file-operations/README.md) | [move-file.md](reference/file-operations/move-file.md) |
| `DELETE FILE` | File IO | [File Operations](reference/file-operations/README.md) | [DELETE.md](reference/statements/dml/delete.md) |
| `ENCRYPT FILE` | File IO | [File Operations](reference/file-operations/README.md) | [ENCRYPT.md](reference/file-operations/encrypt-file.md) |
| `SEND FILE` | File IO / Conn | [File Operations](reference/file-operations/README.md) (see also [TRANSFER.md](reference/file-operations/transfer.md)) | [SEND/FILE.md](reference/file-operations/file.md) |
| `RECEIVE FILE` | File IO / Conn | [File Operations](reference/file-operations/README.md) (see also [TRANSFER.md](reference/file-operations/transfer.md)) | [RECEIVE/FILE.md](reference/file-operations/file.md) |
| `SEND EMAIL` | Notifications | [File Operations](reference/file-operations/README.md) | [SEND/EMAIL.md](reference/file-operations/send-email.md) |
| `DOCKER` | Containers | [File Operations](reference/file-operations/README.md) | [DOCKER.md](reference/file-operations/docker.md) |
| `WAITFOR FILE UNLOCKED` | File IO | [Advanced File Operations](reference/file-operations/advanced-file-operations.md) | [WAITFOR.FILE.UNLOCKED.md](reference/file-operations/waitfor-file-unlocked.md) |
| `CONVERT FILE ENCODING` | File IO | [Advanced File Operations](reference/file-operations/advanced-file-operations.md) | [CONVERT.FILE.ENCODING.md](reference/file-operations/convert-file-encoding.md) |
| `SPLIT FILE` | File IO | [Advanced File Operations](reference/file-operations/advanced-file-operations.md) | [SPLIT.FILE.md](reference/file-operations/split-file.md) |
| `MERGE FILES` | File IO | [Advanced File Operations](reference/file-operations/advanced-file-operations.md) | [MERGE.FILES.md](reference/file-operations/merge-files.md) |
| `SYNC DIRECTORY` | File IO | [Advanced File Operations](reference/file-operations/advanced-file-operations.md) | [SYNC.DIRECTORY.md](reference/file-operations/sync-directory.md) |
| `VERIFY FILE INTEGRITY` | File IO | [Advanced File Operations](reference/file-operations/advanced-file-operations.md) | [VERIFY.FILE.INTEGRITY.md](reference/file-operations/verify-file-integrity.md) |
| `CREATE JOB` | Orchestration | [Statement Reference](reference/statements/README.md) | [SCHEDULE.md](reference/orchestrator-jobs/schedule.md) |
| `KILL JOB` | Orchestration | [Statement Reference](reference/statements/README.md) | [KILL.md](reference/orchestrator-jobs/kill.md) |
| `CREATE INDEX` | DDL | [Statement Reference](reference/statements/README.md) | [CREATE.md](reference/statements/ddl/create.md) |
| `CREATE PROCEDURE` | DDL | [Statement Reference](reference/statements/README.md) | [CREATE.md](reference/statements/ddl/create.md) |
| `CREATE FUNCTION` | DDL | [Statement Reference](reference/statements/README.md) | [CREATE.md](reference/statements/ddl/create.md) |
| `CREATE VIEW` | DDL / Query Alias | [Statement Reference](reference/statements/README.md) | [CREATE.md](reference/statements/ddl/create.md) |
| `ALTER VIEW` | DDL / Query Alias | [Statement Reference](reference/statements/README.md) | [ALTER.md](reference/statements/ddl/alter.md) |
| `DROP VIEW` | DDL / Query Alias | [Statement Reference](reference/statements/README.md) | [DROP.md](reference/statements/ddl/drop.md) |
| `eng.views` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.views](reference/eng/views.md) |
| `DIRECTORY Operations` | File IO | [Statement Reference](reference/statements/README.md) | [DIRECTORY Operations](reference/file-operations/directory.md) |
| `` | File IO / Conn | [Statement Reference](reference/statements/README.md) | [](reference/file-operations/receive-file.md) |
| `` | File IO / Conn | [Statement Reference](reference/statements/README.md) | [](reference/file-operations/send-file.md) |
| `eng.active_sessions` | Portal Admin | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `Service Accounts` | Portal Admin | [Statement Reference](reference/statements/README.md) | [Service Accounts](reference/portal-commands/service-accounts.md) |
| `Admin Identity CLI` | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [admin identity cli](reference/portal-commands/admin-identity-cli.md) |
| `PORTAL SHOW` | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [portal show](reference/portal-commands/portal-show.md) |
| `EXPECT SCHEMA` | DDL / Validation | [Statement Reference](reference/statements/README.md) | [EXPECT SCHEMA](reference/statements/ddl/expect-schema.md) |
| `TEST CONNECTION` | DDL / Diagnostics | [Statement Reference](reference/statements/README.md) | [TEST CONNECTION](reference/statements/ddl/test-connection.md) |
| `CREATE BINDING` | DDL / Declarative | [Statement Reference](reference/statements/README.md) | [CREATE BINDING](reference/statements/ddl/create-binding.md) |
| `CREATE TOOL` | DDL / Machine Tools | [Statement Reference](reference/statements/README.md) | [CREATE TOOL](reference/statements/ddl/create-tool.md) |
| `EXECUTE TOOL` | DML / Machine Tools | [Statement Reference](reference/statements/README.md) | [EXECUTE TOOL](reference/statements/dml/execute-tool.md) |
| `PAGE_LAYOUT` | Reporting DDL | [Statement Reference](reference/statements/README.md) | [PAGE_LAYOUT](reference/statements/ddl/page_layout.md) |
| `PRINT_LAYOUT` | Reporting DDL | [Statement Reference](reference/statements/README.md) | [PRINT_LAYOUT](reference/statements/ddl/print_layout.md) |
| `UNNEST / FLATTEN` | DML / Query | [Statement Reference](reference/statements/README.md) | [UNNEST / FLATTEN](reference/statements/dml/unnest.md) |
| `Execution Blocks` | Flow Control | [Statement Reference](reference/statements/README.md) | [Execution Blocks](reference/statements/execution-blocks.md) |
| `Expressions and Operators` | Expressions | [Statement Reference](reference/statements/README.md) | [Expressions and Operators](reference/statements/expressions-and-operators.md) |
| `Procedures and Functions` | Procedures | [Statement Reference](reference/statements/README.md) | [Procedures and Functions](reference/statements/procedures.md) |
| `Containerized Test Databases (USE DOCKER)` | Containers | [Statement Reference](reference/statements/README.md) | [Containerized Test Databases (USE DOCKER)](reference/statements/use-docker.md) |
| `ASOF JOIN` | Join Syntax | [Statement Reference](reference/statements/README.md) | [ASOF JOIN](reference/statements/query-syntax/asof-join.md) |
| `IS [NOT] DISTINCT FROM` | Expressions | [Statement Reference](reference/statements/README.md) | [IS [NOT] DISTINCT FROM](reference/statements/query-syntax/is-distinct-from.md) |
| `LATERAL` | Query Clauses | [Statement Reference](reference/statements/README.md) | [LATERAL](reference/statements/query-syntax/lateral.md) |
| `Set Operations` | Set Operations | [Statement Reference](reference/statements/README.md) | [Set Operations](reference/statements/query-syntax/set-operations.md) |
| `WINDOW` | Window Functions | [Statement Reference](reference/statements/README.md) | [WINDOW](reference/statements/query-syntax/window.md) |
| `` | Date/Time | [Statement Reference](reference/statements/README.md) | [](reference/functions/datetime/reldate.md) |
| `Data Types` | Data Types | [Statement Reference](reference/statements/README.md) | [Data Types](reference/data-types.md) |
| `eng.alerts()` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.catalog_search()` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.connection_config` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [eng.connection_config](reference/eng/connection-config.md) |
| `eng.data_quality_failures` | Diagnostics / Orchestrator | [Engine Catalog](reference/eng/README.md) | [eng.data_quality_failures](reference/eng/data-quality-failures.md) |
| `eng.data_quality_rules` | Diagnostics / Orchestrator | [Engine Catalog](reference/eng/README.md) | [eng.data_quality_rules](reference/eng/data-quality-rules.md) |
| `eng.data_quality_status` | Diagnostics / Orchestrator | [Engine Catalog](reference/eng/README.md) | [eng.data_quality_status](reference/eng/data-quality-status.md) |
| `eng.stewardship_gaps` | Diagnostics / Orchestrator | [Engine Catalog](reference/eng/README.md) | [eng.stewardship_gaps](reference/eng/stewardship-gaps.md) |
| `eng.stewardship_score` | Diagnostics / Orchestrator | [Engine Catalog](reference/eng/README.md) | [eng.stewardship_score](reference/eng/stewardship-score.md) |
| `eng.effective_permissions()` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.embed_tokens()` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.favorites()` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.host_metrics` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [eng.host_metrics](reference/eng/host-metrics.md) |
| `eng.job_history` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [eng.job_history](reference/eng/job-history.md) |
| `eng.job_state` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [eng.job_state](reference/eng/job-state.md) |
| `eng.jobs` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [eng.jobs](reference/eng/jobs.md) |
| `eng.usage_metrics()` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.recent_reports()` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.reports` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.report_dependencies()` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.report_history()` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.saved_views()` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.share_links()` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.subscriptions` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.tables` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [eng.tables](reference/eng/tables.md) |
| `eng.version` | Diagnostics / Portal | [Engine Catalog](reference/eng/README.md) | [eng.version](reference/eng/version.md) |
| `eng.bundle_dependencies` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.bundle_dependencies](reference/eng/bundle-dependencies.md) |
| `eng.bundle_files` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.bundle_files](reference/eng/bundle-files.md) |
| `eng.bundles` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.bundles](reference/eng/bundles.md) |
| `eng.capabilities` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.capabilities](reference/eng/capabilities.md) |
| `eng.columns` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.columns](reference/eng/columns.md) |
| `eng` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng](reference/eng/eng.md) |
| `eng.effective_permissions` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.effective_permissions](reference/eng/effective-permissions.md) |
| `eng.job_statement_metrics` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.job_statement_metrics](reference/eng/job-statement-metrics.md) |
| `eng.lineage_history` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.lineage_history](reference/eng/lineage-history.md) |
| `eng.lineage` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.lineage](reference/eng/lineage.md) |
| `eng.locks` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.locks](reference/eng/locks.md) |
| `eng.missing_tags` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.missing_tags](reference/eng/missing-tags.md) |
| `eng.portal_catalog` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.portal_catalog](reference/eng/portal-catalog.md) |
| `eng.protected_data_suggestions` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.protected_data_suggestions](reference/eng/protected-data-suggestions.md) |
| `eng.protected_data` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.protected_data](reference/eng/protected-data.md) |
| `eng.safe_zones` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.safe_zones](reference/eng/safe-zones.md) |
| `eng.sessions` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.sessions](reference/eng/sessions.md) |
| `eng.tags` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.tags](reference/eng/tags.md) |
| `eng.tenant_context` | Diagnostics | [Engine Catalog](reference/eng/README.md) | [eng.tenant_context](reference/eng/tenant-context.md) |
| `GENERATE` | DML | [Statement Reference](reference/statements/README.md) | [GENERATE.md](reference/statements/session-control/generate.md) |
| `CASE` | Expressions | [Statement Reference](reference/statements/README.md) | [CASE.md](reference/statements/query-syntax/case.md) |
| `WITH` | CTE | [Statement Reference](reference/statements/README.md) | [WITH.md](reference/statements/query-syntax/with.md) |
| `WITH RECURSIVE` | CTE | [Statement Reference](reference/statements/README.md) | [WITH.md](reference/statements/query-syntax/with.md) |
| `WITH (WATERMARK = ...)` | Query / Incremental ETL | [Statement Reference](reference/statements/README.md) | [WATERMARK.md](reference/statements/query-syntax/watermark.md) |
| `PIVOT` / `UNPIVOT` | DML / Transform | [Statement Reference](reference/statements/README.md) | [PIVOT.md](reference/statements/query-syntax/pivot.md) |
| `MATCH_RECOGNIZE` | DML / Pattern Matching | [MATCH_RECOGNIZE](reference/statements/query-syntax/match-recognize.md) | [MATCH_RECOGNIZE.md](reference/statements/query-syntax/match-recognize.md) |
| `EXPORT REPORT` | Orchestration | [Statement Reference](reference/statements/README.md) | [EXPORT.md](reference/orchestrator-jobs/export.md) |
| `EXPORT REPORT ... WITH (PDF_MODE = ...)` | Reporting / Export | `PDF_MODE = STATIC\|AUTO\|HOSTED\|BROWSER`, `HOST`, `BROWSER_PATH` | [EXPORT.md](reference/orchestrator-jobs/export.md) |
| `SUBSCRIPTION` | Orchestration | [Statement Reference](reference/statements/README.md) | [SUBSCRIPTION.md](reference/orchestrator-jobs/subscription.md) |
| `RELDATE` | Variables | [RelativeDate_Parameters.md](reference/functions/datetime/reldate.md) | [RELDATE.md](reference/functions/datetime/reldate.md) |
| `RAISEERROR` | Flow Control | [Statement Reference](reference/statements/README.md) | [THROW.md](reference/control-flow/throw.md) |
| `HELP` | Diagnostics | [Statement Reference](reference/statements/README.md) | [HELP.md](reference/statements/session-control/help.md) |
| `ANALYZE` | Diagnostics | [Statement Reference](reference/statements/README.md) | [ANALYZE.md](reference/statements/session-control/analyze.md) |
| `RENAME FILE` | File IO | [File Operations](reference/file-operations/README.md) | [FILE.md](reference/file-operations/file.md) |
| `COMPRESS FILE` | File IO | [File Operations](reference/file-operations/README.md) | [COMPRESS.md](reference/file-operations/compress-file.md) |
| `DECOMPRESS FILE` | File IO | [File Operations](reference/file-operations/README.md) | [FILE.md](reference/file-operations/file.md) |
| `DECRYPT FILE` | File IO | [File Operations](reference/file-operations/README.md) | [FILE.md](reference/file-operations/file.md) |
| `CREATE DIRECTORY` | Dir IO | [File Operations](reference/file-operations/README.md) | [CREATE.md](reference/statements/ddl/create.md) |
| `COPY DIRECTORY` | Dir IO | [File Operations](reference/file-operations/README.md) | [COPY.md](reference/file-operations/copy-file.md) |
| `MOVE DIRECTORY` | Dir IO | [File Operations](reference/file-operations/README.md) | [DIRECTORY.md](reference/functions/file-path/directory.md) |
| `RENAME DIRECTORY` | Dir IO | [File Operations](reference/file-operations/README.md) | [DIRECTORY.md](reference/functions/file-path/directory.md) |
| `DELETE DIRECTORY` | Dir IO | [File Operations](reference/file-operations/README.md) | [DELETE.md](reference/statements/dml/delete.md) |
| `DELETE DIRECTORY_CONTENTS`| Dir IO | [File Operations](reference/file-operations/README.md) | [DIRECTORY.md](reference/functions/file-path/directory.md) |
| `COMPRESS DIRECTORY` | Dir IO | [File Operations](reference/file-operations/README.md) | [COMPRESS.md](reference/file-operations/compress-file.md) |
| `DECOMPRESS DIRECTORY` | Dir IO | [File Operations](reference/file-operations/README.md) | [DIRECTORY.md](reference/functions/file-path/directory.md) |
| `ENCRYPT DIRECTORY` | Dir IO | [File Operations](reference/file-operations/README.md) | [ENCRYPT.md](reference/file-operations/encrypt-file.md) |
| `DECRYPT DIRECTORY` | Dir IO | [File Operations](reference/file-operations/README.md) | [DIRECTORY.md](reference/functions/file-path/directory.md) |
| `CREATE SSH_KEY_PAIR` | Security | [File Operations](reference/file-operations/README.md) | [CREATE.SSH_KEY_PAIR.md](reference/file-operations/create-ssh-key-pair.md) |
| `CREATE PGP_KEY_PAIR` | Security | [File Operations](reference/file-operations/README.md) | [CREATE.PGP_KEY_PAIR.md](reference/file-operations/create-pgp-key-pair.md) |
| `START DOCKER` | Containers | [File Operations](reference/file-operations/README.md) | [DOCKER.md](reference/file-operations/docker.md) |
| `STOP DOCKER` | Containers | [File Operations](reference/file-operations/README.md) | [DOCKER.md](reference/file-operations/docker.md) |
| `PAUSE DOCKER` | Containers | [File Operations](reference/file-operations/README.md) | [DOCKER.md](reference/file-operations/docker.md) |
| `CLOSE DOCKER` | Containers | [File Operations](reference/file-operations/README.md) | [DOCKER.md](reference/file-operations/docker.md) |
| `CREATE USER` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_USER.md](reference/portal-commands/portal-user.md) |
| `ALTER USER` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_USER.md](reference/portal-commands/portal-user.md) |
| `DROP USER` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_USER.md](reference/portal-commands/portal-user.md) |
| `DISCONNECT USER` | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_USER.md](reference/portal-commands/portal-user.md) |
| `REVOKE TOKENS FOR USER` | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_USER.md](reference/portal-commands/portal-user.md) |
| `CREATE GROUP` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_GROUP.md](reference/portal-commands/portal-group.md) |
| `DROP GROUP` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_GROUP.md](reference/portal-commands/portal-group.md) |
| `ADD USER TO GROUP` | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_GROUP.md](reference/portal-commands/portal-group.md) |
| `CREATE FOLDER` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_FOLDER.md](reference/portal-commands/portal-folder.md) |
| `DROP FOLDER` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_FOLDER.md](reference/portal-commands/portal-folder.md) |
| `GRANT` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_PERMISSIONS.md](reference/portal-commands/portal-permissions.md) |
| `REVOKE` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [REVOKE.md](reference/portal-commands/revoke.md) |
| `FAVORITE REPORT` | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [FAVORITE.md](reference/portal-commands/favorite.md) |
| `UNFAVORITE REPORT` | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [FAVORITE.md](reference/portal-commands/favorite.md) |
| `PUBLISH REPORT` | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_REPORT.md](reference/portal-commands/portal-report.md) |
| `ALTER REPORT` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_REPORT.md](reference/portal-commands/portal-report.md) |
| `DROP REPORT` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_REPORT.md](reference/portal-commands/portal-report.md) |
| `REFRESH REPORT` | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_REPORT.md](reference/portal-commands/portal-report.md) |
| `CREATE SHARE LINK` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_SHARE.md](reference/portal-commands/portal-share.md) |
| `REVOKE SHARE LINK` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_SHARE.md](reference/portal-commands/portal-share.md) |
| `CREATE SAVED VIEW` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_SAVEDVIEW.md](reference/portal-commands/portal-savedview.md) |
| `DROP SAVED VIEW` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_SAVEDVIEW.md](reference/portal-commands/portal-savedview.md) |
| `CREATE ALERT` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_ALERT.md](reference/portal-commands/portal-alert.md) |
| `DROP ALERT` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_ALERT.md](reference/portal-commands/portal-alert.md) |
| `CREATE CONNECTION ... AS SMTP` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_SMTP.md](reference/portal-commands/portal-smtp.md) |
| `DROP CONNECTION` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_SMTP.md](reference/portal-commands/portal-smtp.md) |
| `CREATE SUBSCRIPTION` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_SUBSCRIPTION.md](reference/portal-commands/portal-subscription.md) |
| `DROP SUBSCRIPTION` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_SUBSCRIPTION.md](reference/portal-commands/portal-subscription.md) |
| `REFRESH DATASET` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_DATASET.md](reference/portal-commands/portal-dataset.md) |
| `ALTER DATASET` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_DATASET.md](reference/portal-commands/portal-dataset.md) |
| `DROP DATASET` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_DATASET.md](reference/portal-commands/portal-dataset.md) |
| `REBUILD SNAPSHOT` | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_DATASET.md](reference/portal-commands/portal-dataset.md) |
| `DROP SNAPSHOT` (portal) | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_DATASET.md](reference/portal-commands/portal-dataset.md) |
| `CREATE JOB ... FOR REPORT` | Orchestrator | [Job Orchestration](reference/orchestrator-jobs/schedule.md) | [PORTAL_REFRESHJOB.md](reference/portal-commands/portal-refreshjob.md) |
| `ALTER JOB ... ADD SCHEDULE` | Orchestrator | [Job Orchestration](reference/orchestrator-jobs/schedule.md) | [PORTAL_REFRESHJOB.md](reference/portal-commands/portal-refreshjob.md) |
| `eng.users` (portal) | Portal Admin | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.reports` (portal) | Portal Admin | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `eng.active_sessions` (portal) | Portal Admin | [Engine Catalog](reference/eng/README.md) | [Portal eng.* Catalog](reference/eng/portal-catalog.md) |
| `RESTART PORTAL` | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_ADMIN.md](reference/portal-commands/portal-admin.md) |
| `SHUTDOWN PORTAL` | Portal Admin | [Portal Admin](reference/portal-commands/README.md) | [PORTAL_ADMIN.md](reference/portal-commands/portal-admin.md) |

---

## 2. Data Connectors

Connectors define how to communicate with external data sources.

| Connector | Type | Help File | Supported Options |
| :--- | :--- | :--- | :--- |
| `MSSQL` | SQL | [MSSQL.md](reference/connectors/databases/mssql.md) | HOST, DATABASE, USER, PASSWORD, TRUSTED_CONNECTION, ... |
| `POSTGRES` | SQL | [POSTGRES.md](reference/connectors/databases/postgres.md) | HOST, PORT, DATABASE, USER, PASSWORD, SSL_MODE, ... |
| `ORACLE` | SQL | [ORACLE.md](reference/connectors/databases/oracle.md) | HOST, PORT, SERVICE_NAME, USER, PASSWORD, ... |
| `SQLITE` | SQL | [SQLITE.md](reference/connectors/databases/sqlite.md) | DATABASE, TIMEOUT_SECONDS, TABLE |
| `MYSQL` | SQL | [MYSQL.md](reference/connectors/databases/mysql.md) | HOST, PORT, DATABASE, USER, PASSWORD, SSL_MODE, ALLOW_PUBLIC_KEY_RETRIEVAL, ALLOW_USER_VARIABLES, ... |
| `ODBC` | SQL | [ODBC.md](reference/connectors/databases/odbc.md) | DSN, DRIVER, SERVER, DATABASE, UID, PASSWORD, ... |
| `SNOWFLAKE` | SQL | [SNOWFLAKE.md](reference/connectors/databases/snowflake.md) | HOST, DATABASE, SCHEMA, WAREHOUSE, USERNAME, PASSWORD, PRIVATE_KEY_FILE, ... |
| `BIGQUERY` | SQL | [BIGQUERY.md](reference/connectors/databases/bigquery.md) | PROJECT_ID, DATASET_ID, KEY_FILE, ... |
| `MONGODB` | NoSQL Document | [MONGODB.md](reference/connectors/databases/mongodb.md) | CONNECTION_STRING, DATABASE, COLLECTION, HOST, PORT, USER, PASSWORD, TIMEOUT_SECONDS |
| `NEO4J` | Graph Database | [NEO4J.md](reference/connectors/databases/neo4j.md) | CONNECTION_STRING, URI, DATABASE, USER, PASSWORD, TIMEOUT_SECONDS, HOST, PORT, PROTOCOL, KEY_COLUMNS, FROM_LABEL, TO_LABEL, FROM_KEY_COLUMN, TO_KEY_COLUMN, SKIP_MISSING_ENDPOINTS, SCHEMA_SAMPLE_SIZE |
| `FLATFILE` | File | [FLATFILE.md](reference/connectors/files/flatfile.md) | PATH, FORMAT, DELIMITER, HEADER, ENCODING, ... |
| `EXCEL` | File | [EXCEL.md](reference/connectors/files/excel.md) | PATH, SHEET, RANGE, HEADER, ... |
| `JSON` | File | [JSON.md](reference/connectors/files/json.md) | PATH, ROOT_PATH, ENCODING, ... |
| `XML` | File | [XML.md](reference/connectors/files/xml.md) | PATH, ROOT_PATH, ENCODING, ... |
| `PARQUET` | File | [PARQUET.md](reference/connectors/files/parquet.md) | PATH, COMPRESSION, ... |
| `AVRO` | File | [AVRO.md](reference/connectors/files/avro.md) | PATH, ... |
| `Transactional Writes` | File Ops | [Transactional Writes](reference/connectors/files/transactional-writes.md) | Atomic and isolated file overwrite patterns |
| `SFTP` | Transfer | [SFTP.md](reference/connectors/services/sftp.md) | HOST, PORT, USER, PASSWORD, KEYFILE, PASSPHRASE |
| `FTP` | Transfer | [FTP.md](reference/connectors/services/ftp.md) | HOST, PORT, USER, PASSWORD, USE_SSL |
| `AZURE_BLOB` | Transfer | [AZURE_BLOB.md](reference/connectors/services/azure-blob.md) | ACCOUNT_NAME, ACCOUNT_KEY, CONTAINER |
| `S3` | Transfer | [S3.md](reference/connectors/services/s3.md) | BUCKET, ENDPOINT, ACCESS_KEY, SECRET_KEY, REGION, FORCE_PATH_STYLE |
| `API` / `REST` | Service | [API.md](reference/connectors/services/api.md) | URL, METHOD, AUTH_TYPE, TOKEN, BODY, ROOT_PATH, ... |
| `SMTP` | Service | [SMTP.md](reference/connectors/services/smtp.md) | HOST, PORT, USER, PASSWORD, USE_SSL, DEFAULT_FROM |
| `DIRECTORY` | Service | [DIRECTORY](reference/connectors/services/directory.md) | Treats a local or UNC filesystem folder as a data source for file-management operations... |
| `PORTAL` | Service | [PORTAL](reference/connectors/services/portal.md) | Admin service connector for an ETL-SQL Portal service. Does not transfer data — stateme... |
| `SHAREPOINT` | Transfer/Service | [SHAREPOINT.md](reference/connectors/services/sharepoint.md) | URL, AUTH_MODE, USER, PASSWORD, DOMAIN, CLIENT_ID, CLIENT_SECRET, TENANT_ID, DOCUMENT_LIBRARY, LIST_NAME |
| `KAFKA` | Streaming | [KAFKA.md](reference/connectors/services/kafka.md) | BOOTSTRAP_SERVERS, TOPIC, GROUP_ID, AUTO_OFFSET_RESET, TIMEOUT_MS, MAX_MESSAGES, SASL_USERNAME, SASL_PASSWORD, SASL_MECHANISM, SECURITY_PROTOCOL |
| `DIRECTORY` | Service | [DIRECTORY.md](reference/functions/file-path/directory.md) | PATH, RECURSIVE, ... |
| `MOCKDB` | Testing | [MOCKDB.md](reference/connectors/services/mockdb.md) | - |
| `PORTAL` | Admin Service | [Portal Admin](reference/portal-commands/README.md) | HOST, PORT, USER, PASSWORD |
| `ORCHESTRATOR` | Admin Service | [Orchestrator Connector](reference/connectors/services/orchestrator.md) | HOST, PORT, API_KEY |
| `ACTIVE_DIRECTORY` | Admin Service | [ACTIVE_DIRECTORY.md](reference/connectors/services/active-directory.md) | HOST, PORT, USE_SSL, AUTH_MODE, USER, PASSWORD, DOMAIN, BASE_DN, FILTER_CONTEXT, FILTER, ATTRIBUTES |
| `WEBHOOK` | Service | [WEBHOOK.md](reference/connectors/services/webhook.md) | URL, METHOD, AUTH_TYPE, TOKEN, BODY, ... |

### 2.1 File-Based Table Alias
`FILE` is the default table name used when querying any file-based connection (e.g. `SELECT * FROM src` where `src` is a FLATFILE connection).

### 2.2 Connector Aliases
`CSV` is an accepted alias for `FLATFILE` in `CREATE CONNECTION` statements.

---

## 3. Standard Library (Functions)

Functions used within `SELECT`, `WHERE`, `SET`, and other expressions.

| Function | Category | Help File | Description |
| :--- | :--- | :--- | :--- |
| `UPPER(string)` | String | [UPPER.md](reference/functions/string/upper.md) | Converts string to uppercase |
| `Data Type Conversion` | Conversion | [Data Type Conversion](reference/functions/conversion/data-conversion.md) | Convert values between ETL-SQL [data types](reference/data-types.md). Use [`CAST`](reference/functions/conversion/cast.md)... |
| `DATE_PART` | Date/Time | [DATE_PART](reference/functions/datetime/date_part.md) | Extracts a specified date part component from a date as an integer value. |
| `DATETRUNC` | Date/Time | [DATETRUNC](reference/functions/datetime/datetrunc.md) | Truncates a date to the beginning of the specified date part boundary. |
| `GET_JOB_STATE` | Job | [GET_JOB_STATE](reference/functions/job/get_job_state.md) | Returns the saved state value for the current script or job execution context. |
| `SET_JOB_STATE` | Job | [SET_JOB_STATE](reference/functions/job/set_job_state.md) | Sets a saved state value for the current script or job execution context. |
| `TRUNCATE` | Math | [TRUNCATE](reference/functions/math/truncate.md) | Truncates a number to a specified number of decimal places without rounding. |
| `IS_NULL` | Null Handling | [IS_NULL](reference/functions/null-handler/is_null.md) | Returns whether an expression evaluates to `NULL`. |
| `REMOVE_HIDDEN_CHARACTERS` | String | [REMOVE_HIDDEN_CHARACTERS](reference/functions/string/remove_hidden_characters.md) | Cleans invisible and whitespace-class characters out of a string. A specialized form of... |
| `REMOVE_HTML_CHARACTERS` | String | [REMOVE_HTML_CHARACTERS](reference/functions/string/remove_html_characters.md) | Decodes HTML entities and normalizes typographic ("smart") Unicode to plain ASCII, fixi... |
| `LOWER(string)` | String | [LOWER.md](reference/functions/string/lower.md) | Converts string to lowercase |
| `CONCAT(string1, string2, ...)` | String | [CONCAT.md](reference/functions/string/concat.md) | Concatenates multiple strings |
| `LEN(string)` / `LENGTH(string)` | String | [LEN.md](reference/functions/string/len.md) / [LENGTH.md](reference/functions/string/length.md) | Returns string length |
| `SUBSTRING(string, start, length)` | String | [SUBSTRING.md](reference/functions/string/substring.md) | Returns part of a string |
| `TRIM(string)` | String | [TRIM.md](reference/functions/string/trim.md) | Removes leading/trailing whitespace |
| `REPLACE(string, find, replacement)` | String | [REPLACE.md](reference/functions/string/replace.md) | Replaces occurrences of a substring |
| `CHARINDEX(find, string)` | String | [CHARINDEX.md](reference/functions/string/charindex.md) | Returns index of first occurrence |
| `INITCAP(string)` | String | [INITCAP.md](reference/functions/string/initcap.md) | Capitalizes first letter of each word |
| `LTRIM(string)` | String | [LTRIM.md](reference/functions/string/ltrim.md) | Removes leading whitespace |
| `RTRIM(string)` | String | [RTRIM.md](reference/functions/string/rtrim.md) | Removes trailing whitespace |
| `REVERSE(string)` | String | [REVERSE.md](reference/functions/string/reverse.md) | Reverses string characters |
| `LEFT(string, count)` | String | [LEFT.md](reference/functions/string/left.md) | Returns leftmost N characters |
| `RIGHT(string, count)` | String | [RIGHT.md](reference/functions/string/right.md) | Returns rightmost N characters |
| `LPAD(string, length, [pad_string])` | String | [LPAD.md](reference/functions/string/lpad.md) | Left-pads a string to length |
| `RPAD(string, length, [pad_string])` | String | [RPAD.md](reference/functions/string/rpad.md) | Right-pads a string to length |
| `INSTR(string, find)` | String | [INSTR.md](reference/functions/string/instr.md) | Alias for POSITION |
| `CONCAT_WS(separator, string1, ...)` | String | [CONCAT_WS.md](reference/functions/string/concat_ws.md) | Join with separator; skips nulls |
| `SPLIT_PART(string, delimiter, part)` | String | [SPLIT_PART.md](reference/functions/string/split_part.md) | Returns Nth segment after split |
| `SPACE(count)` | String | [SPACE.md](reference/functions/string/space.md) | Returns N space characters |
| `TO_STR(value)` | String | [TO_STR.md](reference/functions/string/to_str.md) | Converts any value to string |
| `PATINDEX(pattern, string)` | String | [PATINDEX.md](reference/functions/string/patindex.md) | Position of wildcard pattern |
| `REPLICATE(string, count)` | String | [REPLICATE.md](reference/functions/string/replicate.md) | Repeats string N times |
| `REPEAT(string, count)` | String | [REPEAT.md](reference/functions/string/repeat.md) | Alias for REPLICATE |
| `QUOTENAME(string, [delimiter])` | String | [QUOTENAME.md](reference/functions/string/quotename.md) | Returns delimited identifier |
| `ASCII(string)` | String | [ASCII.md](reference/functions/string/ascii.md) | Numeric code of first character |
| `UNICODE(string)` | String | [UNICODE.md](reference/functions/string/unicode.md) | Unicode code of first character |
| `CHAR(code)` | String | [CHAR.md](reference/functions/string/char.md) | Character for given code |
| `DATALENGTH(value)` | String | [DATALENGTH.md](reference/functions/string/datalength.md) | Byte count of value |
| `TRANSLATE(string, find_chars, replace_chars)` | String | [TRANSLATE.md](reference/functions/string/translate.md) | Replaces chars 1-to-1 |
| `STRING_ESCAPE(text, type)` | String | [STRING_ESCAPE.md](reference/functions/string/string_escape.md) | Escapes special characters |
| `STRING_SPLIT(string, delimiter)` | String | [STRING_SPLIT.md](reference/functions/string/string_split.md) | Table-valued split |
| `CHAR_LENGTH(string)` | String | [CHAR_LENGTH.md](reference/functions/string/char_length.md) | String length (SQL standard alias) |
| `OVERLAY(string, replacement, start, length)` | String | [OVERLAY.md](reference/functions/string/overlay.md) | Replaces substring at position |
| `POSITION(find IN string)` | String | [POSITION.md](reference/functions/string/position.md) | Position of substring (SQL standard) |
| `SUBSTR(string, start, length)` | String | [SUBSTR.md](reference/functions/string/substr.md) | Alias for SUBSTRING |
| `STUFF(string, start, length, replacement)` | String | [STUFF.md](reference/functions/string/stuff.md) | Deletes part of string and inserts replacement |
| `STR(number, [length], [decimals])` | String | [STR.md](reference/functions/string/str.md) | Formats number as string |
| `CLEAN_STRING(string)` | String | [CLEAN_STRING.md](reference/functions/string/clean_string.md) | Clean string |
| `MASK_EMAIL(string)` | String | [MASK_EMAIL.md](reference/functions/string/mask_email.md) | Mask email |
| `MASK_PHONE(string)` | String | [MASK_PHONE.md](reference/functions/string/mask_phone.md) | Mask phone |
| `MASK_SSN(string)` | String | [MASK_SSN.md](reference/functions/string/mask_ssn.md) | Mask SSN |
| `GETDATE()` | Date | [GETDATE.md](reference/functions/datetime/getdate.md) | Current local datetime |
| `SYSDATE()` | Date | [SYSDATE.md](reference/functions/datetime/sysdate.md) | Current system datetime (Oracle style) |
| `NOW()` | Date | [NOW.md](reference/functions/datetime/now.md) | Current UTC datetime |
| `DATEADD(datepart, number, date)` | Date | [DATEADD.md](reference/functions/datetime/dateadd.md) | Adds units to a date |
| `DATEDIFF(datepart, start_date, end_date)` | Date | [DATEDIFF.md](reference/functions/datetime/datediff.md) | Difference between dates |
| `DATENAME(datepart, date)` | Date | [DATENAME.md](reference/functions/datetime/datename.md) | Returns name of date part |
| `DATEPART(datepart, date)` | Date | [DATEPART.md](reference/functions/datetime/datepart.md) | Returns integer date part |
| `DATE_PART(datepart, date)` | Date | [DATE_PART.md](reference/functions/datetime/datepart.md) | Postgres-style datepart extractor |
| `EXTRACT(datepart FROM date)` | Date | [EXTRACT.md](reference/functions/datetime/extract.md) | SQL-standard datepart extractor |
| `EOMONTH(date)` | Date | [EOMONTH.md](reference/functions/datetime/eomonth.md) | Last day of the month |
| `ISDATE(string)` | Date | [ISDATE.md](reference/functions/datetime/isdate.md) | 1 if parseable as date |
| `TO_TIMESTAMP(string, [format])` | Date | [TO_TIMESTAMP.md](reference/functions/datetime/to_timestamp.md) | Parses string to a timestamp |
| `TO_DATE(string, [format])` | Date | [TO_DATE.md](reference/functions/datetime/to_date.md) | Converts a string to a date |
| `RELDATE(expression)` | Date | [RELDATE.md](reference/functions/datetime/reldate.md) | Resolves relative date expression (e.g. 'D-7', 'M-1') |
| `DATETIMEFROMPARTS(year, month, day, hour, minute, second, ms)` | Date | [DATETIMEFROMPARTS.md](reference/functions/datetime/datetimefromparts.md) | Build DATETIME from components |
| `DATETIMEOFFSETSFROMPARTS(year, month, day, hour, minute, second, fractions, hour_offset, minute_offset, precision)` | Date | [DATETIMEOFFSETSFROMPARTS.md](reference/functions/datetime/datetimeoffsetsfromparts.md) | Build DATETIMEOFFSET from components |
| `TIMEFROMPARTS(hour, minute, second, fractions, precision)` | Date | [TIMEFROMPARTS.md](reference/functions/datetime/timefromparts.md) | Build TIME from components |
| `TRUNC(date)` | Date | [TRUNC.md](reference/functions/datetime/trunc.md) | Truncates time portion |
| `AT TIME ZONE(date, timezone)` | Date | [AT_TIME_ZONE.md](reference/dates-times/dates-times.md) | Converts to specified timezone |
| `CURRENT_DATE()` | Date | [CURRENT_DATE.md](reference/functions/datetime/current_date.md) | Current date (no time) |
| `CURRENT_TIME()` | Date | [CURRENT_TIME.md](reference/functions/datetime/current_time.md) | Current time |
| `CURRENT_TIMESTAMP()` | Date | [CURRENT_TIMESTAMP.md](reference/functions/datetime/current_timestamp.md) | Current datetime (UTC) |
| `DATETRUNC(datepart, date)` | Date | [DATETRUNC.md](reference/functions/datetime/date_trunc.md) | Truncates date to unit boundary |
| `DATE_TRUNC(datepart, date)` | Date | [DATE_TRUNC.md](reference/functions/datetime/date_trunc.md) | Postgres-style datetrunc function |
| `DAY(date)` | Date | [DAY.md](reference/functions/datetime/day.md) | Day-of-month component |
| `MONTH(date)` | Date | [MONTH.md](reference/functions/datetime/month.md) | Month component |
| `YEAR(date)` | Date | [YEAR.md](reference/functions/datetime/year.md) | Year component |
| `HOUR(date)` | Date | [HOUR.md](reference/functions/datetime/hour.md) | Hour component |
| `MINUTE(date)` | Date | [MINUTE.md](reference/functions/datetime/minute.md) | Minute component |
| `SECOND(date)` | Date | [SECOND.md](reference/functions/datetime/second.md) | Second component |
| `END_OF_MONTH(date)` | Date | [END_OF_MONTH.md](reference/functions/datetime/end_of_month.md) | End of month |
| `END_OF_QUARTER(date)` | Date | [END_OF_QUARTER.md](reference/functions/datetime/end_of_quarter.md) | End of quarter |
| `END_OF_WEEK(date)` | Date | [END_OF_WEEK.md](reference/functions/datetime/end_of_week.md) | End of week |
| `SAME_PERIOD_LAST_YEAR(date)` | Date | [SAME_PERIOD_LAST_YEAR.md](reference/functions/datetime/same_period_last_year.md) | Same period last year |
| `START_OF_MONTH(date)` | Date | [START_OF_MONTH.md](reference/functions/datetime/start_of_month.md) | Start of month |
| `START_OF_QUARTER(date)` | Date | [START_OF_QUARTER.md](reference/functions/datetime/start_of_quarter.md) | Start of quarter |
| `START_OF_WEEK(date)` | Date | [START_OF_WEEK.md](reference/functions/datetime/start_of_week.md) | Start of week |
| `ABS(number)` | Math | [ABS.md](reference/functions/math/abs.md) | Absolute value |
| `ROUND(number, decimals)` | Math | [ROUND.md](reference/functions/math/round.md) | Rounds to N decimal places |
| `FLOOR(number)` | Math | [FLOOR.md](reference/functions/math/floor.md) | Largest integer <= number |
| `CEILING(number)` | Math | [CEILING.md](reference/functions/math/ceiling.md) | Smallest integer >= number |
| `CEIL(number)` | Math | [CEIL.md](reference/functions/math/ceil.md) | Alias for CEILING |
| `RAND()` | Math | [RAND.md](reference/functions/math/rand.md) | Random number [0, 1) |
| `RANDOM()` | Math | [RANDOM.md](reference/functions/random-guid/random.md) | Alias for RAND() |
| `RANDOM_INT(min, max)` | Math | [RANDOM_INT.md](reference/functions/random-guid/random_int.md) | Random integer in range |
| `RANDOM_DECIMAL(min, max)` | Math | [RANDOM_DECIMAL.md](reference/functions/random-guid/random_decimal.md) | Random decimal in range |
| `MOD(number, divisor)` / `number % divisor` | Math | [MOD.md](reference/functions/math/mod.md) | Remainder of division |
| `POWER(base, exponent)` | Math | [POWER.md](reference/functions/math/power.md) | Base raised to exponent |
| `POW(base, exponent)` | Math | [POW.md](reference/functions/math/pow.md) | Alias for POWER |
| `SQRT(number)` | Math | [SQRT.md](reference/functions/math/sqrt.md) | Square root |
| `EXP(number)` | Math | [EXP.md](reference/functions/math/exp.md) | e raised to the power of number |
| `LOG(number)` / `LN(number)` | Math | [LOG.md](reference/functions/math/log.md) | Natural logarithm |
| `LOG10(number)` | Math | [LOG10.md](reference/functions/math/log10.md) | Base-10 logarithm |
| `LEAST(value1, value2, ...)` | Math | [LEAST.md](reference/functions/collections/least.md) | Smallest of arguments |
| `GREATEST(value1, value2, ...)` | Math | [GREATEST.md](reference/functions/collections/greatest.md) | Largest of arguments |
| `AGE_BUCKET(...)` | Math | [AGE_BUCKET.md](reference/functions/math/age_bucket.md) | Age bucket |
| `SAFE_DIVIDE(...)` | Math | [SAFE_DIVIDE.md](reference/functions/math/safe_divide.md) | Safe divide |
| `VALUE_BUCKET(...)` | Math | [VALUE_BUCKET.md](reference/functions/math/value_bucket.md) | Value bucket |
| `SIN(radians)` | Math | [SIN.md](reference/functions/math/sin.md) | Sine |
| `COS(radians)` | Math | [COS.md](reference/functions/math/cos.md) | Cosine |
| `TAN(radians)` | Math | [TAN.md](reference/functions/math/tan.md) | Tangent |
| `COT(radians)` | Math | [COT.md](reference/functions/math/cot.md) | Cotangent |
| `ASIN(number)` | Math | [ASIN.md](reference/functions/math/asin.md) | Arcsine |
| `ACOS(number)` | Math | [ACOS.md](reference/functions/math/acos.md) | Arccosine |
| `ATAN(number)` | Math | [ATAN.md](reference/functions/math/atan.md) | Arctangent |
| `ATAN2(y, x)` | Math | [ATAN2.md](reference/functions/math/atan2.md) | Arctangent of y/x |
| `SIGN(number)` | Math | [SIGN.md](reference/functions/math/sign.md) | Returns -1, 0, or 1 |
| `DEGREES(radians)` | Math | [DEGREES.md](reference/functions/math/degrees.md) | Converts radians to degrees |
| `RADIANS(degrees)` | Math | [RADIANS.md](reference/functions/math/radians.md) | Converts degrees to radians |
| `PI()` | Math | [PI.md](reference/functions/math/pi.md) | Mathematical constant Ï€ |
| `QUOTIENT(number, divisor)` | Math | [QUOTIENT.md](reference/functions/math/quotient.md) | Integer quotient of division |
| `TRUNCATE(number, decimals)` | Math | [TRUNCATE.md](reference/statements/dml/truncate.md) | Truncates number to N decimal places |
| `BITAND(a, b)` | Math | [BITAND.md](reference/functions/bitwise/bitand.md) | Bitwise AND |
| `BITOR(a, b)` | Math | [BITOR.md](reference/functions/bitwise/bitor.md) | Bitwise OR |
| `BITXOR(a, b)` | Math | [BITXOR.md](reference/functions/bitwise/bitxor.md) | Bitwise XOR |
| `BITNOT(a)` | Math | [BITNOT.md](reference/functions/bitwise/bitnot.md) | Bitwise NOT (negation) |
| `BITSHIFTLEFT(a, shift)` | Math | [BITSHIFTLEFT.md](reference/functions/bitwise/bitshiftleft.md) | Bitwise left shift |
| `BITSHIFTRIGHT(a, shift)` | Math | [BITSHIFTRIGHT.md](reference/functions/bitwise/bitshiftright.md) | Bitwise right shift |
| `BIT_COUNT(a)` | Math | [BIT_COUNT.md](reference/functions/bitwise/bit_count.md) | Number of set bits (popcount) |
| `COALESCE(value1, value2, ...)` | Logic | [COALESCE.md](reference/functions/null-handler/coalesce.md) | First non-null value |
| `ISNULL(value, default)` | Logic | [ISNULL.md](reference/functions/null-handler/isnull.md) | Returns default if value is null |
| `IIF(condition, true_value, false_value)` | Logic | [IIF.md](reference/functions/conversion/iif.md) | Inline IF |
| `NVL(value, default)` | Logic | [NVL.md](reference/functions/null-handler/nvl.md) | Alias for ISNULL |
| `IFNULL(value, default)` | Logic | [IFNULL.md](reference/functions/null-handler/ifnull.md) | Alias for ISNULL |
| `NVL2(value, not_null_result, null_result)` | Logic | [NVL2.md](reference/functions/null-handler/nvl2.md) | Oracle-style null conditional |
| `NULLIF(value1, value2)` | Logic | [NULLIF.md](reference/functions/null-handler/nullif.md) | NULL if value1 = value2 |
| `IS_NULL(value)` | Logic | [IS_NULL.md](reference/functions/null-handler/isnull.md) | 1 if value is null |
| `IS_NOT_NULL(value)` | Logic | [IS_NOT_NULL.md](reference/functions/null-handler/is_not_null.md) | 1 if value is not null |
| `DECODE(value, search1, result1, ..., [default])` | Logic | [DECODE.md](reference/functions/conversion/decode.md) | Oracle-style CASE shorthand |
| `CAST(value AS type)` | System | [CAST.md](reference/functions/conversion/cast.md) | Converts value to type |
| `TRY_CAST(value AS type)` | System | [TRY_CAST.md](reference/functions/conversion/try_cast.md) | Converts value to type, NULL on fail |
| `CONVERT(type, value)` | System | [CONVERT.md](reference/functions/conversion/convert.md) | Converts value to type |
| `TRY_CONVERT(type, value)` | System | [TRY_CONVERT.md](reference/functions/conversion/try_convert.md) | CONVERT with NULL on failure |
| `PARSE(string, type)` | System | [PARSE.md](reference/functions/conversion/parse.md) | Culture-aware string to type |
| `TRY_PARSE(string, type)` | System | [TRY_PARSE.md](reference/functions/conversion/try_parse.md) | PARSE with NULL on failure |
| `HASHBYTES(algorithm, string)` | System | [HASHBYTES.md](reference/functions/cryptography/hashbytes.md) | Returns hash of string |
| `NEWID()` | System | [NEWID.md](reference/functions/random-guid/newid.md) | Generates a new GUID |
| `NEWSEQUENTIALID()` | System | [NEWSEQUENTIALID.md](reference/functions/random-guid/newsequentialid.md) | Time-ordered GUID v7 |
| `FORMAT(value, format_string)` | System | [FORMAT.md](reference/functions/string/format.md) | Formats value using string pattern |
| `CHECKSUM(value1, ...)` | System | [CHECKSUM.md](reference/functions/cryptography/checksum.md) | 64-bit integer hash |
| `BINARY_CHECKSUM(value1, ...)` | System | [BINARY_CHECKSUM.md](reference/functions/cryptography/binary_checksum.md) | Binary-compatible hash |
| `ENV(variable_name)` | System | [ENV.md](reference/functions/general/env.md) | Host environment variable value |
| `CURRENT_TENANT()` | System | [CURRENT_TENANT.md](reference/functions/general/current_tenant.md) | Display name or identifier of current tenant |
| `TENANT_ID()` | System | [TENANT_ID.md](reference/functions/general/tenant_id.md) | Unique ID of current tenant context |
| `IS_SANDBOX()` | System | [IS_SANDBOX.md](reference/functions/general/is_sandbox.md) | Returns TRUE if running in an isolated sandbox |
| `CONNECTION_PROPERTY(connection, property)` | System | [CONNECTION_PROPERTY.md](reference/functions/table-valued/connection_property.md) | Resolves properties of configured connections |
| `GENERATE_SERIES(start, stop, [step])` | System | [GENERATE_SERIES.md](reference/functions/table-valued/generate_series.md) | Returns table of numbers/dates |
| `ERROR_MESSAGE()` | System | [ERROR_MESSAGE.md](reference/functions/error/error_message.md) | Error string in CATCH block |
| `ERROR_NUMBER()` | System | [ERROR_NUMBER.md](reference/functions/error/error_number.md) | Error code in CATCH block |
| `ERROR_SEVERITY()` | System | [ERROR_SEVERITY.md](reference/functions/error/error_severity.md) | Error severity in CATCH block |
| `ERROR_STATE()` | System | [ERROR_STATE.md](reference/functions/error/error_state.md) | Error state in CATCH block |
| `ERROR_LINE()` | System | [ERROR_LINE.md](reference/functions/error/error_line.md) | Error line in CATCH block |
| `JSON_VALUE(json, path)` / `JSON_EXTRACT` | JSON | [JSON_VALUE.md](reference/functions/json-xml/json_value.md) / [JSON_EXTRACT.md](reference/functions/json-xml/json_extract.md) | Extracts scalar from JSON (alias: JSON_EXTRACT) |
| `JSON_QUERY(json, path)` | JSON | [JSON_QUERY.md](reference/functions/json-xml/json_query.md) | Extracts object/array from JSON |
| `JSON_GET(json, key)` / `->` | JSON | [JSON_GET.md](reference/functions/json-xml/json_get.md) | One access step (field/element) as JSON; the `->` operator |
| `JSON_GET_TEXT(json, key)` / `->>` | JSON | [JSON_GET_TEXT.md](reference/functions/json-xml/json_get_text.md) | One access step as text; the `->>` operator |
| `JSON_MODIFY(json, path, new_value)` | JSON | [JSON_MODIFY.md](reference/functions/json-xml/json_modify.md) | Updates JSON string |
| `ISJSON(string)` | JSON | [ISJSON.md](reference/functions/json-xml/isjson.md) | 1 if valid JSON |
| `JSON_EXISTS(json, path)` | JSON | [JSON_EXISTS.md](reference/functions/json-xml/json_exists.md) | 1 if path exists |
| `JSON_OBJECT(key, value, ...)` | JSON | [JSON_OBJECT.md](reference/functions/json-xml/json_object.md) | Builds JSON object |
| `JSON_ARRAY(value1, ...)` | JSON | [JSON_ARRAY.md](reference/functions/json-xml/json_array.md) | Builds JSON array |
| `JSON_TABLE(json, path COLUMNS (...))` | JSON | [JSON_TABLE.md](reference/functions/json-xml/json_table.md) | Table projected from JSON rows |
| `OPENJSON(json, [path])` | JSON | [OPENJSON.md](reference/functions/json-xml/openjson.md) | SQL Server-style JSON expansion |
| `XMLVALUE(xml, xpath)` / `EXTRACTVALUE` | XML | [XMLVALUE.md](reference/functions/json-xml/xmlvalue.md) / [EXTRACTVALUE.md](reference/functions/json-xml/extractvalue.md) | Extracts scalar from XML (alias: EXTRACTVALUE) |
| `XMLEXISTS(xml, xpath)` | XML | [XMLEXISTS.md](reference/functions/json-xml/xmlexists.md) | 1 if XPath exists |
| `XMLQUERY(xml, xpath)` | XML | [XMLQUERY.md](reference/functions/json-xml/xmlquery.md) | XML fragment |
| `XMLTABLE(xml, xpath)` | XML | [XMLTABLE.md](reference/functions/json-xml/xmltable.md) | Table from XML |
| `XMLELEMENT(name, content)` | XML | [XMLELEMENT.md](reference/functions/json-xml/xmlelement.md) | Builds XML element |
| `XMLATTRIBUTES(name, value, ...)` | XML | [XMLATTRIBUTES.md](reference/functions/json-xml/xmlattributes.md) | XML attributes |
| `XMLFOREST(value1, ...)` | XML | [XMLFOREST.md](reference/functions/json-xml/xmlforest.md) | Forest of XML elements |
| `FILE_EXISTS(path)` | File | [FILE_EXISTS.md](reference/functions/file-path/file_exists.md) | 1 if file exists, 0 otherwise |
| `FILE_SIZE(path)` | File | [FILE_SIZE.md](reference/functions/file-path/file_size.md) | Returns local file size in bytes |
| `FILE_MODIFIED(path)` | File | [FILE_MODIFIED.md](reference/functions/file-path/file_modified.md) | Returns local file last modified timestamp |
| `FILE_HASH(path, [algorithm])` | File | [FILE_HASH.md](reference/functions/file-path/file_hash.md) | Computes cryptographic hash of a file |
| `DIRECTORY_EXISTS(path)` | File | [DIRECTORY_EXISTS.md](reference/functions/file-path/directory_exists.md) | 1 if directory exists, 0 otherwise |
| `FILE_LIST(path, [mask])` | File | [FILE_LIST.md](reference/functions/file-path/file_list.md) | Returns table of files in path |
| `REMOTE_FILE_LIST(connection, path)` | File | [REMOTE_FILE_LIST.md](reference/functions/file-path/remote_file_list.md) | Table of files on remote connection |
| `REMOTE_FILE_EXISTS(connection, path)` | File | [REMOTE_FILE_EXISTS.md](reference/functions/file-path/remote_file_exists.md) | 1 if remote file exists, 0 otherwise |
| `DIRECTORY(path)` | File | [DIRECTORY.md](reference/functions/file-path/directory.md) | Returns directory metadata |
| `PATH_COMBINE(path1, path2, ...)` | File | [PATH_COMBINE.md](reference/functions/file-path/path_combine.md) | Combines multiple path segments |
| `PATH_DIRECTORY(path)` | File | [PATH_DIRECTORY.md](reference/functions/file-path/path_directory.md) | Extracts directory path from a full path |
| `PATH_EXTENSION(path)` | File | [PATH_EXTENSION.md](reference/functions/file-path/path_extension.md) | Extracts file extension from a path |
| `PATH_FILENAME(path)` | File | [PATH_FILENAME.md](reference/functions/file-path/path_filename.md) | Extracts filename from a full path |
| `SUM(expression)` | Aggregate | [SUM.md](reference/functions/aggregate/sum.md) | Sum of values |
| `COUNT(expression)` | Aggregate | [COUNT.md](reference/functions/aggregate/count.md) | Count of non-null values |
| `AVG(expression)` | Aggregate | [AVG.md](reference/functions/aggregate/avg.md) | Average of values |
| `MAX(expression)` | Aggregate | [MAX.md](reference/functions/aggregate/max.md) | Maximum value |
| `MIN(expression)` | Aggregate | [MIN.md](reference/functions/aggregate/min.md) | Minimum value |
| `APPROX_COUNT_DISTINCT(expression)` | Aggregate | [Aggregate Functions](reference/functions/aggregate/avg.md) | HyperLogLog approximate distinct count |
| `EVERY(expression)` / `ANY(expression)` / `SOME(expression)` | Aggregate | [Aggregate Functions](reference/functions/aggregate/avg.md) | Standard boolean aggregates |
| `MEDIAN(expression)` | Aggregate | [MEDIAN.md](reference/functions/aggregate/median.md) | Median (50th percentile) |
| `VAR(expression)` / `VAR_SAMP` | Aggregate | [VAR.md](reference/functions/aggregate/var.md) | Sample variance |
| `VARP(expression)` / `VAR_POP` | Aggregate | [VARP.md](reference/functions/aggregate/varp.md) | Population variance |
| `STDEV(expression)` / `STDDEV` | Aggregate | [STDEV.md](reference/functions/aggregate/stdev.md) / [STDDEV.md](reference/functions/aggregate/stddev.md) | Sample standard deviation |
| `STDEVP(expression)` | Aggregate | [STDEVP.md](reference/functions/aggregate/stdevp.md) | Population standard deviation |
| `COVAR_SAMP(expr1, expr2)` | Aggregate | [COVAR_SAMP.md](reference/functions/aggregate/covar_samp.md) | Sample covariance |
| `COVAR_POP(expr1, expr2)` | Aggregate | [COVAR_POP.md](reference/functions/aggregate/covar_pop.md) | Population covariance |
| `CORR(expr1, expr2)` | Aggregate | [CORR.md](reference/functions/aggregate/corr.md) | Pearson correlation |
| `LISTAGG(expression, separator)` | Aggregate | [LISTAGG.md](reference/functions/aggregate/listagg.md) | Concatenates values with separator |
| `STRING_AGG(expression, separator)` | Aggregate | [STRING_AGG.md](reference/functions/aggregate/string_agg.md) | Concatenates strings with separator |
| `ROW_NUMBER()` | Window | [ROW_NUMBER.md](reference/functions/window/row_number.md) | Sequential row number |
| `RANK()` | Window | [RANK.md](reference/functions/window/rank.md) | Rank with gaps |
| `DENSE_RANK()` | Window | [DENSE_RANK.md](reference/functions/window/dense_rank.md) | Rank without gaps |
| `LAG(expression, [offset], [default])` | Window | [LAG.md](reference/functions/window/lag.md) | Value from N rows before |
| `LEAD(expression, [offset], [default])` | Window | [LEAD.md](reference/functions/window/lead.md) | Value from N rows after |
| `NTILE(buckets)` | Window | [NTILE.md](reference/functions/window/ntile.md) | Bucket number 1-N |
| `PERCENT_RANK()` | Window | [PERCENT_RANK.md](reference/functions/window/percent_rank.md) | Relative rank (0-1) |
| `CUME_DIST()` | Window | [CUME_DIST.md](reference/functions/window/cume_dist.md) | Cumulative distribution |
| `FIRST_VALUE(expression)` | Window | [FIRST_VALUE.md](reference/functions/window/first_value.md) | First value in partition |
| `LAST_VALUE(expression)` | Window | [LAST_VALUE.md](reference/functions/window/last_value.md) | Last value in partition |
| `NTH_VALUE(expression, nth)` | Window | [NTH_VALUE.md](reference/functions/window/nth_value.md) | Nth value in window frame |
| `PERCENTILE_CONT(fraction)` | Window | [PERCENTILE_CONT.md](reference/functions/aggregate/percentile_cont.md) | Continuous percentile |
| `PERCENTILE_DISC(fraction)` | Window | [PERCENTILE_DISC.md](reference/functions/aggregate/percentile_disc.md) | Discrete percentile |
| `REGEXP_LIKE(string, pattern)` | Regex | [REGEXP_LIKE.md](reference/functions/regex/regexp_like.md) | 1 if string matches regex |
| `REGEXP_REPLACE(string, pattern, replacement)` | Regex | [REGEXP_REPLACE.md](reference/functions/regex/regexp_replace.md) | Replace matches in string |
| `REGEXP_SUBSTR(string, pattern)` | Regex | [REGEXP_SUBSTR.md](reference/functions/regex/regexp_substr.md) | Matched substring |
| `REGEXP_INSTR(string, pattern)` | Regex | [REGEXP_INSTR.md](reference/functions/regex/regexp_instr.md) | Position of match |
| `REGEXP_COUNT(string, pattern)` | Regex | [REGEXP_COUNT.md](reference/functions/regex/regexp_count.md) | Count of matches |
| `REGEXP_MATCHES(string, pattern)` | Regex | [REGEXP_MATCHES.md](reference/functions/regex/regexp_matches.md) | Table of all matches |
| `REGEXP_SPLIT_TO_TABLE(string, pattern)` | Regex | [REGEXP_SPLIT_TO_TABLE.md](reference/functions/regex/regexp_split_to_table.md) | Splits a string into a table using regex |
| `ADD_TO_LIST(list, value)` | List | [ADD_TO_LIST.md](reference/functions/collections/add_to_list.md) | Appends value to a LIST |
| `SORT_LIST(list)` | List | [SORT_LIST.md](reference/functions/collections/sort_list.md) | Returns sorted copy of list |
| `APPEND_TO_LIST(list, value)` | List | [APPEND_TO_LIST.md](reference/functions/collections/append_to_list.md) | Alias for ADD_TO_LIST |
| `REMOVE_FROM_LIST(list, value)` | List | [REMOVE_FROM_LIST.md](reference/functions/collections/remove_from_list.md) | Removes occurrences from list |
| `GET_TAGS(table, [column])` | Lineage | [GET_TAGS.md](reference/functions/tags/get_tags.md) | Returns list of tag names |
| `GET_TAG_VALUE(table, column, tag_name)` | Lineage | [GET_TAG_VALUE.md](reference/functions/tags/get_tag_value.md) | Returns value of specific tag |
| `HAS_TAG(table, column, tag_name, [expected_value])` | Lineage | [HAS_TAG.md](reference/functions/tags/has_tag.md) | Returns 1 if tag exists (optionally matching expected value) |
| `NORMALIZE(string, [mode])` | Fuzzy | [NORMALIZE.md](reference/functions/fuzzy-match/normalize.md) | Domain-aware preprocessing |
| `SIMILARITY(string1, string2, [mode])` | Fuzzy | [SIMILARITY.md](reference/functions/fuzzy-match/similarity.md) | Normalized similarity score (0-1) |
| `LEVENSHTEIN(string1, string2)` | Fuzzy | [LEVENSHTEIN.md](reference/functions/fuzzy-match/levenshtein.md) | Raw edit distance |
| `SOUNDEX(string)` | Fuzzy | [SOUNDEX.md](reference/functions/fuzzy-match/soundex.md) | 4-char phonetic code |
| `METAPHONE(string)` | Fuzzy | [METAPHONE.md](reference/functions/fuzzy-match/metaphone.md) | English phonetic code |
| `DMETAPHONE(string)` | Fuzzy | [DMETAPHONE.md](reference/functions/fuzzy-match/dmetaphone.md) | Double Metaphone primary code |
| `DMETAPHONE_ALT(string)` | Fuzzy | [DMETAPHONE_ALT.md](reference/functions/fuzzy-match/dmetaphone_alt.md) | Double Metaphone alternate code |
| `NGRAMS(string, size)` | Fuzzy | [NGRAMS.md](reference/functions/fuzzy-match/ngrams.md) | Table of N-character grams |
| `NGRAM_TOKENS(string)` | Fuzzy | [NGRAM_TOKENS.md](reference/functions/fuzzy-match/ngram_tokens.md) | Table of 3-grams (blocking) |
| `DIFFERENCE(string1, string2)` | Fuzzy | [DIFFERENCE.md](reference/functions/fuzzy-match/difference.md) | SOUNDEX difference score (0-4) |

*Note: Over 190 functions are registered. See [Standard Library](reference/functions/README.md) for full signatures and examples.*

---

### 3.1 Keyword Parameter Enumerations

The following parameters accept a **fixed set of keyword values** only. Functions that use these parameters link here rather than repeating the list inline.

#### `datepart` — DATEADD, DATEDIFF, DATENAME, DATEPART, DATETRUNC, EXTRACT

| Keyword | Abbreviations | Description |
| :--- | :--- | :--- |
| `YEAR` | `YY`, `YYYY` | Calendar year |
| `QUARTER` | `QQ`, `Q` | Quarter of year (1–4) |
| `MONTH` | `MM`, `M` | Month of year (1–12) |
| `WEEK` | `WK`, `WW` | ISO week number |
| `DAYOFYEAR` | `DY`, `Y` | Day within the year (1–366) |
| `DAY` | `DD`, `D` | Day of month (1–31) |
| `WEEKDAY` | `DW` | Day of week (1 = Sunday by default) |
| `HOUR` | `HH` | Hour (0–23) |
| `MINUTE` | `MI`, `N` | Minute (0–59) |
| `SECOND` | `SS`, `S` | Second (0–59) |
| `MILLISECOND` | `MS` | Millisecond (0–999) |

> **Note:** `DATETRUNC` supports only: `YEAR`, `QUARTER`, `MONTH`, `WEEK`, `DAY`, `HOUR`, `MINUTE`, `SECOND`.
> `EXTRACT` uses SQL-standard field names: `YEAR`, `MONTH`, `DAY`, `HOUR`, `MINUTE`, `SECOND`, `DOW` (day-of-week), `DOY` (day-of-year).

#### `algorithm` — HASHBYTES

| Value | Description |
| :--- | :--- |
| `'MD5'` | MD5 hash — 128-bit / 16-byte output |
| `'SHA1'` | SHA-1 hash — 160-bit / 20-byte output |
| `'SHA256'` / `'SHA2_256'` | SHA-256 hash — 256-bit / 32-byte output |
| `'SHA512'` / `'SHA2_512'` | SHA-512 hash — 512-bit / 64-byte output |

#### `mode` — NORMALIZE

| Value | What it does |
| :--- | :--- |
| *(omitted)* | Base: lowercase, trim, collapse whitespace, Unicode NFC, strip control characters |
| `'COMPANY'` | Removes legal suffixes (LLC, Inc, Corp…), expands `&` → `and`, strips leading articles |
| `'PERSON'` | Removes titles and generational suffixes (Mr, Mrs, Dr, Jr, Sr, MD, PhD…) |
| `'ADDRESS'` | Expands directional and street-type abbreviations, removes unit designators |
| `'PHONE'` | Strips all non-digit characters; removes leading country code `1` if 11 digits |
| `'EMAIL'` | Lowercase and trim only |

#### `mode` — SIMILARITY

| Value | Best for |
| :--- | :--- |
| `'JAROWINKLER'` *(default)* | Person names, short identifiers, prefix-heavy strings |
| `'LEVENSHTEIN'` | Short strings with typos, product codes |
| `'TRIGRAM'` | General purpose, partial matches, longer strings |
| `'JACCARD'` | Strings where word presence matters more than order |
| `'TOKENSORT'` | Names where first/last may be swapped |

#### `type` — STRING_ESCAPE

| Value | Description |
| :--- | :--- |
| `'json'` | Escapes characters invalid in JSON strings (`"`, `\`, control chars) |

#### `timezone` — AT TIME ZONE

Any Windows timezone ID string. Common values:

| Value | Region |
| :--- | :--- |
| `'UTC'` | Coordinated Universal Time |
| `'Eastern Standard Time'` | US Eastern (UTC-5 / UTC-4 DST) |
| `'Central Standard Time'` | US Central (UTC-6 / UTC-5 DST) |
| `'Mountain Standard Time'` | US Mountain (UTC-7 / UTC-6 DST) |
| `'Pacific Standard Time'` | US Pacific (UTC-8 / UTC-7 DST) |
| `'GMT Standard Time'` | UK / Ireland |
| `'W. Europe Standard Time'` | Central Europe |
| `'Tokyo Standard Time'` | Japan (UTC+9, no DST) |

Full list: any ID returned by `TimeZoneInfo.GetSystemTimeZones()` on the host OS.

---

## 4. Window Functions

Window functions perform calculations across a set of table rows that are somehow related to the current row.

### 4.1 Window Syntax
```sql
FUNCTION_NAME(args) OVER (
  [PARTITION BY col1, col2, ...]
  [ORDER BY colA [ASC|DESC], ...]
  [ROWS|RANGE|GROUPS BETWEEN <bound> AND <bound>]
  [EXCLUDE CURRENT ROW|GROUP|TIES|NO OTHERS]
)
```

**Supported Bounds:**
- `UNBOUNDED PRECEDING`
- `<n> PRECEDING`
- `CURRENT ROW`
- `<n> FOLLOWING`
- `UNBOUNDED FOLLOWING`

**Frame Modes and Exclusions:**
- `ROWS` counts physical rows.
- `RANGE` groups rows by ordering value range.
- `GROUPS` counts peer groups with equal `ORDER BY` values.
- `EXCLUDE CURRENT ROW`, `EXCLUDE GROUP`, `EXCLUDE TIES`, and `EXCLUDE NO OTHERS` remove rows from the resolved frame.

### 4.2 Dedicated Window Functions
| Function | Help File | Description |
| :--- | :--- | :--- |
| `ROW_NUMBER()` | [ROW_NUMBER.md](reference/functions/window/row_number.md) | Sequential row number within partition |
| `RANK()` | [RANK.md](reference/functions/window/rank.md) | Rank with gaps for ties |
| `DENSE_RANK()` | [DENSE_RANK.md](reference/functions/window/dense_rank.md) | Rank without gaps for ties |
| `PERCENT_RANK()` | [PERCENT_RANK.md](reference/functions/window/percent_rank.md) | Relative rank (0 to 1) |
| `CUME_DIST()` | [CUME_DIST.md](reference/functions/window/cume_dist.md) | Cumulative distribution |
| `NTILE(buckets)` | [NTILE.md](reference/functions/window/ntile.md) | Divide rows into N buckets |
| `LAG(expression, [offset], [default])` | [LAG.md](reference/functions/window/lag.md) | Value from N rows before |
| `LEAD(expression, [offset], [default])` | [LEAD.md](reference/functions/window/lead.md) | Value from N rows after |
| `FIRST_VALUE(expression)` | [FIRST_VALUE.md](reference/functions/window/first_value.md) | First value in window frame |
| `LAST_VALUE(expression)` | [LAST_VALUE.md](reference/functions/window/last_value.md) | Last value in window frame |
| `NTH_VALUE(expression, nth)` | [NTH_VALUE.md](reference/functions/window/nth_value.md) | Nth value in window frame |
| `PERCENTILE_CONT(fraction)` | [PERCENTILE_CONT.md](reference/functions/aggregate/percentile_cont.md) | Continuous percentile |
| `PERCENTILE_DISC(fraction)` | [PERCENTILE_DISC.md](reference/functions/aggregate/percentile_disc.md) | Discrete percentile |

### 4.3 Aggregate-as-Window Functions
Any standard aggregate function can be used as a window function by appending the `OVER` clause.
| Function | Example |
| :--- | :--- |
| `SUM(v)` | `SUM(Sales) OVER(PARTITION BY Region)` |
| `AVG(v)` | `AVG(Price) OVER(ORDER BY Date ROWS BETWEEN 7 PRECEDING AND CURRENT ROW)` |
| `COUNT(v)` | `COUNT(*) OVER()` |
| `MAX(v)` / `MIN(v)` | `MAX(Total) OVER(PARTITION BY Category)` |
| `STDEV(v)` / `VAR(v)` | `STDEV(Score) OVER(PARTITION BY Class)` |

---

## 5. Variables

### 5.1 System Variables (`@@`)
Read-only counters tracking session state.

| Variable | Description | Help File |
| :--- | :--- | :--- |
| `@@ROWCOUNT` | Rows affected by last statement | [@@ROWCOUNT.md](reference/variables-parameters/@@rowcount.md) |
| `@@CURRENT_USER` | Returns the username of the current execution identity. | [@@CURRENT_USER](reference/variables-parameters/@@current_user.md) |
| `@@CURRENT_USER_ID` | Returns the stable, unique identifier of the current execution identity. | [@@CURRENT_USER_ID](reference/variables-parameters/@@current_user_id.md) |
| `@@IS_ADMIN` | Returns whether the current execution identity has administrator privileges. | [@@IS_ADMIN](reference/variables-parameters/@@is_admin.md) |
| `@@REAL_USER` | Returns the username of the actual authenticated session user. | [@@REAL_USER](reference/variables-parameters/@@real_user.md) |
| `@@ERROR` | Last error code (0 = success) | [@@ERROR.md](reference/variables-parameters/@@error.md) |
| `@@VERSION` | Engine version string | [@@VERSION.md](reference/variables-parameters/@@version.md) |
| `@@TRANCOUNT` | Transaction nesting level | [@@TRANCOUNT.md](reference/variables-parameters/@@trancount.md) |
| `@@FETCH_STATUS` | Last fetch result (0 = success) | [@@FETCH_STATUS.md](reference/variables-parameters/@@fetch_status.md) |
| `@@LAST_EXEC_MS` | Duration of last statement | [@@LAST_EXEC_MS.md](reference/variables-parameters/@@last_exec_ms.md) |
| `@@PEAK_MEMORY_MB` | Peak memory usage in MB | [@@PEAK_MEMORY_MB.md](reference/variables-parameters/@@peak_memory_mb.md) |
| `@@TOTAL_SPILLED_BYTES` | Cumulative spill disk usage | [@@TOTAL_SPILLED_BYTES.md](reference/variables-parameters/@@total_spilled_bytes.md) |
| `@@SORT_SPILLS` | Count of external sort spills | [@@SORT_SPILLS.md](reference/variables-parameters/@@sort_spills.md) |
| `@@SUBQUERY_CACHE_HITS` | Subquery cache hit count | [@@SUBQUERY_CACHE_HITS.md](reference/variables-parameters/@@subquery_cache_hits.md) |
| `@@SUBQUERY_CACHE_MISSES` | Subquery cache miss count | [@@SUBQUERY_CACHE_MISSES.md](reference/variables-parameters/@@subquery_cache_misses.md) |
| `@@RESULTSETS` | Count of result sets from last stmt | [@@RESULTSETS.md](reference/variables-parameters/@@resultsets.md) |
| `@@PARTITIONS_COUNT` | External spill partition count | [@@PARTITIONS_COUNT.md](reference/variables-parameters/@@partitions_count.md) |
| `@@FILE_EXISTS(p)` | File existence check (also available as function `FILE_EXISTS()`) | - |
| `@@DIRECTORY_EXISTS(p)` | Directory existence check (also available as function `DIRECTORY_EXISTS()`) | - |

### 5.2 Specialty Variable Types
Used in `DECLARE` to define behavior.

| Type | Purpose | Documentation |
| :--- | :--- | :--- |
| `PATH` | Filesystem path with security validation | [Grammar.md#L63] |
| `JSON` | Validated JSON string | [Grammar.md#L82] |
| `XML` | Validated XML string | [Grammar.md#L106] |
| `LIST` / `LIST(t)` | Ordered collection | [Grammar.md#L137] |
| `MINMAX(t)` | Pair of values (.MIN, .MAX) | [Grammar.md#L151] |
| `RELDATE` | Relative date expression (e.g. 'D-7') | [RelativeDate_Parameters.md](reference/functions/datetime/reldate.md) |
| `SENSITIVE` | Masked in output, auto-decrypts `ENC:` | [Grammar.md#L195] |
| `SECRET` | Same as SENSITIVE, purged at session end | [Grammar.md#L213] |
| `MARKDOWN` | Hint for Portal rendering | [Grammar.md#L125] |

---

## 6. SET Options (Configuration)

Options configured via `SET <Option> = <Value>` or `SET <Option> ON|OFF`.

| Option | Category | Default | Help File |
| :--- | :--- | :--- | :--- |
| `WHAT_IF` | Execution | OFF | [SET WHAT_IF](reference/set-commands/set-what-if.md) |
| `PROFILING` | Execution | OFF | [SET PROFILING](reference/set-commands/set-profiling.md) |
| `SHOW_SECRETS` | Security | OFF | [SET SHOW_SECRETS](reference/set-commands/set-show-secrets.md) |
| `SHOW_PASSWORD` | Security | OFF — alias for `SHOW_SECRETS` | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `ALLOW_PLAINTEXT_SECRETS` | Security | OFF | [SET ALLOW_PLAINTEXT_SECRETS](reference/set-commands/set-allow-plaintext-secrets.md) |
| `NO_SAVE_SENSITIVE` | Security | OFF | [SET NO_SAVE_SENSITIVE](reference/set-commands/set-no-save-sensitive.md) |
| `NO_SAVE_CONNECTION` | Security | OFF | [SET NO_SAVE_CONNECTION](reference/set-commands/set-no-save-connection.md) |
| `CONNECTION_ENCRYPTION` | Security | OFF | [SET CONNECTION_ENCRYPTION](reference/set-commands/set-connection-encryption.md) |
| `LINEAGE` | Data | ON | [LINEAGE.md](reference/statements/session-control/lineage.md) |
| `LINEAGE_NAMESPACE` | Lineage | `'etl-sql'` | [LINEAGE.md](reference/statements/session-control/lineage.md) |
| `LINEAGE_IMPORT_CATALOG` | Lineage | OFF | [LINEAGE.md](reference/statements/session-control/lineage.md) |
| `TELEMETRY` | Metrics | ON | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `BATCHSIZE` | Performance | 10,000 | [SET BATCHSIZE](reference/set-commands/set-batchsize.md) |
| `JOIN_SPILL_THRESHOLD` | Performance | 100,000 | [SET JOIN_SPILL_THRESHOLD](reference/set-commands/set-join-spill-threshold.md) |
| `TEMP_TABLE_SPILL_THRESHOLD` | Performance | 1,000,000 | [SET TEMP_TABLE_SPILL_THRESHOLD](reference/set-commands/set-temp-table-spill-threshold.md) |
| `MAX_PARALLEL_DEGREE` | Performance | CPU Count | [SET MAX_PARALLEL_DEGREE](reference/set-commands/set-max-parallel-degree.md) |
| `WEEK_START_DAY` | Localization | Monday | [SET WEEK_START_DAY](reference/set-commands/set-week-start-day.md) |
| `REPORT TIME_ZONE` | Report | `UTC` | [SET REPORT](reference/set-commands/set-report.md) |
| `REPORT LOCALE` | Report | invariant | [SET REPORT](reference/set-commands/set-report.md) |
| `REPORT NULL_LABEL` | Report | `-` | [SET REPORT](reference/set-commands/set-report.md) |
| `EXTERNAL_HASH_PARTITIONS` | Performance | 32 | [SET EXTERNAL_HASH_PARTITIONS](reference/set-commands/set-external-hash-partitions.md) |
| `EXTERNAL_SORT_CHUNK_SIZE` | Performance | 50,000 | [SET EXTERNAL_SORT_CHUNK_SIZE](reference/set-commands/set-external-sort-chunk-size.md) |
| `FOREACH_PAGE_SIZE` | Performance | 10,000 | [SET FOREACH_PAGE_SIZE](reference/set-commands/set-foreach-page-size.md) |
| `INTERACTIVE_MODE` | Session | OFF | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `MAX_FILE_OPERATIONS` | Security | 100 | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `MAX_GENERATE_ROWS` | Performance | 1,000,000 | [SET MAX_GENERATE_ROWS](reference/set-commands/set-max-generate-rows.md) |
| `MAX_SMTP_EMAILS_PER_SCRIPT` | Security | 100 | [SET MAX_SMTP_EMAILS_PER_SCRIPT](reference/set-commands/set-max-smtp-emails.md) |
| `MAX_GROUPING_SETS` | Performance | 100 | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `MAX_IN_MEMORY_BATCHES` | Performance | 100 | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `MAX_MESSAGES` | Diagnostics | 1,000 | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `MAX_RECURSIVE_DEPTH` | Flow | 10,000 | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `MAX_SESSION_SIZE` | Performance | 500 MB | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `MAX_STRING_RESULT_SIZE` | Performance | 5 MB | [SET MAX_STRING_RESULT_SIZE](reference/set-commands/set-max-string-result-size.md) |
| `PERSIST` | Session | ON | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `REGEX_MATCH_TIMEOUT` | Flow | 1,000ms | [SET REGEX_MATCH_TIMEOUT](reference/set-commands/set-regex-match-timeout.md) |
| `SPILL_COMPRESSION` | Performance | ON | [SET SPILL_COMPRESSION](reference/set-commands/set-spill-compression.md) |
| `SPILL_ENCRYPTION` | Performance | ON | [SET SPILL_ENCRYPTION](reference/set-commands/set-spill-encryption.md) |
| `SPILL_FORMAT` | Performance | AUTO | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `WINDOW_SPILL_THRESHOLD` | Performance | 100,000 | [SET WINDOW_SPILL_THRESHOLD](reference/set-commands/set-window-spill-threshold.md) |
| `MAX_LAST_RESULT_ROWS` | Performance | 1,000 | [SET MAX_LAST_RESULT_ROWS](reference/set-commands/set-max-last-result-rows.md) |
| `MAX_INTERNAL_OPERATIONS`| Performance | 1,000,000 | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `SET_CUBE_LIMIT` | Performance | 10 | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `SCRIPT_HASH_POLICY` | Security | VALIDATE | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `CASE_SENSITIVE` | Execution | OFF | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `TEMPLATE_PATH` | Report | NULL | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |
| `ALLOW_FILE_TYPE_ACCESS` | Security | OFF | [SET ALLOW_FILE_TYPE_ACCESS](reference/set-commands/set-allow-file-type-access.md) |
| `ALLOW_FILE_OPERATIONS` | Security | 100 | [SET ALLOW_FILE_OPERATIONS](reference/set-commands/set-allow-file-operations.md) |
| `ALLOW_RECURSIVE_LAYERS` | Security | 5 | [SET ALLOW_RECURSIVE_LAYERS](reference/set-commands/set-allow-recursive-layers.md) |
| `ALLOW_...` (various) | Security | OFF | [Options/INDEX.md](reference/visuals-reporting/visuals/index.md) |

| `@VARIABLE` | | | [SET @variable](reference/set-commands/set-variable.md) |
| `WITH_PROMPT` | | | [SET WITH_PROMPT](reference/set-commands/set-with-prompt.md) |

---

## 7. Object Creation Options (WITH Clauses)

Options available when creating or altering engine and report objects.

### 7.1 CREATE CONNECTION
```sql
CREATE CONNECTION name AS <Provider>( ... )
```
| Option | Description | Documentation |
| :--- | :--- | :--- |
| `HOST` / `SERVER` | Server hostname or IP | [Database Connectors](reference/connectors/databases/README.md) |
| `PORT` | Network port | [Database Connectors](reference/connectors/databases/README.md) |
| `CONNECTION_STRING` / `URI` | Connection URI / string | [MongoDB](reference/connectors/databases/mongodb.md) · [Neo4j](reference/connectors/databases/neo4j.md) |
| `DATABASE` | Database name | [Database Connectors](reference/connectors/databases/README.md) |
| `USER` / `UID` | Username | [Database Connectors](reference/connectors/databases/README.md) |
| `PASSWORD` | Password (can be 'ENC:...') | [Database Connectors](reference/connectors/databases/README.md) |
| `TIMEOUT_SECONDS` | Connection and query timeout limit | [Database Connectors](reference/connectors/databases/README.md) |
| `TRUSTED_CONNECTION`| Use Windows Auth (MSSQL only) | [MSSQL](reference/connectors/databases/mssql.md) |
| `ENCRYPT` | Enable SSL/TLS encryption | [MSSQL](reference/connectors/databases/mssql.md) |
| `PATH` | Root path for file-based connectors | [FLATFILE](reference/connectors/files/flatfile.md) |
| `DSN` / `DRIVER` | ODBC specific identifiers | [ODBC](reference/connectors/databases/odbc.md) |
| `KEYFILE` | Path to private key (SFTP/PGP) | [SFTP](reference/connectors/services/sftp.md) |
| `PASSPHRASE` | Keyfile decryption password | [SFTP](reference/connectors/services/sftp.md) |
| `SSL_MODE` | Postgres SSL behavior | [Postgres](reference/connectors/databases/postgres.md) |

### 7.2 CREATE TABLE
```sql
CREATE TABLE name ( col type [OPTIONS], ... ) [WITH ( ... )]
```
| Option | Context | Description |
| :--- | :--- | :--- |
| `IDENTITY` | Column | Auto-incrementing integer; supports optional seed/increment parameters as `IDENTITY(seed, increment)` |
| `PRIMARY KEY` | Column/Table | Unique identifier constraint |
| `UNIQUE` | Column/Table | Unique value constraint |
| `NOT NULL` / `NULL` | Column | Nullability constraint |
| `CHECK(expr)` | Column/Table | Validation expression |
| `DEFAULT expr` | Column | Default value expression |
| `REFERENCES tbl(col)`| Column/Table | Foreign key constraint |

### 7.3 CREATE JOB
```text
CREATE [OR ALTER|OR REPLACE] JOB name
FOR SCRIPT|REPORT 'target-path'
[WITH (MAX_RETRIES = n, RETRY_DELAY = seconds, DISPLAY_NAME = '...', ...)]

ALTER JOB name ADD SCHEDULE schedule_name
```
| Option | Default | Description |
| :--- | :--- | :--- |
| `MAX_RETRIES` | 0 | Number of retry attempts on failure (integer) |
| `RETRY_DELAY` | 30 | Delay between retries in seconds (integer) |
| `DISPLAY_NAME` | Job name | Operator-facing label |

### 7.4 CREATE SSH_KEY_PAIR / PGP_KEY_PAIR
```sql
CREATE SSH_KEY_PAIR name WITH ( ... )
```
| Option | Default | Description |
| :--- | :--- | :--- |
| `BITS` | 2048 / 4096 | Key strength |
| `ALGORITHM` | 'RSA' | Key algorithm (RSA, ED25519) |
| `PASSPHRASE` | NULL | Key protection password |
| `IDENTITY` | NULL | PGP User ID |
| `COMMENT` | NULL | Metadata comment |

### 7.5 CREATE DATASET
```sql
CREATE DATASET &name [OPTIONS] AS SELECT ...
```
| Option | Syntax | Description |
| :--- | :--- | :--- |
| `TTL` | `TTL = 'hh:mm:ss'` | Data expiration period |
| `COMPRESS` | `COMPRESS = ON/OFF` | Enable row compression |
| `ENCRYPT` | `ENCRYPT = MACHINE/PASSWORD/KEYFILE` | Data at rest encryption mode |
| `PASSWORD` | `PASSWORD = '...'` | Encryption password |
| `KEYFILE` | `KEYFILE = '...'` | Encryption key path |
| `ACCESS` | `ACCESS = PUBLIC/PRIVATE` | Portal visibility level |

### 7.6 CREATE VISUAL / BUTTON
```sql
CREATE VISUAL name AS <Type> ( ... )
```

Native advanced charts use `CREATE VISUAL name AS CUSTOM (..., CHART (...))` with explicit
renderer-neutral layers, inferred or declared scales, field/`DATUM`/`VALUE` bindings, stacking,
offsets, interval geometry, deterministic position adjustments, continuous color ranges, `TICK`
targets, fixed Cartesian aspect, and grid/wrap facets. See
[CHART](reference/visuals-reporting/visuals/chart.md). Data transformations remain in preceding
ETL-SQL statements rather than hidden inside the visual declaration.

Constrained components use `CREATE VISUAL name AS HTML (...)`. `SOURCE` is optional, `MODE` is
`SINGLE` or `REPEATER`, `TEMPLATE` contains escaped field and parameter bindings, `STYLE (CSS = ...)`
is visual-scoped, `FALLBACK` supplies text-only meaning, and `ACTIONS` remains declarative. See
[HTML](reference/visuals-reporting/visuals/html.md).
| Section | Option | Description |
| :--- | :--- | :--- |
| `SOURCE` | `SOURCE = #dataset / SELECT ...` | Data source definition |
| `TITLE` | `TITLE = '...' / ('MD'...)` | Primary display title |
| `SUBTITLE` | `SUBTITLE = '...' / ('MD'...)` | Secondary display title |
| `VISIBLE` | `VISIBLE = ON/OFF` | Initial visibility state |
| `MAPPINGS` | `MAPPINGS ( Role = Column, ... )` | Data field assignments |
| `OPTIONS` | `OPTIONS ( Key = Value, ... )` | Visual-specific settings (X_AXIS, COLORS, etc.) |
| `ACTIONS` | `ACTIONS ( Trigger = Action, ... )` | Interactive behavior (ON_CLICK, ON_CHANGE) |
| `INTERACTIONS` | `INTERACTIONS ( Key = Value, ... )` | Cross-visual filtering behavior |
| `STYLE` | `STYLE = Name / ( ... )` | CSS/theme/viewer overrides, including `ALLOW_MAXIMIZE = ON/OFF` |
| `SERIES` | `SERIES ( Type Column, ... )` | Multi-series type mapping (BAR/LINE) |
| `FORMATTING` | `FORMATTING ( expr THEN color, ... )` | Conditional formatting rules |
| `OVERLAYS` | `OVERLAYS ( Type AS Style, REFERENCE_LINE(...), REFERENCE_BAND(...), ... )` | Trend lines, goals, averages, forecasts, reference bands, running totals, and percent-of-total lines |
| `SUMMARY` | `SUMMARY ( Agg(Col), ... )` | Table footer/total summaries |
| `TOOLTIP` | `TOOLTIP = ... / ( ... )` | [Transient tooltip or focusable detail popover](reference/visuals-reporting/report/tooltip.md) |
| `MIN` / `MAX` | `MIN = n, MAX = n` | Range limits for controls |
| `DECIMALS` | `DECIMALS = n` | Numeric precision |
| `PLACEHOLDER` | `PLACEHOLDER = '...'` | Empty state text |

Common `OPTIONS` keys for report visuals:

| Key | Applies to | Values | Description |
| :--- | :--- | :--- | :--- |
| `FORMAT` | `CARD`, `TABLE`, data labels | .NET format string such as `'N0'`, `'C2'`, `'P1'` | Numeric display format |
| `GRID_LINES` | Cartesian charts | `ON` / `OFF` | Shows or hides background grid lines. |
| `ZOOM_SLIDER` | Browser-rendered native charts | `ON` / `OFF` | Adds a visible range selector. |
| `LEGEND_POSITION` | Charts with legends | `TOP`, `RIGHT`, `BOTTOM`, `LEFT`, `INSIDE` | Places the legend outside or inside the plot. |
| `LEGEND_ANCHOR` | Charts with `LEGEND_POSITION = INSIDE` | `TOP_LEFT`, `TOP_RIGHT`, `BOTTOM_LEFT`, `BOTTOM_RIGHT` | Anchor corner for inside overlay legend (default `TOP_RIGHT`). |
| `LEGEND_ORIENTATION` | Charts with legends | `HORIZONTAL`, `VERTICAL` | Sets legend layout orientation. |
| `LEGEND_REVERSE` | Charts with legends | `ON` / `OFF` | Flips series order in the legend (default `OFF`). |
| `LEGEND_TITLE` | Charts with legends | `'text'` / `NONE` | Sets or suppresses legend title text. |
| `LEGEND_COLUMNS` | Charts with legends | Positive integer | Column count for multi-column legend layout. |
| `LEGEND_FONT_SIZE` | Charts with legends | Numeric pixel size | Font size for legend text. |
| `LEGEND_FONT_COLOR` | Charts with legends | CSS color string | Font color for legend text. |
| `LEGEND_FONT_WEIGHT` | Charts with legends | `NORMAL`, `BOLD`, etc. | Font weight for legend text. |
| `DATA_LABELS` | Charts | `ON` / `OFF` with `POSITION`, `FONT_SIZE`, `COLOR`, `LABEL_BACKGROUND`, `LABEL_BORDER`, and `LEADER_LINE` | Shows values on marks with optional background, border, and leader line styling. |
| `SERIES_LABELS` | `LINE`, `COMBO` | `ON` / `OFF` with `POSITION = START\|END` | Renders a series title label at the first or last renderable point of each series (default `OFF`). |
| `SYMBOLS` | `LINE`, line layers in `COMBO` | `ON` / `OFF` | Shows or hides data-point markers. |
| `SYMBOL_SHAPE` | `LINE`, `SCATTER` | `CIRCLE`, `SQUARE`, `TRIANGLE`, `DIAMOND`, `CROSS`, `STAR` | Sets named-chart point-marker geometry; `CUSTOM` `POINT` layers use the same vocabulary through `SHAPE`. |
| `SYMBOL_STROKE_COLOR`, `SYMBOL_STROKE_WIDTH` | `LINE`, `SCATTER`; `CUSTOM` `POINT` layer styles | `'#RRGGBB'`, non-negative decimal pixels | Sets point-marker outline color and width. A color alone uses a one-pixel stroke; a width without a color draws no stroke. |
| `LINE_WIDTH` | `LINE`, line series in `COMBO`; `CUSTOM` `LINE` layer styles | Decimal from `0.1` through `10` pixels | Sets the rendered series stroke width without changing point-marker or overlay widths. |
| `BAND_SIZE` | `BAR`, `HBAR`, bar layers in `COMBO` | Decimal greater than `0` and at most `1` | Controls bar width and therefore spacing. |
| `AXIS_SORT` | `BAR`, `HBAR`, `LINE`, `AREA`, `COMBO` | `ASC`, `DESC`, `SOURCE`, `VALUE`, `VALUE_DESC` | Controls category-axis order. `ASC` type-sorts datetime, numeric, then text values; `SOURCE` preserves query order; `VALUE` and `VALUE_DESC` sort by the metric value. |
| `SORT` | `PIE`, `DONUT` | `SOURCE`, `VALUE_DESC`, `VALUE_ASC`, `ALPHA` | Controls slice sort order. `SOURCE` keeps query order; `VALUE_DESC` leads with largest slice; `VALUE_ASC` leads with smallest; `ALPHA` sorts alphabetically. |
| `MIN_SLICE_PCT` | `PIE`, `DONUT` | Positive decimal number | Minimum slice percentage threshold; smaller slices collapse into a single "Other" segment. |
| `OTHER_LABEL` | `PIE`, `DONUT` with `MIN_SLICE_PCT` | String (default `'Other'`) | Label for the collapsed small-slices segment. |
| `EXPLODE` | `PIE`, `DONUT` | String | Slice category name to offset radially from the center for emphasis. |
| `EXPLODE_ALL` | `PIE`, `DONUT` | `ON` / `OFF` or pixel number | Offsets all slices radially outward. |
| `EXPLODE_DISTANCE` | `PIE`, `DONUT` | Number (pixels, default `10`) | Radial offset distance in pixels for exploded slices. |
| `SLICE_BORDER_COLOR` | `PIE`, `DONUT` | CSS color string (default `'white'`) | Stroke color between slices. |
| `SLICE_BORDER_WIDTH` | `PIE`, `DONUT` | Non-negative decimal pixels (default `2`) | Stroke width between slices; set to `0` to remove inter-slice separator lines. |
| `START_ANGLE` | `PIE`, `DONUT` | Number in degrees (default `0`) | Clockwise angular rotation in degrees from the default 12 o'clock position. |
| `ABBREVIATE` | `CARD` | `ON` / `OFF` | Shortens large numbers, such as `1250000` to `1.25M` |
| `ALLOW_MAXIMIZE` | Visual `STYLE` | `ON` / `OFF` | Shows or hides the viewer maximize button. Data/chart visuals default `ON`; input/control visuals default `OFF`. |
| `GOAL` | `CARD` | Numeric literal | Supplies a literal target when `MAPPINGS(GOAL = column)` is not used |
| `SHOW_GOAL` | `CARD` | `ON` / `OFF` | Shows the target value line |
| `SHOW_PERCENT_OF_GOAL` | `CARD` | `ON` / `OFF` | Shows percent-to-target text |
| `SHOW_PROGRESS` | `CARD` | `ON` / `OFF` | Shows a goal progress indicator |
| `PROGRESS_STYLE` | `CARD` | `BAR` / `RING` | Chooses the progress indicator style |
| `CLOSE_PCT` / `MET_PCT` | `CARD` | Decimal ratio from `0` to `1` | Sets the close/met status thresholds |
| `COLOR_MET` / `COLOR_CLOSE` / `COLOR_MISSED` | `CARD` | CSS color | Status accent colors |
| `ICON_SET` | `CARD` | `CHECKS`, `ARROWS`, `TRAFFIC` | Preset status badge icon family |
| `ICON_MET` / `ICON_CLOSE` / `ICON_MISSED` | `CARD` | String | Custom status badge icons |
| `LABEL_MET` / `LABEL_CLOSE` / `LABEL_MISSED` | `CARD` | String | Status label overrides |
| `TREND_DIR` | `CARD` | `POSITIVE_UP`, `POSITIVE_DOWN` | Chooses whether an upward or downward delta is favorable |
| `DELTA_FORMAT` | `CARD` | .NET format string | Numeric format for the delta display |
| `DELTA_LABEL` | `CARD` | String | Label shown next to the delta |

### 7.7 CREATE PAGE / CONTAINER
```sql
CREATE PAGE name AS DASHBOARD|PAGINATED ( ... )
CREATE CONTAINER name AS BOX|SCROLL|DRAWER|SIDEBAR|TABS|ACCORDION|MODAL|POPOVER|LAYER ( ... )
```
| Option | Context | Description |
| :--- | :--- | :--- |
| `STRUCTURE` | Page/Container | CSS Grid template area string |
| `MAP` | Page/Container | Mapping of grid slots to visuals/containers |
| `LAYOUT` | Page/Container | Inner layout configuration; preferred for page layout and required for containers |
| `GAP` | Page/Layout | Space between grid elements |
| `PINNABLE` | Container layout | Enable/disable portal pinning |
| `ICON` | Container top-level | Header or trigger icon identifier |
| `VISIBLE` | Page/Container/Visual top-level | UI visibility only; does not control fetch timing |
| `FETCH` | Visual top-level | `AUTO`, `ON_LOAD`, or `ON_RUN` visual fetch timing |
| `REFRESH` | Page top-level | Auto-refresh interval in seconds |
| `DASHBOARD` | Page mode | Loads result visuals immediately and applies control changes live |
| `PAGINATED` | Page mode | Stages prompt changes until an `APPLY_PARAMETERS` run |
| `LAYER` | Container type | Stacks mapped visuals/containers in the same region; use `STYLE (Z_INDEX = n)` for explicit ordering |

### 7.8 CREATE NAVIGATION
```sql
CREATE NAVIGATION name AS <Type> ( ... )
```
| Option | Default | Description |
| :--- | :--- | :--- |
| `ORIENTATION` | HORIZONTAL | Navigation layout (HORIZONTAL/VERTICAL) |
| `DEFAULT` | NULL | Initial active page |
| `PAGES` | `PAGES ( P1, P2, ... )` | Ordered list of pages in the nav |

---

## 8. Report-SQL (Object Summary)

Specific to `.rptsql` files and the reporting engine.

### 8.1 Report Objects
| Command | Purpose | Help File |
| :--- | :--- | :--- |
| `CREATE VISUAL` | Defines a chart or filter | [VISUAL.md](reference/visuals-reporting/report/visual.md) |
| `CREATE DATASET` | Defines a data source for visuals | [DATASET.md](reference/visuals-reporting/report/dataset.md) |
| `CREATE PAGE` | Defines a dashboard page layout | [PAGE.md](reference/visuals-reporting/report/page.md) |
| `CREATE CONTAINER` | Groups visuals in a layout | [CONTAINER.md](reference/visuals-reporting/report/container.md) |
| `CREATE NAVIGATION` | Defines sidebar/top-nav links | [NAVIGATION.md](reference/visuals-reporting/report/navigation.md) |
| `CREATE STYLE` | Defines CSS/Theme overrides | [STYLE.md](reference/visuals-reporting/report/style.md) |
| `CREATE BUTTON` | Defines a clickable button | [BUTTON.md](reference/visuals-reporting/report/button.md) |
| `ACTIONS` block | Interactive event bindings | [ACTIONS.md](reference/visuals-reporting/report/actions.md) |
| `INTERACTIONS` block | Cross-visual filtering rules | [INTERACTIONS.md](reference/visuals-reporting/report/interactions.md) |
| `PRINT_LAYOUT` block | Physical page dimensions, margins, and page breaks | [PRINT_LAYOUT.md](reference/visuals-reporting/report/print-layout.md) |
| `PAGINATED` (Page Mode) | Print-ready multi-page report layout mode | [PAGE.md](reference/visuals-reporting/report/page.md) |
| `ROW_DETAIL` | Expandable master/detail table rows | [VISUAL.md](reference/visuals-reporting/report/visual.md) |
| `CASCADE` | Atomic dependent option sets for slicers and multiselects | [CASCADE.md](reference/visuals-reporting/report/cascade.md) |
| `CREATE BOOKMARK` | Named parameter/page/state snapshot | [BOOKMARK.md](reference/visuals-reporting/report/bookmark.md) |
| `DROP BOOKMARK` | Remove an author bookmark (supports `IF EXISTS`) | [BOOKMARK.md](reference/visuals-reporting/report/bookmark.md) |
| `APPLY_BOOKMARK` | Action to apply an author bookmark | [ACTIONS.md](reference/visuals-reporting/report/actions.md) |

Lifecycle: every report object above supports `CREATE OR REPLACE <kind> <name>` and
`DROP <kind> [IF EXISTS] <name>`. `ALTER <kind> <name> (...)` patches named clauses and is supported
for `VISUAL`, `PAGE`, `CONTAINER`, `BUTTON`, and `TEMPLATE` only; `STYLE`, `NAVIGATION`, `THEME`, and
`DATASET` are redefined with `CREATE OR REPLACE` instead. Each kind accepts only the clauses it can
patch — see its reference page — and anything else is refused at parse time. For all object kinds,
see the [Lifecycle Capability Matrix](reference/statements/lifecycle-matrix.md).

### 8.2 Visual Types
| Type | Category | Help File |
| :--- | :--- | :--- |
| `BAR` / `HBAR` | Chart | [BAR.md](reference/visuals-reporting/visuals/bar.md) / [HBAR.md](reference/visuals-reporting/visuals/hbar.md) |
| `LINE` | Chart | [LINE.md](reference/visuals-reporting/visuals/line.md) |
| `PIE` / `DONUT` | Chart | [PIE.md](reference/visuals-reporting/visuals/pie.md) / [DONUT.md](reference/visuals-reporting/visuals/donut.md) |
| `GAUGE` | Chart | [GAUGE.md](reference/visuals-reporting/visuals/gauge.md) |
| `HEATMAP` | Chart | [HEATMAP.md](reference/visuals-reporting/visuals/heatmap.md) |
| `SCATTER` | Chart | [SCATTER.md](reference/visuals-reporting/visuals/scatter.md) |
| `GANTT` | Chart | [GANTT.md](reference/visuals-reporting/visuals/gantt.md) |
| `WATERFALL` | Chart | [WATERFALL.md](reference/visuals-reporting/visuals/waterfall.md) |
| `FUNNEL` | Chart | [FUNNEL.md](reference/visuals-reporting/visuals/funnel.md) |
| `BOXPLOT` | Chart | [BOXPLOT.md](reference/visuals-reporting/visuals/boxplot.md) |
| `BUBBLE` | Chart | [BUBBLE.md](reference/visuals-reporting/visuals/bubble.md) |
| `CANDLESTICK` | Chart | [CANDLESTICK.md](reference/visuals-reporting/visuals/candlestick.md) |
| `COMBO` | Chart | [COMBO.md](reference/visuals-reporting/visuals/combo.md) |
| `TREEMAP` | Chart | [TREEMAP.md](reference/visuals-reporting/visuals/treemap.md) |
| `RADAR` | Chart | [RADAR.md](reference/visuals-reporting/visuals/radar.md) |
| `SANKEY` | Chart | [SANKEY.md](reference/visuals-reporting/visuals/sankey.md) |
| `SUNBURST` | Chart | [SUNBURST.md](reference/visuals-reporting/visuals/sunburst.md) |
| `NETWORK` | Chart | [NETWORK.md](reference/visuals-reporting/visuals/network.md) |
| `TRELLIS` | Chart | [TRELLIS.md](reference/visuals-reporting/visuals/trellis.md) |
| `MATRIX` | Data | [MATRIX.md](reference/visuals-reporting/visuals/matrix.md) |
| `TABLE` | Data | [TABLE.md](reference/visuals-reporting/visuals/table.md) |
| `CARD` | KPI with value, label, goal/progress, and delta support | [CARD.md](reference/visuals-reporting/visuals/card.md) |
| `MAP` | Chart | [MAP.md](reference/visuals-reporting/visuals/map.md) |
| `TEXT` | Static | [TEXT.md](reference/visuals-reporting/visuals/text.md) |
| `IMAGE` | Static | [IMAGE.md](reference/visuals-reporting/visuals/image.md) |
| `SLICER` | Filter | [SLICER.md](reference/visuals-reporting/visuals/slicer.md) |
| `DATEPICKER` | Filter | [DATEPICKER.md](reference/visuals-reporting/visuals/datepicker.md) |
| `RELDATEPICKER` | Filter | [RELDATEPICKER.md](reference/visuals-reporting/visuals/reldatepicker.md) |
| `SEARCH` | Filter | [SEARCH.md](reference/visuals-reporting/visuals/search.md) |
| `SLIDER` | Filter | [SLIDER.md](reference/visuals-reporting/visuals/slider.md) |
| `MULTISELECT` | Filter | [MULTISELECT.md](reference/visuals-reporting/visuals/multiselect.md) |
| `CHECKBOX` | Control | [CHECKBOX.md](reference/visuals-reporting/visuals/checkbox.md) |
| `TEXTBOX` | Control | [TEXTBOX.md](reference/visuals-reporting/visuals/textbox.md) |
| `NUMBERBOX` | Control | [NUMBERBOX.md](reference/visuals-reporting/visuals/numberbox.md) |

---


### Further reading

- [Report CLI, Hosting, and Preview](reference/visuals-reporting/report-cli.md) — Reference for building, serving, and previewing `.rptsql` reports: the `etl-sql-report`...
- [ReportManifest JSON Schema](reference/visuals-reporting/report-manifest.md) — The compiled `ReportManifest` is the structure returned by the snapshot and by the Repo...
- [Report Runtime Contract](reference/visuals-reporting/report-runtime-contract.md) — The report canvas is shared infrastructure. ReportPlayer, Portal, and the VS Code previ...
- [Report-SQL](reference/visuals-reporting/report/index.md) — Report-SQL extends ETL-SQL with components for building interactive dashboards: dataset...
- [CREATE THEME](reference/visuals-reporting/report/theme.md) — Defines a renderer-neutral color theme applied to visuals or pages with `STYLE (THEME = name)`.
- [ETL-SQL Performance Reference](reference/performance/performance.md) — **Applies to ETL-SQL 0.19.0**
- [Large Data Certification](reference/performance/large-data-certification.md) — This document describes which large-data scenarios are certified, at which scale tiers,...

## 9. Portal & Orchestrator Admin

Commands executed via `EXECUTE portal BEGIN ... END` or `EXECUTE orch BEGIN ... END`.

| Command | Context | Purpose |
| :--- | :--- | :--- |
| `CREATE USER` | Portal | Adds a portal user |
| `ALTER USER` | Portal | Modifies user properties or status |
| `DROP USER` | Portal | Deletes a user |
| `CREATE GROUP` | Portal | Adds a security group |
| `DROP GROUP` | Portal | Deletes a security group |
| `ADD USER ... TO GROUP` | Portal | Manages group membership |
| `CREATE FOLDER` | Portal | Adds a navigation folder |
| `ALTER FOLDER` | Portal | Renames a folder or moves it to a new parent |
| `DROP FOLDER` | Portal | Deletes a navigation folder |
| `GRANT` | Portal | Assigns folder or dataset permissions |
| `REVOKE` | Portal | Removes folder or dataset permissions |
| `PUBLISH REPORT` | Portal | Deploys a report script |
| `ALTER REPORT` | Portal | Modifies report metadata |
| `DROP REPORT` | Portal | Deletes a report |
| `eng.reports` | Portal | Queries report metadata |
| `eng.report_history()` | Portal | Queries report refresh/history rows |
| `eng.report_dependencies()` | Portal | Queries report dependencies |
| `FAVORITE REPORT` | Portal | Marks a report as a favorite |
| `UNFAVORITE REPORT` | Portal | Removes a report favorite |
| `VALIDATE REPORT SCRIPT` | Portal | Validates a report script without publishing |
| `CREATE SHARE LINK` | Portal | Creates a share link for a report |
| `eng.share_links()` | Portal | Lists report share links |
| `REVOKE SHARE LINK` | Portal | Revokes a share link token |
| `CREATE EMBED TOKEN` | Portal | Creates an embed token for a report |
| `eng.embed_tokens()` | Portal | Lists report embed tokens |
| `REVOKE EMBED TOKEN` | Portal | Revokes an embed token |
| `CREATE SAVED VIEW` | Portal | Saves report parameter/filter values |
| `eng.saved_views()` | Portal | Lists saved views for a report |
| `DROP SAVED VIEW` | Portal | Deletes a saved report view |
| `CREATE ALERT` | Portal | Creates a report alert |
| `eng.alerts()` | Portal | Lists report alerts |
| `DROP ALERT` | Portal | Deletes a report alert |
| `CREATE JOB ... FOR REPORT` | Orchestrator | Schedules automated report snapshot refresh when linked to a schedule |
| `REFRESH REPORT` | Portal | Manually starts a report refresh cycle |
| `REFRESH DATASET` | Portal | Marks a portal dataset stale and queues refresh when possible |
| `ALTER DATASET` | Portal | Updates portal dataset access/TTL metadata |
| `DROP DATASET` | Portal | Removes a portal dataset registry entry |
| `DROP JOB` | Orchestrator | Removes a named refresh job |
| `REBUILD SNAPSHOT` | Portal | Forces a data refresh |
| `DROP SNAPSHOT` | Portal | Not supported — no portal endpoint exists; use REBUILD SNAPSHOT |
| `CREATE CONNECTION <alias> AS SMTP(...)` | Portal | Registers a mail relay in the governed catalog; the password is a `SECRET:` reference, never a value |
| `DROP CONNECTION [IF EXISTS] <alias>` | Portal | Removes a cataloged connection by alias |
| `CREATE SUBSCRIPTION`| Portal | Schedules email/PDF report delivery |
| `ALTER SUBSCRIPTION` | Portal | Modifies subscription settings |
| `DROP SUBSCRIPTION` | Portal | Deletes a subscription |
| `DISCONNECT USER` | Portal | Revokes active refresh sessions for a user |
| `REVOKE TOKENS` | Portal | Invalidates all user authentication tokens |
| `RESTART PORTAL` | Portal | Requests process stop for external supervisor restart; requires `Portal:AllowServiceControl=true` |
| `SHUTDOWN PORTAL` | Portal | Requests portal process shutdown; requires `Portal:AllowServiceControl=true` |
| `CREATE JOB`          | Orch     | Schedules a recurring script task |
| `DROP JOB`            | Orch     | Deletes a scheduled script task |
| `KILL JOB`            | Orch     | Stops a running background task |
| `PUBLISH BUNDLE`      | Orch     | Stores versioned scripts in the Orchestrator lockbox |
| `VALIDATE BUNDLE`     | Orch     | Checks bundle dependencies without publishing |
| `EXPORT SCRIPT`       | Orch     | Recovers published bundle files to disk |
| `eng.users`          | Portal   | Lists all registered users |
| `eng.reports`        | Portal   | Lists reports in a folder |
| `eng.favorites()`      | Portal   | Lists favorite reports |
| `eng.recent_reports()` | Portal   | Lists recently viewed reports |
| `eng.catalog_search()` | Portal   | Searches the portal catalog |
| `eng.effective_permissions()` | Portal | Shows resolved portal permissions |
| `eng.usage_metrics()` | Portal | Shows usage and refresh health metrics |
| `eng.operational_metrics` | Portal | Shows live queue, resource, load, and schema health metrics |
| `eng.audit()`  | Portal   | Lists Portal audit rows, optionally filtered by action |
| `eng.active_sessions`| Portal   | Lists unrevoked, unexpired portal refresh sessions |
| `eng.jobs` | Orch | Queryable virtual table for scheduled background tasks |
| `eng.job_history` | Orch | Queryable virtual table for job execution history |
| `eng.data_quality_status` | Orch | Current/local/remote counts-only run quality summary |
| `eng.data_quality_failures` | Orch | Normalized rule-failure counts by run and target |
| `eng.data_quality_rules` | Orch | Current-session `EXPECT` rules; `eng.data_quality_rules(job)` over a `PORTAL` connection for another job's |
| `eng.job_state` | Orch | Queryable virtual table for saved job-state key/value pairs |
| `eng.host_metrics` | Orch | Queryable virtual table for recent host-utilization samples |
| `eng.lineage_history` | Lineage | Cross-run catalog of lineage entries; qualify with a connection for remote Orchestrators |
| `eng.missing_tags` | Lineage | Cross-run stewardship catalog of targets missing required metadata |
| `eng.protected_data` | Lineage | Cross-run protected-data audit for PII/PHI/PCI/sensitive/confidential/restricted lineage |
| `eng.protected_data_suggestions` | Lineage | Reviewable protected-data classifier findings from names, metadata hints, and supported samples |
| `eng.bundles` | Orch | Queryable virtual table for latest published bundle versions |
| `eng.bundle_files` | Orch | Queryable virtual table for files in published bundle versions |
| `eng.bundle_dependencies` | Orch | Queryable virtual table for packaged `RUN SCRIPT` dependencies |
| `eng.capabilities` | Governance | Queryable virtual table for provisioned capabilities and mounts |
| `eng.columns` | Diagnostics | Queryable virtual table for session and connection column metadata |
| `eng.connections` | Diagnostics | Queryable virtual table for active session connections |
| `eng.connection_config` | Diagnostics | Queryable virtual table for redacted active connection configuration |
| `eng.effective_permissions` | Governance | Queryable virtual table for effective principal permissions |
| `eng.tables` | Diagnostics | Queryable virtual table for session and connection table metadata |
| `eng.tenant_context` | Governance | Queryable virtual table for verified tenant identity and isolation |
| `eng.variables` | Diagnostics | Queryable virtual table for session variables with sensitive values masked |
| `eng.views` | Diagnostics | Queryable virtual table for session view definitions |
| `eng.version` | Diagnostics | Queryable virtual table for engine version metadata |
| `eng.safe_zones` | Diagnostics | Queryable virtual table for configured file-system safe zones |
| `eng.profile` | Diagnostics | Queryable virtual table for captured profiling metrics |
| `CREATE OR REPLACE TABLE\|VIEW` | DDL | Drops any existing object first, then creates |
| `eng.tags` | Lineage | Queryable virtual table for lineage tags |
| `eng.lineage` | Lineage | Queryable current-session lineage events |
| `eng.locks` | Diagnostics | Lists active database/job throttle slots and concurrency queue details |
| `eng.sessions` | Diagnostics | Lists active persisted sessions |

---

## 10. Visual Action Commands

Used inside `ACTIONS ( ... )` blocks for interactive reports.

| Action | Syntax | Description |
| :--- | :--- | :--- |
| `DRILL_DOWN` | `DRILL_DOWN ( Target = Visual, Key = Col )` | Filter target visual by selected key |
| `DRILL_IN` | `DRILL_IN ( HIERARCHY = ( Col1, ... ) )` | Step down through a column hierarchy |
| `SET_PARAMETER` | `SET_PARAMETER (@Name, Column/Expr)` | Updates a report parameter |
| `RUN_SCRIPT` | `RUN_SCRIPT ('Path', @P = Col, ...)` | Executes an ETL-SQL script on event |
| `DRILL_REPORT` | `DRILL_REPORT ( FILE = 'Path', ... )` | Opens another report with parameters |
| `CLEAR_FILTERS` | `CLEAR_FILTERS` | Resets all active filters on the page |
| `APPLY_PARAMETERS`| `APPLY_PARAMETERS` | Forces a data refresh with current params |
| `NAVIGATE_PAGE` | `NAVIGATE_PAGE ( 'PageName' )` | Switch to a specific report page |
| `REFRESH_VISUALS` | `REFRESH_VISUALS ( V1, V2, ... )` | Force data refresh for specific visuals |
| `SET_UI_STATE` | `SET_UI_STATE ( Target, Key, Value )` | Dynamically change UI props (VISIBLE, etc.) |
| `BACK` | `BACK` | Return to previous page/report |
| `REFRESH_REPORT` | `REFRESH_REPORT` | Reload the entire report |
| `EXPORT_CSV` | `EXPORT_CSV` | Download current data as CSV |
| `EXPORT_EXCEL` | `EXPORT_EXCEL` | Download current data as Excel |
| `EXPORT_PDF` | `EXPORT_PDF` | Download current page as PDF |

---

## 11. Operators & Symbols

| Symbol | Name | Usage |
| :--- | :--- : | :--- |
| `@` | Variable | Prefix for user variables (e.g. `@name`) |
| `@@` | System Var | Prefix for system variables (e.g. `@@ROWCOUNT`) |
| `#` | Temp Table | Prefix for in-memory tables (e.g. `#staging`) |
| `!` | Env Set | Prefix for environment sets (e.g. `!PROD`) |
| `.` | Member Access | Dot notation for table/schema or MINMAX members |
| `*` | Wildcard | SELECT all columns or path matching |
| `/*@tag: val */` | Metadata Tag | Row-level or column-level tagging |
| `[` ... `]` | Delimiter | Quotes for identifiers with spaces |
| `''` | String | Single quotes for literal strings |
| `ENC:` | Encrypted | Prefix for ciphertext strings |
| `+`, `-`, `*`, `/`, `%` | Arithmetic | Standard math operators |
| `=`, `<>`, `!=`, `<`, `<=`, `>`, `>=` | Comparison | Equality and range operators |
| `AND`, `OR`, `NOT` | Logical | Boolean logic operators |
| `IS NULL`, `IS NOT NULL` | Nullity | Testing for null values |
| `IS [NOT] DISTINCT FROM` | Null-safe comparison | Compares treating `NULL` as a value; never returns `NULL` |
| `LIKE`, `IN`, `BETWEEN`, `EXISTS` | Membership | SQL-style predicate operators |
| `LIKE ANY (...)` / `LIKE ALL (...)` | Membership | Match against a list of patterns (OR / AND) |
| `(` ... `)` | Grouping | Expression and function call grouping |
| `,` | Separator | Argument and list separator |
| `;` | Terminator | Optional statement terminator |
| `--`, `/* ... */` | Comments | Single and multi-line comments |

---

## 12. Data Types

Supported types for `DECLARE`, `CREATE TABLE`, and `CAST`.

| Type Group | Specific Types | Description |
| :--- | :--- | :--- |
| **Integer** | `INT`, `INTEGER`, `BIGINT`, `SMALLINT`, `TINYINT` | 1 to 8 byte signed integers |
| **Numeric** | `DECIMAL(p,s)`, `NUMERIC`, `FLOAT`, `DOUBLE`, `REAL` | Exact and approximate decimals |
| **Monetary** | `MONEY`, `SMALLMONEY` | Currency types |
| **Boolean** | `BIT`, `BOOLEAN`, `BOOL` | True/False or 0/1 values |
| **Character** | `VARCHAR`, `NVARCHAR`, `TEXT`, `NCHAR`, `STRING` | Variable and fixed length strings |
| **Date/Time** | `DATE`, `DATETIME`, `TIMESTAMP`, `DATETIMEOFFSET` | Calendar and clock types |
| **Binary** | `BINARY`, `VARBINARY`, `IMAGE` | Raw byte buffers |
| **Identity** | `UNIQUEIDENTIFIER`, `UUID`, `GUID` | 128-bit unique identifiers |
| **Specialty** | `MINMAX`, `SENSITIVE`, `SECRET`, `RELDATE` | ETL-SQL specific types |
| **Spatial** | `GEOMETRY`, `GEOGRAPHY` | GIS coordinate types |
| **System** | `VARIANT`, `HIERARCHYID`, `ANY` | Dynamic and hierarchical types |

---

## 13. Join Syntax

Used in the `FROM` clause to combine rows from multiple sources.

| Keyword | Category | Usage |
| :--- | :--- | :--- |
| `INNER JOIN` | Type | Default join; returns rows with matching values |
| `LEFT JOIN` | Type | Returns all rows from left, matching from right |
| `RIGHT JOIN` | Type | Returns all rows from right, matching from left |
| `FULL JOIN` | Type | Returns all rows when there is a match in either |
| `CROSS JOIN` | Type | Cartesian product of both tables |
| `CROSS APPLY` | Type | Joins table to a table-valued function/subquery |
| `OUTER APPLY` | Type | Left outer version of CROSS APPLY |
| `JOIN LATERAL` / `, LATERAL` | Type | ANSI alias for `CROSS APPLY` (correlated subquery) |
| `LEFT JOIN LATERAL` | Type | ANSI alias for `OUTER APPLY` |
| `ASOF [LEFT] JOIN` | Type | Nearest-match join on one inequality + optional equality keys |
| `UNNEST(list)` / `FLATTEN(list)` | Table function | Expands a list/array into rows (use in FROM / CROSS APPLY) |
| `HASH JOIN` | Hint | Forces hash-based join algorithm |
| `LOOP JOIN` | Hint | Forces nested-loop join algorithm |
| `FUZZY JOIN` | Hint | Enables similarity-based matching |
| `SEMI` / `ANTI` | Type | Used for existence/non-existence filtering |

---

## 14. Set Operations

Combine result sets from multiple `SELECT` statements.

| Operation | Description |
| :--- | :--- |
| `UNION` | Returns distinct rows from both sets |
| `UNION ALL` | Returns all rows from both sets (including duplicates) |
| `UNION [ALL] BY NAME` | Aligns inputs by column name (not position); missing columns become NULL |
| `EXCEPT` | Returns rows from first set not present in second |
| `MINUS` | Alias for `EXCEPT` |
| `INTERSECT` | Returns only rows present in both sets |

---

## 15. Query Clauses & Modifiers

Standard clauses available within a `SELECT` statement.

| Clause | Description | Documentation | Help File |
| :--- | :--- | :--- | :--- |
| `DISTINCT` | Returns only unique rows | [Statement Reference](reference/statements/README.md) | - |
| `TOP (n)` | Limits results (MSSQL style) | [Statement Reference](reference/statements/README.md) | - |
| `LIMIT n` | Limits results (Postgres style) | [Statement Reference](reference/statements/README.md) | - |
| `OFFSET n` | Skips first N rows | [Statement Reference](reference/statements/README.md) | - |
| `FETCH FIRST/NEXT n ROWS ONLY` | SQL:2008 result limiting | [SELECT](reference/statements/dml/select.md) | - |
| `USING SAMPLE n PERCENT\|ROWS` | Random row sampling (`REPEATABLE (seed)` for determinism) | [Select Modifiers](reference/statements/query-syntax/select-modifiers.md) | - |
| `VALUES (...) AS alias(...)` | Standalone table constructor in `FROM`/`JOIN` | [SELECT](reference/statements/dml/select.md) | - |
| `GROUP BY` | Aggregates rows by column values (supports positional `GROUP BY 1, 2`) | [Statement Reference](reference/statements/README.md) | - |
| `GROUP BY ALL` | Group by all non-aggregate SELECT expressions | [GROUP BY ALL](reference/statements/query-syntax/group-by-all.md) | [GROUP_BY_ALL.md](reference/statements/query-syntax/group-by-all.md) |
| `HAVING` | Filters aggregated groups | [Statement Reference](reference/statements/README.md) | - |
| `ORDER BY` | Sorts the final result set (supports positional `ORDER BY 1, 2`) | [Statement Reference](reference/statements/README.md) | - |
| `ORDER BY ALL` | Sorts by every output column, left to right (`[DESC]`) | [Select Modifiers](reference/statements/query-syntax/select-modifiers.md) | [SELECT_MODIFIERS.md](reference/statements/query-syntax/select-modifiers.md) |
| `* EXCLUDE / REPLACE / RENAME` | Inline star-projection modifiers | [Select Modifiers](reference/statements/query-syntax/select-modifiers.md) | [SELECT_MODIFIERS.md](reference/statements/query-syntax/select-modifiers.md) |
| `COLUMNS(* EXCLUDE (...))` / `COLUMNS('regex')` | Multi-column projection selector | [Select Modifiers](reference/statements/query-syntax/select-modifiers.md) | [SELECT_MODIFIERS.md](reference/statements/query-syntax/select-modifiers.md) |
| `count()` | Shorthand for `COUNT(*)` | [Select Modifiers](reference/statements/query-syntax/select-modifiers.md) | [SELECT_MODIFIERS.md](reference/statements/query-syntax/select-modifiers.md) |
| Lateral column aliases | A SELECT item (or `ORDER BY`) may reference an alias from an earlier item | [Select Modifiers](reference/statements/query-syntax/select-modifiers.md) | [SELECT_MODIFIERS.md](reference/statements/query-syntax/select-modifiers.md) |
| Trailing commas / `1_000` separators | Lenient list commas; underscores in numeric literals | [Select Modifiers](reference/statements/query-syntax/select-modifiers.md) | [SELECT_MODIFIERS.md](reference/statements/query-syntax/select-modifiers.md) |
| `ASC` / `DESC` | Sorting direction | [Statement Reference](reference/statements/README.md) | - |
| `ROLLUP` | Grouping set extension for hierarchies | [Statement Reference](reference/statements/README.md) | - |
| `CUBE` | Grouping set extension for all permutations| [Statement Reference](reference/statements/README.md) | - |
| `GROUPING SETS` | Explicit grouping set list | [Statement Reference](reference/statements/README.md) | - |
| `QUALIFY` | Filters results of window functions | [QUALIFY](reference/statements/query-syntax/qualify.md) | [QUALIFY.md](reference/statements/query-syntax/qualify.md) |
| `FILTER (WHERE ...)` | Per-aggregate conditional filter | [FILTER](reference/statements/query-syntax/filter.md) | [FILTER.md](reference/statements/query-syntax/filter.md) |
| `ILIKE` | Case-insensitive pattern match | [ILIKE](reference/statements/query-syntax/ilike.md) | [ILIKE.md](reference/statements/query-syntax/ilike.md) |
| `~` / `~*` | Regex match / case-insensitive regex match | [ILIKE and pattern predicates](reference/statements/query-syntax/ilike.md) | - |
| `OUTPUT` | Returns modified rows (DML only) | [Statement Reference](reference/statements/README.md) | - |
| `FOR JSON` | Formats output as JSON (PATH/AUTO/RAW) | [Statement Reference](reference/statements/README.md) | - |
| `FOR XML` | Formats output as XML (PATH/AUTO/RAW) | [Statement Reference](reference/statements/README.md) | - |
| `CASE` | Start of conditional expression | [Statement Reference](reference/statements/README.md) | [CASE.md](reference/statements/query-syntax/case.md) |
| `WHEN / THEN` | Conditional branch | [Statement Reference](reference/statements/README.md) | [CASE.md](reference/statements/query-syntax/case.md) |
| `ELSE / END` | Fallback and termination of CASE | [Statement Reference](reference/statements/README.md) | [CASE.md](reference/statements/query-syntax/case.md) |
| `??` | Null-coalescing operator (lowers to `COALESCE`) | [Expressions and Operators](reference/statements/expressions-and-operators.md) | [Expressions and Operators](reference/statements/expressions-and-operators.md) |
| `=>` ... `:` | Arrow conditional expression (lowers to `CASE`) | [Expressions and Operators](reference/statements/expressions-and-operators.md) | [Expressions and Operators](reference/statements/expressions-and-operators.md) |
| `->` / `->>` | JSON access operators (lowers to `JSON_GET`/`JSON_GET_TEXT`) | [Expressions and Operators](reference/statements/expressions-and-operators.md) | [Expressions and Operators](reference/statements/expressions-and-operators.md) |

---

## 16. Table Operators

Operators that transform the shape of a table in the `FROM` clause.

| Operator | Syntax | Description | Documentation | Help File |
| :--- | :--- | :--- | :--- | :--- |
| `PIVOT` | `PIVOT ( agg(col) FOR pivot_col IN (...) )` | Rotates rows into columns | [PIVOT](reference/statements/query-syntax/pivot.md) | [PIVOT.md](reference/statements/query-syntax/pivot.md) |
| `UNPIVOT` | `UNPIVOT ( val_col FOR name_col IN (...) )` | Rotates columns into rows | [PIVOT](reference/statements/query-syntax/pivot.md) | [PIVOT.md](reference/statements/query-syntax/pivot.md) |
| `PIVOT` (DuckDB) | `PIVOT src ON cols [IN (...)] USING aggs [GROUP BY cols]` | Statement form; dynamic values, multi-col/agg | [PIVOT](reference/statements/query-syntax/pivot.md) | [PIVOT.md](reference/statements/query-syntax/pivot.md) |
| `UNPIVOT` (DuckDB) | `UNPIVOT src ON cols\|COLUMNS(* EXCLUDE (...)) INTO NAME n VALUE v` | Statement form; supports `COLUMNS(* EXCLUDE)` | [PIVOT](reference/statements/query-syntax/pivot.md) | [PIVOT.md](reference/statements/query-syntax/pivot.md) |
| `MATCH_RECOGNIZE` | `MATCH_RECOGNIZE (PARTITION BY ... ORDER BY ... MEASURES ... PATTERN (...) DEFINE ...)` | Finds row patterns in ordered sequences | [MATCH_RECOGNIZE](reference/statements/query-syntax/match-recognize.md) | [MATCH_RECOGNIZE.md](reference/statements/query-syntax/match-recognize.md) |

---

## 17. Metadata & Script Tags

Annotations used for lineage, security, and script behavior.

| Tag | Level | Usage |
| :--- | :--- | :--- |
| `/*@tag: val */` | Row / Column | Lineage and metadata tagging |
| `@tag: val;` | Script Header | Script-level metadata (e.g. `@author: dev`) |
| `ENC:...` | Literal | Prefix for engine-encrypted strings |
| `BANG` / `!` | Session | Prefix for named Environment Sets (e.g. `!PROD`) |

---

## 18. CLI Commands

Commands run outside a script via `etl-sql <command>`. These are shell-level entry points, not SQL statements.

| Command | Purpose | Help File |
| :--- | :--- | :--- |
| `etl-sql admin access-simulate` | Simulate what a user can reach — the access question, answered without a browser | [admin access simulate](reference/cli/admin-access-simulate.md) |
| `etl-sql admin backup` | Back up portal/orchestrator state into split-custody data and keys archives | [admin backup](reference/cli/admin-backup.md) |
| `etl-sql admin doctor` | Perform a system health check to verify the environment | [admin doctor](reference/cli/admin-doctor.md) |
| `etl-sql admin gateway resource approve` | Approve a local Gateway resource | [admin gateway resource approve](reference/cli/admin-gateway-resource-approve.md) |
| `etl-sql admin gateway resource disable` | Disable a local Gateway resource | [admin gateway resource disable](reference/cli/admin-gateway-resource-disable.md) |
| `etl-sql admin gateway resource list` | List local Gateway resources without revealing targets or credentials | [admin gateway resource list](reference/cli/admin-gateway-resource-list.md) |
| `etl-sql admin gateway resource propose` | Propose a local connector resource | [admin gateway resource propose](reference/cli/admin-gateway-resource-propose.md) |
| `etl-sql admin gateway resource` | Administer the protected Gateway-local resource registry | [admin gateway resource](reference/cli/admin-gateway-resource.md) |
| `etl-sql admin gateway setup` | Configure and enroll this machine as an on-premises Data Gateway node | [admin gateway setup](reference/cli/admin-gateway-setup.md) |
| `etl-sql admin gateway start` | Run the enrolled Gateway daemon in the foreground | [admin gateway start](reference/cli/admin-gateway-start.md) |
| `etl-sql admin gateway` | On-premises Data Gateway administration and setup | [admin gateway](reference/cli/admin-gateway.md) |
| `etl-sql admin group add-member` | Add a user to a group | [admin group add member](reference/cli/admin-group-add-member.md) |
| `etl-sql admin group capabilities` | Show a group's Studio capabilities | [admin group capabilities](reference/cli/admin-group-capabilities.md) |
| `etl-sql admin group create` | Create a Portal group | [admin group create](reference/cli/admin-group-create.md) |
| `etl-sql admin group delete` | Delete a Portal group | [admin group delete](reference/cli/admin-group-delete.md) |
| `etl-sql admin group list` | List Portal groups | [admin group list](reference/cli/admin-group-list.md) |
| `etl-sql admin group members` | List the members of a group | [admin group members](reference/cli/admin-group-members.md) |
| `etl-sql admin group remove-member` | Remove a user from a group | [admin group remove member](reference/cli/admin-group-remove-member.md) |
| `etl-sql admin group set-capabilities` | Replace a group's Studio capabilities with the given set | [admin group set capabilities](reference/cli/admin-group-set-capabilities.md) |
| `etl-sql admin group update` | Rename a group or change its description | [admin group update](reference/cli/admin-group-update.md) |
| `etl-sql admin group` | Manage Portal groups and their membership | [admin group](reference/cli/admin-group.md) |
| `etl-sql admin ha-soak diagnostics` | Export a redacted diagnostics bundle for a topology run | [admin ha soak diagnostics](reference/cli/admin-ha-soak-diagnostics.md) |
| `etl-sql admin ha-soak evidence` | Generate the non-secret HA soak evidence checklist | [admin ha soak evidence](reference/cli/admin-ha-soak-evidence.md) |
| `etl-sql admin ha-soak fault-plan` | Generate the HA fault-injection plan | [admin ha soak fault plan](reference/cli/admin-ha-soak-fault-plan.md) |
| `etl-sql admin ha-soak fault-run` | Run the bounded HA fault-injection harness | [admin ha soak fault run](reference/cli/admin-ha-soak-fault-run.md) |
| `etl-sql admin ha-soak large-job-plan` | Generate the concurrent large-job soak plan | [admin ha soak large job plan](reference/cli/admin-ha-soak-large-job-plan.md) |
| `etl-sql admin ha-soak large-job-run` | Run the bounded concurrent large-job soak harness | [admin ha soak large job run](reference/cli/admin-ha-soak-large-job-run.md) |
| `etl-sql admin ha-soak metrics` | Capture a non-secret PostgreSQL metrics snapshot | [admin ha soak metrics](reference/cli/admin-ha-soak-metrics.md) |
| `etl-sql admin ha-soak prepare` | Generate an isolated PostgreSQL HA soak topology run root | [admin ha soak prepare](reference/cli/admin-ha-soak-prepare.md) |
| `etl-sql admin ha-soak runbook` | Generate an ordered operator runbook for a topology run | [admin ha soak runbook](reference/cli/admin-ha-soak-runbook.md) |
| `etl-sql admin ha-soak validate` | Validate completed HA soak evidence before citing it | [admin ha soak validate](reference/cli/admin-ha-soak-validate.md) |
| `etl-sql admin ha-soak workload` | Materialize the sustained-load workload config for a topology run | [admin ha soak workload](reference/cli/admin-ha-soak-workload.md) |
| `etl-sql admin ha-soak` | Prepare and collect PostgreSQL HA soak certification artifacts | [admin ha soak](reference/cli/admin-ha-soak.md) |
| `etl-sql admin machine connection delete` | Permanently remove a machine-local shared connection | [admin machine connection delete](reference/cli/admin-machine-connection-delete.md) |
| `etl-sql admin machine connection disable` | Disable a machine-local shared connection | [admin machine connection disable](reference/cli/admin-machine-connection-disable.md) |
| `etl-sql admin machine connection enable` | Re-enable a machine-local shared connection | [admin machine connection enable](reference/cli/admin-machine-connection-enable.md) |
| `etl-sql admin machine connection list` | List machine-local shared connections and status | [admin machine connection list](reference/cli/admin-machine-connection-list.md) |
| `etl-sql admin machine connection set` | Store a machine-local SHARED: connection | [admin machine connection set](reference/cli/admin-machine-connection-set.md) |
| `etl-sql admin machine connection verify` | Verify a machine-local shared connection without printing values | [admin machine connection verify](reference/cli/admin-machine-connection-verify.md) |
| `etl-sql admin machine connection` | Manage the machine-local shared connection catalog | [admin machine connection](reference/cli/admin-machine-connection.md) |
| `etl-sql admin machine secret delete` | Permanently remove a machine-local secret | [admin machine secret delete](reference/cli/admin-machine-secret-delete.md) |
| `etl-sql admin machine secret disable` | Disable a machine-local secret | [admin machine secret disable](reference/cli/admin-machine-secret-disable.md) |
| `etl-sql admin machine secret enable` | Re-enable a disabled machine-local secret | [admin machine secret enable](reference/cli/admin-machine-secret-enable.md) |
| `etl-sql admin machine secret list` | List names and status from the machine-local secret store | [admin machine secret list](reference/cli/admin-machine-secret-list.md) |
| `etl-sql admin machine secret rotate` | Replace an existing machine-local secret | [admin machine secret rotate](reference/cli/admin-machine-secret-rotate.md) |
| `etl-sql admin machine secret set` | Encrypt and store a named machine-local secret | [admin machine secret set](reference/cli/admin-machine-secret-set.md) |
| `etl-sql admin machine secret verify` | Resolve a machine-local secret without printing the value | [admin machine secret verify](reference/cli/admin-machine-secret-verify.md) |
| `etl-sql admin machine secret` | Manage the machine-local Governance:Secrets provider | [admin machine secret](reference/cli/admin-machine-secret.md) |
| `etl-sql admin machine tool delete` | Permanently remove a machine-local tool | [admin machine tool delete](reference/cli/admin-machine-tool-delete.md) |
| `etl-sql admin machine tool list` | List machine-local tools | [admin machine tool list](reference/cli/admin-machine-tool-list.md) |
| `etl-sql admin machine tool set` | Store a machine-local tool | [admin machine tool set](reference/cli/admin-machine-tool-set.md) |
| `etl-sql admin machine tool` | Manage the machine-local tool catalog | [admin machine tool](reference/cli/admin-machine-tool.md) |
| `etl-sql admin machine` | Manage machine-local governance stores | [admin machine](reference/cli/admin-machine.md) |
| `etl-sql admin migrate-database` | Copy Portal/Orchestrator state from SQLite into the configured PostgreSQL deployment | [admin migrate database](reference/cli/admin-migrate-database.md) |
| `etl-sql admin orchestrator adopt` | Assign an owner to every unowned object (administrators only) | [admin orchestrator adopt](reference/cli/admin-orchestrator-adopt.md) |
| `etl-sql admin orchestrator grant` | Grant a principal a permission on an object | [admin orchestrator grant](reference/cli/admin-orchestrator-grant.md) |
| `etl-sql admin orchestrator revoke` | Revoke a principal's grant on an object | [admin orchestrator revoke](reference/cli/admin-orchestrator-revoke.md) |
| `etl-sql admin orchestrator set-owner` | Reassign an object's owner (administrators only) | [admin orchestrator set owner](reference/cli/admin-orchestrator-set-owner.md) |
| `etl-sql admin orchestrator show` | Show the grants on one Orchestrator object | [admin orchestrator show](reference/cli/admin-orchestrator-show.md) |
| `etl-sql admin orchestrator unowned` | List objects with no recorded owner — reachable only by administrators | [admin orchestrator unowned](reference/cli/admin-orchestrator-unowned.md) |
| `etl-sql admin orchestrator` | Manage per-object Orchestrator grants and ownership | [admin orchestrator](reference/cli/admin-orchestrator.md) |
| `etl-sql admin portal-whoami` | Resolve Portal credentials and print the identity, roles, and scopes (never a secret) | [admin portal whoami](reference/cli/admin-portal-whoami.md) |
| `etl-sql admin promotion export` | Export eligible Orchestrator catalog and governance state | [admin promotion export](reference/cli/admin-promotion-export.md) |
| `etl-sql admin promotion import` | Import an Orchestrator promotion package idempotently | [admin promotion import](reference/cli/admin-promotion-import.md) |
| `etl-sql admin promotion preflight` | Create a secret-safe, mutation-free promotion inventory | [admin promotion preflight](reference/cli/admin-promotion-preflight.md) |
| `etl-sql admin promotion saas-delete` | Delete one Managed Dedicated tenant boundary under signed retention/legal authorization | [admin promotion saas delete](reference/cli/admin-promotion-saas-delete.md) |
| `etl-sql admin promotion saas-fleet-plan` | Plan a release rollout across the Managed Dedicated fleet (plans only; never upgrades) | [admin promotion saas fleet plan](reference/cli/admin-promotion-saas-fleet-plan.md) |
| `etl-sql admin promotion saas-onboard` | Create and populate one physically isolated SaaS tenant boundary | [admin promotion saas onboard](reference/cli/admin-promotion-saas-onboard.md) |
| `etl-sql admin promotion saas-upgrade` | Drain and upgrade one Managed Dedicated tenant boundary | [admin promotion saas upgrade](reference/cli/admin-promotion-saas-upgrade.md) |
| `etl-sql admin promotion validate` | Validate mappings and collisions without changing the target | [admin promotion validate](reference/cli/admin-promotion-validate.md) |
| `etl-sql admin promotion` | Inspect and prepare deployment-profile promotions | [admin promotion](reference/cli/admin-promotion.md) |
| `etl-sql admin restore` | Validate and restore a backup (data + keys archives) | [admin restore](reference/cli/admin-restore.md) |
| `etl-sql admin service-account create` | Create a Portal service account | [admin service account create](reference/cli/admin-service-account-create.md) |
| `etl-sql admin service-account list` | List Portal service accounts | [admin service account list](reference/cli/admin-service-account-list.md) |
| `etl-sql admin service-account revoke` | Permanently revoke a Portal service account | [admin service account revoke](reference/cli/admin-service-account-revoke.md) |
| `etl-sql admin service-account rotate-secret` | Rotate a service account secret | [admin service account rotate secret](reference/cli/admin-service-account-rotate-secret.md) |
| `etl-sql admin service-account update` | Update a Portal service account | [admin service account update](reference/cli/admin-service-account-update.md) |
| `etl-sql admin service-account` | Manage Portal service accounts | [admin service account](reference/cli/admin-service-account.md) |
| `etl-sql admin session disconnect` | Disconnect a user's Portal sessions | [admin session disconnect](reference/cli/admin-session-disconnect.md) |
| `etl-sql admin session list` | List active Portal sessions | [admin session list](reference/cli/admin-session-list.md) |
| `etl-sql admin session` | Inspect and disconnect Portal sign-in sessions | [admin session](reference/cli/admin-session.md) |
| `etl-sql admin support-bundle` | Collect a redacted support archive (config, health, logs, database metrics) | [admin support bundle](reference/cli/admin-support-bundle.md) |
| `etl-sql admin tenant export` | Compose a signed, optionally tenant-encrypted portability bundle | [admin tenant export](reference/cli/admin-tenant-export.md) |
| `etl-sql admin tenant import` | Preflight and apply a bundle with workloads disabled | [admin tenant import](reference/cli/admin-tenant-import.md) |
| `etl-sql admin tenant preflight` | Report what a target must supply before a bundle can be imported | [admin tenant preflight](reference/cli/admin-tenant-preflight.md) |
| `etl-sql admin tenant validate` | Verify a bundle's integrity and, with --operator-key, its authenticity | [admin tenant validate](reference/cli/admin-tenant-validate.md) |
| `etl-sql admin tenant` | Export, inspect, and import tenant portability bundles | [admin tenant](reference/cli/admin-tenant.md) |
| `etl-sql admin user create` | Create a Portal user | [admin user create](reference/cli/admin-user-create.md) |
| `etl-sql admin user delete` | Delete a Portal user | [admin user delete](reference/cli/admin-user-delete.md) |
| `etl-sql admin user disable` | Deactivate a Portal user | [admin user disable](reference/cli/admin-user-disable.md) |
| `etl-sql admin user enable` | Reactivate a Portal user | [admin user enable](reference/cli/admin-user-enable.md) |
| `etl-sql admin user list` | List Portal users | [admin user list](reference/cli/admin-user-list.md) |
| `etl-sql admin user permissions` | Show a user's effective permissions — answers "why can this person see this" | [admin user permissions](reference/cli/admin-user-permissions.md) |
| `etl-sql admin user reset-password` | Set a user's password, read from stdin | [admin user reset password](reference/cli/admin-user-reset-password.md) |
| `etl-sql admin user revoke-tokens` | Revoke a user's issued tokens | [admin user revoke tokens](reference/cli/admin-user-revoke-tokens.md) |
| `etl-sql admin user show` | Show one Portal user | [admin user show](reference/cli/admin-user-show.md) |
| `etl-sql admin user update` | Update a Portal user's details or role | [admin user update](reference/cli/admin-user-update.md) |
| `etl-sql admin user` | Manage Portal users | [admin user](reference/cli/admin-user.md) |
| `etl-sql admin` | Operator and administration commands | [admin](reference/cli/admin.md) |
| `etl-sql config setup-jwt` | Generate a secure JWT secret and update appsettings.json | [config setup jwt](reference/cli/config-setup-jwt.md) |
| `etl-sql config` | Manage application configuration | [config](reference/cli/config.md) |
| `etl-sql doctor` | Perform a system health check to verify the environment | [doctor](reference/cli/doctor.md) |
| `etl-sql encrypt` | Utility to encrypt a string for secure connections | [encrypt](reference/cli/encrypt.md) |
| `etl-sql enterprise enroll` | Enroll this machine in authoritative enterprise policy | [enterprise enroll](reference/cli/enterprise-enroll.md) |
| `etl-sql enterprise status` | Inspect machine enterprise enrollment | [enterprise status](reference/cli/enterprise-status.md) |
| `etl-sql enterprise unenroll` | Remove machine enterprise enrollment | [enterprise unenroll](reference/cli/enterprise-unenroll.md) |
| `etl-sql enterprise` | Manage machine-level enterprise policy enrollment | [enterprise](reference/cli/enterprise.md) |
| `etl-sql extract-spec` | Extract data dictionary / schema pages from a large PDF specification | [extract spec](reference/cli/extract-spec.md) |
| `etl-sql gateway resource approve` | Approve a local Gateway resource | [gateway resource approve](reference/cli/gateway-resource-approve.md) |
| `etl-sql gateway resource disable` | Disable a local Gateway resource | [gateway resource disable](reference/cli/gateway-resource-disable.md) |
| `etl-sql gateway resource list` | List local Gateway resources without revealing targets or credentials | [gateway resource list](reference/cli/gateway-resource-list.md) |
| `etl-sql gateway resource propose` | Propose a local connector resource | [gateway resource propose](reference/cli/gateway-resource-propose.md) |
| `etl-sql gateway resource` | Administer the protected Gateway-local resource registry | [gateway resource](reference/cli/gateway-resource.md) |
| `etl-sql gateway setup` | Configure and enroll this machine as an on-premises Data Gateway node | [gateway setup](reference/cli/gateway-setup.md) |
| `etl-sql gateway start` | Run the enrolled Gateway daemon in the foreground | [gateway start](reference/cli/gateway-start.md) |
| `etl-sql gateway` | On-premises Data Gateway administration and setup | [gateway](reference/cli/gateway.md) |
| `etl-sql gen-script` | Compile a schema JSON specification into a validated ETL-SQL script template | [gen script](reference/cli/gen-script.md) |
| `etl-sql generate` | Generate mock data for testing projects | [generate](reference/cli/generate.md) |
| `etl-sql init` | Scaffold a starter configuration and first ETL-SQL script for new users | [init](reference/cli/init.md) |
| `etl-sql notices` | Show third-party notices and dependency credits | [notices](reference/cli/notices.md) |
| `etl-sql purge` | Delete all ETL-SQL runtime data (reports, snapshots, databases, logs, sessions) | [purge](reference/cli/purge.md) |
| `etl-sql run` | Execute an ETL-SQL script | [run](reference/cli/run.md) |
| `etl-sql scan` | Inspect local or cataloged database schemas for stewardship gaps | [scan](reference/cli/scan.md) |
| `etl-sql serve` | Start a live preview server for a Report-SQL script | [serve](reference/cli/serve.md) |
| `etl-sql session clear` | Clear a session state | [session clear](reference/cli/session-clear.md) |
| `etl-sql session` | Manage ad-hoc execution sessions | [session](reference/cli/session.md) |
| `etl-sql studio list` | List active ETL-SQL Studio background server instances | [studio list](reference/cli/studio-list.md) |
| `etl-sql studio open` | Open or ensure an active ETL-SQL Studio server instance and launch in browser | [studio open](reference/cli/studio-open.md) |
| `etl-sql studio stop` | Stop active ETL-SQL Studio background server instance | [studio stop](reference/cli/studio-stop.md) |
| `etl-sql studio` | Launch the modern ETL-SQL Studio visual & script workbench on local loopback | [studio](reference/cli/studio.md) |
| `etl-sql test` | Run native ETL-SQL test suites (*.test.etlsql) and table assertions | [test](reference/cli/test.md) |
| `etl-sql ui edit` | Start the modern windowed Terminal IDE (default) | [ui edit](reference/cli/ui-edit.md) |
| `etl-sql ui old` | Start the legacy Spectre-based console editor | [ui old](reference/cli/ui-old.md) |
| `etl-sql ui repl` | Start the JSON-based REPL protocol for IDE integration | [ui repl](reference/cli/ui-repl.md) |
| `etl-sql ui simple` | Start the simple interactive menu UI | [ui simple](reference/cli/ui-simple.md) |
| `etl-sql ui` | Interactive user interface commands | [ui](reference/cli/ui.md) |

See [Getting Started](guides/onboarding/getting-started.md) and [Administration](administration/platform/README.md) for full option reference.

---

<!-- BEGIN GENERATED CANONICAL TOKEN INDEX -->
## 19. Canonical Token Inventory

> Generated from `src/ETL-SQL.Core/Common/LanguageMetadata.cs`. Run `node ./scripts/generate-syntax-index.js` after adding, removing, or renaming language tokens.

### 19.1 DML Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `AS` | DML | Canonical language token |
| `ASC` | DML | Canonical language token |
| `BY` | DML | Canonical language token |
| `CALENDAR` | DML | Canonical language token |
| `COMPARE` | DML | Canonical language token |
| `CUBE` | DML | Canonical language token |
| `DATASETS` | DML | Canonical language token |
| `DELETE` | DML | Canonical language token |
| `DESC` | DML | Canonical language token |
| `DISTINCT` | DML | Canonical language token |
| `FETCH` | DML | Canonical language token |
| `FIRST` | DML | Canonical language token |
| `FROM` | DML | Canonical language token |
| `GROUP` | DML | Canonical language token |
| `GROUPING` | DML | Canonical language token |
| `HAVING` | DML | Canonical language token |
| `INSERT` | DML | Canonical language token |
| `INTO` | DML | Canonical language token |
| `LIMIT` | DML | Canonical language token |
| `MATCHED` | DML | Canonical language token |
| `MERGE` | DML | Canonical language token |
| `NEXT` | DML | Canonical language token |
| `OFFSET` | DML | Canonical language token |
| `ONLY` | DML | Canonical language token |
| `ORDER` | DML | Canonical language token |
| `PERCENT` | DML | Canonical language token |
| `PIVOT` | DML | Canonical language token |
| `QUALIFY` | DML | Canonical language token |
| `QUARANTINE` | DML | Canonical language token |
| `REPLACE` | DML | Canonical language token |
| `REPLAY` | DML | Canonical language token |
| `ROLLUP` | DML | Canonical language token |
| `ROW` | DML | Canonical language token |
| `ROWS` | DML | Canonical language token |
| `SELECT` | DML | Canonical language token |
| `SET` | DML | Canonical language token |
| `SOURCE` | DML | Canonical language token |
| `TARGET` | DML | Canonical language token |
| `TIES` | DML | Canonical language token |
| `TOP` | DML | Canonical language token |
| `TRANSFORM` | DML | Canonical language token |
| `TRUNCATE` | DML | Canonical language token |
| `UNPIVOT` | DML | Canonical language token |
| `UPDATE` | DML | Canonical language token |
| `USING` | DML | Canonical language token |
| `VALUES` | DML | Canonical language token |
| `WHERE` | DML | Canonical language token |
| `WINDOW` | DML | Canonical language token |

### 19.2 DDL Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `ADD` | DDL | Canonical language token |
| `ALTER` | DDL | Canonical language token |
| `CHECK` | DDL | Canonical language token |
| `CLEAR` | DDL | Canonical language token |
| `COLUMN` | DDL | Canonical language token |
| `COMMIT` | DDL | Canonical language token |
| `CONNECTION` | DDL | Canonical language token |
| `CONSTRAINT` | DDL | Canonical language token |
| `CONTAINER` | DDL | Canonical language token |
| `CREATE` | DDL | Canonical language token |
| `DATABASE` | DDL | Canonical language token |
| `DATASET` | DDL | Canonical language token |
| `DECLARE` | DDL | Canonical language token |
| `DECRYPT` | DDL | Canonical language token |
| `DIRECTORY` | DDL | Canonical language token |
| `DIRECTORY_CONTENTS` | DDL | Canonical language token |
| `DROP` | DDL | Canonical language token |
| `ENCRYPT` | DDL | Canonical language token |
| `FOREIGN` | DDL | Canonical language token |
| `FUNCTION` | DDL | Canonical language token |
| `INDEX` | DDL | Canonical language token |
| `KEY` | DDL | Canonical language token |
| `LINEAGE` | DDL | Canonical language token |
| `NAVIGATION` | DDL | Canonical language token |
| `PAGE` | DDL | Canonical language token |
| `PGP_KEY_PAIR` | DDL | Canonical language token |
| `PRIMARY` | DDL | Canonical language token |
| `PROCEDURE` | DDL | Canonical language token |
| `REFERENCES` | DDL | Canonical language token |
| `RENAME` | DDL | Canonical language token |
| `RETURNS` | DDL | Canonical language token |
| `ROLLBACK` | DDL | Canonical language token |
| `SCHEMA` | DDL | Canonical language token |
| `SETS` | DDL | Canonical language token |
| `SSH_KEY_PAIR` | DDL | Canonical language token |
| `STYLE` | DDL | Canonical language token |
| `TABLE` | DDL | Canonical language token |
| `TAG` | DDL | Canonical language token |
| `TEMPLATE` | DDL | Canonical language token |
| `TOOL` | DDL | Canonical language token |
| `TRAN` | DDL | Canonical language token |
| `TRANSACTION` | DDL | Canonical language token |
| `UNIQUE` | DDL | Canonical language token |
| `VIEW` | DDL | Canonical language token |
| `VIEWS` | DDL | Canonical language token |
| `VISUAL` | DDL | Canonical language token |

### 19.3 Control Flow Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `ASSERT` | Control Flow | Canonical language token |
| `BEGIN` | Control Flow | Canonical language token |
| `BREAK` | Control Flow | Canonical language token |
| `CASE` | Control Flow | Canonical language token |
| `CATCH` | Control Flow | Canonical language token |
| `CONTINUE` | Control Flow | Canonical language token |
| `ELSE` | Control Flow | Canonical language token |
| `END` | Control Flow | Canonical language token |
| `EXEC` | Control Flow | Canonical language token |
| `EXECUTE` | Control Flow | Canonical language token |
| `FOR` | Control Flow | Canonical language token |
| `FOREACH` | Control Flow | Canonical language token |
| `GO` | Control Flow | Canonical language token |
| `GOTO` | Control Flow | Canonical language token |
| `IF` | Control Flow | Canonical language token |
| `RAISEERROR` | Control Flow | Canonical language token |
| `RAISERROR` | Control Flow | Canonical language token |
| `RETURN` | Control Flow | Canonical language token |
| `THEN` | Control Flow | Canonical language token |
| `THROW` | Control Flow | Canonical language token |
| `TOOL` | Control Flow | Canonical language token |
| `TRY` | Control Flow | Canonical language token |
| `WHEN` | Control Flow | Canonical language token |
| `WHILE` | Control Flow | Canonical language token |

### 19.4 Join Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `ALL` | Join | Canonical language token |
| `APPLY` | Join | Canonical language token |
| `ASOF` | Join | Canonical language token |
| `CROSS` | Join | Canonical language token |
| `EXCEPT` | Join | Canonical language token |
| `FULL` | Join | Canonical language token |
| `FUZZY` | Join | Canonical language token |
| `INNER` | Join | Canonical language token |
| `INTERSECT` | Join | Canonical language token |
| `JOIN` | Join | Canonical language token |
| `KEEP` | Join | Canonical language token |
| `LATERAL` | Join | Canonical language token |
| `LEFT` | Join | Canonical language token |
| `OUTER` | Join | Canonical language token |
| `RIGHT` | Join | Canonical language token |
| `UNION` | Join | Canonical language token |

### 19.5 Operator Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `AND` | Operator | Canonical language token |
| `BETWEEN` | Operator | Canonical language token |
| `ESCAPE` | Operator | Canonical language token |
| `EXISTS` | Operator | Canonical language token |
| `ILIKE` | Operator | Canonical language token |
| `IN` | Operator | Canonical language token |
| `IS` | Operator | Canonical language token |
| `LIKE` | Operator | Canonical language token |
| `NOT` | Operator | Canonical language token |
| `NULL` | Operator | Canonical language token |
| `OR` | Operator | Canonical language token |

### 19.6 Settings & Engine Configuration Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `ALLOW_FILE_OPERATIONS` | Settings & Engine Configuration | Canonical language token |
| `ALLOW_RECURSIVE_LAYERS` | Settings & Engine Configuration | Canonical language token |
| `CASE_SENSITIVE` | Settings & Engine Configuration | Canonical language token |
| `CONFIG` | Settings & Engine Configuration | Canonical language token |
| `EXTERNAL_HASH_PARTITIONS` | Settings & Engine Configuration | Canonical language token |
| `EXTERNAL_SORT_CHUNK_SIZE` | Settings & Engine Configuration | Canonical language token |
| `FOREACH_PAGE_SIZE` | Settings & Engine Configuration | Canonical language token |
| `INTERACTIVE_MODE` | Settings & Engine Configuration | Canonical language token |
| `JOIN_SPILL_THRESHOLD` | Settings & Engine Configuration | Canonical language token |
| `LINT` | Settings & Engine Configuration | Canonical language token |
| `MAX_FILE_OPERATIONS` | Settings & Engine Configuration | Canonical language token |
| `MAX_GENERATE_ROWS` | Settings & Engine Configuration | Canonical language token |
| `MAX_GENERATE_ROWS` | Settings & Engine Configuration | Canonical language token |
| `MAX_GROUPING_SETS` | Settings & Engine Configuration | Canonical language token |
| `MAX_IN_MEMORY_BATCHES` | Settings & Engine Configuration | Canonical language token |
| `MAX_INTERNAL_OPERATIONS` | Settings & Engine Configuration | Canonical language token |
| `MAX_INTERNAL_OPERATIONS` | Settings & Engine Configuration | Canonical language token |
| `MAX_LAST_RESULT_ROWS` | Settings & Engine Configuration | Canonical language token |
| `MAX_MESSAGES` | Settings & Engine Configuration | Canonical language token |
| `MAX_PARALLEL_DEGREE` | Settings & Engine Configuration | Canonical language token |
| `MAX_RECURSIVE_DEPTH` | Settings & Engine Configuration | Canonical language token |
| `MAX_SESSION_SIZE` | Settings & Engine Configuration | Canonical language token |
| `MAX_SMTP_EMAILS_PER_SCRIPT` | Settings & Engine Configuration | Canonical language token |
| `MAX_SMTP_EMAILS_PER_SCRIPT` | Settings & Engine Configuration | Canonical language token |
| `MAX_STRING_RESULT_SIZE` | Settings & Engine Configuration | Canonical language token |
| `PROFILE` | Settings & Engine Configuration | Canonical language token |
| `PROFILING` | Settings & Engine Configuration | Canonical language token |
| `REGEX_MATCH_TIMEOUT` | Settings & Engine Configuration | Canonical language token |
| `SCRIPT_HASH_POLICY` | Settings & Engine Configuration | Canonical language token |
| `SET_CUBE_LIMIT` | Settings & Engine Configuration | Canonical language token |
| `SPILL_COMPRESSION` | Settings & Engine Configuration | Canonical language token |
| `SPILL_ENCRYPTION` | Settings & Engine Configuration | Canonical language token |
| `TELEMETRY` | Settings & Engine Configuration | Canonical language token |
| `TEMP_TABLE_SPILL_THRESHOLD` | Settings & Engine Configuration | Canonical language token |
| `VERSION` | Settings & Engine Configuration | Canonical language token |
| `WEEK_START_DAY` | Settings & Engine Configuration | Canonical language token |
| `WHAT_IF` | Settings & Engine Configuration | Canonical language token |
| `WINDOW_SPILL_THRESHOLD` | Settings & Engine Configuration | Canonical language token |

### 19.7 File & Directory Operations Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `COMPRESS` | File & Directory Operations | Canonical language token |
| `COPY` | File & Directory Operations | Canonical language token |
| `DECOMPRESS` | File & Directory Operations | Canonical language token |
| `DELETE` | File & Directory Operations | Canonical language token |
| `FILES` | File & Directory Operations | Canonical language token |
| `FILES` | File & Directory Operations | Canonical language token |
| `MOVE` | File & Directory Operations | Canonical language token |
| `PATH` | File & Directory Operations | Canonical language token |
| `RENAME` | File & Directory Operations | Canonical language token |
| `ROOT` | File & Directory Operations | Canonical language token |

### 19.8 Data Formatting & File Connector Options Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `BACKSLASH_N` | Data Formatting & File Connector Options | Canonical language token |
| `COLON` | Data Formatting & File Connector Options | Canonical language token |
| `COMMA` | Data Formatting & File Connector Options | Canonical language token |
| `CR` | Data Formatting & File Connector Options | Canonical language token |
| `CRLF` | Data Formatting & File Connector Options | Canonical language token |
| `DATE_FORMAT` | Data Formatting & File Connector Options | Canonical language token |
| `DOUBLEQUOTE` | Data Formatting & File Connector Options | Canonical language token |
| `DOUBLEQUOTES` | Data Formatting & File Connector Options | Canonical language token |
| `EMPTY` | Data Formatting & File Connector Options | Canonical language token |
| `ESCAPE_CHAR` | Data Formatting & File Connector Options | Canonical language token |
| `FIELDTERMINATOR` | Data Formatting & File Connector Options | Canonical language token |
| `FIRSTROW` | Data Formatting & File Connector Options | Canonical language token |
| `INCLUDE_NULL_VALUES` | Data Formatting & File Connector Options | Canonical language token |
| `LATIN1` | Data Formatting & File Connector Options | Canonical language token |
| `LF` | Data Formatting & File Connector Options | Canonical language token |
| `NULL_AS` | Data Formatting & File Connector Options | Canonical language token |
| `PIPE` | Data Formatting & File Connector Options | Canonical language token |
| `ROWTERMINATOR` | Data Formatting & File Connector Options | Canonical language token |
| `SEMICOLON` | Data Formatting & File Connector Options | Canonical language token |
| `SINGLEQUOTE` | Data Formatting & File Connector Options | Canonical language token |
| `SINGLEQUOTES` | Data Formatting & File Connector Options | Canonical language token |
| `STRICT_SCHEMA` | Data Formatting & File Connector Options | Canonical language token |
| `TAB` | Data Formatting & File Connector Options | Canonical language token |
| `TILDE` | Data Formatting & File Connector Options | Canonical language token |
| `UNICODE` | Data Formatting & File Connector Options | Canonical language token |
| `UTF16` | Data Formatting & File Connector Options | Canonical language token |
| `WITHOUT_ARRAY_WRAPPER` | Data Formatting & File Connector Options | Canonical language token |

### 19.9 Security & Secrets Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `ALLOW_PLAINTEXT_SECRETS` | Security & Secrets | Canonical language token |
| `CONNECTION_ENCRYPTION` | Security & Secrets | Canonical language token |
| `NO_SAVE_CONNECTION` | Security & Secrets | Canonical language token |
| `NO_SAVE_SENSITIVE` | Security & Secrets | Canonical language token |
| `PASSPHRASE` | Security & Secrets | Canonical language token |
| `PASSWORD` | Security & Secrets | Canonical language token |
| `PGP_KEY` | Security & Secrets | Canonical language token |
| `SHOW_PASSWORD` | Security & Secrets | Canonical language token |
| `SHOW_SECRETS` | Security & Secrets | Canonical language token |

### 19.10 Reporting & Visuals Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `ABBREVIATE` | Reporting & Visuals | Canonical language token |
| `ACTIONS` | Reporting & Visuals | Canonical language token |
| `ARROWS` | Reporting & Visuals | Canonical language token |
| `AXIS_SORT` | Reporting & Visuals | Canonical language token |
| `BACKGROUND` | Reporting & Visuals | Canonical language token |
| `BAR` | Reporting & Visuals | Canonical language token |
| `BOXPLOT` | Reporting & Visuals | Canonical language token |
| `CARD` | Reporting & Visuals | Canonical language token |
| `CENTER` | Reporting & Visuals | Canonical language token |
| `CHECKBOX` | Reporting & Visuals | Canonical language token |
| `CHECKS` | Reporting & Visuals | Canonical language token |
| `CLEAR_FILTERS` | Reporting & Visuals | Canonical language token |
| `CLOSE_PCT` | Reporting & Visuals | Canonical language token |
| `COLOR_CLOSE` | Reporting & Visuals | Canonical language token |
| `COLOR_MET` | Reporting & Visuals | Canonical language token |
| `COLOR_MISSED` | Reporting & Visuals | Canonical language token |
| `COMBO` | Reporting & Visuals | Canonical language token |
| `CONTENT` | Reporting & Visuals | Canonical language token |
| `CSS` | Reporting & Visuals | Canonical language token |
| `DASHBOARD` | Reporting & Visuals | Canonical language token |
| `DATEPICKER` | Reporting & Visuals | Canonical language token |
| `DECIMALS` | Reporting & Visuals | Canonical language token |
| `DELTA_FORMAT` | Reporting & Visuals | Canonical language token |
| `DELTA_LABEL` | Reporting & Visuals | Canonical language token |
| `DONUT` | Reporting & Visuals | Canonical language token |
| `FAVICON` | Reporting & Visuals | Canonical language token |
| `FONT_SIZE` | Reporting & Visuals | Canonical language token |
| `FOOTER` | Reporting & Visuals | Canonical language token |
| `FUNNEL` | Reporting & Visuals | Canonical language token |
| `GAP` | Reporting & Visuals | Canonical language token |
| `GAUGE` | Reporting & Visuals | Canonical language token |
| `GAUGE_STYLE` | Reporting & Visuals | Canonical language token |
| `HBAR` | Reporting & Visuals | Canonical language token |
| `HEADER` | Reporting & Visuals | Canonical language token |
| `HEATMAP` | Reporting & Visuals | Canonical language token |
| `HIGHLIGHT` | Reporting & Visuals | Canonical language token |
| `ICON_CLOSE` | Reporting & Visuals | Canonical language token |
| `ICON_MET` | Reporting & Visuals | Canonical language token |
| `ICON_MISSED` | Reporting & Visuals | Canonical language token |
| `ICON_SET` | Reporting & Visuals | Canonical language token |
| `INSIDE` | Reporting & Visuals | Canonical language token |
| `INSIDE_BOTTOM` | Reporting & Visuals | Canonical language token |
| `INSIDE_BOTTOM_LEFT` | Reporting & Visuals | Canonical language token |
| `INSIDE_BOTTOM_RIGHT` | Reporting & Visuals | Canonical language token |
| `INSIDE_LEFT` | Reporting & Visuals | Canonical language token |
| `INSIDE_RIGHT` | Reporting & Visuals | Canonical language token |
| `INSIDE_TOP` | Reporting & Visuals | Canonical language token |
| `INSIDE_TOP_LEFT` | Reporting & Visuals | Canonical language token |
| `INSIDE_TOP_RIGHT` | Reporting & Visuals | Canonical language token |
| `INTERACTIONS` | Reporting & Visuals | Canonical language token |
| `JS` | Reporting & Visuals | Canonical language token |
| `LABEL_CLOSE` | Reporting & Visuals | Canonical language token |
| `LABEL_MET` | Reporting & Visuals | Canonical language token |
| `LABEL_MISSED` | Reporting & Visuals | Canonical language token |
| `LABEL_POSITION` | Reporting & Visuals | Canonical language token |
| `LAYER` | Reporting & Visuals | Canonical language token |
| `LINE` | Reporting & Visuals | Canonical language token |
| `LOGO` | Reporting & Visuals | Canonical language token |
| `MAP` | Reporting & Visuals | Canonical language token |
| `MAPPINGS` | Reporting & Visuals | Canonical language token |
| `MATCHING` | Reporting & Visuals | Canonical language token |
| `MET_PCT` | Reporting & Visuals | Canonical language token |
| `MINMAX` | Reporting & Visuals | Canonical language token |
| `NAVIGATE_PAGE` | Reporting & Visuals | Canonical language token |
| `NUMBERBOX` | Reporting & Visuals | Canonical language token |
| `ON_SELECT` | Reporting & Visuals | Canonical language token |
| `PAGINATED` | Reporting & Visuals | Canonical language token |
| `PIE` | Reporting & Visuals | Canonical language token |
| `PINNABLE` | Reporting & Visuals | Canonical language token |
| `PLACEHOLDER` | Reporting & Visuals | Canonical language token |
| `POSITIVE_DOWN` | Reporting & Visuals | Canonical language token |
| `POSITIVE_UP` | Reporting & Visuals | Canonical language token |
| `PREFIX` | Reporting & Visuals | Canonical language token |
| `PROGRESS_STYLE` | Reporting & Visuals | Canonical language token |
| `RELDATEPICKER` | Reporting & Visuals | Canonical language token |
| `RING` | Reporting & Visuals | Canonical language token |
| `SCATTER` | Reporting & Visuals | Canonical language token |
| `SEARCH` | Reporting & Visuals | Canonical language token |
| `SHOW_GOAL` | Reporting & Visuals | Canonical language token |
| `SHOW_NO_DATA_PLACEHOLDER` | Reporting & Visuals | Canonical language token |
| `SHOW_PERCENT_OF_GOAL` | Reporting & Visuals | Canonical language token |
| `SHOW_PROGRESS` | Reporting & Visuals | Canonical language token |
| `SLICER` | Reporting & Visuals | Canonical language token |
| `SLIDER` | Reporting & Visuals | Canonical language token |
| `STRUCTURE` | Reporting & Visuals | Canonical language token |
| `SUBTITLE` | Reporting & Visuals | Canonical language token |
| `SUFFIX` | Reporting & Visuals | Canonical language token |
| `TABLE` | Reporting & Visuals | Canonical language token |
| `TEMPLATE_PATH` | Reporting & Visuals | Canonical language token |
| `TEXT` | Reporting & Visuals | Canonical language token |
| `TEXTBOX` | Reporting & Visuals | Canonical language token |
| `TITLE` | Reporting & Visuals | Canonical language token |
| `TOOLTIP` | Reporting & Visuals | Canonical language token |
| `TRAFFIC` | Reporting & Visuals | Canonical language token |
| `TREEMAP` | Reporting & Visuals | Canonical language token |
| `TREND_DIR` | Reporting & Visuals | Canonical language token |
| `VALUE_DESC` | Reporting & Visuals | Canonical language token |
| `VISIBLE` | Reporting & Visuals | Canonical language token |
| `WATERFALL` | Reporting & Visuals | Canonical language token |

### 19.11 Date & Time Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `CURRENT_DATE` | Date & Time | Canonical language token |
| `CURRENT_TIME` | Date & Time | Canonical language token |
| `CURRENT_TIMESTAMP` | Date & Time | Canonical language token |
| `DAY` | Date & Time | Canonical language token |
| `HOUR` | Date & Time | Canonical language token |
| `MINUTE` | Date & Time | Canonical language token |
| `MONTH` | Date & Time | Canonical language token |
| `RELDATE` | Date & Time | Canonical language token |
| `SECOND` | Date & Time | Canonical language token |
| `SYSDATE` | Date & Time | Canonical language token |
| `YEAR` | Date & Time | Canonical language token |

### 19.12 Email Operations Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `ATTACH` | Email Operations | Canonical language token |
| `BCC` | Email Operations | Canonical language token |
| `BODY` | Email Operations | Canonical language token |
| `CC` | Email Operations | Canonical language token |
| `DELIVER` | Email Operations | Canonical language token |
| `EMAIL` | Email Operations | Canonical language token |
| `RECEIVE` | Email Operations | Canonical language token |
| `RECIPIENT` | Email Operations | Canonical language token |
| `SEND` | Email Operations | Canonical language token |
| `SMTP` | Email Operations | Canonical language token |
| `SUBJECT` | Email Operations | Canonical language token |

### 19.13 Script & Job Execution Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `ACTIVE` | Script & Job Execution | Canonical language token |
| `CRON` | Script & Job Execution | Canonical language token |
| `DELAY` | Script & Job Execution | Canonical language token |
| `EVERY` | Script & Job Execution | Canonical language token |
| `JOB` | Script & Job Execution | Canonical language token |
| `JOB` | Script & Job Execution | Canonical language token |
| `JOBS` | Script & Job Execution | Canonical language token |
| `KILL` | Script & Job Execution | Canonical language token |
| `ON_LOAD` | Script & Job Execution | Canonical language token |
| `ON_RUN` | Script & Job Execution | Canonical language token |
| `PAUSE` | Script & Job Execution | Canonical language token |
| `RUN` | Script & Job Execution | Canonical language token |
| `RUN_SCRIPT` | Script & Job Execution | Canonical language token |
| `SCHEDULE` | Script & Job Execution | Canonical language token |
| `SCRIPT` | Script & Job Execution | Canonical language token |
| `START` | Script & Job Execution | Canonical language token |
| `STEP` | Script & Job Execution | Canonical language token |
| `STEP` | Script & Job Execution | Canonical language token |
| `STOP` | Script & Job Execution | Canonical language token |
| `TRIGGER` | Script & Job Execution | Canonical language token |
| `UNTIL` | Script & Job Execution | Canonical language token |
| `USE` | Script & Job Execution | Canonical language token |
| `WAIT` | Script & Job Execution | Canonical language token |
| `WAITFOR` | Script & Job Execution | Canonical language token |

### 19.14 Portal Administration Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `ALERT` | Portal Administration | Canonical language token |
| `ALERT` | Portal Administration | Canonical language token |
| `ALERTS` | Portal Administration | Canonical language token |
| `BUNDLE` | Portal Administration | Canonical language token |
| `BUNDLES` | Portal Administration | Canonical language token |
| `CATALOG` | Portal Administration | Canonical language token |
| `DEPENDENCIES` | Portal Administration | Canonical language token |
| `EFFECTIVE` | Portal Administration | Canonical language token |
| `EMBED` | Portal Administration | Canonical language token |
| `EXPIRES` | Portal Administration | Canonical language token |
| `EXPORT` | Portal Administration | Canonical language token |
| `FAVORITE` | Portal Administration | Canonical language token |
| `HISTORY` | Portal Administration | Canonical language token |
| `LINK` | Portal Administration | Canonical language token |
| `LINKS` | Portal Administration | Canonical language token |
| `METRICS` | Portal Administration | Canonical language token |
| `PERMISSIONS` | Portal Administration | Canonical language token |
| `PORTAL` | Portal Administration | Canonical language token |
| `PUBLISH` | Portal Administration | Canonical language token |
| `PUBLISHED` | Portal Administration | Canonical language token |
| `RECENT` | Portal Administration | Canonical language token |
| `REPORT` | Portal Administration | Canonical language token |
| `REPORTS` | Portal Administration | Canonical language token |
| `SAVED` | Portal Administration | Canonical language token |
| `SHARE` | Portal Administration | Canonical language token |
| `SHOW` | Portal Administration | Canonical language token |
| `SUBSCRIPTION` | Portal Administration | Canonical language token |
| `TOKEN` | Portal Administration | Canonical language token |
| `TOKENS` | Portal Administration | Canonical language token |
| `UNFAVORITE` | Portal Administration | Canonical language token |
| `USAGE` | Portal Administration | Canonical language token |
| `VALIDATE` | Portal Administration | Canonical language token |
| `VERSIONS` | Portal Administration | Canonical language token |
| `VIEW` | Portal Administration | Canonical language token |
| `VIEWS` | Portal Administration | Canonical language token |

### 19.15 XML, JSON & Query Modifiers Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `ANTI` | XML, JSON & Query Modifiers | Canonical language token |
| `AT` | XML, JSON & Query Modifiers | Canonical language token |
| `AUTO` | XML, JSON & Query Modifiers | Canonical language token |
| `CURRENT` | XML, JSON & Query Modifiers | Canonical language token |
| `DEFAULT` | XML, JSON & Query Modifiers | Canonical language token |
| `ELEMENTS` | XML, JSON & Query Modifiers | Canonical language token |
| `EXCLUDE` | XML, JSON & Query Modifiers | Canonical language token |
| `EXPLAIN` | XML, JSON & Query Modifiers | Canonical language token |
| `EXPLICIT` | XML, JSON & Query Modifiers | Canonical language token |
| `FETCH` | XML, JSON & Query Modifiers | Canonical language token |
| `FOLLOWING` | XML, JSON & Query Modifiers | Canonical language token |
| `GENERATE` | XML, JSON & Query Modifiers | Canonical language token |
| `GROUPS` | XML, JSON & Query Modifiers | Canonical language token |
| `HASH` | XML, JSON & Query Modifiers | Canonical language token |
| `IDENTITY` | XML, JSON & Query Modifiers | Canonical language token |
| `LOOP` | XML, JSON & Query Modifiers | Canonical language token |
| `NO` | XML, JSON & Query Modifiers | Canonical language token |
| `OTHERS` | XML, JSON & Query Modifiers | Canonical language token |
| `OVER` | XML, JSON & Query Modifiers | Canonical language token |
| `PARTITION` | XML, JSON & Query Modifiers | Canonical language token |
| `PERCENT` | XML, JSON & Query Modifiers | Canonical language token |
| `PRECEDING` | XML, JSON & Query Modifiers | Canonical language token |
| `RANGE` | XML, JSON & Query Modifiers | Canonical language token |
| `RAW` | XML, JSON & Query Modifiers | Canonical language token |
| `RECURSIVE` | XML, JSON & Query Modifiers | Canonical language token |
| `ROWS` | XML, JSON & Query Modifiers | Canonical language token |
| `SEMI` | XML, JSON & Query Modifiers | Canonical language token |
| `TIES` | XML, JSON & Query Modifiers | Canonical language token |
| `TIME` | XML, JSON & Query Modifiers | Canonical language token |
| `UNBOUNDED` | XML, JSON & Query Modifiers | Canonical language token |
| `WITH` | XML, JSON & Query Modifiers | Canonical language token |
| `WITHIN` | XML, JSON & Query Modifiers | Canonical language token |
| `ZONE` | XML, JSON & Query Modifiers | Canonical language token |

### 19.16 General Keywords

| Token | Family | Notes |
| :--- | :--- | :--- |
| `ALGORITHM` | General | Canonical language token |
| `ALL_VALUE` | General | Canonical language token |
| `ALPHA` | General | Canonical language token |
| `ANALYZE` | General | Canonical language token |
| `BACK` | General | Canonical language token |
| `BATCHSIZE` | General | Canonical language token |
| `BITS` | General | Canonical language token |
| `BLANK` | General | Canonical language token |
| `BOTH` | General | Canonical language token |
| `BOX_STYLE` | General | Canonical language token |
| `BULK` | General | Canonical language token |
| `BUTTON` | General | Canonical language token |
| `CASCADE` | General | Canonical language token |
| `CASTABLE` | General | Canonical language token |
| `CELL_BORDER` | General | Canonical language token |
| `CELL_BORDER_COLOR` | General | Canonical language token |
| `CELL_BORDER_WIDTH` | General | Canonical language token |
| `CHAR_LENGTH` | General | Canonical language token |
| `CHARACTER_LENGTH` | General | Canonical language token |
| `CIRCLE` | General | Canonical language token |
| `CIRCULAR` | General | Canonical language token |
| `CLIP` | General | Canonical language token |
| `CLOSE` | General | Canonical language token |
| `COLOR_DECREASE` | General | Canonical language token |
| `COLOR_DOWN` | General | Canonical language token |
| `COLOR_HIGH` | General | Canonical language token |
| `COLOR_INCREASE` | General | Canonical language token |
| `COLOR_LOW` | General | Canonical language token |
| `COLOR_MID` | General | Canonical language token |
| `COLOR_SUBTOTAL` | General | Canonical language token |
| `COLOR_TOTAL` | General | Canonical language token |
| `COLOR_UP` | General | Canonical language token |
| `COLUMN_TOTAL` | General | Canonical language token |
| `COLUMNS` | General | Canonical language token |
| `COMMENT` | General | Canonical language token |
| `CONNECTION_PREVIEW_LIMIT` | General | Canonical language token |
| `CONNECTIONS` | General | Canonical language token |
| `CONNECTOR_LINE_COLOR` | General | Canonical language token |
| `CONNECTOR_LINE_WIDTH` | General | Canonical language token |
| `CONNECTOR_LINES` | General | Canonical language token |
| `CONVERT` | General | Canonical language token |
| `DATA_BARS` | General | Canonical language token |
| `DATA_QUALITY_DRY_RUN` | General | Canonical language token |
| `DATA_SOURCE` | General | Canonical language token |
| `DEFAULT_EXPAND` | General | Canonical language token |
| `DELETE_EXTRA` | General | Canonical language token |
| `DEPENDS_ON` | General | Canonical language token |
| `DIRECTED` | General | Canonical language token |
| `DISABLE` | General | Canonical language token |
| `DISCONNECT` | General | Canonical language token |
| `ENABLE` | General | Canonical language token |
| `ENCODING` | General | Canonical language token |
| `ENG` | General | Canonical language token |
| `EXCLUDE_FROM_PRINT` | General | Canonical language token |
| `EXPECT` | General | Canonical language token |
| `EXPECTED_HASH` | General | Canonical language token |
| `EXPLODE` | General | Canonical language token |
| `EXPLODE_ALL` | General | Canonical language token |
| `EXPLODE_DISTANCE` | General | Canonical language token |
| `EXPORT_CSV` | General | Canonical language token |
| `EXPORT_EXCEL` | General | Canonical language token |
| `EXPORT_PDF` | General | Canonical language token |
| `EXPR` | General | Canonical language token |
| `EXTRACT` | General | Canonical language token |
| `FAILURE` | General | Canonical language token |
| `FALSE` | General | Canonical language token |
| `FILL_OPACITY` | General | Canonical language token |
| `FILTER` | General | Canonical language token |
| `FORCE` | General | Canonical language token |
| `FORMAT` | General | Canonical language token |
| `FRESHNESS` | General | Canonical language token |
| `FROM_ENCODING` | General | Canonical language token |
| `FUNNEL_SHAPE` | General | Canonical language token |
| `HANDLING` | General | Canonical language token |
| `HASH_FILE` | General | Canonical language token |
| `HELP` | General | Canonical language token |
| `HISTORICAL` | General | Canonical language token |
| `ICON` | General | Canonical language token |
| `IGNORE_EXTRA_COLUMNS` | General | Canonical language token |
| `IN` | General | Canonical language token |
| `INDEPENDENT_AXES` | General | Canonical language token |
| `INPUT` | General | Canonical language token |
| `INTEGRITY` | General | Canonical language token |
| `INVALID` | General | Canonical language token |
| `JITTER` | General | Canonical language token |
| `JITTER_HEIGHT` | General | Canonical language token |
| `JITTER_WIDTH` | General | Canonical language token |
| `JUSTIFY` | General | Canonical language token |
| `KEEP_TOGETHER` | General | Canonical language token |
| `LABEL_MIN_SIZE` | General | Canonical language token |
| `LABEL_OVERFLOW` | General | Canonical language token |
| `LEADING` | General | Canonical language token |
| `LEVEL_1` | General | Canonical language token |
| `LEVEL_2` | General | Canonical language token |
| `LIMIT_TYPE` | General | Canonical language token |
| `LIMIT_VALUE` | General | Canonical language token |
| `LINEAGE` | General | Canonical language token |
| `LINEAGE_IMPORT_CATALOG` | General | Canonical language token |
| `LINEAGE_NAMESPACE` | General | Canonical language token |
| `LINEAGE_TAGS` | General | Canonical language token |
| `LINK_OPACITY` | General | Canonical language token |
| `LOAD` | General | Canonical language token |
| `LOCAL` | General | Canonical language token |
| `LOG` | General | Canonical language token |
| `LOGARITHMIC` | General | Canonical language token |
| `MAP_BY_HEADER_NAME` | General | Canonical language token |
| `MARGIN` | General | Canonical language token |
| `MARGINS` | General | Canonical language token |
| `MATCHES` | General | Canonical language token |
| `MATRIX` | General | Canonical language token |
| `MAX` | General | Canonical language token |
| `MAX_BUBBLE_SIZE` | General | Canonical language token |
| `MAXERRORS` | General | Canonical language token |
| `MEAN_COLOR` | General | Canonical language token |
| `MIDPOINT` | General | Canonical language token |
| `MILESTONE` | General | Canonical language token |
| `MIN` | General | Canonical language token |
| `MIN_BUBBLE_SIZE` | General | Canonical language token |
| `MIN_SLICE_PCT` | General | Canonical language token |
| `MODE` | General | Canonical language token |
| `MULTISELECT` | General | Canonical language token |
| `NODE_ALIGN` | General | Canonical language token |
| `NODE_COLOR` | General | Canonical language token |
| `NODE_GROUP` | General | Canonical language token |
| `NODE_LABEL_MIN_SIZE` | General | Canonical language token |
| `NODE_LABELS` | General | Canonical language token |
| `NODE_PADDING` | General | Canonical language token |
| `NODE_SIZE` | General | Canonical language token |
| `NODE_X` | General | Canonical language token |
| `NODE_Y` | General | Canonical language token |
| `NONE` | General | Canonical language token |
| `NOTCHED` | General | Canonical language token |
| `NOTIFY` | General | Canonical language token |
| `NULL_COLOR` | General | Canonical language token |
| `NULL_MISSING_COLUMNS` | General | Canonical language token |
| `NULL_PERCENT` | General | Canonical language token |
| `OCTET_LENGTH` | General | Canonical language token |
| `OFF` | General | Canonical language token |
| `ON` | General | Canonical language token |
| `OPERATOR_MEMORY_GRANT` | General | Canonical language token |
| `ORIENTATION` | General | Canonical language token |
| `OTHER_LABEL` | General | Canonical language token |
| `OUTPUT` | General | Canonical language token |
| `OVERFLOW` | General | Canonical language token |
| `OVERLAY` | General | Canonical language token |
| `PAGE_BREAK_AFTER` | General | Canonical language token |
| `PAGE_BREAK_BEFORE` | General | Canonical language token |
| `PAGE_LAYOUT` | General | Canonical language token |
| `PAGE_SIZE` | General | Canonical language token |
| `PARALLEL` | General | Canonical language token |
| `PARENTS` | General | Canonical language token |
| `PERCENT_MODE` | General | Canonical language token |
| `PERSIST` | General | Canonical language token |
| `PLACING` | General | Canonical language token |
| `POLL_INTERVAL_MS` | General | Canonical language token |
| `POLYGON` | General | Canonical language token |
| `POSITION` | General | Canonical language token |
| `PRINT` | General | Canonical language token |
| `PRINT_LAYOUT` | General | Canonical language token |
| `PROGRESS_BAR` | General | Canonical language token |
| `PYRAMID` | General | Canonical language token |
| `QUARANTINE` | General | Canonical language token |
| `QUARANTINE_PERCENT` | General | Canonical language token |
| `REFRESH` | General | Canonical language token |
| `REFRESH_REPORT` | General | Canonical language token |
| `REFRESH_VISUALS` | General | Canonical language token |
| `REPEATABLE` | General | Canonical language token |
| `REPULSION` | General | Canonical language token |
| `REQUIRE` | General | Canonical language token |
| `REQUIRED` | General | Canonical language token |
| `RESTART` | General | Canonical language token |
| `RETENTION` | General | Canonical language token |
| `ROW_COUNT` | General | Canonical language token |
| `ROW_DETAIL` | General | Canonical language token |
| `ROW_TOTAL` | General | Canonical language token |
| `SAFE` | General | Canonical language token |
| `SAMPLE` | General | Canonical language token |
| `SESSION` | General | Canonical language token |
| `SESSIONS` | General | Canonical language token |
| `SETS` | General | Canonical language token |
| `SHOW_BREADCRUMB` | General | Canonical language token |
| `SHOW_MEAN` | General | Canonical language token |
| `SHOW_PERCENT` | General | Canonical language token |
| `SHOW_VIOLIN` | General | Canonical language token |
| `SHUTDOWN` | General | Canonical language token |
| `SIGMA` | General | Canonical language token |
| `SIZE_RANGE` | General | Canonical language token |
| `SKIP_ERROR` | General | Canonical language token |
| `SLICE_BORDER_COLOR` | General | Canonical language token |
| `SLICE_BORDER_WIDTH` | General | Canonical language token |
| `SPARKLINE` | General | Canonical language token |
| `SPLIT` | General | Canonical language token |
| `START_ANGLE` | General | Canonical language token |
| `STEWARD` | General | Canonical language token |
| `SUBSTRING` | General | Canonical language token |
| `SUBTOTAL` | General | Canonical language token |
| `SYNC` | General | Canonical language token |
| `TABLES` | General | Canonical language token |
| `TAG` | General | Canonical language token |
| `TAGS` | General | Canonical language token |
| `TARGET` | General | Canonical language token |
| `TARGET_SIZE` | General | Canonical language token |
| `TIMEOUT` | General | Canonical language token |
| `TO` | General | Canonical language token |
| `TO_ENCODING` | General | Canonical language token |
| `TODAY_COLOR` | General | Canonical language token |
| `TODAY_DATE` | General | Canonical language token |
| `TODAY_LINE` | General | Canonical language token |
| `TRAILING` | General | Canonical language token |
| `TRIM` | General | Canonical language token |
| `TRUE` | General | Canonical language token |
| `TRUNCATE_STRING` | General | Canonical language token |
| `TYPE` | General | Canonical language token |
| `UNIQUE_FIRST` | General | Canonical language token |
| `UNIQUE_LAST` | General | Canonical language token |
| `UNIT` | General | Canonical language token |
| `UNITS` | General | Canonical language token |
| `UNLOCKED` | General | Canonical language token |
| `VALUE` | General | Canonical language token |
| `VALUE_ASC` | General | Canonical language token |
| `VARIABLES` | General | Canonical language token |
| `VERIFY` | General | Canonical language token |
| `VIOLIN` | General | Canonical language token |
| `VIOLIN_COLOR` | General | Canonical language token |
| `VOLUME` | General | Canonical language token |
| `VOLUME_COLOR` | General | Canonical language token |
| `WARN_PERCENT` | General | Canonical language token |
| `WICK_COLOR` | General | Canonical language token |
| `WICK_COLOR_DOWN` | General | Canonical language token |
| `WICK_COLOR_UP` | General | Canonical language token |
| `WINDOW` | General | Canonical language token |
| `WRAP` | General | Canonical language token |
| `X_SORT` | General | Canonical language token |
| `Y_SORT` | General | Canonical language token |
| `ZONES` | General | Canonical language token |

### 19.17 Connector Types

| Token | Group | Notes |
| :--- | :--- | :--- |
| `ACTIVE_DIRECTORY` | Connector | Canonical connector token |
| `AVRO` | Connector | Canonical connector token |
| `AZURE_BLOB` | Connector | Canonical connector token |
| `CSV` | Connector | Canonical connector token |
| `DIRECTORY` | Connector | Canonical connector token |
| `DOCKER` | Connector | Canonical connector token |
| `EXCEL` | Connector | Canonical connector token |
| `FLATFILE` | Connector | Canonical connector token |
| `FTP` | Connector | Canonical connector token |
| `FTP_CONN` | Connector | Canonical connector token |
| `JSON` | Connector | Canonical connector token |
| `KAFKA` | Connector | Canonical connector token |
| `MOCKDB` | Connector | Canonical connector token |
| `MONGODB` | Connector | Canonical connector token |
| `MSSQL` | Connector | Canonical connector token |
| `ODBC` | Connector | Canonical connector token |
| `ORACLE` | Connector | Canonical connector token |
| `ORCH` | Connector | Canonical connector token |
| `ORCHESTRATOR` | Connector | Canonical connector token |
| `PARQUET` | Connector | Canonical connector token |
| `PORTAL` | Connector | Canonical connector token |
| `POSTGRES` | Connector | Canonical connector token |
| `S3` | Connector | Canonical connector token |
| `SFTP` | Connector | Canonical connector token |
| `SHAREPOINT` | Connector | Canonical connector token |
| `SMTP` | Connector | Canonical connector token |
| `SQLITE` | Connector | Canonical connector token |
| `XML` | Connector | Canonical connector token |

### 19.18 Built-in Functions

| Token | Group | Notes |
| :--- | :--- | :--- |
| `ABS` | Function | Canonical built-in function |
| `ACOS` | Function | Canonical built-in function |
| `AGE_BUCKET` | Function | Canonical built-in function |
| `APPEND_TO_LIST` | Function | Canonical built-in function |
| `ASCII` | Function | Canonical built-in function |
| `ASIN` | Function | Canonical built-in function |
| `ATAN` | Function | Canonical built-in function |
| `ATAN2` | Function | Canonical built-in function |
| `AVG` | Function | Canonical built-in function |
| `BINARY_CHECKSUM` | Function | Canonical built-in function |
| `BIT_COUNT` | Function | Canonical built-in function |
| `BITAND` | Function | Canonical built-in function |
| `BITNOT` | Function | Canonical built-in function |
| `BITOR` | Function | Canonical built-in function |
| `BITSHIFTLEFT` | Function | Canonical built-in function |
| `BITSHIFTRIGHT` | Function | Canonical built-in function |
| `BITXOR` | Function | Canonical built-in function |
| `CAST` | Function | Canonical built-in function |
| `CEILING` | Function | Canonical built-in function |
| `CHAR` | Function | Canonical built-in function |
| `CHARINDEX` | Function | Canonical built-in function |
| `CHECKSUM` | Function | Canonical built-in function |
| `CLEAN_STRING` | Function | Canonical built-in function |
| `COALESCE` | Function | Canonical built-in function |
| `CONCAT` | Function | Canonical built-in function |
| `CONNECTION_PROPERTY` | Function | Canonical built-in function |
| `CORR` | Function | Canonical built-in function |
| `COS` | Function | Canonical built-in function |
| `COT` | Function | Canonical built-in function |
| `COUNT` | Function | Canonical built-in function |
| `COVAR_POP` | Function | Canonical built-in function |
| `COVAR_SAMP` | Function | Canonical built-in function |
| `CUME_DIST` | Function | Canonical built-in function |
| `CURRENT_TENANT` | Function | Canonical built-in function |
| `DATALENGTH` | Function | Canonical built-in function |
| `DATE_PART` | Function | Canonical built-in function |
| `DATE_TRUNC` | Function | Canonical built-in function |
| `DATEDIFF` | Function | Canonical built-in function |
| `DATEPART` | Function | Canonical built-in function |
| `DATETIMEFROMPARTS` | Function | Canonical built-in function |
| `DATETIMEOFFSETSFROMPARTS` | Function | Canonical built-in function |
| `DECODE` | Function | Canonical built-in function |
| `DEGREES` | Function | Canonical built-in function |
| `DENSE_RANK` | Function | Canonical built-in function |
| `DIFFERENCE` | Function | Canonical built-in function |
| `DIRECTORY_EXISTS` | Function | Canonical built-in function |
| `DMETAPHONE` | Function | Canonical built-in function |
| `DMETAPHONE_ALT` | Function | Canonical built-in function |
| `END_OF_MONTH` | Function | Canonical built-in function |
| `END_OF_QUARTER` | Function | Canonical built-in function |
| `END_OF_WEEK` | Function | Canonical built-in function |
| `EXP` | Function | Canonical built-in function |
| `EXTRACTVALUE` | Function | Canonical built-in function |
| `FILE_EXISTS` | Function | Canonical built-in function |
| `FILE_HASH` | Function | Canonical built-in function |
| `FILE_LIST` | Function | Canonical built-in function |
| `FILE_MODIFIED` | Function | Canonical built-in function |
| `FILE_SIZE` | Function | Canonical built-in function |
| `FIRST_VALUE` | Function | Canonical built-in function |
| `FLOOR` | Function | Canonical built-in function |
| `FORMAT` | Function | Canonical built-in function |
| `GET_TAG_VALUE` | Function | Canonical built-in function |
| `GET_TAGS` | Function | Canonical built-in function |
| `GETDATE` | Function | Canonical built-in function |
| `GREATEST` | Function | Canonical built-in function |
| `HAS_TAG` | Function | Canonical built-in function |
| `HASHBYTES` | Function | Canonical built-in function |
| `INITCAP` | Function | Canonical built-in function |
| `INSTR` | Function | Canonical built-in function |
| `IS_SANDBOX` | Function | Canonical built-in function |
| `ISJSON` | Function | Canonical built-in function |
| `ISNULL` | Function | Canonical built-in function |
| `JSON_ARRAY` | Function | Canonical built-in function |
| `JSON_EXISTS` | Function | Canonical built-in function |
| `JSON_EXTRACT` | Function | Canonical built-in function |
| `JSON_MODIFY` | Function | Canonical built-in function |
| `JSON_OBJECT` | Function | Canonical built-in function |
| `JSON_QUERY` | Function | Canonical built-in function |
| `JSON_TABLE` | Function | Canonical built-in function |
| `JSON_VALUE` | Function | Canonical built-in function |
| `LAG` | Function | Canonical built-in function |
| `LAST_VALUE` | Function | Canonical built-in function |
| `LEAD` | Function | Canonical built-in function |
| `LEAST` | Function | Canonical built-in function |
| `LEN` | Function | Canonical built-in function |
| `LENGTH` | Function | Canonical built-in function |
| `LEVENSHTEIN` | Function | Canonical built-in function |
| `LN` | Function | Canonical built-in function |
| `LOG` | Function | Canonical built-in function |
| `LOWER` | Function | Canonical built-in function |
| `LPAD` | Function | Canonical built-in function |
| `LTRIM` | Function | Canonical built-in function |
| `MASK_EMAIL` | Function | Canonical built-in function |
| `MASK_PHONE` | Function | Canonical built-in function |
| `MASK_SSN` | Function | Canonical built-in function |
| `MAX` | Function | Canonical built-in function |
| `METAPHONE` | Function | Canonical built-in function |
| `MIN` | Function | Canonical built-in function |
| `MOD` | Function | Canonical built-in function |
| `NEWID` | Function | Canonical built-in function |
| `NEWSEQUENTIALID` | Function | Canonical built-in function |
| `NGRAM_TOKENS` | Function | Canonical built-in function |
| `NGRAMS` | Function | Canonical built-in function |
| `NORMALIZE` | Function | Canonical built-in function |
| `NTH_VALUE` | Function | Canonical built-in function |
| `NTILE` | Function | Canonical built-in function |
| `NULLIF` | Function | Canonical built-in function |
| `NVL` | Function | Canonical built-in function |
| `NVL2` | Function | Canonical built-in function |
| `OPENJSON` | Function | Canonical built-in function |
| `PATH_COMBINE` | Function | Canonical built-in function |
| `PATH_DIRECTORY` | Function | Canonical built-in function |
| `PATH_EXTENSION` | Function | Canonical built-in function |
| `PATH_FILENAME` | Function | Canonical built-in function |
| `PATINDEX` | Function | Canonical built-in function |
| `PERCENT_RANK` | Function | Canonical built-in function |
| `PERCENTILE_CONT` | Function | Canonical built-in function |
| `PERCENTILE_DISC` | Function | Canonical built-in function |
| `PI` | Function | Canonical built-in function |
| `POSITION` | Function | Canonical built-in function |
| `POWER` | Function | Canonical built-in function |
| `QUOTENAME` | Function | Canonical built-in function |
| `RADIANS` | Function | Canonical built-in function |
| `RANDOM` | Function | Canonical built-in function |
| `RANDOM_DECIMAL` | Function | Canonical built-in function |
| `RANDOM_INT` | Function | Canonical built-in function |
| `RANK` | Function | Canonical built-in function |
| `REGEXP_COUNT` | Function | Canonical built-in function |
| `REGEXP_INSTR` | Function | Canonical built-in function |
| `REGEXP_LIKE` | Function | Canonical built-in function |
| `REGEXP_MATCHES` | Function | Canonical built-in function |
| `REGEXP_REPLACE` | Function | Canonical built-in function |
| `REGEXP_SPLIT_TO_TABLE` | Function | Canonical built-in function |
| `REGEXP_SUBSTR` | Function | Canonical built-in function |
| `RELDATE` | Function | Canonical built-in function |
| `REMOVE_FROM_LIST` | Function | Canonical built-in function |
| `REMOVE_HIDDEN_CHARACTERS` | Function | Canonical built-in function |
| `REMOVE_HTML_CHARACTERS` | Function | Canonical built-in function |
| `REPEAT` | Function | Canonical built-in function |
| `REPLACE` | Function | Canonical built-in function |
| `REPLICATE` | Function | Canonical built-in function |
| `ROUND` | Function | Canonical built-in function |
| `ROW_NUMBER` | Function | Canonical built-in function |
| `RPAD` | Function | Canonical built-in function |
| `RTRIM` | Function | Canonical built-in function |
| `SAFE_DIVIDE` | Function | Canonical built-in function |
| `SAME_PERIOD_LAST_YEAR` | Function | Canonical built-in function |
| `SEQUENCE` | Function | Canonical built-in function |
| `SIGN` | Function | Canonical built-in function |
| `SIMILARITY` | Function | Canonical built-in function |
| `SIN` | Function | Canonical built-in function |
| `SORT_LIST` | Function | Canonical built-in function |
| `SOUNDEX` | Function | Canonical built-in function |
| `SQRT` | Function | Canonical built-in function |
| `START_OF_MONTH` | Function | Canonical built-in function |
| `START_OF_QUARTER` | Function | Canonical built-in function |
| `START_OF_WEEK` | Function | Canonical built-in function |
| `STDDEV` | Function | Canonical built-in function |
| `STDDEV_POP` | Function | Canonical built-in function |
| `STDDEV_SAMP` | Function | Canonical built-in function |
| `STDEV` | Function | Canonical built-in function |
| `STDEVP` | Function | Canonical built-in function |
| `STR` | Function | Canonical built-in function |
| `STRING_AGG` | Function | Canonical built-in function |
| `STRING_ESCAPE` | Function | Canonical built-in function |
| `STRING_SPLIT` | Function | Canonical built-in function |
| `STRPOS` | Function | Canonical built-in function |
| `STUFF` | Function | Canonical built-in function |
| `SUBSTR` | Function | Canonical built-in function |
| `SUBSTRING` | Function | Canonical built-in function |
| `SUM` | Function | Canonical built-in function |
| `SYSDATE` | Function | Canonical built-in function |
| `TAN` | Function | Canonical built-in function |
| `TENANT_ID` | Function | Canonical built-in function |
| `TIMEFROMPARTS` | Function | Canonical built-in function |
| `TO_STR` | Function | Canonical built-in function |
| `TO_TIMESTAMP` | Function | Canonical built-in function |
| `TRANSLATE` | Function | Canonical built-in function |
| `TRIM` | Function | Canonical built-in function |
| `TRUNC` | Function | Canonical built-in function |
| `TRY_CAST` | Function | Canonical built-in function |
| `UNICODE` | Function | Canonical built-in function |
| `UPPER` | Function | Canonical built-in function |
| `VALUE_BUCKET` | Function | Canonical built-in function |
| `VAR` | Function | Canonical built-in function |
| `VAR_POP` | Function | Canonical built-in function |
| `VAR_SAMP` | Function | Canonical built-in function |
| `VARP` | Function | Canonical built-in function |
| `XMLATTRIBUTES` | Function | Canonical built-in function |
| `XMLELEMENT` | Function | Canonical built-in function |
| `XMLEXISTS` | Function | Canonical built-in function |
| `XMLFOREST` | Function | Canonical built-in function |
| `XMLQUERY` | Function | Canonical built-in function |
| `XMLTABLE` | Function | Canonical built-in function |
| `XMLVALUE` | Function | Canonical built-in function |

### 19.19 Data Types

| Token | Group | Notes |
| :--- | :--- | :--- |
| `ANY` | Type | Canonical data type token |
| `BIGINT` | Type | Canonical data type token |
| `BINARY` | Type | Canonical data type token |
| `BIT` | Type | Canonical data type token |
| `BLOB` | Type | Canonical data type token |
| `BOOL` | Type | Canonical data type token |
| `BOOLEAN` | Type | Canonical data type token |
| `CHAR` | Type | Canonical data type token |
| `CURSOR` | Type | Canonical data type token |
| `DATE` | Type | Canonical data type token |
| `DATETIME` | Type | Canonical data type token |
| `DATETIME2` | Type | Canonical data type token |
| `DATETIMEOFFSET` | Type | Canonical data type token |
| `DECIMAL` | Type | Canonical data type token |
| `DOUBLE` | Type | Canonical data type token |
| `FLOAT` | Type | Canonical data type token |
| `GEOGRAPHY` | Type | Canonical data type token |
| `GEOMETRY` | Type | Canonical data type token |
| `GUID` | Type | Canonical data type token |
| `HIERARCHYID` | Type | Canonical data type token |
| `IMAGE` | Type | Canonical data type token |
| `INT` | Type | Canonical data type token |
| `INTEGER` | Type | Canonical data type token |
| `JSON` | Type | Canonical data type token |
| `LOB` | Type | Canonical data type token |
| `MARKDOWN` | Type | Canonical data type token |
| `MINMAX` | Type | Canonical data type token |
| `MONEY` | Type | Canonical data type token |
| `NCHAR` | Type | Canonical data type token |
| `NTEXT` | Type | Canonical data type token |
| `NUMBER` | Type | Canonical data type token |
| `NUMERIC` | Type | Canonical data type token |
| `NVARCHAR` | Type | Canonical data type token |
| `REAL` | Type | Canonical data type token |
| `SECRET` | Type | Canonical data type token |
| `SENSITIVE` | Type | Canonical data type token |
| `SMALLDATETIME` | Type | Canonical data type token |
| `SMALLINT` | Type | Canonical data type token |
| `SMALLMONEY` | Type | Canonical data type token |
| `SQL_VARIANT` | Type | Canonical data type token |
| `STRING` | Type | Canonical data type token |
| `TABLE` | Type | Canonical data type token |
| `TEXT` | Type | Canonical data type token |
| `TIME` | Type | Canonical data type token |
| `TIMESTAMP` | Type | Canonical data type token |
| `TINYINT` | Type | Canonical data type token |
| `UNIQUEIDENTIFIER` | Type | Canonical data type token |
| `UUID` | Type | Canonical data type token |
| `VARBINARY` | Type | Canonical data type token |
| `VARCHAR` | Type | Canonical data type token |
| `VARCHAR2` | Type | Canonical data type token |
| `VARIANT` | Type | Canonical data type token |
| `VECTOR` | Type | Canonical data type token |
| `XML` | Type | Canonical data type token |

### 19.20 Standard Tags

| Token | Group | Notes |
| :--- | :--- | :--- |
| `@category` | Tag | Standard governance tag |
| `@certification` | Tag | Standard governance tag |
| `@classification` | Tag | Standard governance tag |
| `@contact` | Tag | Standard governance tag |
| `@d` | Tag | Standard governance tag |
| `@domain` | Tag | Standard governance tag |
| `@encrypted_at_rest` | Tag | Standard governance tag |
| `@example` | Tag | Standard governance tag |
| `@expect` | Tag | Standard governance tag |
| `@fail` | Tag | Standard governance tag |
| `@format` | Tag | Standard governance tag |
| `@freshness` | Tag | Standard governance tag |
| `@load_pattern` | Tag | Standard governance tag |
| `@nullable` | Tag | Standard governance tag |
| `@owner` | Tag | Standard governance tag |
| `@pci` | Tag | Standard governance tag |
| `@phi` | Tag | Standard governance tag |
| `@pii` | Tag | Standard governance tag |
| `@quality` | Tag | Standard governance tag |
| `@sensitive` | Tag | Standard governance tag |
| `@sensitivity` | Tag | Standard governance tag |
| `@sla` | Tag | Standard governance tag |
| `@source_column` | Tag | Standard governance tag |
| `@source_system` | Tag | Standard governance tag |
| `@source_table` | Tag | Standard governance tag |
| `@steward` | Tag | Standard governance tag |
| `@tags` | Tag | Standard governance tag |
| `@trusted` | Tag | Standard governance tag |
| `@unit` | Tag | Standard governance tag |
<!-- END GENERATED CANONICAL TOKEN INDEX -->
