# Report-SQL Scripting Guide
<!-- CreateTemplateStatement -->
<!-- CreateThemeStatement -->
<!-- SetTemplatePathStatement -->

Report-SQL extends ETL-SQL with dedicated statement types for building interactive dashboards: `SET REPORT TITLE`, `CREATE DATASET`, `CREATE VISUAL`, `CREATE PAGE`, `CREATE CONTAINER`, `CREATE NAVIGATION`, `CREATE BUTTON`, and `CREATE STYLE` — plus a CLI build tool and live browser hosts for serving reports.

---

> **Applies to:** every deployment profile. The same `.rptsql` runs under the CLI, the Report Player, the Orchestrator and the Portal without modification.

## How it works — architecture overview

```
┌─────────────────────┐    build / serve      ┌─────────────────────┐
│  (report script)    │                       │  (etl-sql-report)   │
│ your_report.rptsql  │ ──────────────────▶   etl-sql-report CLI   │
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
                  │  → .etlsnap           │           │  http://localhost:5200│
                  └───────────────────────┘           └──────────────────────┘
```

A `.rptsql` file is a normal ETL-SQL script that may also contain Report-SQL statements. The engine evaluates it exactly like any `.etlsql` file; the new statements register definitions in the execution context. After evaluation the `ManifestBuilder` snapshots the data and produces a `.etlsnap` package — an encrypted zip file containing the layout JSON (`layout.json`) and high-performance Arrow IPC files (`.arrow`) for large visuals. This is consumed by the Portal and local viewers to serve data instantly.

### The Report Designer (Visual WYSIWYG)

For developers who prefer a visual approach to layouts, ETL-SQL includes a **Report Designer** integrated directly into the Portal (under `/designer`) and as a webview panel in the VS Code extension. The designer allows you to drag-and-drop visuals onto a 12-column grid, bind them to datasets, and configure styles. Behind the scenes, the designer parses and generates standard, clean `.rptsql` source code, ensuring that your reports remain fully source-controlled and git-diffable.

### The Three-Tier Logic Model

To build interactive reports that respond to user input (slicers, date pickers) efficiently, you must understand the **Three-Tier Logic** model. This distinguishes between logic that runs once during the build and logic that runs every time a user changes a filter.

| Tier | Name | Responsibility | Trigger | Patterns |
| :--- | :--- | :--- | :--- | :--- |
| **Tier 1** | **Ingestion** | Connecting to remote DBs, SFTP, or APIs. | Build / Scheduled Refresh | `CREATE CONNECTION`, `RUN SCRIPT` |
| **Tier 2** | **Preparation** | Heavy lifting: staging into `#temp` tables, massive JOINs, and `GROUP BY` on raw data. | Build / Scheduled Refresh | `SELECT ... INTO #staged FROM ...` |
| **Tier 3** | **Presentation** | Interactive filtering, drilling, and sorting for the user. | **User Interaction** (Slicers, etc.) | `CREATE VISUAL ... SOURCE = (SELECT ... WHERE @var = col)` |

> [!IMPORTANT]
> **The "Tier 2 Trap"**: Never put `@parameters` that are controlled by Slicers inside a `SELECT INTO #tempTable` statement. Why? Because the `#tempTable` is built **once** during the report evaluation. If the user changes a Slicer value, the `#tempTable` is **not** re-evaluated.
>
> **The Correct Pattern**: Always keep your `#temp` tables as "wide" and "unfiltered" as possible (containing all data the user might need). Then, apply the `@parameter` filters inside the `SOURCE = (SELECT ...)` clause of your `CREATE VISUAL`. The engine is optimized to re-evaluate only these "Tier 3" visual queries when a parameter changes.

```sql
-- ❌ INCORRECT (The Tier 2 Trap): @region will only filter on the initial load.
SELECT * INTO #summary FROM sales WHERE region = @region;
SELECT 'Product A' AS Label, 100 AS Value INTO #kpi
UNION ALL
SELECT 'Product B', 250;

CREATE VISUAL KpiCard AS CARD (SOURCE = #kpi, MAPPINGS(VALUE = Value, LABEL = Label));
```

