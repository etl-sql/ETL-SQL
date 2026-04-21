# ETL-SQL Sample Guide

This guide describes the provided sample scripts in the `samples/` folder. These samples are designed to demonstrate the various capabilities of the ETL-SQL engine. The folder contains **64 scripts** in total (60 `.etlsql` + 4 `.rptsql`). The 34 numbered scripts below are the primary demonstrations; additional utility, test, and sub-scripts exist in the folder for advanced exploration.

> [!TIP]
> **Running the samples safely**: All scripts in the **Core Samples** section use `MOCKDB` and require no external connections. Scripts in the **Enterprise Real-World Scenarios** section reference external databases — replace the connection details with your own before running. Use `SET WHAT_IF ON;` at the top of any script to do a dry-run before executing destructive operations.

## Core Samples

### 1. [sample.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample.etlsql)
**Purpose**: Introduction to basic ETL operations.
- Demonstrates defining connections to flat files.
- Shows basic `INSERT INTO ... SELECT` logic.
- Uses common string and math functions (`UPPER`, `CONCAT`).

### 2. [sample_batch.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_batch.etlsql)
**Purpose**: Working with batch processing and temporary tables.
- Demonstrates creating and using local temporary tables (`#temp`).
- Shows multi-step data transformation pipelines.

### 3. [sample_db.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_db.etlsql)
**Purpose**: Interaction with database connectors (e.g., MSSQL/MockDB).
- Shows how to define database connections.
- Demonstrates DML operations (`INSERT`, `UPDATE`, `DELETE`) against database targets.

## Advanced Querying

### 4. [sample_aggregation.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_aggregation.etlsql)
**Purpose**: Advanced grouping and filtering.
- Demonstrates `GROUP BY` with multiple columns.
- Shows the use of the `HAVING` clause to filter aggregated results.

### 5. [sample_window_functions.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_window_functions.etlsql)
**Purpose**: Using Analytical/Window functions.
- Demonstrates `ROW_NUMBER`, `RANK`, and `DENSE_RANK`.
- Shows `LAG` and `LEAD` for accessing previous/next row data.

### 6. [sample_subqueries.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_subqueries.etlsql)
**Purpose**: Subqueries and set logic.
- Demonstrates scalar subqueries in `SELECT` and `SET` statements.
- Shows correlated subqueries and `EXISTS` logic.
- Covers Semi-Joins and Anti-Joins.

## Procedural & Automation

### 7. [sample_modular_etl.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_modular_etl.etlsql)
**Purpose**: Stored Procedures and User-Defined Functions (UDFs).
- Demonstrates creating and executing procedures with parameters.
- Shows how to define and use scalar functions in expressions.

### 8. [sample_variables.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_variables.etlsql)
**Purpose**: State management and variables.
- Demonstrates `DECLARE` and `SET` for various types (`INT`, `STRING`, `DECIMAL`, `LIST`).
- Shows how to use variables in queries and flow control.

### 9. [sample_lists.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_lists.etlsql)
**Purpose**: Working with the `LIST` type.
- Demonstrates list initialization and iteration via `FOREACH`.
- Shows list functions like `APPEND_TO_LIST` and `SORT_LIST`.

## Specialized Scenarios

### 10. [sample_connection_options.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_connection_options.etlsql)
**Purpose**: Fine-grained connector configuration.
- Demonstrates the `WITH` clause for `FILE` connections (Delimiters, Headers).
- Shows how to use `TEXT_QUALIFIER`.

### 11. [sample_complex_query.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_complex_query.etlsql)
**Purpose**: Comprehensive real-world scenario.
- Combines joins, aggregations, window functions, and subqueries into a single complex pipeline.

### 12. [sample_preview.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_preview.etlsql)
**Purpose**: Using the preview mode.
- Demonstrates how to limit result sets for quick data inspection.

