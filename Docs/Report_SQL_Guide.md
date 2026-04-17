# Report-SQL Scripting Guide

Report-SQL extends ETL-SQL with dedicated statement types for building interactive dashboards: `SET REPORT TITLE`, `CREATE DATASET`, `CREATE VISUAL`, `CREATE PAGE`, `CREATE CONTAINER`, and `CREATE NAVIGATION` — plus a CLI build tool and a live web dashboard for serving reports.

---

## How it works — architecture overview

```
┌─────────────────────┐    build / serve      ┌─────────────────────┐
│ your_report.rptsql  │ ───────────────────▶  │ etl-sql-report CLI  │
│  (report script)    │                       │  (ReportBuilder.CLI)│
└─────────────────────┘                       └──────────┬──────────┘
                                                         │ evaluates script
                                                         ▼
                                              ┌─────────────────────┐
                                              │  ETL-SQL Engine     │
                                              │  (Evaluator)        │
                                              └──────────┬──────────┘
                                                         │ builds ReportManifest
                                                         ▼
                                      ┌──────────────────────────────┐
                                      │       ManifestBuilder        │
                                      │  ┌──────────────────────── ┐ │
                                      │  │ VisualManifest (x N)    │ │
                                      │  │ PageManifest   (x N)    │ │
                                      │  │ DatasetManifest (x N)   │ │
                                      │  └─────────────────────────┘ │
                                      └──────────┬───────────────────┘
                                                 │
                              ┌──────────────────┴──────────────────┐
                              │                                     │
                              ▼                                     ▼
                  ┌───────────────────────┐           ┌──────────────────────┐
                  │  MarkdownRenderer     │           │  ReportPlayer        │
                  │  → .report.md         │           │  (ASP.NET Kestrel)   │
                  │  → .snapshot.json     │           │  http://localhost:5200│
                  └───────────────────────┘           └──────────────────────┘
```

A `.rptsql` file is a normal ETL-SQL script that may also contain Report-SQL statements. The engine evaluates it exactly like any `.etlsql` file; the new statements register definitions in the execution context. After evaluation the `ManifestBuilder` snapshots the data and produces a `ReportManifest` — a serialisable JSON structure consumed by both the static Markdown renderer and the live web dashboard.

### The `@@DATASET` System Variable

Report-SQL provides the `@@DATASET` system variable which can be used to manually pass data to visuals or capture the result of a `CREATE DATASET` operation. It is treated as a `LIST` of rows:

```sql
DECLARE @@DATASET = (('Product A', 100), ('Product B', 250));
CREATE VISUAL ManualCard AS CARD (SOURCE = @@DATASET, MAPPINGS(VALUE = Col1, LABEL = Col0));
```

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

-- 3. Arrange on a page (STRUCTURE uses CSS grid-template-areas)
CREATE PAGE Main AS LAYOUT (
  STRUCTURE = 'A',
  MAP ('A' = SalesChart)
);
```

Save as `report.rptsql`, then:

```sh
etl-sql-report build report.rptsql        # → report.report.md + report.snapshot.json
etl-sql-report serve report.rptsql        # → opens http://localhost:5200
```

---

## SET REPORT TITLE / SET REPORT DESCRIPTION

Sets the report title and description displayed in the dashboard header and catalog page.

```sql
SET REPORT TITLE = 'Sales Dashboard';
SET REPORT DESCRIPTION = 'Regional and product-level revenue analysis for Q1 2026.';
```

Both statements are optional. If omitted the script filename is used as the title.

---

## CREATE VISUAL

```
CREATE VISUAL <name> AS <TYPE> (
  [SOURCE = <source>,]
  [TITLE = '<string>',]
  [SUBTITLE = '<string>',]
  [MAPPINGS (role = column, ...),]
  [OPTIONS (key = value, ..., X_AXIS (...), Y_AXIS (...), COLORS (...), LEGEND (...)),]
  [STYLE (key = value, ...),]
  [SERIES (type column, ...),]
  [ACTIONS (trigger = action, ...)]
);
```

All clauses inside the outer `( )` are separated by commas. The closing `)` ends the statement. `SOURCE` is required for all types except `TEXT`, `DATEPICKER`, `SLIDER`, and `SEARCH`.

### Visual types

| Type | Description | Renderer |
|------|-------------|----------|
| `BAR` | Vertical bar chart. Supports grouping via a `SERIES` mapping. | ECharts |
| `HBAR` | Horizontal bar chart. Same mappings as BAR, bars run left-to-right. | ECharts |
| `LINE` | Line chart. Supports multiple series and smooth curves. | ECharts |
| `SCATTER` | X/Y scatter plot. Each row becomes one point. | ECharts |
| `PIE` | Pie chart. One slice per row. | ECharts |
| `DONUT` | Donut chart. Same mappings as PIE; center hole rendered. | ECharts |
| `COMBO` | Combined bar + line chart. Use `SERIES (BAR col, LINE col)` to assign series types. | ECharts |
| `BOXPLOT` | Box-and-whisker plot for distribution visualisation. | ECharts |
| `TREEMAP` | Hierarchical area chart. One rectangle per row, sized by value. | ECharts |
| `HEATMAP` | Grid heatmap. Requires X, Y, and VALUE mappings. | ECharts |
| `GAUGE` | Radial KPI gauge. Single VALUE from first data row against a min/max arc. | ECharts |
| `FUNNEL` | Conversion funnel. Each row is one stage (LABEL + VALUE). | ECharts |
| `WATERFALL` | Cumulative change chart. Positive values rise, negative values fall. | ECharts |
| `TABLE` | Paginated, scrollable data grid. Supports `FORMATTING` for conditional cell colors. | HTML `<table>` |
| `CARD` | Single large KPI number with an optional label. | Styled `<div>` |
| `TEXT` | Free-form text or HTML block. Uses the `VALUE` option, not a SOURCE query. | `<div>` |
| `SLICER` | Dropdown parameter selector. SOURCE provides the option list. | `<select>` |
| `DATEPICKER` | Date input control. No SOURCE required. | `<input type="date">` |
| `SLIDER` | Numeric range slider. No SOURCE required. | `<input type="range">` |
| `MULTISELECT` | Multi-value checkbox list. SOURCE provides the option list. | Checkbox list |
| `SEARCH` | Free-text search box with debounce. No SOURCE required. | `<input type="text">` |

### SOURCE

The `SOURCE` clause provides the data for the visual. It can be:

```sql
-- a) A pre-populated temp table (set earlier in the same script)
SOURCE = #my_temp_table