Temp tables are reusable across multiple visuals, debuggable with a plain `SELECT`, and consistent with the rest of ETL-SQL.

---

## Quick start

```sql
-- 1. Connect and pull data
CREATE CONNECTION c AS FLATFILE('data/sales.csv');

SELECT region, SUM(revenue) AS revenue
INTO #summary
FROM c
GROUP BY region;

-- 2. Define a visual
CREATE VISUAL SalesByRegion AS BAR (
  SOURCE = #summary,
  MAPPINGS (X = region, Y = revenue),
  OPTIONS (
    DATA_LABELS = ON WITH (
      POSITION    = 'INSIDE_TOP',  -- TOP | BOTTOM | LEFT | RIGHT | CENTER | INSIDE | INSIDE_TOP | ...
      COLOR       = '#FFFFFF',     -- CSS color
      FONT_SIZE   = 12,            -- Numeric size
      FONT_WEIGHT = 'BOLD',        -- NORMAL | BOLD
      FONT_FAMILY = 'Arial',
      FORMAT      = 'N1'           -- .NET format string
    ),
    GRID = ON
  )
);

-- 3. Arrange on a page (STRUCTURE uses CSS grid-template-areas)
CREATE PAGE Main AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = SalesByRegion)
  )
);
```

Save as `report.rptsql`, then:

```sh
etl-sql-report build report.rptsql        # → report.report.md + report.etlsnap
etl-sql-report serve report.rptsql        # → opens http://localhost:5200
```

---

## Report Parameters (INPUT Variables)

Report-SQL uses standard ETL-SQL `@variables` for all parameters. Declare them with `DECLARE` at the top of your script — any variable marked `INPUT` can be overridden by the Portal at runtime (when running on-demand or via a subscription).

```sql
-- Basic text/number parameters
DECLARE @region   VARCHAR INPUT = 'All';
DECLARE @min_sale DECIMAL INPUT = 0;

-- Relative date range — the portal resolves these fresh on each subscription fire
DECLARE @start    RELDATE INPUT = 'M-1';  -- first day of last month
DECLARE @end      RELDATE INPUT = 'D';    -- today

SELECT region, SUM(revenue) AS revenue
INTO #summary
FROM prod.Sales
WHERE region   = CASE WHEN @region = 'All' THEN region ELSE @region END
  AND sale_date BETWEEN @start AND @end
  AND amount   >= @min_sale
GROUP BY region;
```

### INPUT parameter types

| Type | Portal control | Notes |
| :--- | :--- | :--- |
| `VARCHAR`, `STRING` | Text input | |
| `INT`, `DECIMAL` | Text input | Parsed as a number at runtime |
| `DATE`, `DATETIME` | Text input | ISO format: `2026-01-15` |
| `RELDATE` | Quick-pick buttons + text | Expression stored; resolved at each subscription fire |
| `LIST` | Text input | Comma-separated; e.g. `North,South,East` |
| `BOOL`, `BIT` | Text input | `true`/`false` or `1`/`0` |

### RELDATE parameters

`RELDATE` is the recommended type for date-range parameters in subscription-friendly reports. Instead of storing a fixed date, the subscriber stores an expression — `M-1`, `W-1`, `D-7` — that is resolved to a concrete date each time the subscription fires.

```sql
DECLARE @start RELDATE INPUT = 'M-1';  -- default: first day of last month
DECLARE @end   RELDATE INPUT = 'D';    -- default: today

-- Override week boundaries if your organisation uses a different week start:
SET WEEK_START_DAY = 'Sunday';
```

See the [RELDATE reference](../../reference/functions/datetime/reldate.md) for the full expression syntax.

### Connecting parameters to interactive filter visuals

Use `ACTIONS (ON_CHANGE = SET_PARAMETER(@var, value))` on `SLICER`, `DATEPICKER`, or `SLIDER` visuals to wire user input to a parameter variable. The variable's value is then injected into every visual that references it.

