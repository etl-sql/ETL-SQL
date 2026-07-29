# ETL-SQL for Visual Studio Code

<p align="center">
  <img src="https://raw.githubusercontent.com/etl-sql/ETL-SQL/main/Docs/assets/logo-wordmark.png" alt="ETL-SQL" width="280" />
</p>

<p align="center">
  <a href="https://marketplace.visualstudio.com/"><img src="https://img.shields.io/badge/VS%20Code-Marketplace-007ACC?style=for-the-badge&logo=visual-studio-code&logoColor=white" alt="VS Code Marketplace" /></a>
  <img src="https://img.shields.io/badge/ETL--SQL-v0.12.0-5C6BC0?style=for-the-badge&logo=dotnet&logoColor=white" alt="ETL-SQL Version" />
  <img src="https://img.shields.io/badge/Platform-Windows%20|%20Linux%20|%20macOS-4CAF50?style=for-the-badge" alt="Platform" />
</p>

---

> **Build cross-source data pipelines, governance, and dashboards without stitching together three separate tools.**
>
> ETL-SQL is a single SQL dialect that connects to anything, transforms everything, and delivers interactive dashboards — all in one script.

---

## The Problem It Solves

You have data in SQL Server. More data in a Postgres instance. A vendor drops CSVs on an SFTP server every morning. Your analyst wants a dashboard. Your manager wants it automated.

The typical answer: stitch together Python, a scheduler, a BI tool, and hope nothing breaks overnight.

**The ETL-SQL answer:**

```sql
-- @author: data_team;
-- @domain: Revenue;
-- @description: Daily adjusted revenue — CRM + warehouse + vendor drop;
-- @schedule: daily;

CREATE CONNECTION crm      AS MSSQL(SERVER='crm-prod', DATABASE='CRM', TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION warehouse AS POSTGRES(HOST='dw-host', DATABASE='analytics', USER='etl', PASSWORD='ENC:...');
CREATE CONNECTION drops     AS SFTP(HOST='vendor.com', USER='feeds', KEYFILE='C:\keys\vendor.pem');

-- 1. Pull active customers from CRM
SELECT
    c.CustomerId  /* @d: Internal customer key; @nullable: false */,
    c.Region      /* @d: Sales region; @owner: sales_ops */,
    c.Tier        /* @d: Customer tier (Gold/Silver/Bronze); @example: Gold */
INTO #customers
FROM crm.dbo.Customers c
WHERE c.Active = 1;

-- 2. Aggregate orders from the warehouse
SELECT
    o.CustomerId  /* @d: Internal customer key */,
    SUM(o.Amount) /* @d: Total order revenue for period; @unit: USD */
        AS Revenue
INTO #orders
FROM warehouse.public.orders o
GROUP BY o.CustomerId;

-- 3. Download vendor adjustment file and load it
RECEIVE FILE FROM '/drops/daily_adjustments.csv'
    TO 'C:\stage\daily_adjustments.csv'
    AT drops WITH(OVERWRITE=ON);

CREATE CONNECTION adj_file AS FLATFILE(
    PATH      = 'C:\stage\daily_adjustments.csv',
    FORMAT    = 'DELIMITED',
    DELIMITER = ',',
    HEADER    = ON
);

SELECT
    CustomerId    /* @d: Customer key for adjustment */,
    Adjustment    /* @d: Revenue adjustment amount; @unit: USD */
INTO #adjustments
FROM adj_file;

-- 4. Join all three sources in engine memory
SELECT
    c.Region,
    c.Tier,
    SUM(o.Revenue + COALESCE(a.Adjustment, 0)) /* @d: Revenue after vendor adjustments; @unit: USD */
        AS AdjustedRevenue
INTO #final
FROM #customers c
JOIN #orders o      ON c.CustomerId = o.CustomerId
LEFT JOIN #adjustments a ON c.CustomerId = a.CustomerId
GROUP BY c.Region, c.Tier;

-- 5. Deliver a live dashboard in the same file
CREATE VISUAL RevenueChart AS BAR (
    SOURCE   = #final,
    MAPPINGS (X = Region, Y = AdjustedRevenue, SERIES = Tier),
    STYLE    (HEIGHT = '400px', THEME = dark)
);

CREATE PAGE Overview AS DASHBOARD (
    TITLE     = 'Adjusted Revenue by Region',
    STRUCTURE = 'A A',
    MAP ('A' = RevenueChart)
);
```

---

## See It In Action

