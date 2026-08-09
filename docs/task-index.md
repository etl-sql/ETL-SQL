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
| Connect to a database, file, or service | [Connectors](reference/connectors/README.md) |
| Load a CSV, Excel, or Parquet file into a table | [Staged Ingestion](cookbooks/etl/staged-ingestion.md) |
| Ingest every file in a directory automatically | [Batch Directory Ingester](cookbooks/etl/batch-directory-ingester.md) |
| Pull data from a REST API | [REST API Ingestion](cookbooks/etl/rest-api-ingestion.md) |
| Copy or reconcile data across two databases | [Cross-Platform Reconciliation](cookbooks/etl/cross-platform-reconciliation.md) |
| Ingest and query a graph database (Neo4j) | [Graph Ingestion & Querying](cookbooks/etl/graph-ingestion-neo4j.md) |

## Transform data

| I want to… | Go to |
| :--- | :--- |
| Stage, clean, and merge source data (classic ETL) | [Staged Ingestion](cookbooks/etl/staged-ingestion.md) |
| Track history with SCD Type 2 | [SCD Type 2](cookbooks/etl/scd-type-2.md) |
| Load only new/changed rows (incremental / high-water mark) | [Incremental Load](cookbooks/etl/incremental-load-with-high-water-mark.md) |
| Truncate and fully reload a table | [Full Refresh](cookbooks/etl/full-refresh.md) |
| Pivot rows into columns for reporting | [Financial Reporting (PIVOT)](cookbooks/etl/financial-reporting-pivot.md) |
| De-duplicate records by fuzzy match | [Fuzzy Entity Resolution](cookbooks/etl/fuzzy-entity-resolution.md) |
| Build and run dynamic SQL | [Dynamic SQL with EXEC](cookbooks/etl/dynamic-sql-with-exec.md) |

## Validate and handle errors

| I want to… | Go to |
| :--- | :--- |
| Fail a pipeline when data quality checks fail | [Data Quality Gate](cookbooks/etl/data-quality-gate.md) |
| Route bad rows to a dead-letter destination | [Dead-Letter Queue](cookbooks/etl/dead-letter-queue.md) |

## Secure data and secrets

| I want to… | Go to |
| :--- | :--- |
| Encrypt a connection string for a script | [etl-sql encrypt](reference/cli/encrypt.md) |
| Manage secrets and secret references | [Security and Secret Management](administration/platform/secrets.md) |
| Mask or hash PII during load | [Secure PII Masking & Hashing](cookbooks/etl/pii-masking-and-hashing.md) |
| Send files securely to a vendor over SFTP | [Secure Vendor Handshake](cookbooks/etl/secure-vendor-handshake.md) |

## Schedule and orchestrate

| I want to… | Go to |
| :--- | :--- |
| Schedule a recurring job (`CREATE JOB`) | [Job Scheduling](administration/orchestration/job-scheduling.md) |
| Schedule a job on a remote Orchestrator | [Scheduling a Recurring Job on a Remote Orchestrator](cookbooks/etl/remote-orchestrator-schedule.md) |
| Compose pipelines as a DAG (fan-out, gating, branching) | [Pipelines and DAGs](guides/feature-guides/pipelines-and-dags.md) |
| Deploy an immutable published script bundle (CI/CD) | [Immutable Published Script Bundles](cookbooks/etl/immutable-published-bundles.md) |

## Notify and deliver

| I want to… | Go to |
| :--- | :--- |
| Send Slack/Teams alerts from a pipeline | [Automated Slack/Teams Alerting](cookbooks/etl/automated-slack-teams-alerting.md) |
| Email report subscriptions to users | [SMTP Connections and Subscriptions](administration/portal/connections-and-subscriptions.md) |
| Burst files out over SFTP on a schedule | [Automated SFTP Bursting](cookbooks/etl/automated-sftp-bursting.md) |

## Operate the server

| I want to… | Go to |
| :--- | :--- |
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

## Author reports

| I want to… | Go to |
| :--- | :--- |
| Author a `.rptsql` report | [Report SQL](guides/feature-guides/report-sql.md) |
| Follow complete report examples | [Report Recipes](cookbooks/report/README.md) |
| Build a master–detail drill-through | [Master-Detail Cross-Report Drill-through](cookbooks/etl/master-detail-drill-through.md) |

## Generate from a specification

| I want to… | Go to |
| :--- | :--- |
| Build a vendor feed from a JSON contract | [Specification-Driven Vendor Feed Build](cookbooks/etl/spec-driven-vendor-feed.md) |
| Understand spec-driven development | [Spec-Driven Development](spec-import/spec-driven-development.md) |

## See Also

- [Syntax Index](syntax-index.md) - language keywords, functions, connectors, options, and CLI commands.
- [Documentation Home](README.md) - all sections.
