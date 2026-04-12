# Report-SQL Scripting Guide

Report-SQL extends ETL-SQL with three new statement types — `CREATE DATASET`, `CREATE VISUAL`, and `CREATE PAGE` — plus a CLI tool and VS Code preview command for building and serving interactive dashboards.

---

## Quick start

```sql
-- 1. Connect and pull data
CREATE CONNECTION c ON FLATFILE('data/sales.csv');

SELECT region, SUM(revenue) AS revenue
INTO #summary
FROM c
GROUP BY region;

-- 2. Define a visual
CREATE VISUAL SalesChart AS BAR (
  SOURCE = #summary,
  MAPPINGS (X = region, Y = revenue),
  OPTIONS (
    X_AXIS (label = 'Region'),
    Y_AXIS (label = 'Revenue ($)')
  )
);

-- 3. Arrange on a page
CREATE PAGE Main AS LAYOUT (
  STRUCTURE = 'grid:1x1',
  MAP ('A' = SalesChart)
);
```

Save as `report.rptsql`, then:

```sh
etl-sql-report build report.rptsql        # → report.report.md + report.snapshot.json
etl-sql-report serve report.rptsql        # → opens http://localhost:5200
```

---

## CREATE VISUAL

```
CREATE VISUAL <name> AS <TYPE> (
  SOURCE = <source>,
  [MAPPINGS (role = column, ...),]
  [OPTIONS (key = value, X_AXIS (...), Y_AXIS (...)),]
  [ACTIONS (ON_CLICK = <action>, ON_CHANGE = <action>)]
);
```

All clauses inside the outer `( )` are separated by commas. The closing `)` ends the statement.

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
-- Temp table (populated earlier in the same script)
SOURCE = #my_temp_table

