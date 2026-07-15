# ETL-SQL Sample Guide

This guide describes the provided sample scripts in the `samples/` folder. These samples are organized into topical subfolders (for example, `01_Basics`, `07_Real_World`, `08_Reporting`, and `10_Kitchen_Sinks`) to demonstrate ETL-SQL engine, connector, reporting, test, and release-readiness scenarios. The folder currently contains **160+ `.etlsql` and `.rptsql` scripts**.

> [!TIP]
> **Running the samples safely**: Many samples are self-contained, but some use local flat files, generated output folders, Docker, or placeholder database connections. Read the connection declarations at the top of a sample before running it. Use `SET WHAT_IF ON;` when validating destructive operations.

## Folder Map

| Folder | What it is for |
| :--- | :--- |
| `00_QuickStart` | Smallest possible starter script. |
| `01_Basics` | Variables, lists, previewing, dynamic filters, date logic, and function basics. |
| `02_Data_Movement` | Flat files, fixed-width files, Avro, Parquet, bulk insert, and text qualifiers. |
| `03_SQL_Engines` | SQL connectors, pushdown examples, Docker-backed SQL engines, and dialect-specific examples. |
| `04_Orchestration` | Modular scripts, batches, jobs, lineage, and multi-system orchestration. |
| `05_Security_Diagnostics` | `WHAT_IF`, linter diagnostics, audit settings, verbose logging, and engine statistics. |
| `06_Advanced_SQL` | Grouping, windows, joins, subqueries, set logic, and join hints. |
| `07_Real_World` | Scenario-style scripts that combine multiple features. |
| `08_Reporting` | Report-SQL interaction, inputs, dashboards, and targeted report behavior checks. |
| `09_Conversions` | Multi-script conversion/check workflows. |
| `10_Kitchen_Sinks` | Broad release-readiness coverage for report visuals and language features. |
| `golden_workflow` | End-to-end Report-SQL workflow used for demos and regression checks. |
| `paginated` | Multi-report/paginated hosting examples. |
| `report_portal_deployment` | Script-first portal promotion and deployment pattern. |
| `99_Experimental` | Stress tests and experiments; not the first place to learn the language. |
| `output` and nested `samples/output` folders | Generated or checked-in sample output artifacts. |

## Golden Workflow

### [golden_workflow/golden_workflow.rptsql](../samples/golden_workflow/golden_workflow.rptsql)
**Purpose**: End-to-end Report-SQL workflow used as a walkthrough, portal demo, and regression target.
- Demonstrates extract -> stage -> validate -> report visuals -> publish -> execute -> interact -> export.
- Extracts from `MOCKDB`, reads flat-file targets, writes a staged CSV, and reports from the reloaded export.
- Runs without external services or machine-specific credentials.
- Shares one script across VS Code preview, standalone serve, Report Portal, and automated tests.

### [report_portal_deployment/portal_promotion.etlsql](../samples/report_portal_deployment/portal_promotion.etlsql)
**Purpose**: Script-first Report Portal promotion pattern for dev/prod deployments.
- Uses `CREATE SETS` and `USE SETS` as the environment boundary.
- Publishes and updates reports with canonical `PUBLISH REPORT` and `ALTER REPORT` syntax.
- Grants folder permissions, creates refresh jobs, and refreshes the report after promotion.
- Keeps portal object names and target paths as explicit string literals for reviewable deployments.

### [08_Reporting/kitchen_sink.rptsql](../samples/08_Reporting/kitchen_sink.rptsql)
**Purpose**: Compact Report-SQL kitchen sink for interactive inputs and deferred execution.
- Demonstrates `SLICER`, `NUMBERBOX`, `CHECKBOX`, `TEXTBOX`, `RELDATEPICKER`, `TABLE`, and `BAR`.
- Uses canonical `ACTIONS (ON_CHANGE = SET_PARAMETER(...))` bindings and `CREATE BUTTON ... AS (...)` for staged apply.
- Places controls, buttons, and visuals directly into quoted `MAP` slots on a `CREATE PAGE`.

### [08_Reporting/report_views.rptsql](../samples/08_Reporting/report_views.rptsql)
**Purpose**: Report-SQL view reuse pattern.
- Uses `CREATE VIEW` to define a shared filtered query once.
- Reuses the view directly as a table visual source and inside an aggregate chart query.
- Demonstrates that views are query aliases, not materialized report datasets.

### [08_Reporting/datasets/README.md](../samples/08_Reporting/datasets/README.md)
**Purpose**: Portal dataset security and portable-transfer verification deck.
- Uses inline rows to deploy separate PUBLIC and PRIVATE datasets without an external data source.
- Verifies cross-folder consumption, private grants and denials, and independent refresh permission.
- Exercises PASSWORD and RSA KEYFILE `EXPORT DATASET` to `PUBLISH DATASET` round trips.
- Requires portal execution for registry, folder, identity, and ACL behavior; local tests parser-check all five scripts.