```sql
DECLARE @region VARCHAR = 'All';

CREATE VISUAL RegionFilter AS SLICER (
  SOURCE  = #regions,
  MAPPINGS (VALUE = region_name),
  DEFAULT = 'All',
  ACTIONS (ON_CHANGE = SET_PARAMETER(@region, value))
);

CREATE VISUAL SalesChart AS BAR (
  SOURCE = (SELECT * FROM #summary WHERE region = @region OR @region = 'All'),
  MAPPINGS (X = region, Y = revenue)
);
```

---

## Row-Level Security in Reports (RLS)

Portal executes report scripts securely using the logged-in viewer's identity. Within your report SQL, you can use built-in identity variables and predicate functions to dynamically filter datasets so users only see data they are authorized to view.

### Identity Variables

- `@@CURRENT_USER` — The username of the logged-in viewer.
- `@@CURRENT_USER_ID` — The numeric identifier of the viewer.
- `@@IS_ADMIN` — Boolean flag indicating if the viewer is an administrator (bypassing RLS by default).

### Identity Predicates

- `HAS_GROUP('group_name')` — Returns `TRUE` if the viewer is a member of the specified group.
- `USER_GROUPS()` — Table-valued function returning all groups the viewer belongs to.

### Examples

**1. Filtering by Username**
Filter rows where the manager matches the current viewer:
```sql
SELECT OrderId, Region, Total
INTO #filtered_orders
FROM prod_db.dbo.Orders
WHERE Manager = @@CURRENT_USER OR @@IS_ADMIN = TRUE;
```

**2. Group-based Regional Filter**
Filter rows using `HAS_GROUP` to check for region-specific access:
```sql
SELECT OrderId, Region, Total
INTO #filtered_orders
FROM prod_db.dbo.Orders
WHERE (Region = 'US' AND HAS_GROUP('US_Sales') = TRUE)
   OR (Region = 'EU' AND HAS_GROUP('EU_Sales') = TRUE)
   OR @@IS_ADMIN = TRUE;
```

**3. Dynamic Set-Membership Filter**
Use a subquery against `USER_GROUPS()` to join against a regional mapping table:
```sql
SELECT o.OrderId, o.Region, o.Total
INTO #filtered_orders
FROM prod_db.dbo.Orders o
JOIN #region_mappings m ON o.Region = m.RegionCode
WHERE m.GroupName IN (SELECT GroupName FROM USER_GROUPS())
   OR @@IS_ADMIN = TRUE;
```

> [!IMPORTANT]
> **Admin Bypass:** By default, administrators bypass RLS constraints if `@@IS_ADMIN = TRUE` is handled in your predicates. If your organization mandates filtering administrators as well, ensure the `Portal:Security:AdminBypassRowLevelSecurity` setting is set to `FALSE` in `appsettings.json`, or design your predicates without the `OR @@IS_ADMIN = TRUE` clause.

---

## SET REPORT TITLE / SET REPORT DESCRIPTION

Sets the report title and description displayed in the dashboard header and catalog page.

```sql
SET REPORT TITLE = 'Sales Dashboard';
SET REPORT DESCRIPTION = 'Regional and product-level revenue analysis for Q1 2026.';
```

Both statements are optional. If omitted the script filename is used as the title.

Markdown flags such as `TITLE_MD`, `SUBTITLE_MD`, and `TOOLTIP_MD` belong on `CREATE VISUAL`, `CREATE PAGE`, `CREATE CONTAINER`, or `CREATE BUTTON` style blocks. `SET REPORT TITLE` and `SET REPORT DESCRIPTION` are plain report metadata strings.

---

## Canonical Report-SQL Syntax

Report-SQL follows normal ETL-SQL statement style: name the object first, use `AS` before the body, and keep object-specific clauses inside the outer parentheses.

Use these forms as the preferred style in docs, samples, and generated scripts:

