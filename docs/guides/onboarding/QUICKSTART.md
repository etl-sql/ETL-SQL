# ETL-SQL 5-Minute Quickstart

Welcome to ETL-SQL, the script-first data orchestration and reporting engine. This guide walks you through verifying your installation and running your first pipeline in under five minutes with zero external dependencies.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## Prerequisites

- [.NET 8.0 SDK / Runtime](https://dotnet.microsoft.com/download) or higher
- The ETL-SQL binary or CLI tool installed

Verify your environment configuration:

```bash
etl-sql doctor
```

This verifies that your operating system, .NET runtime, directory permissions, and temp paths are properly configured.

---

## Step 1: Run Your First Pipeline

Create a file named `hello_pipeline.etlsql`:

```sql
-- hello_pipeline.etlsql
PRINT 'Initializing demo pipeline...';

-- 1. Connect to built-in in-memory mock database
CREATE CONNECTION demo AS MOCKDB();

-- 2. Extract into engine #temp workspace
SELECT Region, Amount
INTO #staged_orders
FROM demo.Orders;

-- 3. Validate with an inline assertion
ASSERT (SELECT COUNT(*) FROM #staged_orders) > 0, 'Orders table should contain rows';

-- 4. Transform in engine memory
SELECT 
    Region, 
    COUNT(*) AS OrderCount, 
    SUM(Amount) AS TotalRevenue
INTO #regional_summary
FROM #staged_orders
GROUP BY Region;

-- 5. Output results
SELECT * FROM #regional_summary ORDER BY TotalRevenue DESC;
```

Run the pipeline from your terminal:

```bash
etl-sql run hello_pipeline.etlsql
```

---

## Step 2: Pass Dynamic Parameters from the CLI

Add an `INPUT` parameter to filter dynamically:

```sql
DECLARE @minAmount DECIMAL INPUT = 100.00;

CREATE CONNECTION demo AS MOCKDB();

SELECT * 
FROM demo.Orders 
WHERE Amount >= @minAmount;
```

Run with parameter overrides:

```bash
etl-sql run hello_pipeline.etlsql --var @minAmount=250.00
```

---

## Step 3: Launch the Terminal User Interface (TUI)

If you prefer an interactive windowed terminal editor with syntax highlighting, F5 execution, and result grids:

```bash
etl-sql-tui
```

---

## Next Steps

- [Thinking in Pipelines](getting-started.md) — Understand the multi-context engine model.
- [Authoring Dashboards](../reporting/authoring-dashboards.md) — Build interactive visual reports.
- [Data Quality Column Rules](../data-quality/column-quality-rules.md) — Add `@expect` validation rules.
- [Data Connectors Reference](../../reference/connectors/README.md) — Connect to PostgreSQL, SQL Server, SFTP, and APIs.
