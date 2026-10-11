# 04_Orchestration: Jobs, Schedules, Lineage, and Modular Workflows

This directory covers orchestration features: composing scripts into modular pipelines, managing scheduled jobs, tagging execution blocks, and capturing OpenLineage data movement.

## Samples Overview

| File | Feature | Description |
| :--- | :--- | :--- |
| [`Job_Scheduling.etlsql`](Job_Scheduling.etlsql) | Schedules & Jobs | Creating cron schedules and linking them to scripts (`CREATE SCHEDULE`, `CREATE JOB`, `ALTER JOB ... ADD SCHEDULE`). |
| [`Modular_Scripts.etlsql`](Modular_Scripts.etlsql) | Sub-Scripts | Invoking external `.etlsql` files with arguments via `RUN SCRIPT`. |
| [`Data_Lineage.etlsql`](Data_Lineage.etlsql) | Data Lineage | Querying `eng.lineage` to trace column-level data provenance and target writes. |
| [`Lineage_Tag_Import.etlsql`](Lineage_Tag_Import.etlsql) | Execution Tags | Applying `@owner`, `@classification`, and `@quality` metadata to execution blocks. |
| [`Batch_Processing.etlsql`](Batch_Processing.etlsql) | Batching | Streaming queries in managed batch sizes to control memory footprint. |
| [`Multi_System_Integration.etlsql`](Multi_System_Integration.etlsql) | Cross-Source Flow | Coordinating extract from one database, transformation in engine, and load to another. |

## Running These Samples

```bash
dotnet run --project src/ETL-SQL.App -- run samples/04_Orchestration/Modular_Scripts.etlsql
dotnet run --project src/ETL-SQL.App -- run samples/04_Orchestration/Job_Scheduling.etlsql
```