```sql
CREATE DATASET &sales_summary AS (
  SELECT region, SUM(revenue) AS revenue
  FROM #sales
  GROUP BY region
);

CREATE VISUAL RevenueByRegion AS BAR (
  SOURCE   = &sales_summary,
  MAPPINGS (X = region, Y = revenue),
  STYLE    (THEME = light)
);

CREATE PAGE Overview AS DASHBOARD (
  LAYOUT (
    STRUCTURE = 'A',
    MAP ('A' = RevenueByRegion),
    GAP = '16px'
  )
);
```

Syntax notes:

- `CREATE PAGE <name> AS DASHBOARD (...)` defines a page that loads result visuals immediately and keeps controls live.
- `CREATE PAGE <name> AS PAGINATED (...)` defines a page that stages prompt changes until an `APPLY_PARAMETERS` button is clicked.
- Page layout may be written directly with `STRUCTURE`/`MAP` or inside `LAYOUT (...)`. Prefer `LAYOUT (...)` in new scripts for consistency with containers.
- `CREATE DATASET` uses `&dataset` names only. Use `#temp` for intermediate engine tables created by `SELECT ... INTO #temp`; use `&dataset` for reusable report-owned datasets.
- `STYLE = StyleName` applies a named style. `STYLE (key = value, ...)` applies inline overrides. A standalone `STYLE (...)` statement defines global report defaults and is valid at top level.
- `SOURCE = #temp`, `SOURCE = &dataset`, `SOURCE = ViewName`, and `SOURCE = (SELECT ...)` are the canonical source forms. `#temp` is engine memory; `&dataset` is a report dataset definition or portal-registered dataset; `ViewName` is a session-scoped ETL-SQL query view created with `CREATE VIEW`.

### Report object buckets

| Bucket | Meaning |
| :--- | :--- |
| `SOURCE` | Data-producing query, table, or dataset reference. |
| `MAPPINGS` | Visual data roles that bind source columns to renderer fields. |
| `LAYOUT` | Page/container placement: structure, slot maps, gaps, responsive layout keys, and pinning behavior. |
| `STYLE` | Presentation and theme choices. |
| `OPTIONS` | Renderer-specific settings and non-layout object state. |
| `ACTIONS` | Outbound events emitted by visuals, controls, and buttons. |
| `INTERACTIONS` | Cross-visual selection, filtering, and highlighting behavior. |
| Portal commands | Administrative DDL/operations such as users, folders, grants, publishing, subscriptions, and refresh jobs. |

### Report documentation roles

Use each report document for a specific job:

- `docs/guides/report-sql.md` explains how to build reports and should favor complete, readable examples.
- `docs/guides/getting-started.md` is the exact syntax contract and should stay close to parser behavior.
- `docs/reference/visuals-reporting/report/*.md` feeds editor help and hover text, so examples must stay short and parser-backed.
- `samples/**/*.rptsql` files are runnable workflows and should use canonical syntax unless they are intentionally testing compatibility.

---

## Global Report Settings (SET REPORT)

Sets global metadata and overrides for the entire report. These settings affect the dashboard shell, navigation profiles, and master branding.

### Hierarchy & Cascade
- **Global vs. Local**: `SET REPORT` settings provide the **master default** for the dashboard. For example, setting `SET REPORT THEME = 'dark'` will theme all pages and the dashboard shell unless a specific `CREATE PAGE` has its own `STYLE (THEME = ...)` override.
- **Shell vs. Content**: Unlike `CREATE VISUAL` or `CREATE PAGE` which define the content, `SET REPORT` affects the **Host Shell** (the browser tab, the header bar, the sidebar container, and injected assets like JS/CSS).