-- Inline SELECT (wrap in parentheses)
SOURCE = (SELECT region, SUM(revenue) AS rev FROM #summary GROUP BY region)
```

### MAPPINGS

Maps column names to semantic roles. Each role depends on the chart type:

```sql
MAPPINGS (X = month, Y = revenue)           -- BAR / LINE
MAPPINGS (LABEL = category, VALUE = total)  -- PIE
MAPPINGS (X = score, Y = rank)              -- SCATTER
MAPPINGS (VALUE = total, LABEL = 'Revenue') -- CARD
MAPPINGS (PARAMETER = @region)              -- SLICER
```

### OPTIONS

General key/value options plus axis sub-blocks:

```sql
OPTIONS (
  STACKED = true,            -- BAR / LINE: stacked series
  SMOOTH  = true,            -- LINE: smooth curves
  X_AXIS (label = 'Month'),
  Y_AXIS (label = 'Revenue ($)', min = 0)
)
```

`X_AXIS` and `Y_AXIS` each take their own `(key = value, ...)` list.

### ACTIONS

```sql
ACTIONS (
  ON_CLICK = DRILL_DOWN(Target = DetailChart, Key = region)
)
```

`ON_CLICK` or `ON_CHANGE` triggers. `DRILL_DOWN` passes the selected value as the key column into the target visual's inline SELECT.

---

## CREATE DATASET

Pre-computes a named snapshot that can be refreshed independently. Options come before `AS`:

```sql
CREATE DATASET #sales_snap
  REFRESH EVERY '1h'
  AS (SELECT * FROM sales WHERE active = 1);
```

With all options:

```sql
CREATE DATASET #sales_snap
  REFRESH EVERY '1h'
  TTL = '24h'
  COMPRESS = ON
  ENCRYPT = ON KEYFILE = 'C:\keys\report.key'
  AS (SELECT region, product, SUM(revenue) AS revenue FROM sales GROUP BY region, product);
```

| Clause | Description |
|--------|-------------|
| `REFRESH EVERY '<interval>'` | Re-run interval: `<n>s`, `<n>m`, `<n>h`, or `<n>d` |
| `TTL = '<duration>'` | How long a snapshot stays valid |
| `COMPRESS = ON` | Compress the snapshot file |
| `ENCRYPT = ON KEYFILE = '<path>'` | AES-encrypt the snapshot. `KEYFILE` is required when encryption is on. |
| `AS (SELECT ...)` | Query to execute — must be last, before the semicolon |

---

## CREATE PAGE

Arranges visuals into a named layout. Slot keys are single-quoted letters:

```sql
CREATE PAGE <name> AS LAYOUT (
  STRUCTURE = '<label>',
  MAP (
    '<slot>' = VisualName,
    '<slot>' = VisualName
  )
)
[WITH PARAMETERS (@param = default, ...)]
;
```

`STRUCTURE` is a free-form descriptive string (e.g. `'grid:2x2'`, `'tabs'`). The ReportPlayer uses it as a hint for rendering.

Example — 2-column, 2-row grid:

```sql
CREATE PAGE Overview AS LAYOUT (
  STRUCTURE = 'grid:2x2',
  MAP (
    'A' = TotalRevenue,
    'B' = RevenueByRegion,
    'C' = RevenueByProduct,
    'D' = SalesTable
  )
)
WITH PARAMETERS (@region = 'All');
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
| `VisualSourceExists` | Warning | `SOURCE = #table` references a temp table not defined in the script |
| `VisualMappingColumnExists` | Warning | A `MAPPINGS` role references a column not in the inline SELECT |
| `PageVisualReferenced` | Warning | `MAP` references a visual name not defined in the script |
| `DatasetRefreshInterval` | Warning | `REFRESH EVERY` value doesn't match `<n>s/m/h/d` |
| `DatasetEncryptWithoutKey` | **Error** | `ENCRYPT = ON` without a `KEYFILE` clause |
| `LayerOrder` | Warning | A visual references a dataset defined later, or a page references a visual defined later |

---

## Full working example

Source CSV columns: `region`, `product`, `units`, `revenue`, `month`, `date`

```sql
DROP CONNECTION IF EXISTS c;
CREATE CONNECTION c ON FLATFILE('C:\Users\chuck\scratch\ETL-SQL\TestData\test_sales.csv');

-- Base dataset
SELECT month, region, product, SUM(units) AS units, SUM(revenue) AS revenue
INTO #summary
FROM c
GROUP BY month, region, product;

-- KPI card: total revenue
CREATE VISUAL TotalRevenue AS CARD (
  SOURCE = (SELECT SUM(revenue) AS val, 'Total Revenue' AS lbl FROM #summary),
  MAPPINGS (VALUE = val, LABEL = lbl)
);

-- Bar chart: revenue by region
CREATE VISUAL RevenueByRegion AS BAR (
  SOURCE = (SELECT region, SUM(revenue) AS revenue FROM #summary GROUP BY region),
  MAPPINGS (X = region, Y = revenue),
  OPTIONS (
    X_AXIS (label = 'Region'),
    Y_AXIS (label = 'Revenue ($)')
  )
);

-- Pie chart: revenue by product
CREATE VISUAL RevenueByProduct AS PIE (
  SOURCE = (SELECT product, SUM(revenue) AS revenue FROM #summary GROUP BY product),
  MAPPINGS (LABEL = product, VALUE = revenue)
);

-- Line chart: units by month
CREATE VISUAL UnitsByMonth AS LINE (
  SOURCE = (SELECT month, SUM(units) AS units FROM #summary GROUP BY month),
  MAPPINGS (X = month, Y = units),
  OPTIONS (
    X_AXIS (label = 'Month'),
    Y_AXIS (label = 'Units Sold'),
    SMOOTH = true
  )
);

-- Detail table: all rows
CREATE VISUAL SalesTable AS TABLE (
  SOURCE = #summary
);

-- Dashboard page
CREATE PAGE Main AS LAYOUT (
  STRUCTURE = 'grid:2x3',
  MAP (
    'A' = TotalRevenue,
    'B' = RevenueByRegion,
    'C' = RevenueByProduct,
    'D' = UnitsByMonth,
    'E' = SalesTable
  )
);
```

This script is saved at `TestData/sales_report.rptsql` and can be run directly.
