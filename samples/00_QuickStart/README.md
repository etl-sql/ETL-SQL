# QuickStart Samples

Welcome to ETL-SQL. These scripts are designed to get you up and running within 30 seconds.
All scripts in this directory are 100% self-contained and require no external databases or files.

## Running the QuickStart Scripts

From the repository root, run any script using the ETL-SQL CLI:

```bash
# 1. Hello World (minimal engine execution check)
dotnet run --project src/ETL-SQL.App -- run samples/00_QuickStart/sample_Hello.etlsql

# 2. Your First ETL Pipeline (Generate in-memory data -> Aggregate -> Output table)
dotnet run --project src/ETL-SQL.App -- run samples/00_QuickStart/first_etl_pipeline.etlsql

# 3. Your First Visual Dashboard (In-memory dataset -> KPI Card -> Bar Chart -> Page layout)
dotnet run --project src/ETL-SQL.App -- run samples/00_QuickStart/first_dashboard.rptsql
```

## What's in This Folder

| File | Type | What It Teaches |
| :--- | :--- | :--- |
| [`sample_Hello.etlsql`](sample_Hello.etlsql) | Engine Check | Minimal `PRINT` statement to verify that the CLI engine is functioning. |
| [`first_etl_pipeline.etlsql`](first_etl_pipeline.etlsql) | Core ETL | `GENERATE ROWS` for mock data, `#temp` tables, aggregations (`SUM`, `AVG`, `ROUND`), and query output. |
| [`first_dashboard.rptsql`](first_dashboard.rptsql) | Report-SQL | Report definitions, `CREATE VISUAL ... AS CARD`, `CREATE VISUAL ... AS BAR`, and `CREATE PAGE ... DASHBOARD` layout. |

## Next Steps

- **Data Ingestion & Export**: Explore [`../02_Data_Movement/`](../02_Data_Movement/) to read/write CSV, Parquet, and Avro files.
- **Database Connectors**: Explore [`../03_SQL_Engines/`](../03_SQL_Engines/) to connect to PostgreSQL, SQL Server, MySQL, SQLite, and Snowflake.
- **Visual Gallery**: Explore [`../visual_gallery/`](../visual_gallery/) to see all 39 supported visual chart types.
