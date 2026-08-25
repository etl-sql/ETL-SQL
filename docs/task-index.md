# Task Index

Goal-oriented "how do I…" locator for ETL-SQL. Find the task you want to accomplish and follow the
link to the page that shows how. For language keywords, functions, and options, use the
[Syntax Index](syntax-index.md) instead.

> [!NOTE]
> This is a locator, not the source of truth. The linked reference pages, guides, and cookbook
> recipes own the actual detail; this index only points to them.

## Ingest and move data

| I want to… | Go to |
| :--- | :--- |
| Choose between staged `#temp` ingestion and direct streaming | [Staged vs. Streaming Ingestion](guides/pipelines/staged-vs-streaming-ingestion.md) |
| Connect to a database, file, or service | [Connectors](reference/connectors/README.md) |
| Load a CSV, Excel, or Parquet file into a table | [Staged Ingestion](cookbooks/etl/staged-ingestion.md) |
| Ingest every file in a directory automatically | [Batch Directory Ingester](cookbooks/etl/batch-directory-ingester.md) |
| Pull data from a REST API | [REST API Ingestion](cookbooks/etl/rest-api-ingestion.md) |
| Copy or reconcile data across two databases | [Cross-Platform Reconciliation](cookbooks/etl/cross-platform-reconciliation.md) |
| Ingest and query a graph database (Neo4j) | [Graph Ingestion & Querying](cookbooks/etl/graph-ingestion-neo4j.md) |

## Transform data

| I want to… | Go to |
| :--- | :--- |
| Break pipelines into reusable sub-scripts with parameters | [Modular Scripts & Parameters](guides/pipelines/modular-scripts-and-parameters.md) |
| Stage, clean, and merge source data (classic ETL) | [Staged Ingestion](cookbooks/etl/staged-ingestion.md) |
| Track history with SCD Type 2 | [SCD Type 2](cookbooks/etl/scd-type-2.md) |
| Load only new/changed rows (incremental / high-water mark) | [Incremental Load](cookbooks/etl/incremental-load-with-high-water-mark.md) |
| Truncate and fully reload a table | [Full Refresh](cookbooks/etl/full-refresh.md) |
| Pivot rows into columns for reporting | [Financial Reporting (PIVOT)](cookbooks/etl/financial-reporting-pivot.md) |
| De-duplicate records by fuzzy match | [Fuzzy Entity Resolution](cookbooks/etl/fuzzy-entity-resolution.md) |
| Build and run dynamic SQL | [Dynamic SQL with EXEC](cookbooks/etl/dynamic-sql-with-exec.md) |

## Validate, quarantine and handle errors

| I want to… | Go to |
| :--- | :--- |
| Enforce column rules with `@expect` and `@fail` | [Column Quality Rules](guides/data-quality/column-quality-rules.md) |
| Quarantine bad rows and triage failure metadata | [Quarantine & Remediation](guides/data-quality/quarantine-and-remediation.md) |
| Deduplicate with `UNIQUE_FIRST BY` or cross-table `EXISTS IN` | [Multi-Row & Cross-Table Rules](guides/data-quality/multi-row-and-cross-table-rules.md) |
| Assert batch volume and freshness against historical baselines | [Run-Level Assertions (`ASSERT JOB`)](guides/data-quality/run-level-assertions.md) |
| Automate quality gates in CI/CD (GitHub Actions, Cron) | [Automating Quality Gates](guides/data-quality/automating-quality-gates.md) |
| Audit lineage tags, metadata gaps, and schema impact | [Data Stewardship & Impact Analysis](guides/data-quality/data-stewardship-and-impact.md) |
| Handle script errors with `TRY...CATCH` and configure retries | [Error Handling, Alerting & Retries](guides/pipelines/error-handling-and-retries.md) |
| Route bad rows to a dead-letter destination | [Dead-Letter Queue](cookbooks/etl/dead-letter-queue.md) |

## Secure data and secrets

| I want to… | Go to |
| :--- | :--- |
| Safely dry-run destructive operations with `SET WHAT_IF ON` | [Script Resilience & Checkpoints](guides/pipelines/script-resilience-and-checkpoints.md) |
| Encrypt a connection string for a script | [etl-sql encrypt](reference/cli/encrypt.md) |
| Manage secrets and secret references (`SECRET:name`) | [Security and Secret Management](administration/platform/secrets.md) |
| Mask or hash PII during load | [Secure PII Masking & Hashing](cookbooks/etl/pii-masking-and-hashing.md) |
| Filter datasets per user with Row-Level Security (RLS) | [Row-Level Security (RLS)](guides/reporting/report-row-level-security.md) |
| Send files securely to a vendor over SFTP | [Secure Vendor Handshake](cookbooks/etl/secure-vendor-handshake.md) |

## Schedule, parallelize and orchestrate

