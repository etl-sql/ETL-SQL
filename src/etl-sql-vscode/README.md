# ETL-SQL Tools

**Unified data orchestration for SQL, NoSQL, and flat files — authored directly in VS Code.**

ETL-SQL is a SQL-like scripting language that orchestrates data movement across heterogeneous sources: relational databases, cloud warehouses, SFTP servers, REST APIs, flat files, and more. This extension brings full IDE support to `.etlsql` scripts and `.rptsql` reports.

---

## Features at a Glance

### 🗄️ Multi-Source Connections
Connect to any data source in a single script. The engine stages data in memory — transformations happen in the engine layer, not on the remote server.

Supported connectors: **MSSQL · PostgreSQL · Oracle · ODBC · Snowflake · BigQuery · MySQL · SQLite · FLATFILE / CSV · Excel · JSON · XML · Parquet · Avro · API / REST · SFTP · FTP · Azure Blob · SMTP · Directory · ReportPortal · MOCKDB**

```sql
CREATE CONNECTION src AS POSTGRES(HOST='db.example.com', DATABASE='sales', USER='etl', PASSWORD='...');
CREATE CONNECTION dest AS MSSQL(SERVER='dw.corp', DATABASE='warehouse', TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION files AS SFTP(HOST='sftp.example.com', USER='uploads', KEYFILE='~/.ssh/id_rsa');
```

---

### ✏️ Language Support
- **Syntax highlighting** for all ETL-SQL keywords, functions, connector types, variables (`@var`), and temp tables (`#table`)
- **Embedded SQL highlighting** inside `EXECUTE conn BEGIN ... END` blocks delegates to the native SQL grammar of the target engine
- **Bracket matching**, auto-closing pairs, and comment toggling (`--` / `/* */`)
- **Code formatter** — `Format Document` (`Shift+Alt+F`) reformats and indents your script

---

### 🧠 IntelliSense (Language Server)
Powered by a full Language Server Protocol (LSP) implementation:

- **Keyword and function completion** across the entire ETL-SQL standard library
- **Table and column completion** — schema metadata fetched live from your connections
- **`@variable` and `#temp` table completion** — knows what you've declared in the current script
- **Connection name completion** — references the connections in your active script
- **Hover documentation** — hover over any function to see its signature and description
- **Dialect-aware linting** — warns when you use `TOP n` against a PostgreSQL connection, or `GETDATE()` against Oracle
- **Security diagnostics** — flags plaintext passwords, recommends encryption

---

### 💰 Money Code Snippets
Type `$` at the start of a line to expand production-ready boilerplate. Snippets are tab-stop aware.

| Trigger | Expands to |
|---|---|
| `$conn` | `CREATE CONNECTION` template |
| `$etl` | Full Extract → Stage → Merge pipeline |
| `$try` | `BEGIN TRY ... BEGIN CATCH ... THROW` |
| `$foreach` | `FOREACH @item IN (...)` loop |
| `$merge` | `MERGE INTO ... USING ... WHEN MATCHED ...` |
| `$job` | `CREATE JOB` scheduling block |
| `$visual` | `CREATE VISUAL` for reports |
| `$parallel` | `PARALLEL` execution block |

---

### ▶️ Script Execution
- **Run Script** — `F5` executes the full active `.etlsql` file
- **Run Selection** — `F5` with a selection runs only the highlighted text
- **Stop Script** — cancel a running script at any time
- **REPL warm-up** — the engine process starts in the background when you open a file, so the first run has no startup delay
- **Session isolation** — each script file gets its own engine session; switching files resets the results panel automatically

---

### 📊 Results Panel
Results stream in real-time as your script executes.

- **Result grid** with sortable columns and proper type rendering
- **CSV export** — download any result set as a `.csv` file
- **Multiple result sets** — arrow navigation (`◀ Result 1 of N ▶`) cycles through all result sets produced by a script
- **Compare mode** — stacks all result sets on screen simultaneously for side-by-side review
- **Performance tab** — execution time, memory peak, and per-step timing
- **Pipeline tab** — visual bubble graph of the execution flow with row counts per step
- **Execution console** — live `PRINT` output and error messages with severity coloring
- **Rollback** — `ETL-SQL: Rollback All Transactions` command for safe recovery during development

---

### 🔒 Zero-Trust Security
ETL-SQL takes credential safety seriously and the extension enforces it at edit time:

- **On-save secret scan** — detects plaintext `PASSWORD=`, `API_KEY=`, and `USE PASSWORD=` values on every save and prompts you to apply the save policy
- **Save-policy enforcement** — when plaintext credentials or `SET CONNECTION_ENCRYPTION ON` / `SET NO_SAVE_SENSITIVE ON` are detected, a prompt offers to encrypt all credentials to `ENC:base64...` format using your master password
- **`ENC:` awareness** — when running a script with encrypted credentials, the extension prompts for the master password and passes it securely via environment variable, never via command-line arguments
- **Script immutability** — the engine blocks reads/writes to `.etlsql`/`.rptsql` files, preventing self-modifying scripts
- **Save policy flags** — `SET NO_SAVE_SENSITIVE ON`, `SET CONNECTION_ENCRYPTION ON`, `SET ALLOW_PLAINTEXT_SECRETS ON` are all checked on save and warned at the IDE level