![ETL-SQL VS Code Extension Demo](https://raw.githubusercontent.com/etl-sql/ETL-SQL/main/Docs/assets/vscode-demo.gif)

*Schema autocomplete · inline diagnostics · REPL results console · live report preview · cell-by-cell notebook execution · data lineage*

---

## Why ETL-SQL Instead of Your Current Stack?

| If you're using… | The ETL-SQL advantage |
| :--- | :--- |
| **Python + pandas** | No environment setup, no dependency hell. SQL is the orchestration language — readable by your whole team. |
| **SSIS / Azure Data Factory** | Plain-text scripts in source control. No XML designer files. Run anywhere: CLI, VS Code, cron, CI/CD. |
| **dbt** | ETL-SQL moves data *and* builds dashboards. No separate BI tool required. Works across SQL and non-SQL sources. |
| **Raw SQL scripts** | Cross-source joins, procedural control flow, SFTP, emails, file ops, scheduling, and reporting — all in one dialect. |

---

## Try It Right Now — No Database Required

Both examples use the built-in `MOCKDB()` connector. No credentials, no network access, no setup needed.

### ETL Pipeline (`pipeline.etlsql`)

```sql
CREATE CONNECTION demo AS MOCKDB();

BEGIN TRY
    -- Extract into engine-managed #temp tables
    SELECT Region, Total
    INTO #orders
    FROM demo.Orders;

    -- Validate assumptions before touching anything downstream
    ASSERT (SELECT COUNT(*) FROM #orders) > 0, 'Expected orders — got nothing.';

    -- Transform in engine memory
    SELECT
        Region,
        COUNT(*)    AS TotalOrders,
        SUM(Total)  AS Revenue,
        AVG(Total)  AS AvgOrder
    INTO #summary
    FROM #orders
    GROUP BY Region;

    SELECT Region, TotalOrders, Revenue, AvgOrder
    FROM #summary
    ORDER BY Revenue DESC;

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

### 🧠 IntelliSense That Knows Your Data

- **Live Schema Autocomplete** — connects to your actual databases and completes real table and column names as you type, not generic SQL keywords.
- **Dialect-Aware Linting** — warns you the moment you write `TOP 10` against a Postgres connection or `GETDATE()` against Oracle. Catch cross-dialect mistakes before they become runtime errors.
- **Hover Documentation** — hover over any built-in function or keyword to see its full signature and a copy-pasteable example, right in the editor.
- **Variable & Temp Table Tracking** — IntelliSense follows your `@variables` and `#temp` tables across the entire script session.

### ▶️ Execute and Explore Without Leaving the Editor

- **F5 to Run** — execute the full script, or highlight any block and run just that selection.
- **Interactive Results Grid** — sort, filter, and page through multi-result sets. Stack two result sets side-by-side with **Compare Mode** to diff them instantly.
- **Step Profiling** — inspect per-statement execution time, memory peaks, and spill events in the results panel.
- **Lineage-Aware Workflow** — inspect lineage metadata from executed scripts and use built-in lineage commands for audit output.

### 📊 See Your Dashboard Before You Ship It

- **Live `.rptsql` Preview** — open a report file and watch the interactive dashboard render side-by-side with your code. Every save refreshes the preview.
- **Interactive Preview** — slicers, date pickers, drill-downs, parameter overrides, and cross-highlighting work in the preview pane.
- **One-Click Export** — export your rendered report to PDF or Markdown directly from the preview panel.

### 📓 Notebooks for Iterative Development (`.etlnb`)

- **Cell-Based Pipelines** — break complex workflows into cells that share a single live session. Run them one at a time or all at once.
- **Mermaid Lineage in Cells** — lineage diagrams render inline as notebook output.
- **Export to Script** — convert any notebook back to a standard `.etlsql` file for headless deployment with a single command.

### 🔒 Security That Catches Mistakes Before You Ship Them

- **Credentials Guard** — scans for plaintext passwords and API keys on every save and surfaces a warning immediately.
- **Save Policy Encryption** — applies the script save policy to convert flagged plaintext secrets into `ENC:...` encrypted values when requested.

### 🗂️ Metadata Explorer Sidebar

Browse all active connections, catalog schemas, declared `@variables`, and live `#temp` tables in a structured sidebar panel — no context-switching required.

---

## 🏷️ Data Governance & Lineage

ETL-SQL keeps governance metadata in the script instead of a separate after-the-fact catalog. Inline tags such as `/* @pii; @classification: confidential; @owner: finance_ops */` travel with columns through joins, aggregations, and derived expressions, so lineage stays connected to the transformations that created the data.

After a script runs, lineage is queryable with `LINEAGE` and tag metadata is queryable through `eng.tags`, exportable as Markdown, and exportable as OpenLineage events for tools such as DataHub, Marquez, Airflow, Collibra, and Alation. See the [Lineage & Governance Reference](https://github.com/etl-sql/ETL-SQL/blob/main/docs/reference/statements/lineage.md) for the full tag catalog and examples.

---

## 🤖 AI-Assisted Script Generation

Vendor file specs usually arrive as PDFs, spreadsheets, Word docs, or sample files. The extension can turn those specs into an ETL-SQL starter pipeline with schema definitions, validation gates, quarantine scaffolding, and governance tags.

Configure `etlsql.ai.*`, then right-click a PDF, Excel, CSV, Word, JSON, or TXT spec and run **ETL-SQL: Generate Script from Spec**. The same workflow is available headlessly through the `gen-script` CLI flow. See [Spec-Driven Development](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Reference/Spec_Driven_Development.md) for provider setup, supported file types, and the JSON contract format.

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
| **Notifications** | SMTP (email) |
| **ETL-SQL Platform** | Portal · Orchestrator |
| **Development & Testing** | `MOCKDB()` — zero-config in-memory mock, no setup required |

---

## Getting Started

**1. Install This Extension**

Install **ETL-SQL Tools** from the VS Code Marketplace. The VSIX bundles everything you need to start immediately — the engine (`ETL-SQL.exe`), language server (`ETL-SQL-LSP.exe`), and report CLI (`ETL-SQL-Report.exe`) are included. No separate SDK download required.

**Want the full platform?** Download the optional extras from [github.com/etl-sql/ETL-SQL](https://github.com/etl-sql/ETL-SQL):
- **Terminal IDE** (`ETL-SQL-TUI.exe`) — full-featured TUI editor with autocomplete and results grid for headless environments
- **Orchestrator** — always-on job runner with scheduling, history, and dependency management
- **Portal** — multi-report hosting, permissions, subscriptions, alerts, and usage metrics

**2. Open a Folder and Create a Script**

Create a `.etlsql` file in any workspace folder. IntelliSense and linting activate automatically.

**3. Run It**

Press **F5** to execute and open the **ETL-SQL Results** console.

---

## Keyboard Shortcuts

| Shortcut | Action |
| :--- | :--- |
| `F5` | Execute the active script |
| `F5` *(with selection)* | Execute only the highlighted lines |
| `Shift + Alt + F` | Format the active script |
| `Ctrl+Shift+P` → `ETL-SQL:` | All ETL-SQL commands — preview, portal upload, stop execution |

---

## Extension Settings

| Setting | Default | Description |
| :--- | :--- | :--- |
| `etlsql.executable.path` | `""` | Override path to `ETL-SQL.exe`. Leave blank to auto-detect. |
| `etlsql.server.path` | `""` | Override path to `ETL-SQL-LSP.exe`. Leave blank to auto-detect. |
| `etlsql.report.executable.path` | `""` | Override path to `ETL-SQL-Report.exe`. Leave blank to auto-detect. |
| `etlsql.report.autoOpenPreview` | `false` | Auto-open the Report Preview pane when opening a `.rptsql` file. |
| `etlsql.portal.url` | `""` | Base URL of your hosted ETL-SQL Portal (for publishing). |
| `etlsql.format.keywordCasing` | `"upper"` | SQL keyword casing: `"upper"`, `"lower"`, `"pascal"`, `"preserve"`. |
| `etlsql.format.indentSize` | `4` | Spaces per indentation level. |
| `etlsql.format.commaPlacement` | `"leading"` | Comma placement: `"leading"` or `"trailing"`. |
| `etlsql.format.indentJoins` | `true` | Indent JOIN clauses relative to FROM. |
| `etlsql.format.onClauseOnNewLine` | `false` | Place the ON clause of a JOIN on a new line. |
| `etlsql.format.caseWhenThenNewLine` | `true` | Place THEN on a new line in CASE WHEN expressions. |
| `etlsql.format.breakoutWindowFunctions` | `true` | Break PARTITION BY / ORDER BY onto separate lines in window functions. |
| `etlsql.ai.provider` | `"Gemini"` | AI provider for spec-driven script generation. |
| `etlsql.ai.apiKey` | `""` | API key for the selected AI provider. Not required for VS Code Chat Extensions. |
| `etlsql.ai.model` | `""` | Optional model override for spec-driven script generation. |
| `etlsql.ai.endpoint` | `""` | Optional custom endpoint for compatible providers. |

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
| [User Manual](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/User_Manual.md) | Core paradigms, connections, variables, control flow, and debugging |
| [Pattern Cookbook](https://github.com/etl-sql/ETL-SQL/blob/main/docs/cookbooks/etl-recipes.md) | 20 production-grade, copy-pasteable ETL recipes |
| [Report-SQL Guide](https://github.com/etl-sql/ETL-SQL/blob/main/docs/guides/report-sql.md) | Visuals, filters, dashboards, drill-downs, and hosting |
| [Lineage & Governance Reference](https://github.com/etl-sql/ETL-SQL/blob/main/docs/reference/statements/lineage.md) | Tags, inheritance rules, `SHOW LINEAGE`, `LINEAGE_TAGS`, OpenLineage export |
| [Spec-Driven Development](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Reference/Spec_Driven_Development.md) | Full guide to the AI spec extraction + `gen-script` workflow |
| [Data Connectors](https://github.com/etl-sql/ETL-SQL/blob/main/docs/guides/administration.md) | Every connector, its options, and authentication patterns |
| [Grammar Reference](https://github.com/etl-sql/ETL-SQL/blob/main/docs/guides/getting-started.md) | Complete language syntax reference |
| [Notebook Guide](https://github.com/etl-sql/ETL-SQL/blob/main/docs/guides/notebook-guide.md) | Cell execution model, cross-cell state, and notebook IntelliSense |