| Key | Description | Example |
|-----|-------------|---------|
| `TITLE` | Custom report title shown in browser tab and header. | `SET REPORT TITLE = 'Ops Dashboard';` |
| `DESCRIPTION` | Summary shown on the report catalog page. | `SET REPORT DESCRIPTION = 'Daily monitoring';` |
| `CSS` | Raw CSS injected into the dashboard `<head>`. Useful for global font or brand overrides. | `SET REPORT CSS = '.v-card { border-radius: 20px; }';` |
| `JS` | Raw JavaScript executed on dashboard load. Use for tracking or custom interactions. | `SET REPORT JS = 'console.log("Report loaded");';` |
| `HEAD` | Custom HTML injected into the `<head>` section (e.g. meta tags). | `SET REPORT HEAD = '<meta name="custom" content="...">';` |
| `BODY` | Custom HTML injected at the start of the `<body>`. Useful for global banners. | `SET REPORT BODY = '<div>Maintenance Mode</div>';` |
| `FOOTER` | Custom HTML injected at the bottom of the page. | `SET REPORT FOOTER = '<span>© 2026 Admin</span>';` |
| `FAVICON` | URL to a custom favicon image (.png, .ico, .svg). | `SET REPORT FAVICON = '/assets/fav.png';` |
| `LOGO` | URL to a custom logo shown in the dashboard header. | `SET REPORT LOGO = '/assets/logo.svg';` |
| `BACKGROUND` | CSS Background for the shell (color, gradient, or URL). | `SET REPORT BACKGROUND = '#f0f0f0';` |
| `THEME` | Named theme applied as the **Global Default**. Themes the shell and all unthemed pages. | `SET REPORT THEME = 'dark';` |
| `NAVIGATION` | **Nav Mode Override**. Controls the shell's navigation behavior (e.g. `Compact`, `Hidden`, `Breadcrumbs`). | `SET REPORT NAVIGATION = 'Compact';` |

> [!TIP]
> **Difference from `CREATE NAVIGATION`**: `CREATE NAVIGATION` defines the **links and layout** of the menu (Tabs vs. Sidebar). `SET REPORT NAVIGATION` defines the **Shell Mode**, which can override the visibility or behavior of the navigation component (e.g., hiding it entirely for embedded reports).

```sql
-- Branding and Shell Customization
SET REPORT TITLE = 'Global Sales Dashboard';
SET REPORT THEME = 'glass'; -- Sets the default for all pages
SET REPORT LOGO = 'https://example.com/assets/logo-white.svg';
SET REPORT CSS = '
  :root { --accent-color: #ff9900; }
  .dashboard-header { border-bottom: 2px solid var(--accent-color); }
';
```

---

## Report Objects

`CREATE VISUAL`, `CREATE DATASET`, `CREATE PAGE`, `CREATE STYLE`, `CREATE THEME`, `CREATE CONTAINER`,
`CREATE NAVIGATION`, and `CREATE BUTTON` — and their `ALTER`/`DROP` forms — are defined in the
Report-SQL reference. Those pages are the source of truth for each object's syntax, properties, and
options; this guide covers the authoring workflow and links to them:

- [Visuals](../../reference/visuals-reporting/report/visual.md) - `CREATE VISUAL`, plus the [full visual-type catalog](../../reference/visuals-reporting/visuals/index.md) (bar, line, matrix, card, slicer, map, and 25+ more).
- [Datasets](../../reference/visuals-reporting/report/dataset.md) - `CREATE DATASET`, `EXPORT DATASET`, `PUBLISH DATASET`.
- [Pages](../../reference/visuals-reporting/report/page.md) · [Containers](../../reference/visuals-reporting/report/container.md) · [Navigation](../../reference/visuals-reporting/report/navigation.md) · [Buttons](../../reference/visuals-reporting/report/button.md).
- [Print Layout](../../reference/visuals-reporting/report/print-layout.md) - `PRINT_LAYOUT`, physical sheet sizes, margins, page breaks, and PDF compilation.
- [Styles](../../reference/visuals-reporting/report/style.md) and [Themes](../../reference/visuals-reporting/report/theme.md).
- [Actions](../../reference/visuals-reporting/report/actions.md) and [Interactions](../../reference/visuals-reporting/report/interactions.md).
- [Report-SQL reference overview](../../reference/visuals-reporting/README.md).

---

## Paginated Reports & Print Layout

While `DASHBOARD` pages provide fluid, responsive single-screen layouts, `PAGINATED` pages are designed for multi-page documents, invoice/statement generation, parameterized data runs, and pixel-precise physical printing or PDF export.