### [10_Kitchen_Sinks/report_kitchen_sink.rptsql](../samples/10_Kitchen_Sinks/report_kitchen_sink.rptsql)
**Purpose**: Full Report-SQL reference report for release readiness and visual/runtime coverage.
- Demonstrates the extended visual set, named styles, datasets, buttons, navigation, containers, interactions, and advanced charts.
- Uses CSS grid-template-area `STRUCTURE` strings and quoted `MAP` slots throughout.
- Pairs with `report_kitchen_sink.snapshot.json` so runtime and snapshot behavior can be inspected without external services.

## Core Samples

Core samples introduce language features and small workflows. Some read or write files under `testdata/` or `samples/output/`; inspect the paths before running them from a different working directory.

### 1. [Basic_ETL.etlsql](../samples/01_Basics/Basic_ETL.etlsql)
**Purpose**: Introduction to basic ETL operations.
- Demonstrates defining connections to flat files.
- Shows basic `INSERT INTO ... SELECT` logic.
- Uses common string and math functions (`UPPER`, `CONCAT`).

### 2. [Batch_Processing.etlsql](../samples/04_Orchestration/Batch_Processing.etlsql)
**Purpose**: Working with batch processing and temporary tables.
- Demonstrates creating and using local temporary tables (`#temp`).
- Shows multi-step data transformation pipelines.

### 3. [Database_Operations.etlsql](../samples/03_SQL_Engines/Database_Operations.etlsql)
**Purpose**: Interaction with database connectors (e.g., MSSQL/MockDB).
- Shows how to define database connections.
- Demonstrates DML operations (`INSERT`, `UPDATE`, `DELETE`) against database targets.

## Advanced Querying

### 4. [Grouping_Aggregations.etlsql](../samples/06_Advanced_SQL/Grouping_Aggregations.etlsql)
**Purpose**: Advanced grouping and filtering.
- Demonstrates `GROUP BY` with multiple columns.
- Shows the use of the `HAVING` clause to filter aggregated results.

### 5. [Window_Functions.etlsql](../samples/06_Advanced_SQL/Window_Functions.etlsql)
**Purpose**: Using Analytical/Window functions.
- Demonstrates `ROW_NUMBER`, `RANK`, and `DENSE_RANK`.
- Shows `LAG` and `LEAD` for accessing previous/next row data.

### 6. [Subqueries_and_Sets.etlsql](../samples/06_Advanced_SQL/Subqueries_and_Sets.etlsql)
**Purpose**: Subqueries and set logic.
- Demonstrates scalar subqueries in `SELECT` and `SET` statements.
- Shows correlated subqueries and `EXISTS` logic.
- Covers Semi-Joins and Anti-Joins.

## Procedural & Automation

### 7. [Modular_Scripts.etlsql](../samples/04_Orchestration/Modular_Scripts.etlsql)
**Purpose**: Stored Procedures and User-Defined Functions (UDFs).
- Demonstrates creating and executing procedures with parameters.
- Shows how to define and use scalar functions in expressions.

### 8. [Variables_and_State.etlsql](../samples/01_Basics/Variables_and_State.etlsql)
**Purpose**: State management and variables.
- Demonstrates `DECLARE` and `SET` for various types (`INT`, `STRING`, `DECIMAL`, `LIST`).
- Shows how to use variables in queries and flow control.

### 9. [Lists_and_Foreach.etlsql](../samples/01_Basics/Lists_and_Foreach.etlsql)
**Purpose**: Working with the `LIST` type.
- Demonstrates list initialization and iteration via `FOREACH`.
- Shows list functions like `APPEND_TO_LIST` and `SORT_LIST`.

## Specialized Scenarios

### 10. [Connection_Parameters.etlsql](../samples/03_SQL_Engines/Connection_Parameters.etlsql)
**Purpose**: Fine-grained connector configuration.
- Demonstrates the `WITH` clause for `FILE` connections (Delimiters, Headers).
- Shows how to use `TEXT_QUALIFIER`.

### 11. [Complex_Join_Pipeline.etlsql](../samples/06_Advanced_SQL/Complex_Join_Pipeline.etlsql)
**Purpose**: Comprehensive real-world scenario.
- Combines joins, aggregations, window functions, and subqueries into a single complex pipeline.

### 12. [Data_Preview.etlsql](../samples/01_Basics/Data_Preview.etlsql)
**Purpose**: Using the preview mode.
- Demonstrates how to limit result sets for quick data inspection.