---

### 📈 Report Designer (`.rptsql`)
Build interactive dashboards alongside your ETL scripts using the `.rptsql` file format.

- **Live preview panel** — renders your report inside VS Code as you build it
- **30+ visual types**: Bar, Line, Pie, Donut, Scatter, Heatmap, Treemap, Sankey, Gantt, Map, Network, Table, Card, Gauge, and more
- **Interactive controls**: Slicers, date pickers, sliders, multi-select, search, and text inputs with parameter binding
- **Report Designer UI** — visual drag-and-drop layout editor that writes `.rptsql` syntax for you
- **Export** — publish reports as PDF, Markdown, or plain text
- **Portal publish** — `ETL-SQL: Publish to Report Portal` deploys the report to your hosted ReportPortal instance
- **Launch options** — serve a single report, all reports in a directory, or a `reports.json` manifest in your browser

---

### 📓 Notebook Support (`.etlnb`)
ETL-SQL Notebooks bring a Jupyter-like experience to data pipeline development.

- Each cell is an independent ETL-SQL script executed against a shared engine session
- Cell outputs render as HTML grids, plain text, or Mermaid lineage graphs
- Notebook context is synced to the LSP — IntelliSense in cell N is aware of variables declared in cells 1 through N−1
- Export a finished notebook to a single `.etlsql` script via `ETL-SQL: Export Notebook to .etlsql`

---

### 🗂️ Metadata Explorer (Sidebar)
The sidebar panel shows everything relevant to the active script:

- **Global connections** — connections saved to VS Code global state, with table and column browsing
- **Script connections** — connections parsed from the active file by the LSP, updated as you type
- **`#temp` tables** — live list of engine-side temp tables declared in the current script
- **Script variables** — live `@variable` values with current value and type, updated during execution
- Copy a `CREATE CONNECTION` statement to the clipboard from any connection node

---

## Keyboard Shortcuts

| Action | Key |
|---|---|
| Run Script | `F5` (no selection) |
| Run Selection | `F5` (with selection) |
| Format Document | `Shift+Alt+F` |
| Stop Script | Command Palette → `ETL-SQL: Stop Script` |

---

## Requirements

- **ETL-SQL Engine** (`ETL-SQL.exe` / `ETL-SQL`) must be installed and accessible on your system `PATH`, or the path configured in settings.
- **ETL-SQL Language Server** (`ETL-SQL-LSP.exe` / `ETL-SQL-LSP`) is required for IntelliSense, linting, Money Code snippets, and formatting. The extension searches your system `PATH` and local build output automatically.
- **ETL-SQL Report CLI** (`ETL-SQL-Report.exe`) is required for report preview, export, and portal publishing.

---

## Extension Settings

| Setting | Default | Description |
|---|---|---|
| `etlsql.executable.path` | `ETL-SQL.exe` | Path to the ETL-SQL engine executable. Leave blank to use system PATH. |
| `etlsql.server.path` | *(auto-detect)* | Path to the ETL-SQL Language Server executable. Leave blank to auto-detect. |
| `etlsql.report.executable.path` | `ETL-SQL-Report.exe` | Path to the ETL-SQL Report CLI. Leave blank to use system PATH. |
| `etlsql.report.autoOpenPreview` | `false` | Automatically open the Report Preview panel when opening a `.rptsql` file. |
| `etlsql.portal.url` | *(blank)* | Base URL of your ETL-SQL Report Portal (e.g. `http://localhost:5001`). Required for the Publish command. |

---

## File Types

| Extension | Type |
|---|---|
| `.etlsql` | ETL-SQL script — data pipelines, jobs, automation |
| `.rptsql` | ETL-SQL Report script — dashboards and interactive reports |
| `.etlnb` | ETL-SQL Notebook — interactive cell-based exploration |

---

## Quick Start

1. Install the ETL-SQL SDK (engine, LSP, and report CLI).
2. Open or create a `.etlsql` file. The Welcome page appears automatically when VS Code starts with no editors open.
3. Use the **Metadata Explorer** sidebar to add a connection, or type `$conn` at the start of a line and select a connector template from the Money Code menu.
4. Press **F5** to run your script. Results appear in the **ETL-SQL Results** panel at the bottom.

---

## Resources

- [User Manual](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/User_Manual.md)
- [Cookbook — 20 production-ready recipes](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Cookbook.md)
- [Grammar reference](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Reference/Grammar.md)
- [Standard Library — all functions](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Reference/Standard_Library.md)
- [Report SQL Guide](https://github.com/etl-sql/ETL-SQL/blob/main/Docs/Report_SQL_Guide.md)
- [Issue tracker](https://github.com/etl-sql/ETL-SQL/issues)

---

## License

MIT — see [LICENSE.md](LICENSE.md) for details.
