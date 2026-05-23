# ETL-SQL

![ETL-SQL Banner](https://img.shields.io/badge/ETL--SQL-v0.7.0-blue?style=for-the-badge&logo=dotnet)
![Language](https://img.shields.io/badge/Language-C%23-green?style=for-the-badge)
![Platform](https://img.shields.io/badge/Platform-Windows%20|%20Linux%20|%20macOS-lightgrey?style=for-the-badge)

ETL-SQL is a SQL-first automation engine for moving, transforming, validating, scheduling, and reporting on data across mixed systems. A single script can connect to databases, APIs, files, SFTP servers, and cloud storage; stage data in engine-managed `#temp` tables; apply procedural logic; publish dashboards; and run headless, in a terminal IDE, in VS Code, or as a scheduled job.

Use ETL-SQL when you want SQL to be the orchestration language, not just the query language.

## Why ETL-SQL

- **One language for the whole workflow**: extraction, staging, transformation, validation, file operations, email, scheduling, lineage, and reporting.
- **Portable across sources**: MSSQL, Postgres, Oracle, ODBC, Snowflake, BigQuery, flat files, Parquet, JSON, XML, Excel, Avro, REST, SFTP, FTP, Azure Blob, and SMTP.
- **Engine-side control**: data flows through the ETL-SQL engine, where variables, `#temp` tables, lineage, linting, security checks, and cross-source transforms live.
- **Zero-trust by default**: scripts run inside a sandbox with path guardrails, script immutability, resource caps, encrypted credentials, and dry-run support.
- **Reports are scripts too**: `.rptsql` files use the same engine and add dashboards, filters, pages, containers, navigation, exports, and portal publishing.

---

## See It In Action

### VS Code Extension & Notebooks

![VS Code demo](Docs/assets/vscode-demo.gif)

*Inline diagnostics · schema autocomplete · REPL results panel · report preview · cell-by-cell notebook execution · Lineage*

### Report-SQL Dashboards

![Report demo](Docs/assets/report-demo.gif)

*Interactive charts · drill-down navigation · live parameter slicers · multi-page navigation · PDF export*

### Terminal IDE

![TUI demo](Docs/assets/tui-demo.gif)

*Syntax highlighting · autocomplete · live results grid · compare mode · profiling dashboard*

---

## Quick Start

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

### Build From Source

```bash
git clone https://github.com/etl-sql/ETL-SQL.git
cd ETL-SQL
dotnet build
```

### Run a Script

```bash
dotnet run --project src/ETL-SQL.App -- run MyScript.etlsql
```

### Open the Terminal IDE

```bash
dotnet run --project src/ETL-SQL.App -- ui edit MyScript.etlsql
```

### Serve a Report Dashboard

```bash
etl-sql-report serve my_report.rptsql
```

### Build Static Report Outputs

```bash
etl-sql-report build my_report.rptsql --format pdf
etl-sql-report build my_report.rptsql --format md
```

---

## ETL Example

This script extracts from SQL Server, stages rows inside the ETL-SQL engine, writes a CSV archive, and sends a completion email.

```sql
CREATE CONNECTION prod_db ON MSSQL(
    HOST = 'prod',
    DATABASE = 'Sales',
    TRUSTED_CONNECTION = TRUE
);

CREATE CONNECTION archive ON FLATFILE(
    PATH = 'C:\Exports\sales_2026.csv',
    FORMAT = 'CSV',
    DELIMITER = ',',
    HEADER = ON
);

CREATE CONNECTION my_smtp ON SMTP(
    HOST = 'smtp.company.com',
    PORT = 587,
    USER = 'admin',
    PASSWORD = 'secret',
    USE_SSL = TRUE
);

BEGIN TRY
    SELECT OrderId, Customer, Amount
    INTO #latest
    FROM prod_db.dbo.Orders
    WHERE OrderDate >= '2026-01-01';

    INSERT INTO archive SELECT * FROM #latest;

    SEND EMAIL
        TO      'admin@company.com'
        FROM    'etl@company.com'
        SUBJECT 'ETL Success'
        BODY    ('Archived ' + CAST((SELECT COUNT(*) FROM #latest) AS STRING) + ' orders.')
        AT      my_smtp;
END TRY
BEGIN CATCH
    PRINT 'Load failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
```

---

## Report-SQL Example

Report-SQL extends ETL-SQL with dashboard primitives. Data prep stays in SQL; visuals, filters, pages, and layouts are declared in the same `.rptsql` file.

```sql
SET REPORT TITLE       = 'Sales Dashboard';
SET REPORT DESCRIPTION = 'Regional revenue by quarter';

CREATE CONNECTION prod ON MSSQL(
    HOST = 'prod',
    DATABASE = 'Sales',
    TRUSTED_CONNECTION = TRUE
);

SELECT Region, Quarter, SUM(Revenue) AS Revenue
INTO #revenue
FROM prod.dbo.Orders
GROUP BY Region, Quarter;

SELECT DISTINCT Region AS Value
INTO #regions
FROM #revenue
ORDER BY Value;

DECLARE @region varchar(200) = 'All';

CREATE VISUAL RegionSlicer AS SLICER (
    SOURCE   = #regions,
    MAPPINGS (VALUE = Value),
    ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, Value))
);

CREATE VISUAL RevChart AS BAR (
    SOURCE   = (
        SELECT Quarter, Region, Revenue
        FROM #revenue
        WHERE (@region = 'All' OR Region = @region)
    ),
    MAPPINGS (X = Quarter, Y = Revenue, SERIES = Region),
    STYLE    (HEIGHT = '380px', THEME = dark)
);

CREATE VISUAL TotalKpi AS CARD (
    SOURCE   = (SELECT SUM(Revenue) AS Value, 'Total Revenue' AS Label FROM #revenue),
    MAPPINGS (VALUE = Value, LABEL = Label),
    OPTIONS  (FORMAT = 'C0')
);

CREATE PAGE Sales AS DASHBOARD (
    TITLE     = 'Sales',
    STRUCTURE = 'A A / B C',
    MAP ('A' = RevChart, 'B' = RegionSlicer, 'C' = TotalKpi)
);
```

Serve it live:

```bash
etl-sql-report serve sales_dashboard.rptsql
```

Or build static outputs:

```bash
etl-sql-report build sales_dashboard.rptsql --format md
etl-sql-report build sales_dashboard.rptsql --format pdf
etl-sql-report build sales_dashboard.rptsql --format json
```

---

## Core Capabilities

### Data Movement & Transformation

- Read and write across SQL databases, flat files, document formats, APIs, SFTP/FTP, Azure Blob, and SMTP.
- Stage rows in engine-managed `#temp` tables for cross-source joins, validation, filtering, enrichment, lineage, and reporting.
- Use procedural control flow: variables, `IF`, `WHILE`, `FOR`, `FOREACH`, `TRY...CATCH`, transactions, and `PARALLEL`.
- Push compatible joins and filters to database connectors while keeping cross-source operations engine-side.

### Performance & Scale

- Stream large datasets through supported execution paths with bounded display results.
- Spill sort, join, aggregate, and window workloads to disk when thresholds are exceeded.
- Cache repeated scalar subqueries within a session.
- Profile statements with `SET PROFILING ON` and inspect runtime metrics with system variables.

### Security & Governance

- Resolve file paths through the engine security boundary.
- Block script mutation of `.etlsql`, `.rptsql`, `.sql`, and other protected source files.
- Encrypt credentials with `ENC:` values.
- Use `SET WHAT_IF ON` to validate side-effecting operations before they run.
- Track data lineage with standard tags, transformation metadata, OpenLineage export, and Mermaid diagrams.

### Reporting & Portal

- Build live dashboards from `.rptsql` scripts using `CREATE VISUAL`, `CREATE PAGE`, `CREATE CONTAINER`, `CREATE NAVIGATION`, and shared datasets.
- Render charts, tables, cards, text, filters, inputs, maps, Sankey, Sunburst, Network, Matrix, Gantt, and other visual types.
- Use slicers, multi-select filters, date pickers, sliders, search boxes, drill-downs, cross-highlighting, saved views, alerts, subscriptions, and portal administration commands.
- Export report outputs as Markdown, JSON manifests, SVG/PDF assets, and paginated reports.

### Developer Experience

- Run scripts headlessly from the CLI.
- Use the terminal IDE for syntax highlighting, autocomplete, live result grids, compare mode, and profiling.
- Use the VS Code extension for LSP diagnostics, hover docs, schema autocomplete, REPL execution, report preview, and `.etlnb` notebooks.
- Use `LINT`, `EXPLAIN`, `SHOW PROFILE`, `SHOW CONNECTIONS`, and `SHOW VERSION` to inspect scripts and sessions.

---

## Tools

| Tool | Purpose |
| :--- | :--- |
| `ETL-SQL.exe` | Headless script executor for pipelines, CI/CD, cron, and server deployments. |
| `ETL-SQL-TUI.exe` | Interactive terminal IDE with editor, results, messages, autocomplete, and profiling. |
| `ETL-SQL-REPORT.exe` | Report-SQL CLI for `build`, `refresh`, and `serve`. |
| VS Code extension | Language server, REPL panel, notebook support, schema sidebar, and report preview. |
| Report Portal | Multi-report hosting, publishing, permissions, subscriptions, alerts, saved views, and usage metrics. |
| Orchestrator service | Job scheduling, execution history, and always-on automation. |

---

## Documentation

### Start Here

| Document | Description |
| :--- | :--- |
| [User Manual](Docs/User_Manual.md) | Pipeline mental model, connections, variables, control flow, and debugging. |
| [ETL Notebook Guide](Docs/ETL_Notebook_Guide.md) | Cell execution model, cross-cell state, and notebook IntelliSense. |
| [Report-SQL Guide](Docs/Report_SQL_Guide.md) | `.rptsql` syntax, visuals, filters, dashboards, drill-downs, and report hosting. |
| [Pattern Cookbook](Docs/Cookbook.md) | Self-contained ETL recipes for common production workflows. |
| [Sample Guide](Docs/Sample_Guide.md) | Inventory of sample scripts in the `samples/` folder. |

### Reference

| Document | Description |
| :--- | :--- |
| [Syntax Index](Docs/Syntax_Index.md) | Central index of commands, functions, options, visual types, and syntax forms. |
| [Grammar](Docs/Reference/Grammar.md) | Complete syntax reference. |
| [Standard Library](Docs/Reference/Standard_Library.md) | Built-in functions: string, date, math, regex, window, JSON/XML, and more. |
| [Data Connectors](Docs/Reference/Data_Connectors.md) | Connector types, `WITH()` options, authentication patterns, and examples. |
| [Specialized Operations](Docs/Reference/Specialized_Operations.md) | File operations, email, transfer, lineage, Docker, jobs, and diagnostics. |

### Engineering

| Document | Description |
| :--- | :--- |
| [Engine Architecture](Docs/Architecture/Engine.md) | Parser, AST, evaluator internals, dispatch, linting, and execution model. |
| [Reporting Architecture](Docs/Architecture/Reporting.md) | Report runtime, manifest builder, renderer, exports, and parameter binding. |
| [Connector Architecture](Docs/Architecture/Connectors.md) | Connector interfaces, lifecycle, pushdown, and security boundaries. |
| [VS Code Extension](Docs/Architecture/VSCodeExtension.md) | LSP, REPL channels, notebook controller, results panel, and report preview. |
| [Security Policy](SECURITY.md) | Zero-trust sandbox, cryptographic architecture, and audit policy. |
| [AI Agent Manual](AGENTS.md) | Mandatory instruction set for AI-assisted development in this repo. |

---

## Release Build

Maintainers can run the release script to validate, package, and publish the 0.7.0 artifacts:

```powershell
.\scripts\Master-Release.ps1 -Version "0.7.0"
```

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, branching, testing, and contribution guidelines.

---

© 2026 ETL-SQL Team. Built for speed, designed for clarity.

**Commercial Use & Licensing** — This software is free for personal, non-commercial use only. For commercial licensing or service agreements, contact [etlsqlsoftware@gmail.com](mailto:etlsqlsoftware@gmail.com).