### 13. [sample_what_if.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_what_if.etlsql)
**Purpose**: Using the dry-run mode.
- Demonstrates `SET WHAT_IF ON` to skip destructive operations during validation.
- Shows how to toggle back to `OFF` for actual execution.


## Docker & Remote Execution

### 14. [sample_docker.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_docker.etlsql)
**Purpose**: Docker-based SQL Server and Remote Block Execution.
- Demonstrates spinning up a SQL Server container via `START DOCKER` and shutting it down with `CLOSE DOCKER`.
- Illustration of `EXECUTE connection BEGIN...END` blocks.

### 15. [sample_mega_integration.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_mega_integration.etlsql)
**Purpose**: Comprehensive multi-system integration.
- Demonstrates Docker orchestration (MSSQL + Postgres).
- Shows cross-database joins and indexing.

## Modern SQL Extensions

### 16. [sample_ansi_sql.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_ansi_sql.etlsql)
**Purpose**: Advanced ANSI SQL sorting and limiting.
- Demonstrates **Positional ORDER BY** (1-based column indices).
- Shows **TOP PERCENT** and **TOP WITH TIES** logic for rank-based filtering.

### 17. [sample_statistics.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_statistics.etlsql)
**Purpose**: Population and Sample statistics.
- Demonstrates advanced aggregates: `VAR`, `STDEV`, `VAR_POP`, `STDDEV_POP`.
- Shows correlation and covariance between variables: `CORR(x, y)`, `COVAR_SAMP(x, y)`.

### 18. [sample_temporal.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_temporal.etlsql)
**Purpose**: Advanced Date/Time handling.
- Demonstrates **AT TIME ZONE** conversion between global regions.
- Shows shorthand **Date Arithmetic** (`SYSDATE + 7`).
- Compares `GETDATE()` (MSSQL style) and `SYSDATE` (Oracle style) usage.

### 19. [sample_fixed_width.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_fixed_width.etlsql)
**Purpose**: Template-based Fixed-Width file ingestion.
- Demonstrates `FORMAT='FIXED'` connections.
- Shows how to use a table schema (`#temp`) as a layout template for bit-perfect field slicing.

### 20. [sample_enhanced_io.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_enhanced_io.etlsql)
**Purpose**: Modernized IO and Automation syntax.
- Demonstrates **Verbose Syntax** for `COPY FILE`, `MOVE FILE`, and `DELETE FILE`.
- Shows the structural `SEND EMAIL` and `SEND FILE` (SFTP) syntax improvements.

## Modern Data Formats & Integration

### 21. [sample_jobs.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_jobs.etlsql)
**Purpose**: Background jobs and scheduling.
- Demonstrates creating scheduled tasks via `CREATE JOB`.
- Shows how to monitor job status using `SHOW JOBS`.

### 22. [sample_avro.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_avro.etlsql) / [sample_parquet.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_parquet.etlsql)
**Purpose**: Modern columnar data formats.
- Demonstrates reading and writing Avro and Parquet files.

## Performance & Optimization Sandbox

### 23. [sample_JoinHintsTest.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_JoinHintsTest.etlsql)
**Purpose**: Forcing specific join algorithms.
- Demonstrates `INNER HASH JOIN`, `INNER LOOP JOIN`, `LEFT HASH JOIN`, etc., to explicitly configure the SQL execution engine's behavior for strict streaming allocations.

### 24. [sample_BulkInsertColumnsTest.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_BulkInsertColumnsTest.etlsql)
**Purpose**: Bulk insertion with target column mapping.
- Demonstrates the `BULK INSERT` statement allocating disparate CSV streams onto explicitly reordered table columns without a staging table.

### 25. [sample_StressTest.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/sample_StressTest.etlsql)
**Purpose**: High-volume throughput evaluation.
- Illustrates a 5-million row streaming data pipeline stressing engine memory ceilings directly evaluating outer-streams against fixed dimension schemas via `.csv` file orchestration.

## Enterprise Real-World Scenarios

These advanced scripts demonstrate complex, production-grade business requirements implemented natively in ETL-SQL.