-- b) An inline SELECT (must be wrapped in parentheses)
SOURCE = (SELECT region, SUM(revenue) AS rev FROM #summary GROUP BY region)
```

Inline SELECTs are evaluated at build time and their results are snapshotted into the manifest. If you need the visual to refresh independently, use `CREATE DATASET` and reference the dataset by name.

`TEXT`, `DATEPICKER`, `SLIDER`, and `SEARCH` visuals do not require `SOURCE`. `MULTISELECT` requires a `SOURCE` to populate the option list.

### TITLE and SUBTITLE

```sql
CREATE VISUAL RevenueCard AS CARD (
  SOURCE = ...,
  TITLE    = 'Total Revenue',
  SUBTITLE = 'All regions, YTD',
  MAPPINGS (VALUE = revenue)
);
```

`TITLE` overrides the visual name as the chart heading. `SUBTITLE` appears below the title in smaller text.

### MAPPINGS

Maps the columns returned by `SOURCE` to semantic **roles** expected by the renderer. Roles are case-insensitive.

#### BAR, HBAR, and LINE

| Role | Description |
|------|-------------|
| `X` | Category axis column (string or date). Required. |
| `Y` | Value axis column (numeric). Required. |
| `SERIES` | Optional grouping column. Each distinct value becomes a separate coloured series. |

```sql
-- Simple: single series
MAPPINGS (X = month, Y = revenue)

-- Grouped: one line/bar per region
MAPPINGS (X = month, Y = revenue, SERIES = region)
```

#### SCATTER

| Role | Description |
|------|-------------|
| `X` | Horizontal axis column (numeric). |
| `Y` | Vertical axis column (numeric). |

```sql
MAPPINGS (X = score, Y = rank)
```

#### PIE and DONUT

| Role | Description |
|------|-------------|
| `LABEL` | Column to use as the slice label. |
| `VALUE` | Column to use as the slice size (numeric). |

```sql
MAPPINGS (LABEL = category, VALUE = total)
```

#### CARD

| Role | Description |
|------|-------------|
| `LABEL` | Small caption rendered above the number. If omitted the column name is used. |
| `VALUE` | The primary number or string to display large. |

```sql
MAPPINGS (VALUE = val, LABEL = lbl)
```

#### SLICER

SLICER uses `SOURCE` to provide option rows and `ACTIONS` to bind the selection to a parameter. The `MAPPINGS` clause specifies which column from the source holds the display value.

```sql
CREATE VISUAL RegionFilter AS SLICER (
  SOURCE = (SELECT DISTINCT region FROM #summary ORDER BY region),
  MAPPINGS (VALUE = region),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@region, region))
);
```

#### MULTISELECT

Same pattern as SLICER: `SOURCE` provides options, `ACTIONS` binds the selection.

```sql
CREATE VISUAL CategoryFilter AS MULTISELECT (
  SOURCE = (SELECT DISTINCT category FROM #products ORDER BY category),
  MAPPINGS (VALUE = category),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@category, category))
);
```

#### HEATMAP

| Role | Description |
|------|-------------|
| `X` | Horizontal axis column. |
| `Y` | Vertical axis column. |
| `VALUE` | Cell intensity value (numeric). |

```sql
MAPPINGS (X = hour, Y = weekday, VALUE = count)
```

#### TREEMAP

| Role | Description |
|------|-------------|
| `LABEL` | Rectangle label column. |
| `VALUE` | Rectangle size column (numeric). |

```sql
MAPPINGS (LABEL = product, VALUE = revenue)
```

#### GAUGE

| Role | Description |
|------|-------------|
| `VALUE` | The current gauge reading (numeric, first data row only). |
| `MAX` | Optional column providing the maximum value for the arc. |
| `LABEL` | Optional label shown at the center of the gauge. |

```sql
CREATE VISUAL RevenueKpi AS GAUGE (
  SOURCE = (SELECT 73 AS pct, 100 AS target, 'Revenue Target' AS lbl),
  MAPPINGS (VALUE = pct, MAX = target, LABEL = lbl),
  OPTIONS (MIN = 0, MAX = 100, TITLE = 'Revenue vs Target')
);
```

`MIN` and `MAX` options override column-derived bounds. Both default to `0` / `100` when omitted.

#### FUNNEL

| Role | Description |
|------|-------------|
| `LABEL` | Stage name column. |
| `VALUE` | Numeric value for each stage (determines bar width). |

```sql
CREATE VISUAL SalesFunnel AS FUNNEL (
  SOURCE = (SELECT Stage, Leads FROM #funnel ORDER BY Leads DESC),
  MAPPINGS (LABEL = Stage, VALUE = Leads)
);
```

#### WATERFALL

| Role | Description |
|------|-------------|
| `X` | Category / period column. |
| `Y` | Numeric delta — positive values rise, negative values fall. |

```sql
CREATE VISUAL CashFlow AS WATERFALL (
  SOURCE = (SELECT Period, Delta FROM #cashflow),
  MAPPINGS (X = Period, Y = Delta)
);
```

Use the `COLORS (positive = '#5cb85c', negative = '#d9534f')` option inside `OPTIONS (COLORS (...))` to customise bar colors.

#### TABLE

TABLE visuals use all columns returned by `SOURCE` in definition order. No `MAPPINGS` clause is needed.

See [Conditional Formatting](#conditional-formatting) for applying cell colors to TABLE visuals.

#### COMBO

COMBO visuals use the `SERIES` block to assign which columns render as bars vs lines. The `X` mapping provides the category axis.

```sql
CREATE VISUAL SalesCombo AS COMBO (
  SOURCE = (SELECT month, revenue, units FROM #summary),
  MAPPINGS (X = month),
  SERIES (BAR revenue, LINE units)
);
```

### OPTIONS

General key/value options plus optional axis sub-blocks. All are optional:

```sql
OPTIONS (
  -- flat options:
  title   = 'Revenue Over Time',
  stacked = true,
  smooth  = true,
  FORMAT  = 'N0',

  -- axis sub-blocks (BAR, HBAR, LINE, SCATTER only):
  X_AXIS (
    label  = 'Month',
    min    = 0,
    max    = 100
  ),
  Y_AXIS (
    label  = 'Revenue ($)',
    min    = 0
  ),

  -- color map (any chart type):
  COLORS (
    'North' = '#4e79a7',
    'South' = '#f28e2b'
  ),

  -- legend position (any chart type):
  LEGEND (position = bottom)
)
```

#### Flat OPTIONS reference

| Key | Applies to | Values | Description |
|-----|------------|--------|-------------|
| `title` | All chart types | Any string | Chart title. Defaults to the visual name. Prefer the top-level `TITLE` clause. |
| `stacked` | BAR, HBAR, LINE | `true` / `false` | Stack series on top of each other. |
| `smooth` | LINE | `true` / `false` | Smooth curves via bezier interpolation. |
| `FORMAT` | CARD | .NET format string | Applies a numeric format to the VALUE column (e.g. `N0`, `C2`, `P1`). |

#### X_AXIS / Y_AXIS sub-block options

| Key | Values | Description |
|-----|--------|-------------|
| `label` | Any string | Human-readable axis label. |
| `min` | Numeric | Force axis minimum. |
| `max` | Numeric | Force axis maximum. |

#### COLORS

Maps category values to specific hex colors. Key is the category value (quoted if it contains spaces); value is a CSS color string.

```sql
COLORS (
  'East'  = '#4e79a7',
  'West'  = '#f28e2b',
  'North' = '#76b7b2'
)
```

#### LEGEND

Controls legend placement.

```sql
LEGEND (position = top)     -- top | bottom | left | right
```

### FORMATTING (Conditional Cell Colors) {#conditional-formatting}

Applies CSS colors to TABLE visual cells based on column value comparisons. Each rule specifies a column, a comparison operator, a threshold, and a color:

```sql
CREATE VISUAL FinancialSummary AS TABLE (
  SOURCE = (SELECT Category, Revenue, Margin FROM #summary),
  FORMATTING (
    Revenue < 0       THEN 'red',
    Revenue >= 100000 THEN '#28a745',
    Margin < 0.05     THEN 'orange'
  )
);
```

**Rule syntax:** `column operator threshold THEN 'color'`

| Operator | Meaning |
|----------|---------|
| `<` | Less than |
| `>` | Greater than |
| `<=` | Less than or equal |
| `>=` | Greater than or equal |
| `=` | Equal |
| `<>` | Not equal |

Multiple rules for the same column are evaluated top-to-bottom; the first matching rule wins. Numeric thresholds use numeric comparison; string thresholds use string equality (`=` / `<>`). `color` may be any CSS color value (`'red'`, `'#ff0000'`, `'rgba(255,0,0,0.5)'`).

### CROSS_FILTER

Setting `CROSS_FILTER = true` in the `OPTIONS` clause of a chart visual makes it act as a cross-filter source. Clicking a data point broadcasts a filter to all TABLE visuals on the same page that also have `CROSS_FILTER = true`. Clicking the same value again clears the filter.

```sql
CREATE VISUAL SalesByRegion AS BAR (
  SOURCE = (SELECT Region, Revenue FROM #sales),
  MAPPINGS (X = Region, Y = Revenue),
  OPTIONS (CROSS_FILTER = true)
);

CREATE VISUAL SalesDetail AS TABLE (
  SOURCE = (SELECT Region, Product, Revenue FROM #sales),
  OPTIONS (CROSS_FILTER = true)  -- becomes a filter target
);
```

When the user clicks "West" in the bar chart, the TABLE is filtered to show only rows where `Region = 'West'`. Cross-filtering operates client-side using the data already in the manifest — no server round-trip is needed.

### STYLE

Applies visual-level styling properties. Visual-level STYLE takes precedence over page-level THEME.

```sql
STYLE (
  THEME      = dark,          -- dark | light
  HEIGHT     = 400,           -- pixels
  WIDTH      = 600,           -- pixels
  BACKGROUND = '#1a1a2e',
  BORDER     = '1px solid #333'
)
```

### ACTIONS

Actions wire up interactive behaviour in the live dashboard:

```sql
ACTIONS (
  ON_CLICK  = DRILL_DOWN(Target = DetailChart, Key = region),
  ON_CHANGE = SET_PARAMETER(@region, region)
)
```

| Trigger | Description |
|---------|-------------|
| `ON_CLICK` | Fires when the user clicks a chart element (bar, slice, point). |
| `ON_CHANGE` | Fires when a SLICER, MULTISELECT, DATEPICKER, SLIDER, or SEARCH value changes. |

#### DRILL_DOWN

```sql
ON_CLICK = DRILL_DOWN(Target = <VisualName>, Key = <column>)
```

When clicked, passes the selected row's value in `Key` as a filter into the target visual's inline SELECT. The target visual is re-queried with the key value injected into the parameter context.

#### SET_PARAMETER

```sql
ON_CHANGE = SET_PARAMETER(@paramName, <columnRef>)
```

Sets the named `@param` to the selected value. Any visual whose inline `SELECT` references `@paramName` is automatically re-queried.

---

## CREATE DATASET

Pre-computes a named temp table that can be independently refreshed and optionally encrypted or compressed. Use this when multiple visuals share the same expensive base query, or when you want separate refresh cadences.

```
CREATE DATASET #<name>
  [REFRESH EVERY '<interval>']
  [TTL = '<duration>']
  [COMPRESS = ON|OFF]
  [ENCRYPT = MACHINE | PASSWORD | KEYFILE]
  [PASSWORD = '<password>']
  [KEYFILE  = '<path>']
AS ( SELECT ... );
```

### Encryption modes

| Mode | Description |
|------|-------------|
| `ENCRYPT = MACHINE` | Encrypts using a machine-bound key (DPAPI on Windows; OS keyring on Linux/macOS). No password or key file needed. Snapshot can only be decrypted on the same machine. |
| `ENCRYPT = PASSWORD, PASSWORD = '...'` | AES encryption with a user-supplied password. Portable — can be decrypted on any machine with the password. |
| `ENCRYPT = KEYFILE, KEYFILE = '...'` | AES encryption using a key file at the specified path. Portable with the key file. |

### Full example

```sql
-- Machine-bound (simplest — no credentials to manage)
CREATE DATASET #sales_snap
  REFRESH EVERY '1h'
  TTL = '24h'
  COMPRESS = ON
  ENCRYPT = MACHINE
  AS (SELECT region, product, SUM(revenue) AS revenue
      FROM sales
      GROUP BY region, product);

-- Password-protected (portable)
CREATE DATASET #sales_secure
  ENCRYPT = PASSWORD
  PASSWORD = 'MyS3cretPhrase'
  AS (SELECT * FROM sensitive_table);

-- Key-file protected
CREATE DATASET #sales_keyfile
  ENCRYPT = KEYFILE
  KEYFILE = 'C:\keys\report.key'
  AS (SELECT * FROM sales);
```

### CREATE DATASET clause reference

| Clause | Required | Description |
|--------|----------|-------------|
| `#<name>` | Yes | Temp table name. The `#` prefix is automatically added if omitted. |
| `REFRESH EVERY '<interval>'` | No | Re-compute interval. Format: `<n>s`, `<n>m`, `<n>h`, or `<n>d` (e.g. `'30m'`, `'1h'`, `'7d'`). |
| `TTL = '<duration>'` | No | How long a snapshot stays valid before `IsStale` returns true. Same interval format. |
| `COMPRESS = ON` | No | Compress the snapshot file on disk. Default `OFF`. |
| `ENCRYPT = MACHINE\|PASSWORD\|KEYFILE` | No | Encryption mode. See table above. |
| `PASSWORD = '<password>'` | Required when `ENCRYPT = PASSWORD` | Password for AES encryption. |
| `KEYFILE = '<path>'` | Required when `ENCRYPT = KEYFILE` | Absolute path to the AES key file. Linter raises an error if `ENCRYPT = KEYFILE` but `KEYFILE` is absent. |
| `AS ( SELECT ... )` | Yes | The query to execute. Must be the last clause before the semicolon. |

---

## CREATE PAGE

Arranges visuals and containers into a named layout. Multiple pages can be defined in one script; the web dashboard renders each as a distinct section.

```
CREATE PAGE <name> AS LAYOUT (
  STRUCTURE = '<grid-template-areas>',
  MAP (
    '<slot>' = VisualOrContainerName,
    ...
  )
  [, STYLE (key = value, ...)]
)
[WITH PARAMETERS (@param = default, ...)]
;
```

### STRUCTURE string

`STRUCTURE` is a CSS grid-template-areas string. Slot letters appear as space-separated names within a row; rows are separated by `/`.

```sql
-- Single visual filling the full width
STRUCTURE = 'A'

-- Two visuals side by side
STRUCTURE = 'A B'

-- Two rows: header spanning both columns, then two below
STRUCTURE = 'A A / B C'

-- Three rows: KPI row, chart row, table row
STRUCTURE = 'A B C / D D D / E E E'
```

Each unique letter becomes a grid area. The renderer calculates column count from the maximum number of distinct letters in any single row.

### MAP

`MAP` assigns each visual or container to a slot letter. Slot letters must match those used in `STRUCTURE`.

```sql
MAP (
  'A' = KpiCard,
  'B' = BarChart,
  'C' = LineChart,
  'D' = DataTable
)
```

### STYLE on PAGE

Applies page-level styling. The `THEME` key cascades to all charts on the page unless overridden at the visual level.

```sql
STYLE (
  THEME      = dark,
  BACKGROUND = '#0f0f1a'
)
```

### WITH PARAMETERS

Declares page-level parameters with optional default values. These drive dynamic queries when a SLICER or other filter control fires `SET_PARAMETER`.

**Untyped (legacy):**

```sql
WITH PARAMETERS (@region = 'All', @year = '2024')
```

**Typed declarations** — add `AS type` and the `DEFAULT` keyword:

```sql
WITH PARAMETERS (
    @startDate AS DATE    DEFAULT '2024-01-01',
    @endDate   AS DATE    DEFAULT '2024-12-31',
    @minSales  AS NUMBER  DEFAULT '0',
    @region    AS VARCHAR DEFAULT 'All'
)
```

Supported types: `DATE`, `DATETIME`, `NUMBER`, `INT`, `DECIMAL`, `VARCHAR`. The declared type is stored in the manifest under `parameterTypes` and can be used by the runtime for type-safe casting before passing to queries. Untyped parameters are treated as `VARCHAR`.

When a parameter changes the DashboardService re-evaluates all visuals whose inline SELECTs reference that parameter. Unaffected visuals are not re-queried.

### Full page example

```sql
CREATE PAGE Overview AS LAYOUT (
  STRUCTURE = 'A B / C C / D D',
  MAP (
    'A' = TotalRevenue,
    'B' = RegionFilter,
    'C' = RevenueByRegion,
    'D' = SalesTable
  ),
  STYLE (THEME = dark)
)
WITH PARAMETERS (@region = 'All');
```

---

## CREATE CONTAINER

Groups multiple visuals into a single layout region, optionally with scrolling. Useful when many visuals share one page slot.

```
CREATE CONTAINER <name> AS BOX|SCROLL (
  [STYLE (key = value, ...),]
  VISUALS (VisualA, VisualB, ...)
);
```

| Type | Description |
|------|-------------|
| `BOX` | Fixed-height container. Visuals are stacked vertically inside. |
| `SCROLL` | Scrollable container. Overflow visuals scroll within the container. |

```sql
CREATE CONTAINER KpiRow AS BOX (
  STYLE (HEIGHT = 200),
  VISUALS (TotalRevenue, TotalUnits, AvgOrderValue)
);

-- Reference the container in MAP just like a visual
CREATE PAGE Main AS LAYOUT (
  STRUCTURE = 'A A / B C',
  MAP (
    'A' = KpiRow,
    'B' = RevenueChart,
    'C' = SalesTable
  )
);
```

---

## CREATE NAVIGATION

Adds a navigation bar that controls which page is visible. The bar renders above the page content.

```
CREATE NAVIGATION <name> AS TAB|BUTTON|LINK (
  [ORIENTATION = HORIZONTAL|VERTICAL,]
  [DEFAULT = <PageName>]
)
WITH PAGES (Page1, Page2, ...);
```

| Nav type | Rendering |
|----------|-----------|
| `TAB` | Tab-style bar (default). |
| `BUTTON` | Pill buttons. |
| `LINK` | Separator-delimited links. |

```sql
CREATE NAVIGATION MainNav AS TAB (
  ORIENTATION = HORIZONTAL,
  DEFAULT = Overview
)
WITH PAGES (Overview, Details, Trends);
```

If `DEFAULT` is omitted, the first page in the list is shown on load.

---

## CLI — etl-sql-report

### build

Evaluates the script, builds a `ReportManifest`, and writes output files:

```sh
etl-sql-report build report.rptsql
etl-sql-report build report.rptsql --output out/dashboard.md
etl-sql-report build report.rptsql --format json
etl-sql-report build report.rptsql --format pdf
```

**Output files produced:**

| File | Description |
|------|-------------|
| `<script>.report.md` | GitHub Flavored Markdown document. Default when `--format md`. |
| `<script>.report.json` | Raw manifest JSON. Default when `--format json`. |
| `<script>.report.pdf` | PDF export via QuestPDF. Charts rendered as SVGs, tables capped at 500 rows. Default when `--format pdf`. |
| `<script>.snapshot.json` | Snapshot of all visual data rows and metadata. Always written alongside the report. |

**Flags:**

| Flag | Default | Description |
|------|---------|-------------|
| `--output`, `-o` | `<script>.report.<ext>` | Override the output file path. |
| `--format`, `-f` | `md` | Output format: `md`, `json`, or `pdf`. |

### refresh

Re-evaluates the script and updates the snapshot without writing a new report document:

```sh
etl-sql-report refresh report.rptsql
```

The snapshot is stored alongside the script as `<script>.snapshot.json`. The ReportPlayer considers the snapshot stale if the script file has been modified since the snapshot was built, or if the TTL (default 24 h) has elapsed.

### serve

Starts the web dashboard at `http://localhost:5200`:

```sh
# Single report
etl-sql-report serve report.rptsql

# Multi-report catalog (see reports.json below)
etl-sql-report serve --manifest reports.json
```

Internally this launches `ETL-SQL.ReportPlayer` (the Kestrel ASP.NET server) and opens the browser after 2.5 s. Keep the process running; the dashboard is served for as long as the process is alive.

---

## Multi-report hosting

Multiple reports can be hosted together using a `reports.json` manifest file:

```json
{
  "reports": [
    { "name": "sales",     "path": "reports/sales.rptsql",     "description": "Regional sales dashboard" },
    { "name": "inventory", "path": "reports/inventory.rptsql", "description": "Inventory levels by SKU" }
  ]
}
```

Start the server:

```sh
etl-sql-report serve --manifest reports.json
```

The catalog page at `http://localhost:5200` lists all reports. Each report is accessible at `http://localhost:5200/reports/<name>`. API routes are prefixed per-report: `/reports/<name>/api/manifest`, `/reports/<name>/api/refresh`, etc.

---

## ReportPlayer — web dashboard

The ReportPlayer is a lightweight ASP.NET Minimal API server that hosts the report as an interactive dashboard.

### Endpoints (single-report mode)

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/` | Serves the full dashboard HTML with pre-embedded manifest. |
| `GET` | `/api/manifest` | Returns the current `ReportManifest` as JSON. |
| `GET` | `/api/refresh` | Triggers a full rebuild and returns `{ rebuilt: true, visuals: N }`. |
| `POST` | `/api/parameter` | Updates one parameter and triggers a selective rebuild. Body: `{ "name": "@region", "value": "West" }`. |
| `POST` | `/api/parameters` | Updates multiple parameters in a single request. Body: `{ "params": [{ "name": "@region", "value": "West" }, ...] }`. |

### Endpoints (multi-report mode)

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/` | Catalog page listing all reports. |
| `GET` | `/reports/{name}` | Dashboard for the named report. |
| `GET` | `/reports/{name}/api/manifest` | Report manifest JSON. |
| `GET` | `/reports/{name}/api/refresh` | Force rebuild. |
| `POST` | `/reports/{name}/api/parameter` | Set one parameter. |
| `POST` | `/reports/{name}/api/parameters` | Set multiple parameters. |

### Selective refresh

When a parameter changes, `DashboardService` checks which visuals depend on that parameter (by scanning the inline SELECT for variable references) and re-queries only those visuals. Unaffected visuals keep their current data without re-evaluation.

### Staleness banner

If the manifest was built before the script file was last written, or if more than 24 hours have passed, a yellow banner appears:

> ⚠ Snapshot may be stale — run `etl-sql-report refresh` to update.

You can also hit `/api/refresh` to force a live rebuild without restarting the server.

### Dashboard rendering (report-runtime.js)

The dashboard frontend is a single vanilla-JS file (`wwwroot/report-runtime.js`) that works in two modes:

- **VS Code WebviewPanel**: reads `window.__MANIFEST__` injected by the extension.
- **Web (ReportPlayer)**: uses pre-embedded manifest (single-report) or fetches from `/api/manifest` (multi-report).

Visual rendering uses [Apache ECharts v5](https://echarts.apache.org/) for all chart types. ECharts is loaded from CDN; no bundler is required.

---

## VS Code preview

With a `.rptsql` file open, run **ETL-SQL: Preview Report** from the command palette or click the `$(graph)` icon in the editor title bar. A webview panel opens beside the editor and auto-refreshes every time you save the file.

**Configuration:**

| Setting | Default | Description |
|---------|---------|-------------|
| `etlsql.reportCliPath` | *(empty)* | Full path to `etl-sql-report.exe`. Leave empty to use `dotnet run` from the source tree in development. |

---

## Linter rules (language server)

The language server checks `.rptsql` files automatically:

| Rule | Severity | Condition |
|------|----------|-----------| 
| `VisualSourceExists` | Warning | `SOURCE = #table` references a temp table not defined earlier in the script. |
| `VisualMappingColumnExists` | Warning | A `MAPPINGS` role references a column not returned by the `SOURCE` inline SELECT. |
| `PageVisualReferenced` | Warning | A `MAP` entry references a visual or container name that is not defined in the script. |
| `DatasetRefreshInterval` | Warning | `REFRESH EVERY` value does not match the `<n>s/m/h/d` format. |
| `DatasetEncryptWithoutKey` | **Error** | `ENCRYPT = KEYFILE` without a `KEYFILE` clause, or `ENCRYPT = PASSWORD` without a `PASSWORD` clause. |
| `LayerOrder` | Warning | A visual references a dataset defined later in the script, or a page references a visual defined later. Forward references are not supported. |
| `InsertColumnCountMismatch` | Warning | `INSERT INTO` omits the column list and the SELECT provides fewer columns than the target table. |

---

## ReportManifest JSON schema

The snapshot and API both return this structure:

```jsonc
{
  "source":      "C:/reports/sales.rptsql",
  "builtAt":     "2026-04-12T18:00:00Z",
  "title":       "Sales Dashboard",
  "description": "Regional revenue analysis",

  "visuals": [
    {
      "name":       "RevenueByRegion",
      "visualType": "Bar",
      "chartConfig": "{ /* ECharts option object JSON */ }",
      "columns":  ["region", "revenue"],
      "rows":     [["East", "12000"], ...],
      "options":  {
        "mapping:x":       "region",
        "mapping:y":       "revenue",
        "axis:x:label":    "Region",
        "axis:y:label":    "Revenue ($)"
      },
      "styles":   { "THEME": "dark" },
      "actions":  [
        { "type": "SET_PARAMETER", "trigger": "ON_CHANGE",
          "parameterName": "@region", "valueExpression": "region" }
      ]
    }
  ],

  "pages": [
    {
      "name":      "Overview",
      "structure": "A B / C C",
      "slotMap":   { "A": "TotalRevenue", "B": "RegionFilter", "C": "RevenueByRegion" },
      "parameters": { "@region": "All" },
      "styles":    { "THEME": "dark" }
    }
  ],

  "containers": [
    {
      "name":          "KpiRow",
      "containerType": "BOX",
      "visuals":       ["TotalRevenue", "TotalUnits"],
      "styles":        { "HEIGHT": "200" }
    }
  ],

  "navigations": [
    {
      "name":        "MainNav",
      "navType":     "TAB",
      "orientation": "HORIZONTAL",
      "defaultPage": "Overview",
      "pages":       ["Overview", "Details"]
    }
  ],

  "datasets": [
    {
      "tempTableName":   "#sales_snap",
      "refreshInterval": "1h",
      "ttl":             "24h",
      "lastRefresh":     "2026-04-12T18:00:00Z",
      "rowCount":        4800
    }
  ]
}
```

---

## Full working example

Source CSV columns: `region`, `product`, `units`, `revenue`, `month`

```sql
SET REPORT TITLE = 'Sales Dashboard';
SET REPORT DESCRIPTION = 'Regional and product-level revenue by month.';

DROP CONNECTION IF EXISTS c;
CREATE CONNECTION c ON FLATFILE('TestData/test_sales.csv');

-- Shared base dataset (refreshed every hour)
CREATE DATASET #summary
  REFRESH EVERY '1h'
  ENCRYPT = MACHINE
  AS (SELECT month, region, product,
             SUM(units)   AS units,
             SUM(revenue) AS revenue
      FROM c
      GROUP BY month, region, product);

-- Region filter slicer
CREATE VISUAL RegionFilter AS SLICER (
  SOURCE = (SELECT DISTINCT region FROM #summary ORDER BY region),
  MAPPINGS (VALUE = region),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@region, region))
);

-- KPI card: total revenue
CREATE VISUAL TotalRevenue AS CARD (
  SOURCE = (SELECT SUM(revenue) AS val, 'Total Revenue' AS lbl
            FROM #summary
            WHERE @region = 'All' OR region = @region),
  TITLE    = 'Total Revenue',
  MAPPINGS (VALUE = val, LABEL = lbl),
  OPTIONS  (FORMAT = 'C0')
);

-- Bar chart: revenue by region
CREATE VISUAL RevenueByRegion AS BAR (
  SOURCE = (SELECT region, SUM(revenue) AS revenue
            FROM #summary
            WHERE @region = 'All' OR region = @region
            GROUP BY region),
  TITLE    = 'Revenue by Region',
  MAPPINGS (X = region, Y = revenue),
  OPTIONS (
    X_AXIS (label = 'Region'),
    Y_AXIS (label = 'Revenue ($)', min = 0)
  )
);

-- Multi-series bar: revenue by region and month
CREATE VISUAL RevenueByRegionMonth AS BAR (
  SOURCE = (SELECT month, region, SUM(revenue) AS revenue
            FROM #summary
            GROUP BY month, region),
  TITLE    = 'Revenue by Region (Monthly)',
  MAPPINGS (X = month, Y = revenue, SERIES = region),
  OPTIONS  (stacked = true, X_AXIS (label = 'Month'), Y_AXIS (label = 'Revenue ($)', min = 0))
);

-- Donut chart: revenue share by product
CREATE VISUAL RevenueByProduct AS DONUT (
  SOURCE = (SELECT product, SUM(revenue) AS revenue
            FROM #summary
            GROUP BY product),
  TITLE    = 'Revenue by Product',
  MAPPINGS (LABEL = product, VALUE = revenue)
);

-- Line chart: units by month
CREATE VISUAL UnitsByMonth AS LINE (
  SOURCE = (SELECT month, SUM(units) AS units
            FROM #summary
            GROUP BY month),
  TITLE    = 'Units Sold by Month',
  MAPPINGS (X = month, Y = units),
  OPTIONS  (smooth = true, X_AXIS (label = 'Month'), Y_AXIS (label = 'Units Sold', min = 0))
);

-- Detail table: all rows
CREATE VISUAL SalesTable AS TABLE (
  SOURCE = #summary
);

-- KPI container
CREATE CONTAINER KpiRow AS BOX (
  VISUALS (TotalRevenue)
);

-- Navigation
CREATE NAVIGATION MainNav AS TAB (
  ORIENTATION = HORIZONTAL,
  DEFAULT = Overview
)
WITH PAGES (Overview, Trends);

-- Dashboard pages
CREATE PAGE Overview AS LAYOUT (
  STRUCTURE = 'A B / C C / D D',
  MAP (
    'A' = KpiRow,
    'B' = RegionFilter,
    'C' = RevenueByRegion,
    'D' = SalesTable
  )
)
WITH PARAMETERS (@region = 'All');

CREATE PAGE Trends AS LAYOUT (
  STRUCTURE = 'A A / B C',
  MAP (
    'A' = RevenueByRegionMonth,
    'B' = UnitsByMonth,
    'C' = RevenueByProduct
  )
);
```

---

## Roadmap (known gaps)

| Item | Status |
|------|--------|
| Conditional formatting on TABLE visuals | Implemented — `FORMATTING (col op val THEN 'color')` clause |
| GAUGE visual type (radial KPI gauge) | Implemented |
| Funnel chart visual type | Implemented |
| Waterfall chart visual type | Implemented |
| Excel export (`--format xlsx`) | Not implemented |
| Cross-filtering between visuals | Implemented — `CROSS_FILTER = true` in OPTIONS |
| Typed parameter declarations | Implemented — `@param AS DATE DEFAULT 'val'` syntax |
| PIVOT / UNPIVOT statement | Not implemented |
