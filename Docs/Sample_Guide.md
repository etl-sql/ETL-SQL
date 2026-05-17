# ETL-SQL Sample Guide

This guide describes the provided sample scripts in the `samples/` folder. These samples are organized into topical subfolders (e.g., `01_Basics`, `07_Real_World`, `08_Reporting`) to demonstrate the various capabilities of the ETL-SQL engine. The folder contains **100+ scripts** across engine, connector, reporting, test, and real-world scenarios.

> [!TIP]
> **Running the samples safely**: All scripts in the **Core Samples** section use `MOCKDB` and require no external connections. Scripts in the **Enterprise Real-World Scenarios** section reference external databases — replace the connection details with your own before running. Use `SET WHAT_IF ON;` at the top of any script to do a dry-run before executing destructive operations.

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

### [10_Kitchen_Sinks/report_kitchen_sink.rptsql](../samples/10_Kitchen_Sinks/report_kitchen_sink.rptsql)
**Purpose**: Full Report-SQL reference report for release readiness and visual/runtime coverage.
- Demonstrates the extended visual set, named styles, datasets, buttons, navigation, containers, interactions, and advanced charts.
- Uses CSS grid-template-area `STRUCTURE` strings and quoted `MAP` slots throughout.
- Pairs with `report_kitchen_sink.snapshot.json` so runtime and snapshot behavior can be inspected without external services.

## Core Samples

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

### 24. [Bulk_Insert_Mapping.etlsql](../samples/02_Data_Movement/Bulk_Insert_Mapping.etlsql)
**Purpose**: Bulk insertion with target column mapping.
- Demonstrates the `BULK INSERT` statement allocating disparate CSV streams onto explicitly reordered table columns without a staging table.

### 25. [Throughput_Stress_Test.etlsql](../samples/99_Experimental/Throughput_Stress_Test.etlsql)
**Purpose**: High-volume throughput evaluation.
- Illustrates a 5-million row streaming data pipeline stressing engine memory ceilings directly evaluating outer-streams against fixed dimension schemas via `.csv` file orchestration.

## Enterprise Real-World Scenarios

These advanced scripts demonstrate complex, production-grade business requirements implemented natively in ETL-SQL.

### 26. [realworld_01_dw_load.etlsql](../samples/07_Real_World/realworld_01_dw_load.etlsql)
**Multi-System DW Load**: Extracts PostgreSQL transactions, joins a legacy CSV dimension map via `INNER HASH JOIN`, aggregates metrics, and bulk inserts them into a SQL Server Data Warehouse.

### 27. [realworld_02_secure_sftp_alert.etlsql](../samples/07_Real_World/realworld_02_secure_sftp_alert.etlsql)
**Secure File Transfer & Alerting**: Extracts daily Oracle ledgers to a CSV, executes 256-bit AES `ENCRYPT_FILE`, moves the payload dynamically via `SFTP`, and uses `TRY/CATCH` blocks to send automated `EMAIL_SEND` exception reports to engineering oncalls upon failure.

### 28. [realworld_03_schema_quarantine.etlsql](../samples/07_Real_World/realworld_03_schema_quarantine.etlsql)
**Strict Schema Quarantine**: Ingests third-party files utilizing `STRICT_SCHEMA='ON'`. Splits traffic dynamically into a "Clean" #Temp table and a "Malaligned" Quarantined audit log, printing validation counts to the console natively.

### 29. [realworld_04_incremental_merge.etlsql](../samples/07_Real_World/realworld_04_incremental_merge.etlsql)
**Incremental UPSERT**: Evaluates a daily delta load using standard SQL `MERGE INTO`. Updates preexisting records, inserts new objects natively, and tracks every state permutation outputively via an internal audit trace matrix.

### 30. [realworld_05_masking_json.etlsql](../samples/07_Real_World/realworld_05_masking_json.etlsql)
**Dynamic Masking & JSON Formatting**: Redacts PII records iteratively (SSN, Email) using native `SUBSTRING`/`CONCAT` operations, reallocates projection vectors dynamically grouping profiles, and deposits a unified struct explicitly defined as JSON onto an Azure storage blob.

### 31. [realworld_06_reconciliation_anti_join.etlsql](../samples/07_Real_World/realworld_06_reconciliation_anti_join.etlsql)
**Data Reconciliation Anti-Join**: Extracts live dimensions simultaneously from a Postgres node versus a secondary database (connected via `ODBC` — MySQL is not a natively supported connector type) to orchestrate a `LEFT IS NULL` anti-join mapping pipeline that immediately logs untracked disparities to a flat report.

### 32. [realworld_07_window_deduplication.etlsql](../samples/07_Real_World/realworld_07_window_deduplication.etlsql)
**Window Analytics Deduplication**: Ingests unstructured clickstream events formatting natively sequentially over a unified `ROW_NUMBER() OVER(PARTITION BY UserID)` logical ranking hierarchy to dynamically compress/delete old transactions before executing a compressed `PARQUET` write.

### 33. [realworld_08_aggregation_pivot.etlsql](../samples/07_Real_World/realworld_08_aggregation_pivot.etlsql)
**Complex Quarterly Pivot**: Orchestrates cross-aggregate logic employing matrixed `SUM(CASE WHEN...)` groupings to artificially pivot continuous sequential transactions into physical grid columns natively deposited into an internal corporate `EXCEL` document.

### 34. [realworld_09_directory_watcher.etlsql](../samples/07_Real_World/realworld_09_directory_watcher.etlsql)
**Event-Driven Daemon Orchestration**: Executes an infinite state machine via a continuous generic `WHILE` loop intelligently monitoring a pickup array waiting for incoming integration drops before archiving the artifact natively out of path and breaking successfully.

### 35. [realworld_10_docker_sync.etlsql](../samples/07_Real_World/realworld_10_docker_sync.etlsql)
**Ephemeral Sandboxed Synchronization**: Instantiates temporary synchronized infrastructure utilizing simultaneous lightweight engine hooks triggering `mcr.mssql` environments and secondary `alpine.postgres` hubs, safely cloning configuration grids autonomously prior to terminating operations flawlessly.

---

*Refer to [User_Manual.md](User_Manual.md) for the pipeline mental model, [Cookbook.md](Cookbook.md) for production recipes, [Reference/Grammar.md](Reference/Grammar.md) for full syntax, and [Reference/Data_Connectors.md](Reference/Data_Connectors.md) for connector options.*