### 26. [realworld_01_dw_load.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/realworld_01_dw_load.etlsql)
**Multi-System DW Load**: Extracts PostgreSQL transactions, joins a legacy CSV dimension map via `INNER HASH JOIN`, aggregates metrics, and bulk inserts them into a SQL Server Data Warehouse.

### 27. [realworld_02_secure_sftp_alert.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/realworld_02_secure_sftp_alert.etlsql)
**Secure File Transfer & Alerting**: Extracts daily Oracle ledgers to a CSV, executes 256-bit AES `ENCRYPT_FILE`, moves the payload dynamically via `SFTP`, and uses `TRY/CATCH` blocks to send automated `EMAIL_SEND` exception reports to engineering oncalls upon failure.

### 28. [realworld_03_schema_quarantine.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/realworld_03_schema_quarantine.etlsql)
**Strict Schema Quarantine**: Ingests third-party files utilizing `STRICT_SCHEMA='ON'`. Splits traffic dynamically into a "Clean" #Temp table and a "Malaligned" Quarantined audit log, printing validation counts to the console natively.

### 29. [realworld_04_incremental_merge.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/realworld_04_incremental_merge.etlsql)
**Incremental UPSERT**: Evaluates a daily delta load using standard SQL `MERGE INTO`. Updates preexisting records, inserts new objects natively, and tracks every state permutation outputively via an internal audit trace matrix.

### 30. [realworld_05_masking_json.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/realworld_05_masking_json.etlsql)
**Dynamic Masking & JSON Formatting**: Redacts PII records iteratively (SSN, Email) using native `SUBSTRING`/`CONCAT` operations, reallocates projection vectors dynamically grouping profiles, and deposits a unified struct explicitly defined as JSON onto an Azure storage blob.

### 31. [realworld_06_reconciliation_anti_join.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/realworld_06_reconciliation_anti_join.etlsql)
**Data Reconciliation Anti-Join**: Extracts live dimensions simultaneously from a Postgres node versus a secondary database (connected via `ODBC` — MySQL is not a natively supported connector type) to orchestrate a `LEFT IS NULL` anti-join mapping pipeline that immediately logs untracked disparities to a flat report.

### 32. [realworld_07_window_deduplication.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/realworld_07_window_deduplication.etlsql)
**Window Analytics Deduplication**: Ingests unstructured clickstream events formatting natively sequentially over a unified `ROW_NUMBER() OVER(PARTITION BY UserID)` logical ranking hierarchy to dynamically compress/delete old transactions before executing a compressed `PARQUET` write.

### 33. [realworld_08_aggregation_pivot.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/realworld_08_aggregation_pivot.etlsql)
**Complex Quarterly Pivot**: Orchestrates cross-aggregate logic employing matrixed `SUM(CASE WHEN...)` groupings to artificially pivot continuous sequential transactions into physical grid columns natively deposited into an internal corporate `EXCEL` document.

### 34. [realworld_09_directory_watcher.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/realworld_09_directory_watcher.etlsql)
**Event-Driven Daemon Orchestration**: Executes an infinite state machine via a continuous generic `WHILE` loop intelligently monitoring a pickup array waiting for incoming integration drops before archiving the artifact natively out of path and breaking successfully.

### 35. [realworld_10_docker_sync.etlsql](file:///c:/Users/chuck/scratch/ETL-SQL/samples/realworld_10_docker_sync.etlsql)
**Ephemeral Sandboxed Synchronization**: Instantiates temporary synchronized infrastructure utilizing simultaneous lightweight engine hooks triggering `mcr.mssql` environments and secondary `alpine.postgres` hubs, safely cloning configuration grids autonomously prior to terminating operations flawlessly.

---

*Refer to [User_Manual.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/User_Manual.md) for the pipeline mental model, [Cookbook.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Cookbook.md) for production recipes, [Reference/Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) for full syntax, and [Reference/Data_Connectors.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Data_Connectors.md) for connector options.*
