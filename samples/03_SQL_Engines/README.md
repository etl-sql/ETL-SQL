# 03_SQL_Engines: Connectors, Dialects, and Pushdown

This directory demonstrates ETL-SQL's heterogeneous database connectors, intelligent query pushdown, and dynamic container lifecycle management (`USE DOCKER`).

## Samples Overview

| Category | Scripts | Description |
| :--- | :--- | :--- |
| **Relational Databases** | [`Sqlite_Operations.etlsql`](Sqlite_Operations.etlsql)<br>[`mysql_operations.etlsql`](mysql_operations.etlsql)<br>[`snowflake_warehouse.etlsql`](snowflake_warehouse.etlsql)<br>[`Oracle_Specifics.etlsql`](Oracle_Specifics.etlsql) | Declaring native connections, executing pass-through dialect queries, and extracting to `#temp` tables. |
| **API & Semi-Structured** | [`rest_api_ingestion.etlsql`](rest_api_ingestion.etlsql)<br>[`excel_workbook_ingest.etlsql`](excel_workbook_ingest.etlsql)<br>[`webhook_alerting.etlsql`](webhook_alerting.etlsql) | Ingesting REST API responses, reading Excel workbooks, and triggering webhooks. |
| **Query Pushdown** | [`simple_pushdown.etlsql`](simple_pushdown.etlsql)<br>[`Pushdown_Having.etlsql`](Pushdown_Having.etlsql)<br>[`Pushdown_Variables.etlsql`](Pushdown_Variables.etlsql) | Pushing `WHERE`, `HAVING`, and parameter filters down to the source database to minimize data transfer. |
| **Ephemeral Docker** | [`Docker_Aliases.etlsql`](Docker_Aliases.etlsql)<br>[`Docker_Orchestration.etlsql`](Docker_Orchestration.etlsql) | Using `USE DOCKER(...) AS alias;` to spin up live SQL Server or PostgreSQL test databases on demand. |

## Running These Samples

```bash
# SQLite (zero setup required):
dotnet run --project src/ETL-SQL.App -- run samples/03_SQL_Engines/Sqlite_Operations.etlsql

# Excel Ingestion:
dotnet run --project src/ETL-SQL.App -- run samples/03_SQL_Engines/excel_workbook_ingest.etlsql
```
