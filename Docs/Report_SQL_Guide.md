# Report-SQL Scripting Guide

Report-SQL extends ETL-SQL with three new statement types — `CREATE DATASET`, `CREATE VISUAL`, and `CREATE PAGE` — plus a CLI tool and VS Code preview command for building and serving interactive dashboards.

---

## Quick start

```sql
-- 1. Pull data
SELECT category, SUM(revenue) AS total
INTO #monthly
FROM sales
WHERE month = '2025-01';

-- 2. Define a visual
CREATE VISUAL SalesChart TYPE BAR
  SOURCE #monthly
  MAPPING (X = category, Y = total)
  TITLE 'Monthly Revenue by Category';

-- 3. Arrange on a page
CREATE PAGE MainDashboard LAYOUT GRID(2,1)
  SLOT MAP (1,1) = SalesChart;
```

Save as `report.rptsql`, then:

```sh
etl-sql-report build report.rptsql        # → report.report.md + report.snapshot.json
etl-sql-report serve report.rptsql        # → opens http://localhost:5200
```

---

## CREATE VISUAL

```
CREATE VISUAL <name> TYPE <type>
  SOURCE <source>
  [MAPPING (...)]
  [AXIS (...)]
  [TITLE '<string>']
  [WITH (...)]
  [ACTION <action>]
;
```

### Visual types

| Type | Description |
|------|-------------|
| `BAR` | Vertical bar chart |
| `LINE` | Line chart |
| `PIE` | Pie / doughnut chart |
| `SCATTER` | X/Y scatter plot |
| `TABLE` | Paginated data grid |
| `CARD` | Single KPI number |
| `SLICER` | Parameter dropdown |

### SOURCE

The data source is either a temp table reference or an inline SELECT:

```sql
-- Temp table (defined elsewhere in the script)
SOURCE #my_temp_table

-- Inline SELECT
SOURCE (SELECT region, sales FROM results WHERE year = 2025)
```

### MAPPING

Maps column names to semantic roles. Available roles depend on the chart type:

```sql
MAPPING (X = month, Y = revenue)            -- BAR / LINE
MAPPING (LABEL = category, VALUE = total)   -- PIE
MAPPING (X = score, Y = rank)               -- SCATTER
MAPPING (VALUE = total, LABEL = 'Revenue')  -- CARD
MAPPING (PARAMETER = @region)               -- SLICER
```

### AXIS

Customizes axis labels and scale:

```sql
AXIS (
  X LABEL = 'Month',
  Y LABEL = 'Revenue ($)',
  Y MIN   = 0,
  Y MAX   = 100000
)
```

### WITH options

```sql
WITH (
  STACKED   = ON,    -- BAR / LINE: stacked series
  LEGEND    = ON,    -- show legend (default ON)
  SMOOTH    = ON     -- LINE: smooth curves
)
```

### ACTION — drill-down

```sql
ACTION DRILL_DOWN (TARGET = DetailChart, KEY = category)
```

Clicking a bar/segment passes the selected value to `@key` in the target visual's inline SELECT.

---

## CREATE DATASET

Pre-computes a named snapshot that can be refreshed independently:

```sql
CREATE DATASET SalesSnapshot
  AS (SELECT * FROM sales WHERE active = 1)
  INTO #sales_snap
  REFRESH INTERVAL = '1h'
  [ENCRYPT = ON KEYFILE = 'C:\keys\report.key']
;
```

| Clause | Description |
|--------|-------------|
| `AS (SELECT ...)` | Query to execute |
| `INTO #name` | Temp table to populate |
| `REFRESH INTERVAL` | `<n>s`, `<n>m`, `<n>h`, or `<n>d` |
| `ENCRYPT = ON KEYFILE = '...'` | AES-encrypt the snapshot at rest. `KEYFILE` is required when encryption is on. |

---

## CREATE PAGE

Arranges visuals into a named layout:

```
CREATE PAGE <name> LAYOUT GRID(<cols>,<rows>)
  SLOT MAP (<col>,<row>) = <visual>
  [SLOT MAP (<col>,<row>) = <visual> ...]
  [TITLE '<string>']
;
```