| I want to… | Go to |
| :--- | :--- |
| Run independent tasks concurrently with `PARALLEL(n)` | [Parallel Execution](guides/pipelines/parallel-execution.md) |
| Coordinate complex multi-stage DAG workflows and signals | [DAG Dependencies & Signals](guides/pipelines/dag-dependencies-and-signals.md) |
| Checkpoint and resume long-running jobs (`--session`/`--resume`) | [Script Resilience & Checkpoints](guides/pipelines/script-resilience-and-checkpoints.md) |
| Schedule a recurring job (`CREATE JOB`) | [Job Scheduling](administration/orchestration/job-scheduling.md) |
| Schedule a job on a remote Orchestrator | [Scheduling a Recurring Job on a Remote Orchestrator](cookbooks/etl/remote-orchestrator-schedule.md) |
| Deploy an immutable published script bundle (CI/CD) | [Immutable Published Script Bundles](cookbooks/etl/immutable-published-bundles.md) |

## Author reports & dashboards

| I want to… | Go to |
| :--- | :--- |
| Build interactive dashboards with 3-tier architecture | [Authoring Dashboards](guides/reporting/authoring-dashboards.md) |
| Wire `INPUT` variables, `RELDATE`, slicers, and sliders | [Report Parameters & Filters](guides/reporting/report-parameters-and-filters.md) |
| Build hierarchical parent-child cascading slicers | [Cascading Slicers](guides/reporting/cascading-slicers.md) |
| Create print-ready, multi-page paginated reports | [Paginated & Print-Ready Reports](guides/reporting/paginated-and-print-reports.md) |
| Add in-cell sparklines, progress bars, and KPI cards | [Micro-Charts & KPI Cards](guides/reporting/micro-charts-and-kpis.md) |
| Customize report themes, CSS styling, and custom buttons | [Custom Theming & Branding](guides/reporting/custom-theming-and-branding.md) |
| Configure ownership, certification, and freshness badges | [Report Badges & Trust](guides/reporting/report-badges-and-trust.md) |
| Follow complete report examples | [Report Recipes](cookbooks/report/README.md) |
| Build a master–detail drill-through | [Master-Detail Cross-Report Drill-through](cookbooks/etl/master-detail-drill-through.md) |

## Operate, tune and test

| I want to… | Go to |
| :--- | :--- |
| Configure script logging, rotation, and secret redaction | [Configuring Script Logging](guides/operations/configuring-script-logging.md) |
| Tune buffer batch sizes, phase metrics, and SQL profiling | [Tuning Pipeline Performance](guides/operations/tuning-pipeline-performance.md) |
| Run the solo workstation quality loop | [One-Person Quality Loop](guides/patterns/one-person-quality-loop.md) |
| Write unit tests for pipeline logic with `MOCKDB` and `ASSERT` | [Pipeline Unit Testing & Mocking](guides/pipelines/pipeline-unit-testing.md) |
| Execute test lanes and pre-push validation (contributors) | [Test Lanes & Execution](guides/testing/test-lanes-and-execution.md) |
| Install ETL-SQL in production | [Installation and Deployment](administration/platform/installation.md) |
| Configure Practical High Availability | [Portal State, Data Roots, and High Availability](administration/platform/state-and-ha.md) |
| Back up and restore state | [Backup, Monitoring, and Health](administration/platform/backup-and-monitoring.md) · [etl-sql admin backup](reference/cli/admin-backup.md) · [restore](reference/cli/admin-restore.md) |
| Migrate from SQLite to PostgreSQL | [etl-sql admin migrate-database](reference/cli/admin-migrate-database.md) |
| Run an environment health check | [etl-sql admin doctor](reference/cli/admin-doctor.md) |

## Administer the Portal

| I want to… | Go to |
| :--- | :--- |
| Add, edit, or deactivate a user | [User Management](administration/portal/users.md) |
| Grant folder permissions with groups | [Groups and Folder Permissions](administration/portal/permissions.md) |
| Publish a report and manage a catalog | [Publishing Reports](administration/portal/publishing.md) · [Publishing and Operating a Portal Catalog](cookbooks/etl/portal-catalog.md) |
| Configure and review the audit log | [Health Monitoring and Audit Log](administration/portal/monitoring-and-audit.md) |

## Troubleshooting & FAQs

| I want to… | Go to |
| :--- | :--- |
| Resolve dialect mismatches (`TOP` vs `LIMIT`, function names) | [Troubleshooting: Syntax & Dialects](guides/patterns/troubleshooting-syntax-and-dialect.md) |
| Fix `CREATE CONNECTION` conflicts and decrypt `ENC:` values | [Troubleshooting: Connections & Security](guides/patterns/troubleshooting-connections-and-security.md) |
| Fix `RELDATE` casting, Tier 2 traps, and slicer bindings | [Troubleshooting: Report-SQL](guides/patterns/troubleshooting-reporting.md) |
| Optimize slow cross-source joins and large memory spills | [Troubleshooting: Performance](guides/patterns/troubleshooting-performance.md) |
| Search central frequently asked questions | [ETL-SQL FAQ](guides/patterns/faq.md) |

## Generate from a specification

| I want to… | Go to |
| :--- | :--- |
| Build a vendor feed from a JSON contract | [Specification-Driven Vendor Feed Build](cookbooks/etl/spec-driven-vendor-feed.md) |
| Understand spec-driven development | [Spec-Driven Development](spec-import/spec-driven-development.md) |

## See Also

- [Syntax Index](syntax-index.md) - language keywords, functions, connectors, options, and CLI commands.
- [Documentation Home](README.md) - all sections.
