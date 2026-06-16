# ETL-SQL Developer Tools for Visual Studio Code

[![VS Code Extension](https://img.shields.io/badge/Extension-VS%20Code-blue?style=for-the-badge&logo=visual-studio-code)](https://marketplace.visualstudio.com/)
[![ETL-SQL Version](https://img.shields.io/badge/ETL--SQL-v0.12.0-blue?style=for-the-badge&logo=dotnet)](https://github.com/etl-sql/ETL-SQL)

Bring the power of **ETL-SQL** directly into your development environment. Author data pipelines, build interactive dashboards, execute cell-based notebooks, and manage databases—all from a single, unified SQL dialect.

---

## What is ETL-SQL?

ETL-SQL is a hybrid, SQL-first automation engine designed for data movement, transformation, validation, and dashboard delivery across mixed infrastructure. 

Instead of writing complex Python, bash, or SSIS packages, a single ETL-SQL script handles it all. By staging data in engine-managed `#temp` tables, you can query, join, and cleanse data from multiple heterogeneous systems (SQL databases, APIs, SFTP, flat files) before writing it to your destination.

### Why Download This Extension?

While you can run ETL-SQL headlessly from the CLI or in the Terminal IDE, the VS Code extension provides a premium, full-featured workspace built for development teams:

- **Autocompletes Live Schema**: Fetches schemas dynamically from your defined database connections, letting you autocomplete tables and column names as you type.
- **Renders Dashboards Live**: Renders Report-SQL (`.rptsql`) files side-by-side with your code in an interactive preview pane.
- **Executes Cell-by-Cell Notebooks**: Develop iteratively using `.etlnb` notebooks with shared session context.
- **Streams Real-Time Results**: View your query output, execution plans, and performance metrics in an interactive results console.
- **Zero-Trust Security Lints**: Scans for plaintext credentials on save and offers one-click master password encryption directly in the editor.

---

## 🚀 Copy-Paste Examples (Run Instantly)

You don't need databases, credentials, or network access to try ETL-SQL. Both examples below use the built-in, in-memory `MOCKDB()` connector and run out of the box.

### 1. The ETL Pipeline Example (`pipeline.etlsql`)
This script showcases the classic "Extract → Stage/Validate → Transform → Deliver" pipeline using local memory tables.

```sql
-- Safe, zero-config in-memory database
CREATE CONNECTION demo AS MOCKDB();

BEGIN TRY
    -- 1. Extract: Pull rows into the engine memory (#temp tables)
    SELECT Region, Total
    INTO #orders
    FROM demo.Orders;

    -- 2. Validate: Enforce critical schema & content assumptions
    ASSERT (SELECT COUNT(*) FROM #orders) > 0, 'Error: Expected orders to be populated';

    -- 3. Transform: Compute regional aggregates in-memory
    SELECT 
        Region,
        COUNT(*) AS TotalOrders,
        SUM(Total) AS Revenue,
        AVG(Total) AS AvgOrder
    INTO #summary
    FROM #orders
    GROUP BY Region;

    -- 4. Deliver: Return the finalized rows
    SELECT Region, TotalOrders, Revenue, AvgOrder
    FROM #summary
    ORDER BY Revenue DESC;

END TRY
BEGIN CATCH
    PRINT 'Pipeline failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
```

### 2. The Dashboard Report Example (`sales_report.rptsql`)
This script prepares data and declares an interactive dashboard layout in a single file. Press `F1` and select `ETL-SQL: Open Report Preview` to see the live rendering!

```sql
SET REPORT TITLE = 'Mock Sales Performance';
SET REPORT DESCRIPTION = 'Live dashboard powered by in-memory MOCKDB data';

-- Safe development connection
CREATE CONNECTION demo AS MOCKDB();

-- Prepare data
SELECT 
    Region, 
    COUNT(*) AS OrderCount, 
    SUM(Total) AS Revenue
INTO #sales_summary
FROM demo.Orders
GROUP BY Region;

-- Create the bar chart visual
CREATE VISUAL RegionalSalesChart AS BAR (
    SOURCE   = #sales_summary,
    MAPPINGS (X = Region, Y = Revenue),
    STYLE    (HEIGHT = '350px', THEME = dark)
);

-- Create KPI cards
CREATE VISUAL OrdersKpi AS CARD (
    SOURCE   = (SELECT SUM(OrderCount) AS TotalOrders FROM #sales_summary),
    MAPPINGS (VALUE = TotalOrders, LABEL = 'Total Orders')
);

CREATE VISUAL RevenueKpi AS CARD (
    SOURCE   = (SELECT SUM(Revenue) AS TotalRevenue FROM #sales_summary),
    MAPPINGS (VALUE = TotalRevenue, LABEL = 'Total Revenue'),
    OPTIONS  (FORMAT = 'C0')
);

-- Assemble dashboard grid layout
CREATE PAGE SalesDashboard AS DASHBOARD (
    TITLE     = 'Executive Overview',
    STRUCTURE = 'A A / B C', -- Chart spans the top row, KPI cards share the bottom row
    MAP ('A' = RegionalSalesChart, 'B' = OrdersKpi, 'C' = RevenueKpi)
);
```

---

## 🎨 Key Features

### 🧠 Dynamic IntelliSense & Linting
- **Live Metadata Autocomplete**: Automatically completes table names, column structures, variable names, and connection identifiers as you write.
- **Dialect-Aware Validation**: Warns you immediately if you write dialect-specific code against the wrong connection context (e.g. using `TOP n` against Postgres instead of `LIMIT`).
- **Hover Documentation**: Hover over any built-in functions, variables, or table structures to see signatures and quick-use examples.

### ▶️ Real-Time Execution Console (REPL)
- **F5 Execution**: Run the active script or execute a highlighted text selection instantly.
- **Interactive Results Grid**: Sort, search, and browse multi-result tables, or stack them side-by-side using **Compare Mode**.
- **Interactive Metrics & Lineage**: Inspect step-by-step query profiling (spill status, memory peaks) and view a bubble graph visualization of data lineage.

### 📊 Live Report Previewer
- **Side-by-Side Dashboard Preview**: Renders interactive charts, slicers, cards, and maps inside your editor as you modify your `.rptsql` files.
- **Designer Synchronizer**: Supports live data bindings, pagination, parameter overrides, and exports directly to PDF or Markdown.

### 📓 Interactive Notebooks (`.etlnb`)
- **Cell-Based ETL**: Write cell-by-cell pipelines that share a live, underlying session context.
- **Mermaid Lineage Rendering**: View data lineage charts generated inside the notebook cells.
- **Single-Script Export**: Export any notebook back to a standard `.etlsql` script for headless orchestration with a single command.

### 🔒 Zero-Trust Security Scans
- **Credentials Guard**: Automatically alerts you of plaintext passwords, API tokens, or session secrets on save.
- **One-Click Encryption**: Offers to convert plaintext passwords into encrypted strings (`ENC:...`) using a session-master key.

### 🗂️ Metadata Explorer (Sidebar)
- Browse active file connections, catalog schemas, local variables, and current `#temp` tables.

---

## ⚙️ Configuration & Requirements

### Installation Steps

1. Install the **ETL-SQL SDK** (make sure `ETL-SQL.exe`, `ETL-SQL-LSP.exe`, and `ETL-SQL-Report.exe` are on your system `PATH`).
2. Install this extension in VS Code.
3. Open any folder or workspace, and create a `.etlsql` file.
4. Press **F5** to execute the file and display the **ETL-SQL Results** console.

### Keyboard Shortcuts

| Command | Action |
| :--- | :--- |
| `F5` | Execute full active script |
| `F5` (with selection) | Execute only the highlighted lines |
| `Shift + Alt + F` | Format active script |
| Command Palette | Search `ETL-SQL:` for stops, previews, and portal uploads |

### Extension Settings

| Setting | Default | Description |
| :--- | :--- | :--- |
| `etlsql.executable.path` | `""` | **(Advanced Override)** Path to custom `ETL-SQL` runner. Leave blank to auto-detect bundled or system `PATH` version. |
| `etlsql.server.path` | `""` | **(Advanced Override)** Path to custom Language Server (`ETL-SQL-LSP.exe`). Leave blank to auto-detect bundled or system `PATH` version. |
| `etlsql.report.executable.path` | `""` | **(Advanced Override)** Path to custom Report CLI (`ETL-SQL-Report.exe`). Leave blank to auto-detect bundled or system `PATH` version. |
| `etlsql.report.autoOpenPreview` | `false` | Automatically open the Report Preview pane when opening a `.rptsql` file. |
| `etlsql.portal.url` | `""` | The base URL of your hosted **ETL-SQL Report Portal** (used to publish reports). |
| `etlsql.format.keywordCasing` | `"upper"` | Casing style for SQL keywords (`"upper"`, `"lower"`, `"pascal"`, `"preserve"`). |
| `etlsql.format.indentSize` | `4` | Number of spaces per indentation level. |
| `etlsql.format.commaPlacement` | `"leading"` | Position of list commas (`"leading"` or `"trailing"`). |
| `etlsql.format.indentJoins` | `true` | Indent JOIN statements relative to the FROM clause. |
| `etlsql.format.onClauseOnNewLine` | `false` | Place the ON clause of a JOIN statement on a new line. |
| `etlsql.format.caseWhenThenNewLine` | `true` | Place the THEN clause of a CASE WHEN statement on a new line. |
| `etlsql.format.breakoutWindowFunctions` | `true` | Break out window function parameters (PARTITION BY, ORDER BY) onto separate lines. |

### Supported File Formats

- `.etlsql`: Standalone scripts, data migrations, scheduled jobs.
- `.rptsql`: Dashboard configurations and reporting layouts.
- `.etlnb`: Interactive data notebooks.

---

## 📚 Resources & Documentation

- [User Manual](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/User_Manual.md) — Fundamental paradigms, control flows, and patterns.
- [Design Cookbook](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Cookbook.md) — 20 production-grade data pipelines.
- [Report-SQL Guide](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Report_SQL_Guide.md) — Charts, visuals, and page grid layouts.
- [Data Connectors Index](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Reference/Data_Connectors.md) — Connection parameters and syntax options.
