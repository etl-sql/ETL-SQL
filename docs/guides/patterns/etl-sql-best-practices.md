# ETL-SQL Pipeline & Report-SQL Best Practices Guide

This guide outlines the best practices for script authors and operations teams when designing resilient, secure, and performant pipelines (`.etlsql`) and dashboard reports (`.rptsql`) in ETL-SQL.

> [!TIP]
> **Looking for focused guides?** See our dedicated guide sections:
> - **Pipelines**: [Staged vs. Streaming Ingestion](../pipelines/staged-vs-streaming-ingestion.md) · [Script Resilience & Checkpoints](../pipelines/script-resilience-and-checkpoints.md) · [Error Handling & Retries](../pipelines/error-handling-and-retries.md)
> - **Reporting**: [Authoring Dashboards](../reporting/authoring-dashboards.md) · [Report Parameters & Filters](../reporting/report-parameters-and-filters.md) · [Cascading Slicers](../reporting/cascading-slicers.md)

---

## 1. The Core Mental Model

ETL-SQL is an **orchestration conductor**, not a traditional database. Data flows *through* the engine.

```
┌────────────────────────────────────────────────────────┐
│               ETL-SQL Engine (In-Memory)               │
│  - Holds @variables and #temp tables                   │
│  - Executes data validation and lineage tagging        │
│  - Coordinates cross-connection reads and writes       │
└─────────────┬──────────────────────────────┬───────────┘
              │                              │
     Database Source (e.g. Postgres)   Target File (e.g. CSV/Excel)
```

### Ingestion Patterns: Staged vs. Direct Streaming

ETL-SQL supports two primary ingestion patterns. Selecting the correct pattern depends on your dataset size, connection constraints, and recovery requirements.

| Ingestion Pattern | How it Works | Pros | Cons | Best Used When... |
| :--- | :--- | :--- | :--- | :--- |
| **Staged Ingestion** (Scenario A) | Extract to `#temp` first, then insert/merge into destination in a separate statement. | • Isolates the source system (minimized connection hold time).<br>• Fully supports checkpoint-resume recovery.<br>• Allows multi-pass updates and indexes on staged data. | • I/O double-tax (data is written locally, then read back).<br>• Higher disk space usage. | • Source database is busy and connections must be closed quickly.<br>• Loading takes a long time, and you want rollback/resume capabilities. |
| **Direct Streaming** (Scenario B) | Stream directly from source to destination in a single statement (e.g., `INSERT INTO dest SELECT FROM src`). | • High performance (zero local write/read I/O overhead).<br>• Low local disk/memory footprint. | • Both source and target connections must remain open concurrently.<br>• No checkpoint-resume possible; failures require full restarts. | • Working with very large datasets where I/O double-tax is prohibitive.<br>• Source and target databases can handle prolonged concurrent connections. |

*Note: Lineage tracking, data-quality rules (`@expect`), and inline transformations (like regex or `HASHBYTES`) are fully supported in **both** patterns. The engine processes them on each 10k batch as it streams.*

---

## 2. ETL Pipeline (`.etlsql`) Best Practices

### A. Zero-Trust Security & Secrets
* **Never hardcode passwords or API keys.** Reference them using the `SECRET:name` format. The engine automatically integrates with your configured secret provider and redacts these values from all logs:
  ```sql
  CREATE CONNECTION SrcDb AS POSTGRES(
      HOST='pg.prod.local', 
      DATABASE='sales', 
      USER='etl_user', 
      PASSWORD='SECRET:prod_db_password'
  );
  ```
* **Use destructive guards**: Always validate destructive operations (`DELETE`, `TRUNCATE`, `MERGE`) by wrapping them in a `SET WHAT_IF ON` block first, or within a transaction:
  ```sql
  SET WHAT_IF ON;
  DELETE FROM dest.logs WHERE log_date < '2026-01-01';
  SET WHAT_IF OFF;
  ```

