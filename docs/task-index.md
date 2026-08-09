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
| Load a CSV, Excel, or Parquet file into a table | [ETL Recipes](cookbooks/etl-recipes.md) — Staged Ingestion |
| Ingest every file in a directory automatically | [ETL Recipes](cookbooks/etl-recipes.md) — Batch Directory Ingester |
| Pull data from a REST API | [ETL Recipes](cookbooks/etl-recipes.md) — REST API Ingestion |
| Copy or reconcile data across two databases | [ETL Recipes](cookbooks/etl-recipes.md) — Cross-Platform Reconciliation |
| Ingest and query a graph database (Neo4j) | [ETL Recipes](cookbooks/etl-recipes.md) — Graph Ingestion & Querying |

## Transform data

| I want to… | Go to |
| :--- | :--- |
| Stage, clean, and merge source data (classic ETL) | [ETL Recipes](cookbooks/etl-recipes.md) — Staged Ingestion |
| Track history with SCD Type 2 | [ETL Recipes](cookbooks/etl-recipes.md) — SCD Type 2 |
| Load only new/changed rows (incremental / high-water mark) | [ETL Recipes](cookbooks/etl-recipes.md) — Incremental Load |
| Truncate and fully reload a table | [ETL Recipes](cookbooks/etl-recipes.md) — Full Refresh |
| Pivot rows into columns for reporting | [ETL Recipes](cookbooks/etl-recipes.md) — Financial Reporting (PIVOT) |
| De-duplicate records by fuzzy match | [ETL Recipes](cookbooks/etl-recipes.md) — Fuzzy Entity Resolution |
| Build and run dynamic SQL | [ETL Recipes](cookbooks/etl-recipes.md) — Dynamic SQL with EXEC |

## Validate and handle errors

| I want to… | Go to |
| :--- | :--- |
| Fail a pipeline when data quality checks fail | [ETL Recipes](cookbooks/etl-recipes.md) — Data Quality Gate |
| Route bad rows to a dead-letter destination | [ETL Recipes](cookbooks/etl-recipes.md) — Dead-Letter Queue |

## Secure data and secrets

| I want to… | Go to |
| :--- | :--- |
| Encrypt a connection string for a script | [etl-sql encrypt](reference/cli/encrypt.md) |
| Manage secrets and secret references | [Security and Secret Management](administration/platform/secrets.md) |
| Mask or hash PII during load | [ETL Recipes](cookbooks/etl-recipes.md) — Secure PII Masking & Hashing |
| Send files securely to a vendor over SFTP | [ETL Recipes](cookbooks/etl-recipes.md) — Secure Vendor Handshake |

## Schedule and orchestrate

| I want to… | Go to |
| :--- | :--- |
| Schedule a recurring job (`CREATE JOB`) | [Job Scheduling](administration/orchestration/job-scheduling.md) |
| Schedule a job on a remote Orchestrator | [ETL Recipes](cookbooks/etl-recipes.md) — Scheduling a Recurring Job on a Remote Orchestrator |
| Compose pipelines as a DAG (fan-out, gating, branching) | [Pipelines and DAGs](guides/feature-guides/pipelines-and-dags.md) |
| Deploy an immutable published script bundle (CI/CD) | [ETL Recipes](cookbooks/etl-recipes.md) — Immutable Published Script Bundles |

## Notify and deliver

| I want to… | Go to |
| :--- | :--- |
| Send Slack/Teams alerts from a pipeline | [ETL Recipes](cookbooks/etl-recipes.md) — Automated Slack/Teams Alerting |
| Email report subscriptions to users | [SMTP Connections and Subscriptions](administration/portal/connections-and-subscriptions.md) |
| Burst files out over SFTP on a schedule | [ETL Recipes](cookbooks/etl-recipes.md) — Automated SFTP Bursting |

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
| Publish a report and manage a catalog | [Publishing Reports](administration/portal/publishing.md) · [ETL Recipes](cookbooks/etl-recipes.md) — Publishing and Operating a Portal Catalog |
| Configure and review the audit log | [Health Monitoring and Audit Log](administration/portal/monitoring-and-audit.md) |

## Author reports

| I want to… | Go to |
| :--- | :--- |
| Author a `.rptsql` report | [Report SQL](guides/feature-guides/report-sql.md) |
| Follow complete report examples | [Report Recipes](cookbooks/report-recipes.md) |
| Build a master–detail drill-through | [ETL Recipes](cookbooks/etl-recipes.md) — Master-Detail Cross-Report Drill-through |

## Generate from a specification

| I want to… | Go to |
| :--- | :--- |
| Build a vendor feed from a JSON contract | [ETL Recipes](cookbooks/etl-recipes.md) — Specification-Driven Vendor Feed Build |
| Understand spec-driven development | [Spec-Driven Development](spec-import/spec-driven-development.md) |

## See Also

- [Syntax Index](syntax-index.md) - language keywords, functions, connectors, options, and CLI commands.
- [Documentation Home](README.md) - all sections.
