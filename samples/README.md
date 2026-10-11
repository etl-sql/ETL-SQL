# ETL-SQL Samples

This directory contains runnable `.etlsql` and `.rptsql` examples for learning, demos, regression checks, and release-readiness coverage. The samples are intentionally source-controlled because they are script-first product artifacts: they document supported syntax, exercise cross-source orchestration, and provide reviewable examples for CLI, VS Code, Portal, Orchestrator, and CI/CD workflows.

For the comprehensive walkthrough of highlighted scripts, see [`../docs/guides/patterns/sample-guide.md`](../docs/guides/patterns/sample-guide.md).

## Quick Start (30 Seconds)

To run your first script without any database setup:

```bash
# 1. Hello World check
dotnet run --project src/ETL-SQL.App -- run samples/00_QuickStart/sample_Hello.etlsql

# 2. In-memory ETL pipeline (Extract -> Stage -> Transform -> Load)
dotnet run --project src/ETL-SQL.App -- run samples/00_QuickStart/first_etl_pipeline.etlsql

# 3. Interactive Report (KPI Card + Bar Chart)
dotnet run --project src/ETL-SQL.App -- run samples/00_QuickStart/first_dashboard.rptsql
```

---

## Sample Library Map by Learning Track

### 1. Getting Started
| Folder | Focus |
| :--- | :--- |
| [`00_QuickStart/`](00_QuickStart/) | 30-second hello world, first in-memory ETL pipeline, and first visual dashboard. |
| [`01_Basics/`](01_Basics/) | Variables, state, lists, previews, dynamic filters, date logic, and standard library functions. |

### 2. Data Ingestion & Connectors
| Folder | Focus |
| :--- | :--- |
| [`02_Data_Movement/`](02_Data_Movement/) | Flat files, fixed-width files, Avro, Parquet, and text qualifiers. |
| [`03_SQL_Engines/`](03_SQL_Engines/) | Database connectors (MSSQL, PostgreSQL, MySQL, Snowflake, SQLite, REST API, Excel) and pushdown. |
| [`transforms/`](transforms/) | Declarative data prep (`GENERATE CALENDAR`, `COMPARE DATASETS`, `TRANSFORM ... USING`). |

### 3. Advanced SQL & Real-World Pipelines
| Folder | Focus |
| :--- | :--- |
| [`06_Advanced_SQL/`](06_Advanced_SQL/) | `ASOF JOIN`, `QUALIFY`, `GROUP BY ALL`, `LATERAL` joins, `MATCH_RECOGNIZE` CEP, windowing, and hints. |
| [`07_Real_World/`](07_Real_World/) | 12 production scenarios: DW loading, incremental merge, reconciliation, SFTP, masking, and deduplication. |
| [`integration/`](integration/) | Cross-system data integration, manufacturing telemetry generation, and parquet archiving. |
| [`quality-loop/`](quality-loop/) | Solo-operator quality policy, assertions, and local automated schedule. |

### 4. Reporting & Visual Dashboards
| Folder | Focus |
| :--- | :--- |
| [`visual_gallery/`](visual_gallery/) | 39 standalone visual chart, grid, and control references with clean semantic names. |
| [`visual_analytics/`](visual_analytics/) | End-to-end analytical dashboards (Pareto 80/20, Sales Funnel, Cohort Heatmap, Waterfall, Trends). |
| [`08_Reporting/`](08_Reporting/) | Interactive dashboards, slicers, master-detail rows, views, and portal datasets. |
| [`golden_workflow/`](golden_workflow/) | Flagship end-to-end Report-SQL demo script covering the full reporting lifecycle. |
| [`paginated/`](paginated/) | Multi-report and paginated executive decks. |

### 5. Platform, Orchestration & Security
| Folder | Focus |
| :--- | :--- |
| [`04_Orchestration/`](04_Orchestration/) | Modular scripts, batches, jobs, lineage tracking, and multi-system flows. |
| [`05_Security_Diagnostics/`](05_Security_Diagnostics/) | Zero-trust validation: `SET WHAT_IF ON;`, audit trails, assertions, and diagnostics. |
| [`admin_operations/`](admin_operations/) | Script-first portal and orchestrator administration (backups, capacity reports, failure digests). |
| [`portal_deployment/`](portal_deployment/) | Script-first portal promotion pattern (folders, groups, ACLs, and report publishing). |

### 6. Automated Test Decks & Fixtures
| Folder | Focus |
| :--- | :--- |
| [`10_Kitchen_Sinks/`](10_Kitchen_Sinks/) | Comprehensive language feature sinks and visual regression suites used by CI. |
| [`09_Conversions/`](09_Conversions/) | Multi-script conversion workflows (HR entity setup, export, and import). |
| [`99_Experimental/`](99_Experimental/) | Hyper-scale memory spilling checks and throughput stress benchmarks. |

---

## Running Samples Safely

Many samples are completely self-contained and run in-memory or with flat files. For scripts interacting with databases:
- Read connection declarations at the top of the file.
- Use `SET WHAT_IF ON;` when validating destructive operations (`DELETE`, `TRUNCATE`, `DROP`).
- Connectors requiring external daemons are tagged with `-- @requires: <service>` (e.g. `docker`, `postgres`, `portal`, `orchestrator`).