### 1. Paginated Page Definition

A paginated page declares physical dimensions via `PRINT_LAYOUT` (or `PAGE_LAYOUT`):

```sql
CREATE PAGE MonthlyInvoice AS PAGINATED (
  LAYOUT (
    STRUCTURE = 'H / T / S',
    MAP (
      'H' = InvoiceHeader,
      'T' = LineItemsTable,
      'S' = InvoiceSummary
    )
  ),
  PRINT_LAYOUT (
    PAGE_SIZE   = 'Letter',            -- Letter, A4, Legal, Executive, Tabloid, Custom
    ORIENTATION = 'PORTRAIT',          -- PORTRAIT or LANDSCAPE
    MARGINS     = (0.75, 0.75, 0.75, 0.75), -- top, right, bottom, left
    UNITS       = 'in',                -- in, cm, mm, pt, px
    OVERFLOW    = 'AUTO'               -- AUTO, CLIP, SPLIT, SCROLL
  )
);
```

### 2. Page Breaks & Print Control on Visuals

Control how individual visuals interact with page boundaries:

```sql
CREATE VISUAL InvoiceSummary AS CARD (
  SOURCE = #summary,
  MAPPINGS (VALUE = BalanceDue, LABEL = 'Balance Due'),
  PRINT_LAYOUT (
    PAGE_BREAK_BEFORE = ON,    -- Forces this visual onto a fresh physical page
    KEEP_TOGETHER     = ON     -- Prevents splitting the card across page edges
  )
);

-- Exclude interactive prompt visuals from printed output
CREATE VISUAL DatePrompt AS DATEPICKER (
  ACTIONS (ON_CHANGE = SET_PARAMETER(@asOfDate, value)),
  PRINT_LAYOUT (EXCLUDE_FROM_PRINT = ON)
);
```

### 3. Automatic Table Splitting

When a `TABLE` visual contains more rows than can fit on a single physical page, the engine's `PhysicalPageCompiler` automatically splits the table into consecutive physical page slices (`startRowIndex` to `endRowIndex`), repeating column headers on each sheet.

### 4. Parameter Staging and Deferred Execution

On `PAGINATED` pages:
- Prompts and slicers stage parameter changes locally until `APPLY_PARAMETERS` is clicked.
- Visuals with `FETCH = ON_RUN` (or `FETCH = AUTO` on paginated pages) defer heavy database queries until the user executes the page run.
- In automated scripts or CLI builds, use `--run-page` to compile all paginated result sets immediately.

## Building, serving, and previewing reports

The report tooling — the `etl-sql-report` CLI (`build`/`refresh`/`serve` and PDF export modes), multi-report hosting via `reports.json`, the ReportPlayer web dashboard and its API, VS Code preview, and the `.rptsql` linter rules — has its own reference: **[Report CLI, Hosting, and Preview](../../reference/visuals-reporting/report-cli.md)**.

---

## ReportManifest

The snapshot and the ReportPlayer API return a compiled `ReportManifest` (visuals, pages, containers, navigations, and datasets). See the [ReportManifest JSON schema](../../reference/visuals-reporting/report-manifest.md) and the [report runtime contract](../../reference/visuals-reporting/report-runtime-contract.md).

---

## Full working example

Source CSV columns: `region`, `product`, `units`, `revenue`, `month`