### B. Dialect Awareness
* When querying a **remote database** directly, use its native SQL features.
* When querying a **file connector** or a **#temp table**, use the ETL-SQL engine standard library functions (e.g., `COALESCE`, `GETDATE()`) rather than database-specific ones (like `ISNULL` or `NOW()`).

### C. Data Quality (DQ) Gates
* Enforce rules on incoming columns using `@expect` or `@fail` attributes.
* Use `ON FAILURE` routing to prevent dirty data from corrupting your target table while allowing the script to finish:
  ```sql
  SELECT 
      id,
      email @expect(LIKE '%@%'),
      amount @expect(>= 0)
  INTO #clean_orders
  FROM SrcDb.orders
  ON FAILURE ROUTE TO #quarantine_orders;
  ```

### D. Checkpoint & Session Lifecycle
* Use top-level labels as checkpoints for long-running jobs so that they can be resumed with `--resume` on a failure.
* Always clean up successful runs immediately by adding `CLEAR SESSION` in your `cleanup:` step to prevent temporary files from taking up disk space.

---

## 3. Template: ETL Pipeline Success Boilerplate

Copy and use this template when writing a new pipeline script (`.etlsql`):

```sql
-- =========================================================================
-- TITLE: EDW Ingestion Template
-- DESCRIPTION: Standard Extract-Stage-Validate-Load pipeline with safety gates
-- =========================================================================

-- 1. Initialize Connections & Variables
Init:
  CREATE CONNECTION src AS POSTGRES(HOST='pg.prod.local', DATABASE='sales', USER='etl_user', PASSWORD='SECRET:src_password');
  CREATE CONNECTION dest AS MSSQL(SERVER='sql.prod.local', DATABASE='edw', TRUSTED_CONNECTION=TRUE);
  
  DECLARE @JobStartTime DATETIME = GETDATE();
  DECLARE @BatchId INT = 1045;

-- 2. Extract Stage
Extract:
  -- Stage remote data in memory (applies lineage automatically)
  SELECT 
      order_id,
      customer_id,
      email,
      order_total,
      order_date
  INTO #staged_orders
  FROM src.orders
  WHERE order_date >= DATEADD(day, -1, @JobStartTime);

-- 3. Data Quality Gate (Transform & Cleanse)
Transform:
  -- Filter and validate input formats; route failures to quarantine
  SELECT 
      order_id,
      customer_id,
      email @expect(LIKE '%_@_%._%') @fail(action = WARN),
      order_total @expect(> 0) @fail(action = QUARANTINE),
      order_date
  INTO #validated_orders
  FROM #staged_orders
  ON FAILURE ROUTE TO #quarantine_orders;

-- 4. Load (Merge into Target)
Load:
  BEGIN TRANSACTION;
  BEGIN TRY
      -- Merge cleaned rows into target EDW table
      MERGE INTO dest.dbo.SalesOrders AS T
      USING #validated_orders AS S ON T.OrderID = S.order_id
      WHEN MATCHED THEN
          UPDATE SET T.OrderTotal = S.order_total, T.LastModified = GETDATE()
      WHEN NOT MATCHED THEN
          INSERT (OrderID, CustomerID, Email, OrderTotal, OrderDate, BatchID)
          VALUES (S.order_id, S.customer_id, S.email, S.order_total, S.order_date, @BatchId);

      COMMIT TRANSACTION;
  END TRY
  BEGIN CATCH
      ROLLBACK TRANSACTION;
      PRINT 'Transaction rolled back due to error: ' + ERROR_MESSAGE();
      THROW;
  END CATCH;

-- 5. Audit & Reporting
Audit:
  -- Record job execution metrics
  DECLARE @QuarantineCount INT;
  SELECT @QuarantineCount = COUNT(*) FROM #quarantine_orders;
  
  IF @QuarantineCount > 0
  BEGIN
      PRINT 'Warning: ' + CAST(@QuarantineCount AS VARCHAR(10)) + ' rows routed to quarantine.';
  END

-- 6. Cleanup Resources
Cleanup:
  PRINT 'Pipeline successful. Clearing temporary sessions.';
  CLEAR SESSION;
```

---

## 4. Report-SQL (`.rptsql`) Best Practices

