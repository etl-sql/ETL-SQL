/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Stable host routes and script templates consumed by the Studio composition layer.
 */

export { STUDIO_ROUTES, STUDIO_CATALOG_ROUTES, STUDIO_WORKSPACE_ROUTES } from './studio-routes.generated.js';

export const STUDIO_STARTER_SCRIPTS = Object.freeze({
    report: `-- Sample dashboard. MOCKDB is a built-in in-memory connector, so this needs no database.
-- Replace the connection below with your own when you are ready.
SET REPORT TITLE = 'Sample Dashboard';

CREATE CONNECTION demo AS MOCKDB();

-- Stage what the visuals read. A #temp table is computed once per run and shared by
-- every visual below, so the same numbers cannot disagree between two tiles.
SELECT Region, COUNT(*) AS Orders, SUM(Total) AS Revenue
INTO #revenue_by_region
FROM demo.Orders
GROUP BY Region;

SELECT SUM(Total) AS Revenue
INTO #revenue_total
FROM demo.Orders;

-- A KPI card prints one number, so its source is a query that returns one row.
CREATE VISUAL total_revenue AS CARD (
    TITLE = 'Total revenue',
    SOURCE = #revenue_total,
    MAPPINGS (VALUE = Revenue)
);

CREATE VISUAL revenue_by_region AS BAR (
    TITLE = 'Revenue by region',
    SOURCE = #revenue_by_region,
    MAPPINGS (X = Region, Y = Revenue),
    OPTIONS (LEGEND = OFF)
);

CREATE VISUAL region_detail AS TABLE (
    TITLE = 'Regions',
    SOURCE = #revenue_by_region
);

-- The page places the visuals. Each letter is a grid cell; a letter repeated across
-- cells is one visual spanning them, and '/' starts the next row.
CREATE PAGE [Sample Dashboard] AS DASHBOARD (
    LAYOUT (
        STRUCTURE = 'K C C / T T T',
        MAP (
            'K' = total_revenue,
            'C' = revenue_by_region,
            'T' = region_detail
        )
    )
);
`,
    etl: `-- Sample ETL Pipeline Exercise
--
-- This exercise introduces the core ETL-SQL workflow:
-- 1. Declare an in-memory sample connection (zero-trust, no external database needed).
-- 2. Stage raw source data into an engine #temp table.
-- 3. Inspect intermediate staged rows.
-- 4. Transform and aggregate staged data in engine context.
-- 5. Inspect aggregated summary rows.
-- 6. Enforce a data quality invariant with ASSERT (includes a deliberate failure and repair).
-- 7. Clean up temporary engine tables.

-- Step 1. Declare Connection
-- MOCKDB is a built-in in-memory connector with realistic sample data.
-- No external database server, network access, or credentials are required.
CREATE CONNECTION demo AS MOCKDB();

-- Step 2. Extract & Stage
-- Stage source rows into an engine #temp table (#recent_sales).
-- Staging in engine context isolates data transformations and keeps operations portable.
SELECT SaleID, OrderDate, Region, Total
INTO #recent_sales
FROM demo.Orders
WHERE Total > 100;

-- Step 3. Intermediate Inspection (Staged Rows)
-- Inspect the staged data. Studio displays these rows in the results output grid.
SELECT * FROM #recent_sales;

-- Step 4. Transform & Summarize
-- Aggregate staged rows in engine context to compute order counts and revenue by region.
SELECT Region, COUNT(*) AS Orders, SUM(Total) AS Revenue
INTO #revenue_by_region
FROM #recent_sales
GROUP BY Region;

-- Step 5. Intermediate Inspection (Summary Rows)
-- Inspect the aggregated regional results in the results grid.
SELECT * FROM #revenue_by_region;

-- Step 6. Data Quality Gate (Deliberate Validation Failure & Repair)
-- Use ASSERT to enforce data contracts and invariants before downstream movement.
-- If an assertion condition evaluates to false, ETL-SQL immediately halts execution.
--
-- EXERCISE:
-- On your first run, this assertion deliberately fails because the sample batch contains
-- fewer than 500 orders (~200 orders staged). Observe the failure in the output panel.
--
-- TO REPAIR:
-- Change 500 to 50 in the ASSERT statement below (or uncomment the repair line), then Run again.
ASSERT (SELECT COUNT(*) FROM #recent_sales) >= 500,
    'Data quality check failed: Expected at least 500 orders, but batch volume was lower! (Deliberate exercise failure: repair by changing 500 to 50)';

-- REPAIRED ASSERTION:
-- ASSERT (SELECT COUNT(*) FROM #recent_sales) >= 50,
--     'Data quality check failed: Staged order count is below expected minimum.';

-- Step 7. Cleanup
-- Release temporary engine memory by dropping #temp tables when processing completes.
DROP TABLE #revenue_by_region;
DROP TABLE #recent_sales;
`,
    sql: `-- MOCKDB is a built-in in-memory connector, so this needs no database.
CREATE CONNECTION demo AS MOCKDB();

SELECT Region, COUNT(*) AS Orders, SUM(Total) AS Revenue
FROM demo.Orders
GROUP BY Region;
`,
});

export const REPORT_WORKFLOW_TEMPLATES = Object.freeze({
    dashboard: `-- Dashboard canvas: add data, then arrange charts, KPIs, tables, and slicers.
CREATE PAGE [Dashboard] AS DASHBOARD ( LAYOUT ( STRUCTURE = '.' ) );
`,
    paginated: `-- Paginated report: build detail bands for a fixed physical page.
CREATE PAGE [Paginated Report] AS PAGINATED (
  LAYOUT ( STRUCTURE = '.' ),
  PRINT_LAYOUT (
    PAGE_SIZE = 'Letter',
    ORIENTATION = 'PORTRAIT',
    MARGINS = (0.75, 0.75, 0.75, 0.75),
    UNITS = 'in',
    OVERFLOW = 'SPLIT'
  )
);
`,
});