### 13. [WhatIf_Dry_Run.etlsql](../samples/05_Security_Diagnostics/WhatIf_Dry_Run.etlsql)
**Purpose**: Using the dry-run mode.
- Demonstrates `SET WHAT_IF ON` to skip destructive operations during validation.
- Shows how to toggle back to `OFF` for actual execution.


## Docker & Remote Execution

### 14. [Docker_Orchestration.etlsql](../samples/03_SQL_Engines/Docker_Orchestration.etlsql)
**Purpose**: Docker-based SQL Server and Remote Block Execution.
- Demonstrates spinning up a SQL Server container via `START DOCKER` and shutting it down with `CLOSE DOCKER`.
- Illustration of `EXECUTE connection BEGIN...END` blocks.

### 15. [Multi_System_Integration.etlsql](../samples/04_Orchestration/Multi_System_Integration.etlsql)
**Purpose**: Comprehensive multi-system integration.
- Demonstrates Docker orchestration (MSSQL + Postgres).
- Shows cross-database joins and indexing.

## Modern SQL Extensions

### 16. [ANSI_SQL_Extensions.etlsql](../samples/03_SQL_Engines/ANSI_SQL_Extensions.etlsql)
**Purpose**: Advanced ANSI SQL sorting and limiting.
- Demonstrates **Positional ORDER BY** (1-based column indices).
- Shows **TOP PERCENT** and **TOP WITH TIES** logic for rank-based filtering.

### 17. [Engine_Statistics.etlsql](../samples/05_Security_Diagnostics/Engine_Statistics.etlsql)
**Purpose**: Population and Sample statistics.
- Demonstrates advanced aggregates: `VAR`, `STDEV`, `VAR_POP`, `STDDEV_POP`.
- Shows correlation and covariance between variables: `CORR(x, y)`, `COVAR_SAMP(x, y)`.

### 18. [Temporal_Date_Logic.etlsql](../samples/01_Basics/Temporal_Date_Logic.etlsql)
**Purpose**: Advanced Date/Time handling.
- Demonstrates **AT TIME ZONE** conversion between global regions.
- Shows shorthand **Date Arithmetic** (`SYSDATE + 7`).
- Compares `GETDATE()` (MSSQL style) and `SYSDATE` (Oracle style) usage.

### 19. [Fixed_Width_Ingestion.etlsql](../samples/02_Data_Movement/Fixed_Width_Ingestion.etlsql)
**Purpose**: Template-based Fixed-Width file ingestion.
- Demonstrates `FORMAT='FIXED'` connections.
- Shows how to use a table schema (`#temp`) as a layout template for bit-perfect field slicing.

### 20. [Enhanced_File_IO.etlsql](../samples/02_Data_Movement/Enhanced_File_IO.etlsql)
**Purpose**: Modernized IO and Automation syntax.
- Demonstrates **Verbose Syntax** for `COPY FILE`, `MOVE FILE`, and `DELETE FILE`.
- Shows the structural `SEND EMAIL` and `SEND FILE` (SFTP) syntax improvements.

## Modern Data Formats & Integration

### 21. [Job_Scheduling.etlsql](../samples/04_Orchestration/Job_Scheduling.etlsql)
**Purpose**: Background jobs and scheduling.
- Demonstrates creating scheduled tasks via `CREATE JOB`.
- Shows how to monitor job status using `SHOW JOBS`.

### 22. [Avro_Read_Write.etlsql](../samples/02_Data_Movement/Avro_Read_Write.etlsql) / [Parquet_Read_Write.etlsql](../samples/02_Data_Movement/Parquet_Read_Write.etlsql)
**Purpose**: Modern columnar data formats.
- Demonstrates reading and writing Avro and Parquet files.

## Performance & Optimization Sandbox

### 23. [Join_Algorithm_Hints.etlsql](../samples/06_Advanced_SQL/Join_Algorithm_Hints.etlsql)
**Purpose**: Forcing specific join algorithms.
- Demonstrates `INNER HASH JOIN`, `INNER LOOP JOIN`, `LEFT HASH JOIN`, etc., to explicitly configure the SQL execution engine's behavior for strict streaming allocations.

### 24. [Fuzzy_Matching_Functions.etlsql](../samples/06_Advanced_SQL/Fuzzy_Matching_Functions.etlsql)
**Purpose**: Choosing fuzzy matching functions and thresholds.
- Compares `SIMILARITY` algorithms, raw `LEVENSHTEIN` distance, company-name `NORMALIZE`, and `SOUNDEX`.

### 25. [Fuzzy_Joins.etlsql](../samples/06_Advanced_SQL/Fuzzy_Joins.etlsql)
**Purpose**: Similarity-based joins with candidate control.
- Demonstrates `FUZZY JOIN`, `LEFT FUZZY JOIN`, the injected `__score` column, normalization, and `KEEP BEST`.