Example — 2-column, 2-row grid:

```sql
CREATE PAGE Overview LAYOUT GRID(2,2)
  SLOT MAP (1,1) = SalesChart
  SLOT MAP (2,1) = RegionMap
  SLOT MAP (1,2) = KpiCard
  SLOT MAP (2,2) = CategorySlicer
  TITLE 'Sales Overview';
```

If no pages are defined the build tool renders all visuals in definition order.

---

## CLI — etl-sql-report

### build

Evaluates the script, builds a `ReportManifest`, and writes output:

```sh
etl-sql-report build report.rptsql
etl-sql-report build report.rptsql --output out/dashboard.md
etl-sql-report build report.rptsql --format json
```

| Flag | Default | Description |
|------|---------|-------------|
| `--output`, `-o` | `<script>.report.md` | Output file path |
| `--format`, `-f` | `md` | `md` (GitHub Flavored Markdown) or `json` (raw manifest) |

Also writes a `<script>.snapshot.json` alongside the output.

### refresh

Re-evaluates the script and updates the snapshot without writing a report file:

```sh
etl-sql-report refresh report.rptsql
```

Use this on a schedule to keep snapshots fresh.

### serve

Starts the web dashboard at `http://localhost:5200`:

```sh
etl-sql-report serve report.rptsql
```

Opens the browser automatically after 2.5 s. The dashboard supports parameter slicers that re-run queries live.

---

## VS Code preview

With a `.rptsql` file open, run **ETL-SQL: Preview Report** (command palette or the `$(graph)` icon in the editor title bar). A webview panel opens beside the editor and auto-refreshes every time you save the file.

**Configuration:**

| Setting | Default | Description |
|---------|---------|-------------|
| `etlsql.reportCliPath` | *(empty)* | Full path to `etl-sql-report` exe. Leave empty to use `dotnet run` in development. |

---

## Linter rules

The language server checks `.rptsql` files automatically:

| Rule | Severity | Condition |
|------|----------|-----------|
| `VisualSourceExists` | Warning | `SOURCE #table` references a temp table not defined in the script |
| `VisualMappingColumnExists` | Warning | A `MAPPING` role references a column not in the inline SELECT |
| `PageVisualReferenced` | Warning | `SLOT MAP` references a visual name not defined in the script |
| `DatasetRefreshInterval` | Warning | `REFRESH INTERVAL` value doesn't match `<n>s/m/h/d` |
| `DatasetEncryptWithoutKey` | **Error** | `ENCRYPT = ON` without a `KEYFILE` |
| `LayerOrder` | Warning | A visual references a dataset defined later, or a page references a visual defined later |

---

## Full example

```sql
-- Pull sales data
SELECT region, product, SUM(units) AS units, SUM(revenue) AS revenue
INTO #summary
FROM warehouse.sales
WHERE year = 2025;

-- KPI card
CREATE VISUAL TotalRevenue TYPE CARD
  SOURCE (SELECT SUM(revenue) AS val, 'Total Revenue' AS lbl FROM #summary)
  MAPPING (VALUE = val, LABEL = lbl)
;

-- Bar chart with region filter
CREATE VISUAL RevenueByRegion TYPE BAR
  SOURCE (SELECT region, revenue FROM #summary)
  MAPPING (X = region, Y = revenue)
  AXIS (X LABEL = 'Region', Y LABEL = 'Revenue ($)')
  TITLE 'Revenue by Region'
;

-- Region slicer
CREATE VISUAL RegionFilter TYPE SLICER
  SOURCE (SELECT DISTINCT region FROM #summary)
  MAPPING (PARAMETER = @region)
  TITLE 'Filter by Region'
;

-- Arrange dashboard
CREATE PAGE Main LAYOUT GRID(2,2)
  SLOT MAP (1,1) = TotalRevenue
  SLOT MAP (2,1) = RegionFilter
  SLOT MAP (1,2) = RevenueByRegion
  SLOT MAP (2,2) = RevenueByRegion
  TITLE '2025 Sales Dashboard'
;
```
