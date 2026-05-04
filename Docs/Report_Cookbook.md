# Report-SQL Cookbook

A collection of copy-paste-ready dashboard recipes for ETL-SQL. Every example is **self-contained** — inline data is included so you can run it immediately and adapt it to your real source.

---

## Contents

1. [Executive KPI Dashboard](#1-executive-kpi-dashboard)
2. [Sales Trend with Forecasting](#2-sales-trend-with-forecasting)
3. [Year-over-Year Comparison](#3-year-over-year-comparison)
4. [Master-Detail Drill-Down](#4-master-detail-drill-down)
5. [Cross-Page Filtering with Navigation](#5-cross-page-filtering-with-navigation)
6. [Inventory Heatmap & Low-Stock Alerts](#6-inventory-heatmap--low-stock-alerts)
7. [Financial Waterfall, Funnel & Gauge](#7-financial-waterfall-funnel--gauge)
8. [Combo Chart: Revenue + Volume](#8-combo-chart-revenue--volume)
9. [Multi-Select + Search Filter Table](#9-multi-select--search-filter-table)
10. [Themed Dashboard with CREATE STYLE](#10-themed-dashboard-with-create-style)
11. [Choropleth Map Charts](#11-choropleth-map-charts)

---

## 1. Executive KPI Dashboard

**Pattern**: KPI cards, regional bar chart, slicer-filtered detail table. The go-to starting point for any executive summary report.

**Demonstrates**: `CARD`, `BAR`, `TABLE`, `SLICER`, `WITH PARAMETERS`, `CREATE DATASET`, `FORMAT`, `FORMATTING`, conditional formatting, multi-slot layout.

```sql
SET REPORT TITLE       = 'Executive Sales Dashboard';
SET REPORT DESCRIPTION = 'Top-level KPIs with regional breakdown and filterable order detail.';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT 'Jan' AS month, 'North' AS region, 'Widget A' AS product, 120 AS units, 14400 AS revenue INTO #raw
UNION ALL SELECT 'Jan', 'South', 'Widget A', 95,  11400
UNION ALL SELECT 'Jan', 'East',  'Widget B', 60,   9000
UNION ALL SELECT 'Feb', 'North', 'Widget A', 140, 16800
UNION ALL SELECT 'Feb', 'South', 'Widget B', 110, 16500
UNION ALL SELECT 'Feb', 'East',  'Widget A', 75,   9000
UNION ALL SELECT 'Mar', 'North', 'Widget B', 160, 24000
UNION ALL SELECT 'Mar', 'South', 'Widget A', 130, 15600
UNION ALL SELECT 'Mar', 'East',  'Widget B', 90,  13500
UNION ALL SELECT 'Apr', 'North', 'Widget A', 180, 21600
UNION ALL SELECT 'Apr', 'South', 'Widget B', 145, 21750
UNION ALL SELECT 'Apr', 'East',  'Widget A', 105, 12600;

-- Shared pre-aggregated dataset (swap FLATFILE source here when using real data)
CREATE DATASET &sales
  REFRESH EVERY '1h'
  COMPRESS = ON
  AS (SELECT month, region, product,
             SUM(units)   AS units,
             SUM(revenue) AS revenue
      FROM #raw
      GROUP BY month, region, product);

-- ── Filter control ────────────────────────────────────────────────────────
CREATE VISUAL RegionFilter AS SLICER (
  SOURCE   = (SELECT DISTINCT region FROM &sales ORDER BY region),
  MAPPINGS (VALUE = region),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, region)),
  DEFAULT  = 'All'
);

-- ── KPI cards ─────────────────────────────────────────────────────────────
CREATE VISUAL TotalRevenue AS CARD (
  SOURCE   = (SELECT SUM(revenue) AS val FROM &sales
              WHERE @region = 'All' OR region = @region),
  TITLE    = 'Total Revenue',
  MAPPINGS (VALUE = val),
  OPTIONS  (FORMAT = 'C0')
);

CREATE VISUAL TotalUnits AS CARD (
  SOURCE   = (SELECT SUM(units) AS val FROM &sales
              WHERE @region = 'All' OR region = @region),
  TITLE    = 'Units Sold',
  MAPPINGS (VALUE = val),
  OPTIONS  (FORMAT = 'N0')
);

CREATE VISUAL AvgRevenue AS CARD (
  SOURCE   = (SELECT AVG(revenue) AS val FROM &sales
              WHERE @region = 'All' OR region = @region),
  TITLE    = 'Avg Monthly Revenue',
  MAPPINGS (VALUE = val),
  OPTIONS  (FORMAT = 'C0')
);

-- ── Chart ─────────────────────────────────────────────────────────────────
CREATE VISUAL RevenueByRegion AS BAR (
  SOURCE   = (SELECT region, SUM(revenue) AS revenue FROM &sales
              WHERE @region = 'All' OR region = @region
              GROUP BY region
              ORDER BY revenue DESC),
  TITLE    = 'Revenue by Region',
  MAPPINGS (X = region, Y = revenue),
  OPTIONS  (
    X_AXIS (LABEL = 'Region'),
    Y_AXIS (LABEL = 'Revenue ($)', MIN = 0)
  )
);

-- ── Detail table with conditional formatting ──────────────────────────────
CREATE VISUAL SalesDetail AS TABLE (
  SOURCE = (SELECT month, region, product, units, revenue FROM &sales
            WHERE @region = 'All' OR region = @region
            ORDER BY month, region),
  FORMATTING (
    revenue >= 20000 THEN '#d4edda',
    revenue < 10000  THEN '#f8d7da'
  )
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE CONTAINER KpiRow AS BOX (
  VISUALS (TotalRevenue, TotalUnits, AvgRevenue)
);

CREATE PAGE Main AS LAYOUT (
  STRUCTURE = 'A A B / C C C / D D D',
  MAP (
    'A' = KpiRow,
    'B' = RegionFilter,
    'C' = RevenueByRegion,
    'D' = SalesDetail
  )
)
WITH PARAMETERS (@region = 'All');
```

---

## 2. Sales Trend with Forecasting

**Pattern**: A line chart over time with goal line, rolling average, and linear trend overlaid. Add a date-range picker to narrow the window.

**Demonstrates**: `LINE`, `OVERLAYS`, `GOAL`, `AVERAGE`, `MOVING_AVG`, `LINEAR`, `SMOOTH`, `DATEPICKER`, `WITH PARAMETERS`, typed date parameters.

```sql
SET REPORT TITLE = 'Monthly Sales Trend & Forecast';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT '2025-01-01' AS sale_date, 42000 AS revenue INTO #monthly
UNION ALL SELECT '2025-02-01',  38000
UNION ALL SELECT '2025-03-01',  51000
UNION ALL SELECT '2025-04-01',  47000
UNION ALL SELECT '2025-05-01',  55000
UNION ALL SELECT '2025-06-01',  62000
UNION ALL SELECT '2025-07-01',  58000
UNION ALL SELECT '2025-08-01',  71000
UNION ALL SELECT '2025-09-01',  66000
UNION ALL SELECT '2025-10-01',  74000
UNION ALL SELECT '2025-11-01',  80000
UNION ALL SELECT '2025-12-01',  88000;

-- ── Date filter controls ──────────────────────────────────────────────────
CREATE VISUAL StartPicker AS DATEPICKER (
  TITLE   = 'From',
  ACTIONS (ON_CHANGE = SET_PARAMETER(@start, value))
);

CREATE VISUAL EndPicker AS DATEPICKER (
  TITLE   = 'To',
  ACTIONS (ON_CHANGE = SET_PARAMETER(@end, value))
);

-- ── Trend chart with overlays ─────────────────────────────────────────────
CREATE VISUAL RevenueTrend AS LINE (
  SOURCE   = (SELECT sale_date AS month, revenue
              FROM #monthly
              WHERE sale_date >= @start AND sale_date <= @end
              ORDER BY sale_date),
  TITLE    = 'Monthly Revenue with Trend',
  MAPPINGS (X = month, Y = revenue),
  OPTIONS  (
    SMOOTH = ON,
    X_AXIS (LABEL = 'Month'),
    Y_AXIS (LABEL = 'Revenue ($)', MIN = 0),
    LEGEND_POSITION = BOTTOM
  ),
  OVERLAYS (
    GOAL(75000)   AS DASHED WITH (COLOR = '#e74c3c', LABEL = 'Annual Target / Month'),
    AVERAGE       AS DOTTED WITH (COLOR = '#3498db', LABEL = 'Period Average'),
    MOVING_AVG(3) AS SOLID  WITH (COLOR = '#2ecc71', LABEL = '3-Month Moving Avg'),
    LINEAR        AS DASHED WITH (COLOR = '#9b59b6', LABEL = 'Linear Trend')
  )
);

-- ── Summary card ──────────────────────────────────────────────────────────
CREATE VISUAL PeriodTotal AS CARD (
  SOURCE   = (SELECT SUM(revenue) AS val FROM #monthly
              WHERE sale_date >= @start AND sale_date <= @end),
  TITLE    = 'Period Revenue',
  MAPPINGS (VALUE = val),
  OPTIONS  (FORMAT = 'C0')
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE PAGE Trends AS LAYOUT (
  STRUCTURE = 'A B C / D D D',
  MAP (
    'A' = PeriodTotal,
    'B' = StartPicker,
    'C' = EndPicker,
    'D' = RevenueTrend
  )
)
WITH PARAMETERS (
  @start AS DATE DEFAULT '2025-01-01',
  @end   AS DATE DEFAULT '2025-12-31'
);
```

---

## 3. Year-over-Year Comparison

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
CREATE PAGE YoY AS LAYOUT (
  STRUCTURE = 'A A B / C C C',
  MAP (
    'A' = YoyLine,
    'B' = ProductShare,
    'C' = YoyTable
  )
);
```

---

## 4. Master-Detail Drill-Down

**Pattern**: Click a region bar to filter a branch-level detail chart. The detail chart title updates to show what's selected. Add a reset slicer to return to "all".

**Demonstrates**: `DRILL_DOWN`, `ON_CLICK`, `SET_PARAMETER`, `@param` in inline SELECT, reset via SLICER.

```sql
SET REPORT TITLE = 'Regional Drill-Down';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT 'North' AS region, 'Albany'   AS branch, 28000 AS revenue INTO #branches
UNION ALL SELECT 'North', 'Buffalo',    21000
UNION ALL SELECT 'North', 'Syracuse',   18000
UNION ALL SELECT 'South', 'Atlanta',    34000
UNION ALL SELECT 'South', 'Miami',      29000
UNION ALL SELECT 'South', 'Tampa',      22000
UNION ALL SELECT 'East',  'Boston',     31000
UNION ALL SELECT 'East',  'New York',   52000
UNION ALL SELECT 'East',  'Philly',     24000;

-- ── Region summary — clicking a bar fires DRILL_DOWN ──────────────────────
CREATE VISUAL RegionChart AS BAR (
  SOURCE   = (SELECT region, SUM(revenue) AS revenue
              FROM #branches GROUP BY region ORDER BY revenue DESC),
  TITLE    = 'Revenue by Region  (click to drill down)',
  MAPPINGS (X = region, Y = revenue),
  ACTIONS  (ON_CLICK = DRILL_DOWN(Target = BranchChart, Key = region)),
  OPTIONS  (
    X_AXIS (LABEL = 'Region'),
    Y_AXIS (LABEL = 'Revenue ($)', MIN = 0),
    COLORS (
      'North' = '#4e79a7',
      'South' = '#f28e2b',
      'East'  = '#59a14f'
    )
  )
);

-- ── Branch detail — filtered by @region set via DRILL_DOWN ───────────────
CREATE VISUAL BranchChart AS BAR (
  SOURCE   = (SELECT branch, revenue
              FROM #branches
              WHERE @region = 'All' OR region = @region
              ORDER BY revenue DESC),
  TITLE    = 'Branch Detail',
  MAPPINGS (X = branch, Y = revenue),
  OPTIONS  (
    X_AXIS (LABEL = 'Branch'),
    Y_AXIS (LABEL = 'Revenue ($)', MIN = 0)
  )
);

-- ── Reset control ─────────────────────────────────────────────────────────
CREATE VISUAL RegionReset AS SLICER (
  SOURCE   = (SELECT 'All' AS region
              UNION ALL SELECT DISTINCT region FROM #branches ORDER BY region),
  MAPPINGS (VALUE = region),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, region)),
  DEFAULT  = 'All'
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE PAGE DrillDown AS LAYOUT (
  STRUCTURE = 'A A B / C C C',
  MAP (
    'A' = RegionChart,
    'B' = RegionReset,
    'C' = BranchChart
  )
)
WITH PARAMETERS (@region = 'All');
```

---

## 5. Cross-Page Filtering with Navigation

**Pattern**: A slicer on Page 1 sets a parameter that Page 2 also reads. Navigation tabs let users switch pages while the filter persists. Both pages share the same `@region` parameter.

**Demonstrates**: `CREATE NAVIGATION`, multi-page `WITH PARAMETERS`, cross-page parameter sharing, `ORIENTATION = HORIZONTAL`.

```sql
SET REPORT TITLE = 'Multi-Page Sales Dashboard';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT 'Jan' AS month, 'North' AS region, 52000 AS revenue, 420 AS units INTO #sales
UNION ALL SELECT 'Jan', 'South', 41000, 310
UNION ALL SELECT 'Jan', 'East',  38000, 290
UNION ALL SELECT 'Feb', 'North', 58000, 470
UNION ALL SELECT 'Feb', 'South', 47000, 355
UNION ALL SELECT 'Feb', 'East',  43000, 320
UNION ALL SELECT 'Mar', 'North', 61000, 490
UNION ALL SELECT 'Mar', 'South', 53000, 400
UNION ALL SELECT 'Mar', 'East',  49000, 370
UNION ALL SELECT 'Apr', 'North', 67000, 535
UNION ALL SELECT 'Apr', 'South', 58000, 440
UNION ALL SELECT 'Apr', 'East',  54000, 405;

-- ── Shared filter — lives on Page 1, persists across pages ───────────────
CREATE VISUAL RegionFilter AS SLICER (
  SOURCE   = (SELECT 'All' AS region
              UNION ALL SELECT DISTINCT region FROM #sales ORDER BY region),
  MAPPINGS (VALUE = region),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, region)),
  DEFAULT  = 'All'
);

-- ── Page 1: Overview ──────────────────────────────────────────────────────
CREATE VISUAL TotalRev AS CARD (
  SOURCE   = (SELECT SUM(revenue) AS val FROM #sales
              WHERE @region = 'All' OR region = @region),
  TITLE    = 'Total Revenue',
  MAPPINGS (VALUE = val),
  OPTIONS  (FORMAT = 'C0')
);

CREATE VISUAL RevByRegion AS BAR (
  SOURCE   = (SELECT region, SUM(revenue) AS revenue FROM #sales
              WHERE @region = 'All' OR region = @region
              GROUP BY region ORDER BY revenue DESC),
  TITLE    = 'Revenue by Region',
  MAPPINGS (X = region, Y = revenue),
  OPTIONS  (Y_AXIS (LABEL = 'Revenue ($)', MIN = 0))
);

-- ── Page 2: Monthly Trends (reads same @region parameter) ────────────────
CREATE VISUAL RevTrend AS LINE (
  SOURCE   = (SELECT month, SUM(revenue) AS revenue FROM #sales
              WHERE @region = 'All' OR region = @region
              GROUP BY month ORDER BY month),
  TITLE    = 'Revenue Trend',
  MAPPINGS (X = month, Y = revenue),
  OPTIONS  (
    SMOOTH = ON,
    X_AXIS (LABEL = 'Month'),
    Y_AXIS (LABEL = 'Revenue ($)', MIN = 0)
  )
);

CREATE VISUAL UnitTrend AS LINE (
  SOURCE   = (SELECT month, SUM(units) AS units FROM #sales
              WHERE @region = 'All' OR region = @region
              GROUP BY month ORDER BY month),
  TITLE    = 'Units Trend',
  MAPPINGS (X = month, Y = units),
  OPTIONS  (
    SMOOTH = ON,
    X_AXIS (LABEL = 'Month'),
    Y_AXIS (LABEL = 'Units', MIN = 0)
  )
);

-- ── Pages ─────────────────────────────────────────────────────────────────
CREATE PAGE Overview AS LAYOUT (
  STRUCTURE = 'A B / C C',
  MAP (
    'A' = TotalRev,
    'B' = RegionFilter,
    'C' = RevByRegion
  )
)
WITH PARAMETERS (@region = 'All');

CREATE PAGE Trends AS LAYOUT (
  STRUCTURE = 'A B',
  MAP (
    'A' = RevTrend,
    'B' = UnitTrend
  )
)
WITH PARAMETERS (@region = 'All');

-- ── Navigation ────────────────────────────────────────────────────────────
CREATE NAVIGATION MainNav AS TAB (
  ORIENTATION = HORIZONTAL,
  DEFAULT     = Overview
)
WITH PAGES (Overview, Trends);
```

> **How cross-page filtering works**: When the user changes `RegionFilter` on the Overview page, `SET_PARAMETER` sets `@region` in the dashboard session. The Trends page declares the same `@region` parameter — when the user navigates there, its visuals re-query using the already-set value.

---

## 6. Inventory Heatmap & Low-Stock Alerts

**Pattern**: A warehouse bin heatmap shows stock intensity at a glance. A table below highlights items below reorder point in red. A MULTISELECT filters by category.

**Demonstrates**: `HEATMAP`, `MULTISELECT`, `FORMATTING`, multi-value parameter pattern.

```sql
SET REPORT TITLE = 'Warehouse Inventory Monitor';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT 'A' AS bin, 'Shelf 1' AS shelf, 'Electronics' AS category, 'Widget Pro'  AS item, 45 AS qty, 20 AS reorder INTO #inv
UNION ALL SELECT 'A', 'Shelf 2', 'Electronics', 'Widget Lite', 8,  15
UNION ALL SELECT 'A', 'Shelf 3', 'Accessories', 'Cable Pack',  62, 25
UNION ALL SELECT 'B', 'Shelf 1', 'Electronics', 'Adapter',     3,  10
UNION ALL SELECT 'B', 'Shelf 2', 'Accessories', 'Mount Kit',   28, 20
UNION ALL SELECT 'B', 'Shelf 3', 'Electronics', 'Charger',     17, 15
UNION ALL SELECT 'C', 'Shelf 1', 'Accessories', 'Case',        55, 30
UNION ALL SELECT 'C', 'Shelf 2', 'Electronics', 'Screen',      6,  12
UNION ALL SELECT 'C', 'Shelf 3', 'Accessories', 'Stand',       41, 20
UNION ALL SELECT 'D', 'Shelf 1', 'Electronics', 'Sensor',      2,  10
UNION ALL SELECT 'D', 'Shelf 2', 'Accessories', 'Hub',         19, 15
UNION ALL SELECT 'D', 'Shelf 3', 'Electronics', 'Dock',        33, 20;

-- ── Category filter ───────────────────────────────────────────────────────
CREATE VISUAL CategoryFilter AS MULTISELECT (
  SOURCE   = (SELECT DISTINCT category FROM #inv ORDER BY category),
  MAPPINGS (VALUE = category),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@category, category))
);

-- ── Heatmap: bin × shelf intensity ───────────────────────────────────────
CREATE VISUAL StockHeatmap AS HEATMAP (
  SOURCE   = (SELECT bin, shelf, SUM(qty) AS qty FROM #inv
              WHERE @category = 'All' OR category = @category
              GROUP BY bin, shelf),
  TITLE    = 'Stock Level by Bin & Shelf',
  MAPPINGS (X = bin, Y = shelf, VALUE = qty)
);

-- ── Donut: stock by category ──────────────────────────────────────────────
CREATE VISUAL CategoryShare AS DONUT (
  SOURCE   = (SELECT category, SUM(qty) AS qty FROM #inv
              WHERE @category = 'All' OR category = @category
              GROUP BY category),
  TITLE    = 'Stock by Category',
  MAPPINGS (LABEL = category, VALUE = qty)
);

-- ── Low-stock alert table ─────────────────────────────────────────────────
CREATE VISUAL LowStockTable AS TABLE (
  SOURCE = (SELECT item, bin, shelf, category, qty, reorder,
                   reorder - qty AS shortfall
            FROM #inv
            WHERE qty < reorder
              AND (@category = 'All' OR category = @category)
            ORDER BY shortfall DESC),
  FORMATTING (
    shortfall >= 10 THEN '#f8d7da',
    shortfall > 0   THEN '#fff3cd'
  )
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE PAGE Inventory AS LAYOUT (
  STRUCTURE = 'A B C / D D D',
  MAP (
    'A' = StockHeatmap,
    'B' = CategoryShare,
    'C' = CategoryFilter,
    'D' = LowStockTable
  )
)
WITH PARAMETERS (@category = 'All');
```

---

## 7. Financial Waterfall, Funnel & Gauge

**Pattern**: Three financial visuals on one page — a cash-flow waterfall, a sales conversion funnel, and a KPI gauge showing actuals vs target.

**Demonstrates**: `WATERFALL`, `FUNNEL`, `GAUGE`, `MIN`/`MAX` options on GAUGE, `COLORS` for waterfall bars.

```sql
SET REPORT TITLE = 'Financial Performance';

-- ── Inline sample data ────────────────────────────────────────────────────

-- Cash flow waterfall (positive = inflow, negative = outflow)
SELECT 'Starting Balance' AS period, 50000 AS delta INTO #cashflow
UNION ALL SELECT 'Product Sales',    120000
UNION ALL SELECT 'Service Revenue',   35000
UNION ALL SELECT 'COGS',            -68000
UNION ALL SELECT 'Salaries',        -45000
UNION ALL SELECT 'Marketing',       -18000
UNION ALL SELECT 'Office / IT',      -9000
UNION ALL SELECT 'Ending Balance',       0;  -- zero: waterfall total is implicit

-- Sales funnel stages
SELECT 'Leads'       AS stage, 1200 AS count INTO #funnel
UNION ALL SELECT 'Qualified',  480
UNION ALL SELECT 'Demo',       210
UNION ALL SELECT 'Proposal',   130
UNION ALL SELECT 'Closed Won',  68;

-- Revenue vs target (for gauge)
SELECT 283000 AS actual, 300000 AS target, 'Revenue vs Target' AS label INTO #gauge_data;

-- ── Waterfall ─────────────────────────────────────────────────────────────
CREATE VISUAL CashFlow AS WATERFALL (
  SOURCE   = (SELECT period, delta FROM #cashflow),
  TITLE    = 'Cash Flow Statement',
  MAPPINGS (X = period, Y = delta),
  OPTIONS  (
    COLORS (positive = '#28a745', negative = '#dc3545')
  )
);

-- ── Funnel ────────────────────────────────────────────────────────────────
CREATE VISUAL SalesFunnel AS FUNNEL (
  SOURCE   = (SELECT stage, count FROM #funnel ORDER BY count DESC),
  TITLE    = 'Sales Conversion Funnel',
  MAPPINGS (LABEL = stage, VALUE = count)
);

-- ── Gauge ─────────────────────────────────────────────────────────────────
CREATE VISUAL RevenueGauge AS GAUGE (
  SOURCE   = (SELECT actual AS val, target AS mx, label FROM #gauge_data),
  TITLE    = 'Revenue Attainment',
  MAPPINGS (VALUE = val, MAX = mx, LABEL = label),
  OPTIONS  (MIN = 0)
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE PAGE Financial AS LAYOUT (
  STRUCTURE = 'A A B / A A C',
  MAP (
    'A' = CashFlow,
    'B' = SalesFunnel,
    'C' = RevenueGauge
  )
);
```

---

## 8. Combo Chart: Revenue + Volume

**Pattern**: Revenue bars and unit-volume line on the same axes — the classic dual-metric chart for spotting when revenue and volume diverge.

**Demonstrates**: `COMBO`, `SERIES (BAR col, LINE col)`, `STACKED = ON` on a separate grouped bar example.

```sql
SET REPORT TITLE = 'Revenue & Volume Combined';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT 'Jan' AS month, 52000 AS revenue, 420 AS units INTO #metrics
UNION ALL SELECT 'Feb', 47000, 385
UNION ALL SELECT 'Mar', 61000, 490
UNION ALL SELECT 'Apr', 58000, 465
UNION ALL SELECT 'May', 67000, 535
UNION ALL SELECT 'Jun', 72000, 580
UNION ALL SELECT 'Jul', 69000, 550
UNION ALL SELECT 'Aug', 78000, 625
UNION ALL SELECT 'Sep', 74000, 590
UNION ALL SELECT 'Oct', 83000, 665
UNION ALL SELECT 'Nov', 91000, 730
UNION ALL SELECT 'Dec', 88000, 705;

-- ── Combo: bars for revenue, line for units ────────────────────────────────
CREATE VISUAL RevUnitsCombo AS COMBO (
  SOURCE   = (SELECT month, revenue, units FROM #metrics ORDER BY month),
  TITLE    = 'Revenue ($) vs Units Sold',
  MAPPINGS (X = month),
  SERIES   (BAR revenue, LINE units),
  OPTIONS  (
    X_AXIS (LABEL = 'Month'),
    Y_AXIS (LABEL = 'Value'),
    LEGEND_POSITION = BOTTOM
  )
);

-- ── Stacked bar variant: revenue by product per month ────────────────────
SELECT 'Jan' AS month, 'Widget A' AS product, 31000 AS revenue INTO #by_product
UNION ALL SELECT 'Jan', 'Widget B', 21000
UNION ALL SELECT 'Feb', 'Widget A', 28000
UNION ALL SELECT 'Feb', 'Widget B', 19000
UNION ALL SELECT 'Mar', 'Widget A', 37000
UNION ALL SELECT 'Mar', 'Widget B', 24000
UNION ALL SELECT 'Apr', 'Widget A', 35000
UNION ALL SELECT 'Apr', 'Widget B', 23000;

CREATE VISUAL StackedRevenue AS BAR (
  SOURCE   = (SELECT month, revenue, product FROM #by_product ORDER BY month),
  TITLE    = 'Revenue by Product (Stacked)',
  MAPPINGS (X = month, Y = revenue, SERIES = product),
  OPTIONS  (
    STACKED = ON,
    X_AXIS  (LABEL = 'Month'),
    Y_AXIS  (LABEL = 'Revenue ($)', MIN = 0),
    LEGEND_POSITION = TOP,
    COLORS ('Widget A' = '#0d6efd', 'Widget B' = '#fd7e14')
  )
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE PAGE Combo AS LAYOUT (
  STRUCTURE = 'A A / B B',
  MAP (
    'A' = RevUnitsCombo,
    'B' = StackedRevenue
  )
);
```

---

## 9. Multi-Select + Search Filter Table

**Pattern**: A MULTISELECT narrows a TABLE to chosen categories; a SEARCH box further filters by text. Both controls update `@category` and `@search` independently.

**Demonstrates**: `MULTISELECT`, `SEARCH`, `SLIDER`, combined parameter filtering, typed parameters.

```sql
SET REPORT TITLE = 'Product Catalog Browser';

-- ── Inline sample data ────────────────────────────────────────────────────
SELECT 'Electronics' AS category, 'Widget Pro 2000' AS name, 299.99 AS price, 4.8 AS rating INTO #products
UNION ALL SELECT 'Electronics', 'Widget Lite',        149.99, 4.2
UNION ALL SELECT 'Electronics', 'Sensor Module',       79.99, 3.9
UNION ALL SELECT 'Electronics', 'Smart Dock',         199.99, 4.6
UNION ALL SELECT 'Accessories', 'Carry Case',          29.99, 4.5
UNION ALL SELECT 'Accessories', 'Mount Kit Pro',       44.99, 4.3
UNION ALL SELECT 'Accessories', 'USB-C Cable 3m',       9.99, 4.7
UNION ALL SELECT 'Accessories', 'Screen Protector',    14.99, 4.1
UNION ALL SELECT 'Software',    'Dashboard License',  499.00, 4.9
UNION ALL SELECT 'Software',    'Analytics Add-on',   199.00, 4.4
UNION ALL SELECT 'Software',    'Support Plan 1yr',   299.00, 4.8;

-- ── Filter controls ───────────────────────────────────────────────────────
CREATE VISUAL CategoryPicker AS MULTISELECT (
  SOURCE   = (SELECT DISTINCT category FROM #products ORDER BY category),
  MAPPINGS (VALUE = category),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@category, category))
);

CREATE VISUAL NameSearch AS SEARCH (
  TITLE   = 'Search by name...',
  ACTIONS (ON_CHANGE = SET_PARAMETER(@search, value))
);

CREATE VISUAL MaxPrice AS SLIDER (
  TITLE   = 'Max Price',
  ACTIONS (ON_CHANGE = SET_PARAMETER(@max_price, value)),
  OPTIONS (MIN = 0, MAX = 500)
);

-- ── Filtered product table ────────────────────────────────────────────────
CREATE VISUAL ProductTable AS TABLE (
  SOURCE = (
    SELECT category, name, price, rating
    FROM #products
    WHERE (@category = 'All' OR category = @category)
      AND (@search    = ''    OR name LIKE '%' + @search + '%')
      AND price <= @max_price
    ORDER BY category, price DESC
  ),
  FORMATTING (
    rating >= 4.7 THEN '#d4edda',
    rating < 4.0  THEN '#f8d7da'
  )
);

-- ── Bar: average price by category ───────────────────────────────────────
CREATE VISUAL PriceByCategory AS BAR (
  SOURCE   = (SELECT category, AVG(price) AS avg_price FROM #products
              WHERE @category = 'All' OR category = @category
              GROUP BY category),
  TITLE    = 'Average Price by Category',
  MAPPINGS (X = category, Y = avg_price),
  OPTIONS  (Y_AXIS (LABEL = 'Avg Price ($)', MIN = 0))
);

-- ── Layout ────────────────────────────────────────────────────────────────
CREATE PAGE Catalog AS LAYOUT (
  STRUCTURE = 'A B C / D D E',
  MAP (
    'A' = CategoryPicker,
    'B' = NameSearch,
    'C' = MaxPrice,
    'D' = ProductTable,
    'E' = PriceByCategory
  )
)
WITH PARAMETERS (
  @category  AS VARCHAR DEFAULT 'All',
  @search    AS VARCHAR DEFAULT '',
  @max_price AS NUMBER  DEFAULT '500'
);
```

---

## Tips

**Use `CREATE DATASET` for shared expensive queries** — if multiple visuals query the same data, compute it once as `CREATE DATASET &name` and reference it everywhere. Add `COMPRESS = ON` for large result sets.

**The "All or filtered" pattern** — use `WHERE @param = 'All' OR col = @param` so visuals show full data before the user makes a selection. Pair with a SLICER whose option list includes an `'All'` row.

**Slicer and MULTISELECT require a SOURCE** — the source rows become the dropdown options. Include an `'All'` row via `UNION ALL SELECT 'All' ...` if you want a reset option.

**Page parameters are initialized at load time** — `WITH PARAMETERS` default values run immediately, so every visual has valid data on first render even before any filter interaction.

**TITLE on visuals vs OPTIONS** — the top-level `TITLE = '...'` clause is preferred over `OPTIONS (TITLE = '...')`. Both work but the clause form is cleaner.

---

## 10. Themed Dashboard with CREATE STYLE

**Pattern**: Define a shared visual identity once with `CREATE STYLE`, then apply it across all visuals, pages, and containers. Override individual properties inline where needed.

**Demonstrates**: `CREATE STYLE`, `STYLE = <name>`, inline `STYLE (...)` overrides, applying styles to visuals / pages / containers.

```sql
SET REPORT TITLE       = 'Themed Sales Overview';
SET REPORT DESCRIPTION = 'Demonstrates CREATE STYLE for consistent branding across all report objects.';

-- ── Sample data ───────────────────────────────────────────────────────────
SELECT 'Jan' AS month, 142000 AS revenue, 88 AS orders INTO #summary
UNION ALL SELECT 'Feb', 158000, 97
UNION ALL SELECT 'Mar', 173000, 110
UNION ALL SELECT 'Apr', 191000, 124
UNION ALL SELECT 'May', 167000, 103
UNION ALL SELECT 'Jun', 205000, 138;

SELECT 'North' AS region, 312000 AS revenue INTO #byregion
UNION ALL SELECT 'South', 248000
UNION ALL SELECT 'East',  197000
UNION ALL SELECT 'West',  279000;

-- ── Named styles ─────────────────────────────────────────────────────────
-- Base dark theme applied to most visuals
CREATE STYLE DarkTheme (
  background-color = '#1e1e2e',
  color            = '#cdd6f4',
  border-radius    = '8px',
  padding          = '12px',
  font-size        = '13px'
);

-- Accent for KPI cards that need emphasis
CREATE STYLE KpiAccent (
  background-color = '#313244',
  color            = '#89dceb',
  border           = '1px solid #45475a',
  border-radius    = '10px',
  padding          = '16px',
  font-size        = '15px'
);

-- Subtle border for layout containers
CREATE STYLE PanelFrame (
  border        = '1px solid #45475a',
  border-radius = '6px',
  padding       = '8px'
);

-- ── Visuals ───────────────────────────────────────────────────────────────
-- KPI card — uses KpiAccent style
CREATE VISUAL TotalRevenue AS CARD (
  TITLE    = 'Total Revenue',
  SOURCE   = (SELECT SUM(revenue) AS revenue, 'Total' AS label FROM #summary),
  STYLE    = KpiAccent,
  MAPPINGS (VALUE = revenue, LABEL = label),
  OPTIONS  (FORMAT = 'C0')
);

-- KPI card — uses KpiAccent but overrides color to signal a warning
CREATE VISUAL TotalOrders AS CARD (
  TITLE    = 'Total Orders',
  SOURCE   = (SELECT SUM(orders) AS orders, 'Orders' AS label FROM #summary),
  STYLE    = KpiAccent,
  STYLE    (color = '#a6e3a1'),   -- green override
  MAPPINGS (VALUE = orders, LABEL = label)
);

-- Trend line — base DarkTheme
CREATE VISUAL RevenueTrend AS LINE (
  TITLE    = 'Revenue by Month',
  SOURCE   = #summary,
  STYLE    = DarkTheme,
  MAPPINGS (X = month, Y = revenue)
);

-- Regional bar chart — base DarkTheme, wider padding for readability
CREATE VISUAL RegionBar AS BAR (
  TITLE    = 'Revenue by Region',
  SOURCE   = #byregion,
  STYLE    = DarkTheme,
  STYLE    (padding = '20px'),    -- inline override
  MAPPINGS (X = region, Y = revenue)
);

-- Detail table — DarkTheme with alternating row color override
CREATE VISUAL SummaryTable AS TABLE (
  TITLE    = 'Monthly Detail',
  SOURCE   = #summary,
  STYLE    = DarkTheme,
  STYLE    (font-size = '12px')
);

-- ── Container ─────────────────────────────────────────────────────────────
-- Wrap KPI cards in a styled horizontal container
CREATE CONTAINER KpiRow AS BOX (
  STYLE   = PanelFrame,
  VISUALS (TotalRevenue, TotalOrders)
);

-- ── Page layout ───────────────────────────────────────────────────────────
CREATE PAGE Overview AS LAYOUT (
  TITLE     = 'Sales Overview',
  STRUCTURE = 'A A / B C / D D',
  STYLE     = PanelFrame,
  MAP ('A' = KpiRow,
       'B' = RegionBar,
       'C' = RevenueTrend,
       'D' = SummaryTable)
);

CREATE NAVIGATION MainNav AS TAB (
  ORIENTATION = HORIZONTAL,
  DEFAULT     = Overview
)
WITH PAGES (Overview);
```

### Key Points

- **`CREATE STYLE <name> (...)`** defines a reusable style block. Declare styles before the visuals that reference them.
- **`STYLE = <name>`** applies the named style as a base. All properties from the named style are inherited.
- **`STYLE (...)`** on the same visual overrides specific properties; other properties from the named style are unchanged.
- Both `STYLE = <name>` and an inline `STYLE (...)` block can appear on the same visual — the named style is the base and the inline block wins on any overlapping key.
- Styles apply to `CREATE VISUAL`, `CREATE PAGE`, and `CREATE CONTAINER` equally.
- Named styles are resolved at manifest build time and are not stored as separate entities in the manifest output.

**Conditional formatting is ordered** — rules evaluate top-to-bottom, first match wins. Put the strictest condition first.

---

## 11. Choropleth Map Charts

**Pattern**: Color-scaled geographic regions driven by a data column. Six bundled maps require no external files; zip-code and custom-boundary maps require a user-supplied GeoJSON file.

### Bundled maps (no file needed)

Specify `MAP_NAME` with one of the six built-in keys. The engine serves the GeoJSON from `/maps/{name}.geojson` automatically.

| `MAP_NAME` | Regions | Match column contains |
|---|---|---|
| `WORLD` | 177 countries | Country name — `"France"`, `"United States of America"` |
| `US_STATES` | 50 states + DC | State name — `"Minnesota"`, `"New York"` |
| `US_COUNTIES` | 3 221 counties | County name — `"Hennepin"`, `"Cook"` |
| `MN_COUNTIES` | 87 MN counties | County name — `"Hennepin"`, `"Ramsey"` |
| `CANADA_PROVINCES` | 13 provinces/territories | Province name — `"Alberta"`, `"Ontario"` |
| `EUROPE` | 39 countries | Country name — `"France"`, `"Germany"` |

```sql
-- Revenue by US state
SELECT State, SUM(Revenue) AS Revenue
INTO #state_rev
FROM dbo.Sales
GROUP BY State;

CREATE VISUAL RevenueMap AS MAP (
  SOURCE   = #state_rev,
  MAPPINGS (REGION = State, VALUE = Revenue),
  OPTIONS  (
    MAP_NAME   = US_STATES,
    COLOR_LOW  = '#e0f2fe',
    COLOR_HIGH = '#0369a1',
    TITLE      = 'Revenue by State'
  )
);
```

### Matching by FIPS code instead of name

County data often carries FIPS codes rather than names. Set `MATCH_BY = FIPS` and put the 5-digit FIPS code in the region column.

```sql
SELECT fips_code, incident_count
INTO #incidents
FROM dbo.CountyIncidents;

CREATE VISUAL IncidentMap AS MAP (
  SOURCE   = #incidents,
  MAPPINGS (REGION = fips_code, VALUE = incident_count),
  OPTIONS  (
    MAP_NAME = US_COUNTIES,
    MATCH_BY = FIPS,           -- matches feature id (e.g. "27053") not name
    COLOR_LOW  = '#fef9c3',
    COLOR_HIGH = '#b45309',
    TITLE = 'Incidents by County'
  )
);
```

### Zip code choropleth

> **There is no bundled ZIP code map.** US ZIP Code Tabulation Area (ZCTA) GeoJSON from the Census Bureau is ~300 MB uncompressed — too large to bundle. To map zip codes:
>
> 1. Download the simplified ZCTA file yourself from the Census Cartographic Boundary Files:  
>    `https://www.census.gov/geographies/mapping-files/time-series/geo/cartographic-boundary.html`  
>    (choose **ZCTAs**, **20m** simplification for the smallest usable file, ~25 MB).
> 2. Place the file anywhere accessible to the Report Player — e.g., alongside your `.rptsql` file.
> 3. Reference it with `MAP_FILE`:

```sql
SELECT zip_code, SUM(orders) AS Orders
INTO #zip_orders
FROM dbo.OrdersByZip
GROUP BY zip_code;

CREATE VISUAL ZipMap AS MAP (
  SOURCE   = #zip_orders,
  MAPPINGS (REGION = zip_code, VALUE = Orders),
  OPTIONS  (
    MAP_FILE   = 'C:\Reports\Maps\cb_2023_us_zcta520_20m.geojson',
    MATCH_BY   = NAME,          -- ZCTA features use the zip code as their name property
    COLOR_LOW  = '#f0fdf4',
    COLOR_HIGH = '#166534',
    TITLE      = 'Orders by ZIP Code'
  )
);
```

> **Tip**: If your ZCTA file is still too large to load comfortably, filter it to only the states or metro area you need using a tool like [mapshaper.org](https://mapshaper.org) (free, browser-based) before placing it in your maps folder.

### Point map (city names, lat/lon coordinates)

Cities are points, not polygons — they cannot be choropleth-filled. Use `MODE = POINTS` with `LON_COL` and `LAT_COL` mappings to scatter-plot locations on a base map instead. The `VALUE` mapping controls dot size.

```sql
SELECT city_name, longitude, latitude, SUM(revenue) AS Revenue
INTO #city_rev
FROM dbo.SalesByCity
GROUP BY city_name, longitude, latitude;

CREATE VISUAL CityMap AS MAP (
  SOURCE   = #city_rev,
  MAPPINGS (
    LON   = longitude,
    LAT   = latitude,
    VALUE = Revenue,
    LABEL = city_name
  ),
  OPTIONS  (
    MAP_NAME = US_STATES,      -- base map for context
    MODE     = POINTS,
    TITLE    = 'Revenue by City'
  )
);
```

> **If you only have city names and no coordinates**: geocode them first in your ETL script using a `LOOKUP` against a reference table, or pre-join to a reference dataset that maps city names to lat/lon before the visual's `SOURCE` query.

### Key Points

- `MAP_NAME` selects a bundled map; `MAP_FILE` points to a custom GeoJSON file on disk. Exactly one is required.
- The `REGION` mapping column must match the region's `name` property in the GeoJSON (case-insensitive). Use `MATCH_BY = FIPS` to match on the numeric FIPS `id` instead.
- `COLOR_LOW` and `COLOR_HIGH` define the two-color gradient. Regions with no data row are rendered in a neutral grey.
- `MODE = CHOROPLETH` (default) fills regions. `MODE = POINTS` plots scatter dots using `LON`/`LAT` mappings on the same base map.
- Zip code maps require a user-supplied ZCTA GeoJSON (~25 MB at 20m simplification). See the Census Cartographic Boundary Files link above.
