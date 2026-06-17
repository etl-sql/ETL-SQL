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

> **Stop writing Python glue code. Stop fighting SSIS. Stop managing three tools to do one job.**
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

Press **`F1` → `ETL-SQL: Open Report Preview`** to see this render live in your editor.

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
- **Lineage Visualization** — view a bubble graph of data lineage for any executed script without leaving VS Code.

### 📊 See Your Dashboard Before You Ship It

- **Live `.rptsql` Preview** — open a report file and watch the interactive dashboard render side-by-side with your code. Every save refreshes the preview.
- **Full Interactivity in Preview** — slicers, date pickers, drill-downs, parameter overrides, and cross-highlighting all work in the preview pane — not just in a browser.
- **One-Click Export** — export your rendered report to PDF or Markdown directly from the preview panel.

### 📓 Notebooks for Iterative Development (`.etlnb`)

- **Cell-Based Pipelines** — break complex workflows into cells that share a single live session. Run them one at a time or all at once.
- **Mermaid Lineage in Cells** — lineage diagrams render inline as notebook output.
- **Export to Script** — convert any notebook back to a standard `.etlsql` file for headless deployment with a single command.

### 🔒 Security That Catches Mistakes Before You Ship Them

- **Credentials Guard** — scans for plaintext passwords and API keys on every save and surfaces a warning immediately.
- **One-Click Encryption** — converts any flagged plaintext secret into an `ENC:...` encrypted string using your session master key, right from the inline warning action.

### 🗂️ Metadata Explorer Sidebar

Browse all active connections, catalog schemas, declared `@variables`, and live `#temp` tables in a structured sidebar panel — no context-switching required.

---

## 🏷️ Data Governance & Lineage — Built Into the Language

Most ETL tools treat governance as an afterthought — a separate catalog you populate after the fact. In ETL-SQL, governance metadata lives *in* the script itself, as inline tag comments that travel with the data through every transformation.

### Inline Column Tags

Tag any column inline using `/* @tag: value; */` comments. Tags are read by the engine at execution time, stored in the lineage tracker, and inherited automatically by derived columns:

```sql
SELECT
    customer_id  /* @d: Unique customer identifier; @nullable: false */,
    email        /* @pii; @classification: confidential; @d: Primary contact email */,
    card_number  /* @pci; @classification: restricted; @d: Masked PAN */,
    region       /* @d: Sales region; @owner: sales_ops */,
    total_spend  /* @d: Lifetime spend in USD; @unit: USD */
INTO #customers
FROM crm.dbo.Customers;
```

The shorthand `/* @pii; */` with no value is equivalent to `/* @pii: true; */`.

### Script-Level Metadata Headers

Tag an entire script with ownership, environment, and scheduling intent at the top:

```sql
-- @author: jane.smith;
-- @domain: Finance;
-- @environment: production;
-- @schedule: daily;
-- @description: Monthly revenue reconciliation pipeline;
```

### Tags Inherit Through Transformations Automatically

If a source column is tagged `@pii`, every column derived from it carries that tag forward — through joins, aggregations, CASE expressions, and string operations — without you having to re-tag every step:

```sql
-- email is tagged @pii at the source
SELECT UPPER(email) AS email_upper   -- inherits @pii: true automatically
FROM #customers;
```

Security tags (`@pii`, `@phi`, `@pci`, `@sensitive`) use **true-wins** inheritance: if *any* contributing source is tagged true, the output is tagged true. Classification uses **highest-tier wins**: restricted beats confidential beats internal.

### Standard Tag Catalog

| Domain | Tags |
| :--- | :--- |
| **Security & Privacy** | `@pii` · `@phi` · `@pci` · `@sensitive` · `@classification` · `@encrypted_at_rest` |
| **Ownership** | `@owner` · `@domain` · `@steward` · `@contact` |
| **Quality & Freshness** | `@quality` · `@freshness` · `@sla` · `@nullable` |
| **Documentation** | `@d` · `@example` · `@unit` · `@format` |
| **Traceability** | `@source_system` · `@source_table` · `@source_column` · `@load_pattern` |

Custom tags are always allowed alongside the standard set.

