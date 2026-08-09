# ETL-SQL Samples

This directory contains runnable `.etlsql` and `.rptsql` examples for learning, demos, regression checks, and release-readiness coverage. The samples are intentionally source-controlled because they are script-first product artifacts: they document supported syntax, exercise cross-source orchestration, and provide reviewable examples for CLI, VS Code, Portal, Orchestrator, and CI/CD workflows.

For the detailed walkthrough and highlighted scripts, see [`../docs/guides/patterns/sample-guide.md`](../docs/guides/patterns/sample-guide.md).

## Source control policy

Include files here when they are:

- **Canonical examples** - scripts that demonstrate supported ETL-SQL or Report-SQL behavior.
- **Regression fixtures** - snapshots, manifests, or sample data needed by tests or release checks.
- **Self-contained demos** - workflows that can run with `MOCKDB`, flat files, or documented local setup.
- **Sanitized** - no real credentials, tokens, private endpoints, customer data, or machine-specific paths.

Do not check in ad hoc local output, temporary portal state, generated logs, or one-off experiment results unless they are deliberately promoted into a documented sample or fixture.

## Folder map

| Folder | Purpose |
| :--- | :--- |
| `00_QuickStart/` | Smallest possible starter script. |
| `01_Basics/` | Variables, state, lists, previews, filters, date logic, and functions. |
| `02_Data_Movement/` | Flat files, fixed-width ingestion, bulk mapping, Avro, Parquet, and text qualifiers. |
| `03_SQL_Engines/` | SQL connectors, pushdown, Docker-backed SQL engines, and dialect examples. |
| `04_Orchestration/` | Batches, jobs, modular scripts, lineage, tags, and multi-system flows. |
| `05_Security_Diagnostics/` | `WHAT_IF`, audit settings, linter diagnostics, verbose logging, and engine statistics. |
| `06_Advanced_SQL/` | Joins, fuzzy matching, grouping, windows, subqueries, set logic, and join hints. |
| `07_Real_World/` | Scenario-style ETL examples that combine extraction, staging, validation, masking, reconciliation, and loading. |
| `08_Reporting/` | Report-SQL dashboards, interactions, inputs, datasets, required parameters, and snapshots. |
| `09_Conversions/` | Multi-script conversion and validation workflows. |
| `10_Kitchen_Sinks/` | Broad language and report visual coverage used for release-readiness checks. |
| `99_Experimental/` | Stress, environment verification, and experimental scripts that are not first-stop learning material. |
| `admin_operations/` | Script-first portal and orchestrator administration examples. |
| `golden_workflow/` | End-to-end Report-SQL demo and regression workflow. |
| `integration/` | Integration-oriented sample scripts and fixtures. |
| `logs/` | Sample log artifacts when needed by examples or tests. |
| `output/` | Sample output artifacts when needed by examples or tests. |
| `paginated/` | Multi-report and paginated hosting examples. |
| `portal_deployment/` | Script-first Portal promotion pattern. |

## Running samples safely

Read connection declarations and file paths before running a sample. Many scripts are self-contained, but some expect `testdata/`, Docker, local output folders, or placeholder database connections. Use `SET WHAT_IF ON;` when validating destructive operations, and keep generated secrets or local credentials outside this tree.