### 26. [Bulk_Insert_Mapping.etlsql](../samples/02_Data_Movement/Bulk_Insert_Mapping.etlsql)
**Purpose**: Bulk insertion with target column mapping.
- Demonstrates the `BULK INSERT` statement allocating disparate CSV streams onto explicitly reordered table columns without a staging table.

### 27. [Throughput_Stress_Test.etlsql](../samples/99_Experimental/Throughput_Stress_Test.etlsql)
**Purpose**: High-volume throughput evaluation.
- Illustrates a 5-million row streaming data pipeline stressing engine memory ceilings directly evaluating outer-streams against fixed dimension schemas via `.csv` file orchestration.

## Enterprise Real-World Scenarios

These advanced scripts demonstrate complex, production-grade business requirements implemented natively in ETL-SQL.

### 28. [realworld_01_dw_load.etlsql](../samples/07_Real_World/realworld_01_dw_load.etlsql)
**Multi-System DW Load**: Extracts PostgreSQL transactions, joins a legacy CSV dimension map via `INNER HASH JOIN`, aggregates metrics, and bulk inserts them into a SQL Server Data Warehouse.

### 29. [realworld_02_secure_sftp_alert.etlsql](../samples/07_Real_World/realworld_02_secure_sftp_alert.etlsql)
**Secure File Transfer & Alerting**: Self-contained local simulation of a finance extract, encrypted payload handoff, and `TRY/CATCH` failure handling. It uses local folders to stand in for an SFTP drop.

### 30. [realworld_03_schema_quarantine.etlsql](../samples/07_Real_World/realworld_03_schema_quarantine.etlsql)
**Strict Schema Quarantine**: Ingests third-party files utilizing `STRICT_SCHEMA='ON'`. Splits traffic dynamically into a "Clean" #Temp table and a "Malaligned" Quarantined audit log, printing validation counts to the console natively.

### 31. [realworld_04_incremental_merge.etlsql](../samples/07_Real_World/realworld_04_incremental_merge.etlsql)
**Incremental UPSERT**: Demonstrates a daily delta load with `MERGE INTO`, updating existing rows and inserting new rows while recording an audit-style result set.

### 32. [realworld_05_masking_json.etlsql](../samples/07_Real_World/realworld_05_masking_json.etlsql)
**Dynamic Masking & JSON Formatting**: Redacts PII-like fields with string functions and writes a masked profile export to local JSON output.

### 33. [realworld_06_reconciliation_anti_join.etlsql](../samples/07_Real_World/realworld_06_reconciliation_anti_join.etlsql)
**Data Reconciliation Anti-Join**: Demonstrates a reconciliation pattern using anti-join logic and writes unmatched rows to a flat audit report.

### 34. [realworld_07_window_deduplication.etlsql](../samples/07_Real_World/realworld_07_window_deduplication.etlsql)
**Window Analytics Deduplication**: Uses `ROW_NUMBER() OVER(PARTITION BY ...)` to identify duplicate or older events before writing a cleaned Parquet-style output.

### 35. [realworld_08_aggregation_pivot.etlsql](../samples/07_Real_World/realworld_08_aggregation_pivot.etlsql)
**Complex Quarterly Pivot**: Uses `SUM(CASE WHEN ...)` aggregation to build a quarterly pivot-style board report.

### 36. [realworld_09_directory_watcher.etlsql](../samples/07_Real_World/realworld_09_directory_watcher.etlsql)
**Directory Watcher Pattern**: Uses a `WHILE` loop and directory/file checks to wait for an incoming drop, process it, and archive the artifact.

### 37. [realworld_10_docker_sync.etlsql](../samples/07_Real_World/realworld_10_docker_sync.etlsql)
**Docker-Based Synchronization**: Starts temporary SQL Server and Postgres containers, copies a small reference table between them through the engine, and closes the containers. Requires Docker to be available.

### 38. [realworld_11_customer_entity_resolution.etlsql](../samples/07_Real_World/realworld_11_customer_entity_resolution.etlsql)
**Customer Entity Resolution**: Blends normalized company-name and city similarity scores, automatically accepts strong matches, and creates a review queue for weak or unmatched rows.

### 39. [realworld_12_spec_driven_customer_feed.etlsql](../samples/07_Real_World/realworld_12_spec_driven_customer_feed.etlsql)
**Specification-Driven Vendor Feed**: Shows the completed script from a matching JSON spec contract, including schema validation, validation issue summaries, quarantine rows, valid-row export, and lineage tagging.

---

*Refer to [User_Manual.md](User_Manual.md) for the pipeline mental model, [Cookbook.md](Cookbook.md) for production recipes, [Reference/Grammar.md](Reference/Grammar.md) for full syntax, and [Reference/Data_Connectors.md](Reference/Data_Connectors.md) for connector options.*
