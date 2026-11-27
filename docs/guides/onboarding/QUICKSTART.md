# ETL-SQL 5-Minute Quickstart

Run a pipeline, add a data quality gate, and render a live dashboard in five minutes. Every step
uses the built-in `MOCKDB()` connector, so there are no databases, credentials, or network access to
set up.

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

---

## Before You Start

**VS Code (recommended):** install **ETL-SQL Tools** from the VS Code Marketplace and open any
folder. The extension bundles the engine, language server, and report renderer for your platform —
nothing else to install. If commands do not appear after installing, restart VS Code.

**Command line:** download the ETL-SQL CLI from the
[GitHub releases page](https://github.com/etl-sql/ETL-SQL/releases) and check your environment:

```bash
ETL-SQL doctor
```

---

## Step 1: Run Your First Pipeline (1 minute)

Create `hello_pipeline.etlsql` and press **F5**:

```sql
-- hello_pipeline.etlsql
PRINT 'Initializing demo pipeline...';

-- 1. Connect to the built-in in-memory mock database
CREATE CONNECTION demo AS MOCKDB();

-- 2. Extract into the engine's #temp workspace
SELECT Region, Total
INTO #staged_orders
FROM demo.Orders;

-- 3. Validate with an inline assertion
ASSERT (SELECT COUNT(*) FROM #staged_orders) > 0, 'Orders table should contain rows';

-- 4. Transform in engine memory
SELECT
    Region,
    COUNT(*)   AS OrderCount,
    SUM(Total) AS TotalRevenue
INTO #regional_summary
FROM #staged_orders
GROUP BY Region;

-- 5. Output results
SELECT Region, OrderCount, TotalRevenue
FROM #regional_summary
ORDER BY TotalRevenue DESC;
```

The results open in the **ETL-SQL Results** panel. Highlight any single statement and press **F5**
to run just that selection.

---

## Step 2: Add a Data Quality Gate (1 minute)

`EXPECT` rules check every row as it streams in. Replace step 2 of the script with:

```sql
SELECT
    Region /* @d: Sales territory; @owner: rev_ops */
        EXPECT NOT NULL ON FAILURE THROW,
    Total  /* @d: Order total; @unit: USD */
        EXPECT >= 0     ON FAILURE THROW
INTO #staged_orders
FROM demo.Orders;
```

Change `>= 0` to `>= 100` and run again — the pipeline stops and reports the failing rule. Swap
`THROW` for `WARN` to log and continue, or `QUARANTINE` to route bad rows to a side table. The
`@d:` and `@owner:` tags travel with the column into lineage; see what the engine captured with:

```sql
SELECT target_column, source_tables, source_columns
FROM eng.lineage
WHERE target_table = '#regional_summary';
```

---

## Step 3: Turn It into a Dashboard (2 minutes)

Create `sales.rptsql`, then press **F1** → **ETL-SQL: Preview Report**. The dashboard renders beside
your code and refreshes every time you save.

```sql
SET REPORT TITLE = 'Mock Sales Performance';

CREATE CONNECTION demo AS MOCKDB();

SELECT Region, COUNT(*) AS OrderCount, SUM(Total) AS Revenue
INTO #sales_summary
FROM demo.Orders
GROUP BY Region;

CREATE VISUAL RegionalSalesChart AS BAR (
    SOURCE   = #sales_summary,
    MAPPINGS (X = Region, Y = Revenue),
    STYLE    (HEIGHT = '350px')
);

CREATE VISUAL OrdersKpi AS CARD (
    SOURCE   = (SELECT SUM(OrderCount) AS TotalOrders FROM #sales_summary),
    MAPPINGS (VALUE = TotalOrders, LABEL = 'Total Orders')
);

CREATE VISUAL RevenueKpi AS CARD (
    SOURCE   = (SELECT SUM(Revenue) AS TotalRevenue FROM #sales_summary),
    MAPPINGS (VALUE = TotalRevenue, LABEL = 'Total Revenue'),
    OPTIONS  (FORMAT = 'C0')
);

CREATE PAGE SalesDashboard AS DASHBOARD (
    TITLE     = 'Executive Overview',
    STRUCTURE = 'A A / B C',
    MAP ('A' = RegionalSalesChart, 'B' = OrdersKpi, 'C' = RevenueKpi)
);
```

`STRUCTURE` is the layout: each letter is a slot, `/` starts a new row, and a repeated letter
stretches a visual across columns.

---

## Step 4: Pass Parameters (1 minute)

Mark a variable `INPUT` and callers can override it:

```sql
DECLARE @minTotal DECIMAL INPUT = 100.00;

CREATE CONNECTION demo AS MOCKDB();

SELECT SaleID, Region, Total
FROM demo.Orders
WHERE Total >= @minTotal
ORDER BY Total DESC;
```

From the command line:

```bash
ETL-SQL run hello_pipeline.etlsql --var @minTotal=250.00
```

In a report, the same `INPUT` variable can be bound to a slicer or date picker so viewers change it
from the dashboard.

---

## What `MOCKDB` Contains

`MOCKDB()` seeds realistic sample tables, including `Orders` (`SaleID`, `OrderDate`, `CustomerID`,
`ProductID`, `Quantity`, `UnitPrice`, `Total`, `Region`), `Products`, `Users`, `Employee`, and
calendar tables such as `DimDate`. Swap `MOCKDB()` for a real connection — `MSSQL`, `POSTGRES`,
`SNOWFLAKE`, a CSV file — and the rest of the script stays the same.

---

## Next Steps

- [Report Cookbook](../../cookbooks/report/README.md) — 15 complete, runnable dashboards: maps, Sankey, waterfall, forecasting, themed layouts.
- [ETL Cookbook](../../cookbooks/etl/README.md) — production patterns: incremental loads, SCD Type 2, dead-letter queues, fuzzy matching.
- [Thinking in Pipelines](getting-started.md) — the multi-context engine model.
- [Data Quality Column Rules](../data-quality/column-quality-rules.md) — every `EXPECT` rule and failure action.
- [Data Connectors Reference](../../reference/connectors/README.md) — connect to PostgreSQL, SQL Server, SFTP, APIs, and more.