```sql
SET REPORT TITLE = 'Sales Dashboard';
SET REPORT DESCRIPTION = 'Regional and product-level revenue by month.';

DROP CONNECTION IF EXISTS c;
CREATE CONNECTION c AS FLATFILE('testdata/test_sales.csv');

-- Shared base dataset (cache expires after one hour)
CREATE DATASET &summary
  TTL = '1h'
  ENCRYPT = MACHINE
  AS (SELECT month, region, product,
             SUM(units)   AS units,
             SUM(revenue) AS revenue
      FROM c
      GROUP BY month, region, product);

-- Region filter slicer
CREATE VISUAL RegionFilter AS SLICER (
  SOURCE   = (SELECT DISTINCT region FROM &summary ORDER BY region),
  MAPPINGS (VALUE = region),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, region)),
  DEFAULT  = 'All'
);

-- KPI card: total revenue
CREATE VISUAL TotalRevenue AS CARD (
  SOURCE = (SELECT SUM(revenue) AS val, 'Total Revenue' AS lbl
            FROM &summary
            WHERE @region = 'All' OR region = @region),
  TITLE    = 'Total Revenue',
  MAPPINGS (VALUE = val, LABEL = lbl),
  OPTIONS  (FORMAT = 'C0')
);

-- Bar chart: revenue by region
CREATE VISUAL RevenueByRegion AS BAR (
  SOURCE = (SELECT region, SUM(revenue) AS revenue
            FROM &summary
            WHERE @region = 'All' OR region = @region
            GROUP BY region),
  TITLE    = 'Revenue by Region',
  MAPPINGS (X = region, Y = revenue),
  OPTIONS (
    X_AXIS (LABEL = 'Region'),
    Y_AXIS (LABEL = 'Revenue ($)', MIN = 0)
  )
);

-- Multi-series bar: revenue by region and month
CREATE VISUAL RevenueByRegionMonth AS BAR (
  SOURCE = (SELECT month, region, SUM(revenue) AS revenue
            FROM &summary
            GROUP BY month, region),
  TITLE    = 'Revenue by Region (Monthly)',
  MAPPINGS (X = month, Y = revenue, SERIES = region),
  OPTIONS  (STACKED = ON, X_AXIS (LABEL = 'Month'), Y_AXIS (LABEL = 'Revenue ($)', MIN = 0))
);

-- Donut chart: revenue share by product
CREATE VISUAL RevenueByProduct AS DONUT (
  SOURCE = (SELECT product, SUM(revenue) AS revenue
            FROM &summary
            GROUP BY product),
  TITLE    = 'Revenue by Product',
  MAPPINGS (LABEL = product, VALUE = revenue)
);

-- Line chart: units by month
CREATE VISUAL UnitsByMonth AS LINE (
  SOURCE = (SELECT month, SUM(units) AS units
            FROM &summary
            GROUP BY month),
  TITLE    = 'Units Sold by Month',
  MAPPINGS (X = month, Y = units),
  OPTIONS  (SMOOTH = ON, X_AXIS (LABEL = 'Month'), Y_AXIS (LABEL = 'Units Sold', MIN = 0))
);

-- Detail table: all rows with summary
CREATE VISUAL SalesTable AS TABLE (
  SOURCE = &summary,
  SUMMARY (
    GRAND_TOTAL = ON,
    SUM(revenue) AS 'Total Revenue'
  )
);

-- Brand logo
CREATE VISUAL BrandLogo AS IMAGE (
  OPTIONS (
    SRC = 'https://etl-sql.io/logo.png',
    FIT = 'contain'
  )
);

-- KPI container using modern grid layout
CREATE CONTAINER KpiRow AS BOX (
  LAYOUT (
    STRUCTURE = 'A B',
    MAP (
      'A' = BrandLogo,
      'B' = TotalRevenue
    )
  )
);

-- Dashboard pages
CREATE PAGE Overview AS DASHBOARD (
  STRUCTURE = 'A B / C C / D D',
  MAP (
    'A' = KpiRow,
    'B' = RegionFilter,
    'C' = RevenueByRegion,
    'D' = SalesTable
  )
);

CREATE PAGE Trends AS DASHBOARD (
  STRUCTURE = 'A A / B C',
  MAP (
    'A' = RevenueByRegionMonth,
    'B' = UnitsByMonth,
    'C' = RevenueByProduct
  )
);

-- Navigation (defined after pages so the LayerOrder linter is satisfied)
CREATE NAVIGATION MainNav AS TAB (
  ORIENTATION = HORIZONTAL,
  DEFAULT = Overview,
  PAGES (Overview, Trends)
);
```

---

## Native Card and Table Micro-Charts