`.rptsql` scripts generate interactive HTML reports and dashboards. They combine standard ETL-SQL statements (for data preparation) with visual definitions.

### A. File Structure Structure
* **Data Prep First**: Put all connections, variables, temp tables, and datasets at the top.
* **Layout & Visuals Last**: Define `CREATE VISUAL`, `CREATE DATASET`, `CREATE PAGE`, `CREATE CONTAINER`, and `CREATE NAVIGATION` at the very end of the script.

### B. Interactive Filter Bindings
* **Mapped Columns**: If a control has a `SOURCE` clause (like a `SLICER` or `MULTISELECT` populated by a query), bind parameter variables to the column name:
  ```sql
  ACTIONS (ON_CHANGE = SET_PARAMETER(@selectedRegion, region_name))
  ```
* **Literal `value`**: If a control has no query source (like a `DATEPICKER`, `SLIDER`, or `TEXTBOX`), bind the parameter to the literal keyword `value`:
  ```sql
  ACTIONS (ON_CHANGE = SET_PARAMETER(@startDate, value))
  ```

### C. Styling Rules
* Apply broad styles (like `THEME = dark`) at the `PAGE` level. Use visual-level `STYLE` statements only to override specific charts.
* Use `STRUCTURE` strings to design a clean, responsive CSS Grid. Quote the area grid template precisely:
  ```sql
  STRUCTURE = 'A A / B C' -- Area A spans full width on row 1; B and C share row 2
  ```

---

## 5. Template: Report-SQL Success Boilerplate

Copy and use this template when creating dashboard reports (`.rptsql`):

```sql
-- =========================================================================
-- TITLE: Executive Sales Dashboard
-- DESCRIPTION: Monthly revenue trends, regional slices, and quarantine metrics
-- =========================================================================

-- 1. Gather & Process Data
CREATE CONNECTION dw AS MSSQL(SERVER='sql.prod.local', DATABASE='edw', TRUSTED_CONNECTION=TRUE);

DECLARE @selectedRegion VARCHAR(50) = 'All';

-- Prepare data for Slicer dropdown
SELECT DISTINCT RegionName INTO #regions FROM dw.dbo.Regions;
INSERT INTO #regions (RegionName) VALUES ('All');

-- Prepare chart data based on selected parameter
SELECT 
    SalesMonth,
    SUM(Revenue) AS TotalRevenue,
    SUM(OrderCount) AS TotalOrders
INTO #revenue_data
FROM dw.dbo.MonthlySalesSummary
WHERE @selectedRegion = 'All' OR RegionName = @selectedRegion
GROUP BY SalesMonth;

-- 2. Define Visuals
CREATE VISUAL RegionalSlicer AS SLICER (
    SOURCE = #regions,
    MAPPINGS (VALUE = RegionName),
    DEFAULT = 'All',
    TITLE = 'Select Region',
    ACTIONS (
        ON_CHANGE = SET_PARAMETER(@selectedRegion, RegionName)
    )
);

CREATE VISUAL RevenueChart AS LINE (
    SOURCE = #revenue_data,
    MAPPINGS (X = SalesMonth, Y = TotalRevenue),
    TITLE = 'Revenue Trend ($)'
)
STYLE (
    COLOR = '#2563eb'
);

CREATE VISUAL OrdersChart AS BAR (
    SOURCE = #revenue_data,
    MAPPINGS (X = SalesMonth, Y = TotalOrders),
    TITLE = 'Monthly Order Count'
)
STYLE (
    COLOR = '#10b981'
);

-- 3. Assemble Dashboard Page
SET REPORT TITLE = 'Executive Sales Overview';
SET REPORT DESCRIPTION = 'Real-time sales performance metrics filtered by region.';

CREATE PAGE SalesOverview AS DASHBOARD (
    STRUCTURE = 'S S / A B',
    MAP (
        'S' = RegionalSlicer,
        'A' = RevenueChart,
        'B' = OrdersChart
    )
)
STYLE (
    THEME = dark,
    FONT = 'Outfit'
);
```
