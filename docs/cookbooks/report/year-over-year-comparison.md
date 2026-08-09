# Year-over-Year Comparison

**Pattern**: Stack current year and prior year on the same chart. A donut shows the full-year share breakdown by product alongside.

**Demonstrates**: Multi-series `LINE` with `SERIES` column, `COLORS`, `DONUT`, `LEGEND_POSITION`, `UNION ALL` to merge two datasets.

```sql
SET REPORT TITLE = 'Year-over-Year Performance';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT 'Jan' AS month, 'Widget A' AS product, 38000 AS revenue, 2024 AS yr INTO #all_sales
UNION ALL SELECT 'Feb', 'Widget A', 41000, 2024
UNION ALL SELECT 'Mar', 'Widget A', 45000, 2024
UNION ALL SELECT 'Apr', 'Widget B', 39000, 2024
UNION ALL SELECT 'May', 'Widget B', 48000, 2024
UNION ALL SELECT 'Jun', 'Widget A', 52000, 2024
UNION ALL SELECT 'Jan', 'Widget A', 42000, 2025
UNION ALL SELECT 'Feb', 'Widget B', 45000, 2025
UNION ALL SELECT 'Mar', 'Widget A', 51000, 2025
UNION ALL SELECT 'Apr', 'Widget A', 47000, 2025
UNION ALL SELECT 'May', 'Widget B', 55000, 2025
UNION ALL SELECT 'Jun', 'Widget B', 62000, 2025;

-- ── YoY line chart ────────────────────────────────────────────────────────
-- Stack both years as separate series via a label column
CREATE VISUAL YoyLine AS LINE (
  SOURCE = (
    SELECT month,
           SUM(revenue)              AS revenue,
           CAST(yr AS VARCHAR(4))    AS year_label
    FROM #all_sales
    GROUP BY month, yr
    ORDER BY yr, month
  ),
  TITLE    = 'Revenue: 2024 vs 2025',
  MAPPINGS (X = month, Y = revenue, SERIES = year_label),
  OPTIONS  (
    SMOOTH = ON,
    LEGEND_POSITION = TOP,
    COLORS (
      '2024' = '#6c757d',
      '2025' = '#0d6efd'
    ),
    X_AXIS (LABEL = 'Month'),
    Y_AXIS (LABEL = 'Revenue ($)', MIN = 0)
  )
);

-- ── Revenue share by product (current year only) ──────────────────────────
CREATE VISUAL ProductShare AS DONUT (
  SOURCE   = (SELECT product, SUM(revenue) AS revenue
              FROM #all_sales WHERE yr = 2025
              GROUP BY product),
  TITLE    = '2025 Revenue by Product',
  MAPPINGS (LABEL = product, VALUE = revenue)
);

-- ── Change summary table ───────────────────────────────────────────────────
CREATE VISUAL YoyTable AS TABLE (
  SOURCE = (
    SELECT cy.month,
           cy.revenue           AS this_year,
           py.revenue           AS last_year,
           cy.revenue - py.revenue AS change
    FROM (SELECT month, SUM(revenue) AS revenue FROM #all_sales WHERE yr = 2025 GROUP BY month) cy
    JOIN (SELECT month, SUM(revenue) AS revenue FROM #all_sales WHERE yr = 2024 GROUP BY month) py
      ON cy.month = py.month
    ORDER BY cy.month
  ),
  FORMATTING (
    change >= 0 THEN '#d4edda',
    change < 0  THEN '#f8d7da'
  )
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE PAGE YoY AS DASHBOARD (
  STRUCTURE = 'A A B / C C C',
  MAP (
    'A' = YoyLine,
    'B' = ProductShare,
    'C' = YoyTable
  )
);
```