Micro-charts use the same typed `ChartSpec`, `ChartDataSet`, and resolved `PlotPlan` contracts as the
representative chart set. They render as native SVG in the browser and PDF/Markdown email exports,
with semantic text in terminals and other non-graphical readers. These supported forms require no V8
server-side rendering.

```sql
SELECT 'Revenue' AS label, 42000 AS total INTO #summary;
SELECT 'Mon' AS day, 10000 AS amount INTO #daily;
INSERT INTO #daily (day, amount) VALUES ('Tue', 14500), ('Wed', 12800);

CREATE VISUAL RevenueCard AS CARD (
  SOURCE = #summary,
  MAPPINGS (
    LABEL = label,
    VALUE = total,
    SPARKLINE = #daily (X = day, Y = amount, TYPE = AREA)
  )
);
```

For tables, a wide sparkline consumes two or more scalar source columns. A progress indicator consumes
one numeric column. `TYPE` accepts `LINE`, `AREA`, or `BAR`; progress colors accept hexadecimal values.

```sql
CREATE VISUAL GoalStatus AS TABLE (
  SOURCE = #goals,
  MAPPINGS (
    team,
    SPARKLINE(jan, feb, mar, apr) LINE AS 'Trend',
    attainment PROGRESS_BAR (MIN = 0, MAX = 1, COLOR = '#16A34A') AS 'Attainment'
  )
);
```

Null sparkline values become gaps. Progress values are clamped to the declared range for geometry.
Invalid color strings fall back to the built-in safe palette. Array-column sparklines, normalized child
queries, conditional color maps, bullet charts, and HTML-template micro-chart macros are not part of
this syntax slice.

---

## Best Practices & FAQ

### How do I create a "Run-to-Data" paginated report?
For reports with heavy queries or many parameters, you should avoid refreshing the report on every character change. Use the **Run-to-Data** pattern:
1. Define the page as `CREATE PAGE <name> AS PAGINATED (...)`.
2. Put prompt controls and the Run button before the result visuals in the page layout.
3. Use `ACTIONS (ON_CLICK = APPLY_PARAMETERS)` on the Run button.
4. Leave result visuals at `FETCH = AUTO` or set `FETCH = ON_RUN` explicitly. The engine shows a placeholder until Run is clicked.

### Why do I get a "Failed to cast" error when using `RELDATE` parameters?
**Never explicitly `CAST` a `RELDATE` parameter to a `DATE` or `DATETIME` in your SQL.**
- `RELDATE` variables hold relative expressions like `'D-7'` or `'M-1'`. 
- If you write `CAST(@start AS DATE)`, the engine attempts to treat the string `'D-7'` as a literal date, which fails.
- **Correct Pattern**: Use the variable directly: `WHERE event_time >= @start`. The engine handles the resolution of the relative expression into an absolute timestamp automatically during comparison.

### `DATEPICKER` vs `RELDATEPICKER`
- Use `DATEPICKER` for fixed date parameters that do not need relative logic.
- Use `RELDATEPICKER` for `RELDATE INPUT` variables. It provides a specialized UI with quick-action buttons (Today, Yesterday, Last 7 Days, etc.) and ensures the string passed back to the engine is a valid relative date expression.

### Can I customize button styling?
Yes. The `STYLE (...)` block on a `CREATE BUTTON` supports standard CSS properties. The report runtime specifically handles:
- `BACKGROUND-COLOR` (or `BACKGROUND`)
- `COLOR`
- `PADDING`
- `BORDER-RADIUS`
- `FONT-WEIGHT` (e.g., `bold`)
- `FONT-SIZE`
- `BORDER`
- `BOX-SHADOW` (e.g., `0 2px 4px rgba(0,0,0,0.2)`)

```sql
CREATE BUTTON RunBtn AS (
    TITLE = 'Run Report',
    ACTIONS (ON_CLICK = APPLY_PARAMETERS),
    STYLE (
        BACKGROUND = '#2563eb', 
        COLOR = '#ffffff', 
        FONT-WEIGHT = 'bold',
        BOX-SHADOW = '0 2px 4px rgba(0,0,0,0.2)'
    )
);
```
