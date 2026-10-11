# ETL-SQL Samples

This directory contains runnable `.etlsql` and `.rptsql` examples for learning, demos, regression checks, and release-readiness coverage. The samples are intentionally source-controlled because they are script-first product artifacts: they document supported syntax, exercise cross-source orchestration, and provide reviewable examples for CLI, VS Code, Portal, Orchestrator, and CI/CD workflows.

For the detailed walkthrough and highlighted scripts, see [`../docs/guides/patterns/sample-guide.md`](../docs/guides/patterns/sample-guide.md).

## Source Control Policy

Include files here when they are:

- **Canonical examples** — scripts that demonstrate supported ETL-SQL or Report-SQL behavior.
- **Regression fixtures** — snapshots, manifests, or sample data needed by tests or release checks.
- **Self-contained demos** — workflows that can run with `MOCKDB`, flat files, or documented local setup.
- **Sanitized** — no real credentials, tokens, private endpoints, customer data, or machine-specific paths.

Do not check in ad hoc local output, temporary portal state, generated logs, or one-off experiment results unless they are deliberately promoted into a documented sample or fixture.

## Folder Map

| Folder | Purpose |
| :--- | :--- |
| [`00_QuickStart/`](00_QuickStart/) | Smallest possible starter script. |
| [`01_Basics/`](01_Basics/) | Variables, state, lists, previews, filters, date logic, and standard library functions. |
| [`02_Data_Movement/`](02_Data_Movement/) | Flat files, fixed-width ingestion, bulk mapping, Avro, Parquet, and text qualifiers. |
| [`03_SQL_Engines/`](03_SQL_Engines/) | Database connectors (MSSQL, PostgreSQL, MySQL, Snowflake, SQLite, REST API, Excel, Webhooks) and pushdown. |
| [`04_Orchestration/`](04_Orchestration/) | Batches, jobs, modular scripts, lineage, tags, and multi-system flows. |
| [`05_Security_Diagnostics/`](05_Security_Diagnostics/) | `WHAT_IF` validation, audit settings, linter diagnostics, assertions, verbose logging, and statistics. |
| [`06_Advanced_SQL/`](06_Advanced_SQL/) | `ASOF JOIN`, `QUALIFY`, `GROUP BY ALL`, `LATERAL` subqueries, `MATCH_RECOGNIZE` CEP, fuzzy joins, and windowing. |
| [`07_Real_World/`](07_Real_World/) | Scenario-style ETL pipelines combining extract, stage, validate, mask, reconcile, and load phases. |
| [`08_Reporting/`](08_Reporting/) | Report-SQL dashboards, interactions, inputs, datasets, required parameters, and snapshots. |
| [`09_Conversions/`](09_Conversions/) | Multi-script conversion and validation workflows. |
| [`10_Kitchen_Sinks/`](10_Kitchen_Sinks/) | Monolithic test suites and release-readiness fixture decks used by CI test harnesses. |
| [`transforms/`](transforms/) | Declarative data prep and transformations (`GENERATE CALENDAR`, `COMPARE DATASETS`, `TRANSFORM ... USING`). |
| [`visual_analytics/`](visual_analytics/) | End-to-end analytical query-to-visual dashboards (Pareto 80/20, Sales Funnel, Cohort Heatmap, Waterfall, Trends). |
| [`visual_gallery/`](visual_gallery/) | Focused reference deck for 39 visual charts, grids, controls, and layout structures with clean semantic names. |
| [`admin_operations/`](admin_operations/) | Script-first portal and orchestrator administration examples. |
| [`golden_workflow/`](golden_workflow/) | End-to-end Report-SQL demo and regression workflow. |
| [`integration/`](integration/) | Integration-oriented sample scripts and fixtures. |
| [`paginated/`](paginated/) | Multi-report and paginated hosting examples. |
| [`portal_deployment/`](portal_deployment/) | Script-first Portal promotion pattern. |
| [`quality-loop/`](quality-loop/) | Runnable solo-operator policy, pipeline, quality gate, and local schedule. |
| [`99_Experimental/`](99_Experimental/) | Stress tests, environment verification, and benchmark scripts. |

## Running Samples Safely

Read connection declarations and file paths before running a sample. Many scripts are self-contained, but some expect `testdata/`, Docker, local output folders, or placeholder database connections. Use `SET WHAT_IF ON;` when validating destructive operations, and keep generated secrets or local credentials outside this tree.