### Query Lineage as Data

After a script runs, the full lineage graph is queryable inside the same session:

```sql
-- Find every PII column that touched this pipeline
SELECT DISTINCT target_table, target_column
FROM LINEAGE_TAGS
WHERE tag_name = 'pii' AND tag_value = 'true';

-- Find sensitive columns with no assigned owner
SELECT s.target_table, s.target_column
FROM LINEAGE_TAGS s
WHERE s.tag_name IN ('pii', 'phi', 'pci') AND s.tag_value = 'true'
  AND NOT EXISTS (
      SELECT 1 FROM LINEAGE_TAGS o
      WHERE o.target_table = s.target_table
        AND o.target_column = s.target_column
        AND o.tag_name = 'owner'
  );
```

### Export for Compliance & Audit

```sql
-- Human-readable Markdown with a Mermaid lineage graph
SHOW LINEAGE FOR #final_output TO 'audit_lineage.md';

-- OpenLineage events — importable into DataHub, Marquez, Airflow, Collibra, Alation
SHOW LINEAGE EXPORT AS OPENLINEAGE TO 'audit_2026_Q2.jsonl';
```

---

## 🤖 AI-Assisted Script Generation — From Vendor Spec to Running Pipeline

Every data team eventually receives a vendor spec: a PDF, Excel sheet, or Word doc describing a file format. Normally someone manually reads it, creates a table definition, writes the ETL script, and discovers the edge cases at 2 AM on the first load.

ETL-SQL has a better workflow.

### ⚡ Integrated VS Code Workflow

The extension integrates this entire workflow into a single-click editor experience. 

#### 1. Configure Your AI Settings
Configure your AI credentials in your VS Code settings (`settings.json`):
```json
{
  "etlsql.ai.provider": "Gemini", // Gemini, Anthropic, OpenAI, OpenRouter, VS Code Chat Extensions (Copilot/Claude/etc.), or Custom
  "etlsql.ai.apiKey": "YOUR_API_KEY", // API Key (Not required for VS Code Chat Extensions)
  "etlsql.ai.model": "gemini-1.5-flash", // Optional: target model (or specify 'claude'/'gpt-4o' to target a local chat extension)
  "etlsql.ai.endpoint": "" // Optional: custom endpoint URL
}
```

> [!NOTE]
> **No API Key Required for Chat Extensions**: If you select `VS Code Chat Extensions (Copilot/Claude/etc.)` as your provider, the extension queries the models provided by extensions already active in your editor (e.g. GitHub Copilot, local chat extensions). VS Code handles authentication for you—no API key needed! If you have multiple extensions active, write a keyword (e.g., `claude` or `gpt-4o`) in the `model` setting to select which model should handle the spec. Note that chat extensions only support text-based files (CSV, JSON, TXT). For binary formats like PDF or Excel sheets, use direct providers (Gemini or Anthropic) with an API key.

#### 2. Run the Command
1. Right-click any specification file (PDF, Excel, CSV, Word, JSON, TXT) in the VS Code File Explorer and select **ETL-SQL: Generate Script from Spec** (or open the Command Palette `Ctrl+Shift+P` and select it).
2. If you select a PDF, the command will prompt you to optionally run `extract-spec` to trim administrative pages first, optimizing prompt token usage.
3. The extension automatically loads the prompt instructions, transmits the spec to your configured AI provider, validates the response, runs `gen-script` under the hood, and asks where to save your new `.etlsql` script.
4. The generated script opens instantly, ready for you to write your extraction query.

---

### 🛠️ CLI Fallback (Headless / Manual Flow)

For headless pipelines or local terminal usage, you can still run the underlying commands manually:

**Step 1 — Run the extraction prompt**
Paste the structured prompt instructions ([`data_spec_parser_instructions.md`](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/data_spec_parser_instructions.md)) along with your spec file into your AI assistant. The assistant returns a standardized JSON contract:

```json
{
  "pipeline_name": "daily_vendor_feed",
  "metadata": { "classification": "confidential", "owner": "data_team" },
  "source": { "format": "CSV", "delimiter": "comma", "has_header": true },
  "schema": [
    { "column_name": "customer_id", "type_family": "INT",     "is_key": true,  "nullable": false },
    { "column_name": "email",       "type_family": "VARCHAR", "max_length": 200, "tags": ["pii"], "validation_regex": "^[^@]+@[^@]+\\.[^@]+$" },
    { "column_name": "amount",      "type_family": "DECIMAL", "precision": 10, "scale": 2 }
  ]
}
```

The AI automatically identifies PII/PHI/PCI columns, maps vendor type names to ETL-SQL types, extracts validation regex from format rules, and flags ambiguous fields with a `confidence` score.

**Step 2 — Generate the script boilerplate**

```bash
etl-sql gen-script vendor_feed.json
```

The engine reads the JSON contract and produces an `.etlsql` script complete with:
- Connection declarations for source and destination
- `CREATE TABLE` with correct types, lengths, and nullability
- Inline `/* @pii; @d: ... */` governance tags on every tagged column
- `ASSERT` validation gates for non-nullable fields, regex patterns, and allowed values
- A quarantine branch that routes bad rows aside instead of failing the whole batch
- Lineage tags seeded from the spec's classification and ownership fields
- Review notes embedded as comments wherever the AI's `confidence` was below threshold

A first draft with schema validation, governance tags, and quarantine scaffolding — instead of a blank file.

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
| **ETL-SQL Platform** | Report Portal · Orchestrator |
| **Development & Testing** | `MOCKDB()` — zero-config in-memory mock, no setup required |

---

## Getting Started

**1. Install This Extension**

Install **ETL-SQL Developer Tools** from the VS Code Marketplace. The VSIX bundles everything you need to start immediately — the engine (`ETL-SQL.exe`), language server (`ETL-SQL-LSP.exe`), and report CLI (`ETL-SQL-Report.exe`) are included. No separate SDK download required.

**Want the full platform?** Download the optional extras from [github.com/etl-sql/ETL-SQL](https://github.com/etl-sql/ETL-SQL):
- **Terminal IDE** (`ETL-SQL-TUI.exe`) — full-featured TUI editor with autocomplete and results grid for headless environments
- **Orchestrator** — always-on job runner with scheduling, history, and dependency management
- **Report Portal** — multi-report hosting, permissions, subscriptions, alerts, and usage metrics

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
| `etlsql.portal.url` | `""` | Base URL of your hosted ETL-SQL Report Portal (for publishing). |
| `etlsql.format.keywordCasing` | `"upper"` | SQL keyword casing: `"upper"`, `"lower"`, `"pascal"`, `"preserve"`. |
| `etlsql.format.indentSize` | `4` | Spaces per indentation level. |
| `etlsql.format.commaPlacement` | `"leading"` | Comma placement: `"leading"` or `"trailing"`. |
| `etlsql.format.indentJoins` | `true` | Indent JOIN clauses relative to FROM. |
| `etlsql.format.onClauseOnNewLine` | `false` | Place the ON clause of a JOIN on a new line. |
| `etlsql.format.caseWhenThenNewLine` | `true` | Place THEN on a new line in CASE WHEN expressions. |
| `etlsql.format.breakoutWindowFunctions` | `true` | Break PARTITION BY / ORDER BY onto separate lines in window functions. |

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
| [Pattern Cookbook](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Cookbook.md) | 20 production-grade, copy-pasteable ETL recipes |
| [Report-SQL Guide](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Report_SQL_Guide.md) | Visuals, filters, dashboards, drill-downs, and hosting |
| [Lineage & Governance Reference](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Reference/Lineage.md) | Tags, inheritance rules, `SHOW LINEAGE`, `LINEAGE_TAGS`, OpenLineage export |
| [Spec-Driven Development](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Reference/Spec_Driven_Development.md) | Full guide to the AI spec extraction + `gen-script` workflow |
| [Data Connectors](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Reference/Data_Connectors.md) | Every connector, its options, and authentication patterns |
| [Grammar Reference](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Reference/Grammar.md) | Complete language syntax reference |
| [Notebook Guide](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/ETL_Notebook_Guide.md) | Cell execution model, cross-cell state, and notebook IntelliSense |
