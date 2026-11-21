# ETL-SQL for Visual Studio Code

<p align="center">
  <img src="https://raw.githubusercontent.com/etl-sql/ETL-SQL/main/docs/assets/logo-wordmark.png" alt="ETL-SQL" width="280" />
</p>

<p align="center">
  <a href="https://marketplace.visualstudio.com/items?itemName=Octocolee.etl-sql-vscode"><img src="https://img.shields.io/badge/VS%20Marketplace-v0.23.0-007ACC?style=for-the-badge&logo=visual-studio-code&logoColor=white" alt="Visual Studio Marketplace Version" /></a>
  <img src="https://img.shields.io/badge/Tests-10%2C000%2B%20Passing-brightgreen?style=for-the-badge&logo=checkmarx&logoColor=white" alt="10,000+ Tests Passing" />
  <img src="https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-4CAF50?style=for-the-badge" alt="Platform" />
</p>

---

> **Build cross-source data pipelines, in-stream data quality, governance, and dashboards without stitching together separate tools.**
>
> ETL-SQL is a single, deterministic SQL dialect that connects to 30+ sources, enforces data quality rules, and delivers interactive dashboards in source-control friendly scripts backed by 10,000+ tests.

**v0.22.0 is the first public release.** If you run into issues or have feedback, report them at [github.com/etl-sql/ETL-SQL/issues](https://github.com/etl-sql/ETL-SQL/issues).

<p align="center">
  <img src="https://raw.githubusercontent.com/etl-sql/ETL-SQL/main/docs/assets/vscode-demo.gif" alt="ETL-SQL VS Code Extension Demo" />
</p>

<p align="center">
  <em>Schema autocomplete · inline diagnostics · live results console · report preview · notebook execution · data lineage</em>
</p>

---

## Getting Started in 60 Seconds

1. **Install** ETL-SQL Tools. The engine, language server, and report renderer are bundled for your platform — no SDK, runtime, or database required. *(If commands don't appear right away, restart VS Code once.)*
2. **Create** a `.etlsql` file in any folder and paste the [pipeline below](#try-it-right-now--no-database-required).
3. **Press F5.** Results open in the **ETL-SQL Results** panel.
4. **Make it a dashboard:** save the [dashboard example](#interactive-dashboard-sales_reportrptsql) as `sales_report.rptsql` and run **`F1` → `ETL-SQL: Preview Report`**.

Then follow the [5-Minute Quickstart](https://github.com/etl-sql/ETL-SQL/blob/main/docs/guides/onboarding/QUICKSTART.md) or open a ready-made dashboard from the [Report Cookbook](https://github.com/etl-sql/ETL-SQL/blob/main/docs/cookbooks/report/README.md).

---

## The Problem It Solves

You have data in SQL Server. More data in a Postgres instance. Your analyst wants a dashboard. Your manager wants it automated.

The typical answer: stitch together Python, a scheduler, a BI tool, and hope nothing breaks overnight.

**The ETL-SQL answer** (`revenue.rptsql` — a complete script; swap in your own servers):

```sql
-- @author: data_team;
-- @domain: Revenue;
-- @description: Daily revenue by region, CRM + warehouse;
-- @schedule: daily;

CREATE CONNECTION crm       AS MSSQL(SERVER='crm-prod', DATABASE='CRM', TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION warehouse AS POSTGRES(HOST='dw-host', DATABASE='analytics', USER='etl', PASSWORD='ENC:...');

-- 1. Pull active customers from CRM with governance metadata
SELECT
    c.CustomerId  /* @d: Internal customer key; @nullable: false */,
    c.Region      /* @d: Sales region; @owner: sales_ops */
INTO #customers
FROM crm.dbo.Customers c
WHERE c.Active = 1;

-- 2. Aggregate orders from the warehouse with in-stream data quality gates
SELECT
    o.CustomerId  /* @d: Internal customer key */
        EXPECT NOT NULL ON FAILURE THROW,
    SUM(o.Amount) /* @d: Total order revenue for period; @unit: USD */
        AS Revenue
        EXPECT >= 0     ON FAILURE THROW
INTO #orders
FROM warehouse.public.orders o
GROUP BY o.CustomerId;

-- 3. Join both sources in engine memory
SELECT c.Region, SUM(o.Revenue) AS Revenue
INTO #final
FROM #customers c
JOIN #orders o ON c.CustomerId = o.CustomerId
GROUP BY c.Region;

-- 4. Deliver a live dashboard from the same file
CREATE VISUAL RevenueChart AS BAR (
    SOURCE   = #final,
    MAPPINGS (X = Region, Y = Revenue),
    STYLE    (HEIGHT = '400px', THEME = dark)
);

CREATE PAGE Overview AS DASHBOARD (
    TITLE     = 'Revenue by Region',
    STRUCTURE = 'A',
    MAP ('A' = RevenueChart)
);
```

Save as `revenue.rptsql` and press **`F1` → `ETL-SQL: Preview Report`** to render the dashboard side-by-side with your code.

Files, SFTP, REST, and the rest of the [connectors](#connectors--everything-you-already-have) mix into the same script.

---

## Why ETL-SQL Instead of Your Current Stack?

| If you're using… | The ETL-SQL advantage |
| :--- | :--- |
| **Python + pandas** | No environment setup, no dependency drift. SQL is the orchestration language — readable and maintainable across your entire team. |
| **Visual ETL designers** | Plain-text scripts in Git. No proprietary XML or UI-locked project files. Run the same script from VS Code, the CLI, the Orchestrator, or CI/CD. |
| **Separate BI & reporting tools** | Data prep, quality gates, and interactive dashboards live in one `.rptsql` file — previewed side-by-side in VS Code and hosted in the Portal. |
| **Warehouse-only SQL (ELT)** | ETL-SQL moves data across heterogeneous databases, files, SFTP, and APIs, enforces in-stream quality gates, *and* renders dashboards from the same script. |
| **AI Agents & Copilots** | Plain-text, deterministic SQL with structured diagnostics and `WHAT_IF` dry-runs that agents can read, write, and review safely. |
| **Raw SQL scripts** | Cross-source joins, procedural control flow, SFTP, emails, file ops, scheduling, lineage, and reporting — all in one unified dialect. |

---

## Try It Right Now — No Database Required

Both examples use the built-in `MOCKDB()` connector. No credentials, no network access, no setup needed.

### ETL Pipeline (`pipeline.etlsql`)

```sql
CREATE CONNECTION demo AS MOCKDB();

BEGIN TRY
    -- 1. Extract and enforce in-stream Data Quality with first-class metadata tags
    SELECT
        Region      /* @d: Sales territory; @owner: rev_ops */
            EXPECT NOT NULL ON FAILURE THROW,
        Total       /* @d: Order total amount; @unit: USD */
            EXPECT >= 0     ON FAILURE THROW
    INTO #orders
    FROM demo.Orders;

    -- 2. Transform in engine memory
    SELECT
        Region,
        COUNT(*)               AS TotalOrders,
        SUM(Total)             AS Revenue,
        ROUND(AVG(Total), 2)   AS AvgOrder
    INTO #summary
    FROM #orders
    GROUP BY Region;

    -- 3. Output results
    SELECT Region, TotalOrders, Revenue, AvgOrder
    FROM #summary
    ORDER BY Revenue DESC;

    -- 4. Inspect column-level lineage automatically captured by the engine
    SELECT target_column, source_tables, source_columns
    FROM eng.lineage
    WHERE target_table = '#summary';

END TRY
BEGIN CATCH
    PRINT 'Pipeline failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
```

### Interactive Dashboard (`sales_report.rptsql`)

Press **`F1` → `ETL-SQL: Preview Report`** to see this render live in your editor.

```sql
SET REPORT TITLE       = 'Mock Sales Performance';
SET REPORT DESCRIPTION = 'Live dashboard powered by in-memory MOCKDB data';

CREATE CONNECTION demo AS MOCKDB();

SELECT Region, COUNT(*) AS OrderCount, SUM(Total) AS Revenue
INTO #sales_summary
FROM demo.Orders
GROUP BY Region;

CREATE VISUAL RegionalSalesChart AS BAR (
    SOURCE   = #sales_summary,
    MAPPINGS (X = Region, Y = Revenue),
    STYLE    (HEIGHT = '350px', THEME = dark)
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

---

## What This Extension Gives You

### ▶️ Interactive Results & Live Execution
- **F5 to Run** — execute the full script or highlight any query block to run just that selection.
- **Interactive Results Grid** — sort, filter, and page through multi-result sets directly in the editor.
- **Dataset Diff & Compare** — stack two result sets side-by-side with Compare Mode to inspect differences immediately.

### 🧠 IntelliSense That Knows Your Data
- **Live Schema Autocomplete** — connects to your real databases to complete table and column names as you type.
- **Dialect-Aware Linting** — catches cross-dialect mismatches before runtime (e.g. `TOP` against Postgres or `GETDATE()` against Oracle).
- **Hover Documentation** — hover over any built-in function or keyword for full signatures and copy-pasteable examples.
- **Variable & Temp Table Tracking** — tracks declared `@variables` and `#temp` tables across your entire session.

### 📊 Live Dashboard & Visual Reporting (`.rptsql`)
- **Side-by-Side Preview** — open a `.rptsql` file and watch the interactive dashboard render alongside your code on every save.
- **25+ Chart Types** — bar, line, combo, scatter, KPI cards, gauges, waterfalls, funnels, heatmaps, treemaps, Sankey, sunburst, Gantt, and choropleth/point maps, plus a `CUSTOM` grammar-of-graphics visual for anything else. All rendered as crisp SVG.
- **Interactive Controls** — test slicers, cascading filters, date pickers, sliders, and cross-highlighting right in the preview pane.
- **One-Click Export** — export rendered reports to SVG, PDF, or Markdown directly from VS Code.

### 🛡️ In-Stream Data Quality (`EXPECT`) & Lineage
- **Inline Validation Gates** — declare rules directly in queries (`EXPECT NOT NULL`, `EXPECT UNIQUE`, `EXPECT >= 0`, regex matches).
- **Flexible Failure Actions** — halt pipelines with `ON FAILURE THROW`, warn with `ON FAILURE WARN`, or route invalid records to quarantine tables with `ON FAILURE QUARANTINE`.
- **Queryable Lineage** — query column-level lineage and dependencies with `SELECT ... FROM eng.lineage`.
- **OpenLineage Export** — send execution provenance to any OpenLineage-compatible catalog, such as Marquez or DataHub.

### 🔒 Built-in Security & Safety Guards
- **Project Sandbox** — scripts can't read files outside your project.
- **Credentials Guard** — automatically warns if plaintext passwords or connection strings are detected on save.
- **Save Policy Encryption** — automatically encrypts flagged plaintext credentials into `ENC:...` strings on save.

*Also included: [Notebooks (`.etlnb`)](https://github.com/etl-sql/ETL-SQL/blob/main/docs/guides/tooling/notebook-guide.md) for cell-based development, [AI Spec-to-Script Generation](https://github.com/etl-sql/ETL-SQL/blob/main/docs/spec-import/spec-driven-development.md) from specifications (the spec is sent to a chat model from your installed VS Code chat extension, such as GitHub Copilot), and the [Metadata Explorer Sidebar](https://github.com/etl-sql/ETL-SQL/blob/main/docs/guides/onboarding/getting-started.md).*

---

## Beyond the Editor — The Full ETL-SQL Platform

VS Code is where you author, test, and preview. When you're ready to share dashboards with your team or run pipelines on a schedule, the rest of the self-hosted **ETL-SQL platform** runs the exact same `.etlsql` and `.rptsql` scripts with zero rewrites.

| Component | What it gives your team |
| :--- | :--- |
| **ETL-SQL Portal** *(Reporting & Dashboards)* | Self-hosted web hub for interactive dashboards and paginated reports. Publish directly from VS Code (`ETL-SQL: Publish to Portal`) with multi-page navigation, live slicers and drill-through, PDF/SVG/Markdown export, scheduled email/Slack/Teams subscriptions, data-driven alerts, row-level security (RLS), role-based access control, and usage analytics. |
| **ETL-SQL Orchestrator** *(Job Scheduling & Operations)* | Always-on pipeline runner with cron schedules, retries, timeouts, incremental watermarks (`GET_JOB_STATE` / `SET_JOB_STATE`), quality-gate assertions (`ASSERT JOB`), quarantine replay, live per-statement run waterfalls, parameter overrides, and fleet health monitoring — managed from the Portal operations console or SQL (`CREATE JOB`, `TRIGGER JOB`). |
| **ETL-SQL Studio** *(Web Authoring)* | Browser-based workbench in the Portal for authoring, previewing, and approving reports and pipelines without leaving the web app. |
| **CLI & Terminal IDE** (`ETL-SQL`, `ETL-SQL-TUI`) | Single-binary execution for CI/CD, servers, and containers, plus a keyboard-driven terminal IDE with autocomplete, live results grid, and compare mode for headless or SSH environments. |

**One workflow end to end:** write and preview in VS Code → review plain-text scripts in Git → publish dashboards to the Portal and schedule pipelines in the Orchestrator.

Download installers (Windows MSI, Linux DEB, native archives) and read the deployment guide at [github.com/etl-sql/ETL-SQL](https://github.com/etl-sql/ETL-SQL/blob/main/docs/administration/platform/README.md).

---

## Connectors — Everything You Already Have

Mix and match any of these freely within a single script.

| Category | Connectors |
| :--- | :--- |
| **Relational Databases** | SQL Server · PostgreSQL · Oracle · MySQL · SQLite · ODBC |
| **Cloud Warehouses** | Snowflake · BigQuery |
| **Flat Files & Documents** | CSV · Excel · JSON · XML · Parquet · Avro |
| **Cloud Storage** | Azure Blob · AWS S3 · SharePoint |
| **File Transfer** | SFTP · FTP |
| **APIs** | REST / HTTP |
| **Messaging** | Kafka |
| **Graph & Document** | MongoDB · Neo4j |
| **Directory Services** | Active Directory / LDAP |
| **Notifications** | SMTP (email) · Slack · Microsoft Teams |
| **ETL-SQL Platform** | Portal · Orchestrator |
| **Development & Testing** | `MOCKDB()` — zero-config in-memory mock, no setup required |

---

## Requirements

Supported operating systems:
- **Windows**: x64 (ARM64 is not currently supported)
- **Linux**: x64 (ARM64 is not currently supported)
- **macOS**: x64 (Intel) and Apple Silicon (ARM64)
- **VS Code**: version 1.91.0 or higher

The extension bundles the native language server and engine binaries for supported platforms; no external runtime or SDK installation is required.

---

## Keyboard Shortcuts

| Windows / Linux | macOS | Action |
| :--- | :--- | :--- |
| `F5` | `F5` | Execute the active script |
| `F5` *(with selection)* | `F5` *(with selection)* | Execute only the highlighted lines |
| `Shift+Alt+F` | `Shift+Option+F` | Format the active script |
| `Ctrl+Shift+P` → `ETL-SQL:` | `Cmd+Shift+P` → `ETL-SQL:` | All ETL-SQL commands — preview, export, publish, stop execution |

---

## Extension Settings

Formatting options (`etlsql.format.*`: keyword casing, indent size, comma placement, JOIN and CASE layout) are in the VS Code Settings UI.

| Setting | Default | Description |
| :--- | :--- | :--- |
| `etlsql.executable.path` | `""` | Override path to `ETL-SQL.exe`. Leave blank to auto-detect. |
| `etlsql.server.path` | `""` | Override path to `ETL-SQL-LSP.exe`. Leave blank to auto-detect. |
| `etlsql.report.executable.path` | `""` | Override path to `ETL-SQL-Report.exe`. Leave blank to auto-detect. |
| `etlsql.report.autoOpenPreview` | `false` | Auto-open the Report Preview pane when opening a `.rptsql` file. |
| `etlsql.portal.url` | `""` | Base URL of your hosted ETL-SQL Portal (for publishing). |
| `etlsql.ai.model` | `""` | Chat model family for spec-driven script generation, from an installed VS Code chat extension such as GitHub Copilot. Blank uses the first available model. |

---

## Supported File Types

| Extension | Purpose |
| :--- | :--- |
| `.etlsql` | Standalone scripts — pipelines, migrations, scheduled jobs |
| `.rptsql` | Report-SQL — dashboards, charts, filters, and page layouts |
| `.etlnb` | ETL Notebooks — cell-based iterative development |

---

## Documentation

| Resource | Description |
| :--- | :--- |
| [5-Minute Quickstart](https://github.com/etl-sql/ETL-SQL/blob/main/docs/guides/onboarding/QUICKSTART.md) | Pipeline, data quality gate, and live dashboard — no database needed |
| [Getting Started](https://github.com/etl-sql/ETL-SQL/blob/main/docs/guides/onboarding/getting-started.md) | Core paradigms, connections, variables, control flow, and debugging |
| [Report-SQL & Dashboards](https://github.com/etl-sql/ETL-SQL/blob/main/docs/guides/reporting/README.md) | Dashboards, cascading slicers, parameters, RLS, and custom theming |
| [Data Quality & Governance](https://github.com/etl-sql/ETL-SQL/blob/main/docs/guides/data-quality/README.md) | Value rules (`EXPECT`), quarantine remediation, cross-table checks, and impact analysis |
| [ETL Pipelines & Orchestration](https://github.com/etl-sql/ETL-SQL/blob/main/docs/guides/pipelines/README.md) | Staged vs. streaming ingestion, modular scripts, parallel runs, and unit testing |
| [Report Cookbook](https://github.com/etl-sql/ETL-SQL/blob/main/docs/cookbooks/report/README.md) | Complete dashboards you can preview immediately — maps, Sankey, waterfall, forecasting, themes |
| [ETL Cookbook](https://github.com/etl-sql/ETL-SQL/blob/main/docs/cookbooks/etl/README.md) | Production-grade pipeline patterns — incremental loads, SCD Type 2, dead-letter queues, fuzzy matching |
| [Task Index](https://github.com/etl-sql/ETL-SQL/blob/main/docs/task-index.md) | Goal-oriented "How do I..." locator for common tasks |
| [Syntax Reference](https://github.com/etl-sql/ETL-SQL/blob/main/docs/syntax-index.md) | Complete language syntax reference and function signatures |
| [Data Connectors](https://github.com/etl-sql/ETL-SQL/blob/main/docs/reference/connectors/README.md) | Every connector, its options, and authentication patterns |
| [Lineage Reference](https://github.com/etl-sql/ETL-SQL/blob/main/docs/reference/statements/session-control/lineage.md) | Tags, inheritance rules, `SHOW LINEAGE`, `LINEAGE_TAGS`, OpenLineage export |
| [Spec-Driven Development](https://github.com/etl-sql/ETL-SQL/blob/main/docs/spec-import/spec-driven-development.md) | Full guide to the AI spec extraction + `gen-script` workflow |
| [Notebook Guide](https://github.com/etl-sql/ETL-SQL/blob/main/docs/guides/tooling/notebook-guide.md) | Cell execution model, cross-cell state, and notebook IntelliSense |
