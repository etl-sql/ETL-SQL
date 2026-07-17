# ETL-SQL User Manual: Thinking in Pipelines

Welcome to ETL-SQL. This guide is designed to help you transition from thinking in "Single Database SQL" to "Multi-Context Data Flow." Work through each section in order — each one builds on the last.

> [!TIP]
> **Stuck on something specific?** Use the table of contents below to jump directly to the section you need. For a searchable list of errors and gotchas, see the [FAQ](faq.md). For connector-specific syntax, see [Data Connectors](../reference/connectors/data-connectors.md).

## What Makes ETL-SQL Different

ETL-SQL is script-first data orchestration. Pipelines, reports, schedules, validation, and governance metadata live in plain-text `.etlsql` and `.rptsql` files that can be reviewed, diffed, tested, packaged, and run from the CLI, VS Code, notebooks, Portal, Orchestrator, or CI/CD.

The engine puts the **T** back in the middle of ETL. Instead of loading everything first and hoping every downstream transformation fits one warehouse dialect, ETL-SQL stages data in engine-managed `#temp` tables where validation, masking, enrichment, fuzzy matching, lineage tags, and quality gates can run before rows are written to their destination. Compatible database work can still be pushed down, but cross-source work stays portable and explicit.

Lineage is part of the workflow rather than an after-the-fact reconstruction. Tags and transformation metadata can travel with rows and columns through joins, aggregations, reports, OpenLineage exports, and Mermaid diagrams.

## First Hour Walkthrough

The fastest way to understand ETL-SQL is to run one small pipeline that has no external dependencies. `MOCKDB` is an in-memory demo connector, so this script does not need a database server, files, credentials, or network access.

Save this as `first_hour.etlsql`:

```sql
SET PROFILING ON;

CREATE CONNECTION demo AS MOCKDB();

-- Extract: copy remote rows into the engine workspace.
SELECT
    Region,
    Total
INTO #orders
FROM demo.Orders;

-- Validate: fail early if the source contract is not what this pipeline expects.
ASSERT (SELECT COUNT(*) FROM #orders) > 0,
    'MOCKDB Orders should contain rows';

-- Transform: create a reusable engine-side summary.
SELECT
    Region,
    COUNT(*) AS OrderCount,
    SUM(Total) AS Revenue,
    AVG(Total) AS AverageOrder
INTO #regional_summary
FROM #orders
GROUP BY Region;

-- Deliver: return the final result to the caller.
SELECT
    Region,
    OrderCount,
    Revenue,
    AverageOrder
FROM #regional_summary
ORDER BY Revenue DESC;

SHOW PROFILE;
SET PROFILING OFF;
```

Run it from the project checkout:

```powershell
dotnet run --project src\ETL-SQL.App -- run first_hour.etlsql
```

This one script shows the core workflow used throughout the rest of the manual:

| Step | What happened | Why it matters |
| :--- | :--- | :--- |
| Connect | `CREATE CONNECTION demo AS MOCKDB()` | Every source gets a named connection. |
| Stage | `SELECT ... INTO #orders` | Data enters the engine workspace before transformation. |
| Validate | `ASSERT ...` | Bad input stops the pipeline before load or delivery. |
| Transform | `GROUP BY Region` against `#orders` | Engine-side tables let ETL-SQL apply its own functions and rules. |
| Deliver | Final `SELECT` | The last query is the result a caller, job, or report can consume. |
| Diagnose | `SHOW PROFILE` | Profiling exposes statement timing while you develop. |

After this works, change only one thing at a time: swap `MOCKDB` for a real connector, add a `WHERE` filter, write the summary to a file, or turn the final query into a report visual.

---

## 1. The Pipeline Mental Model

The most important concept to master is **Context Awareness**. In standard SQL, your query runs against a single engine. In ETL-SQL, you are the **Conductor** of an orchestra of engines.

```
┌──────────────────────────────────────────────────────┐
│              ETL-SQL Engine ("The Brain")            │
│  - Holds @variables and #temp tables in memory       │
│  - Evaluates ETL-SQL syntax and functions            │
│  - Coordinates reads/writes across connections       │
└────────────┬──────────────┬──────────────┬───────────┘
             │              │              │
       MSSQL conn     FLATFILE conn   SFTP conn
       (remote SQL)   (local file)  (remote files)
```

### 1.1 Engine Context vs. Remote Context

| Context | What runs here | Key examples |
| :--- | :--- | :--- |
| **Engine** | ETL-SQL syntax, `@variables`, `#temp` tables, functions, `FOREACH`, `IF`, `MERGE` | `SELECT ... INTO #stage FROM conn.Table` |
| **Remote** | Native SQL of the target engine — passed verbatim | `EXECUTE mssql_conn BEGIN ... END` |

> [!IMPORTANT]
> **The Golden Rule**: Data always flows *through* the engine. If you want to move data from Postgres to a CSV file, you stage it in a `#temp` table first. This is where validation, masking, regex, and lineage tagging happen. The remote engines only ever receive simple, native SQL they can execute directly.

### 1.2 Why This Matters

- ETL-SQL functions like `REGEXP_LIKE`, `HASHBYTES`, and `FORMAT()` only work in **engine context** (`#temp` tables, engine-side SELECT).
- SQL dialect keywords (`TOP`, `GETDATE()`, `NOW()`) are validated against the target connection. Writing `SELECT TOP 10` against a Postgres connection causes a lint error — use `LIMIT 10` instead.
- When you use `EXECUTE conn BEGIN ... END`, that block is sent to the remote engine **unchanged** — write pure native SQL for the target inside those blocks.

---

## 2. Your First Connection

Every data source is represented by a named **connection**. Create one before querying it.

```sql
-- Create a connection to SQL Server using Windows auth
CREATE CONNECTION prod AS MSSQL(SERVER='sql01', DATABASE='SalesDB', TRUSTED_CONNECTION=TRUE);

-- Query it like any table
SELECT TOP 100 * FROM prod.dbo.Customers WHERE Status = 'Active';
```

### 2.1 Connection Syntax Options

```sql
-- Structured (recommended — readable and diffable)
CREATE CONNECTION mydb AS MSSQL(SERVER='sql01', DATABASE='HR', USER='etl', PASSWORD='s3cr3t');

-- Traditional string (useful for encrypted or DSN strings)
CREATE CONNECTION mydb AS MSSQL('Server=sql01;Database=HR;User=etl;Password=s3cr3t;');
```

### 2.2 Encrypting Credentials (`ENC:`)

Never commit plaintext passwords. Use the master password to encrypt connection strings:

```sql
USE PASSWORD = 'myMasterSecret';

-- The engine auto-decrypts ENC: strings at connection time
CREATE CONNECTION secure_db AS MSSQL('ENC:U2FsdGVkX1+abc...');
```

> [!TIP]
> The IDE and CLI can automatically encrypt all plaintext connection strings in a script when you save with a master password set. Use `HELP CONNECTION <type>` in the TUI (e.g. `HELP CONNECTION MSSQL`) to see every available `WITH()` option and its default for any connector type.

### 2.3 Environment Switching

Use `CREATE SETS` to define named groups of variables for different environments or setups:

```sql
CREATE SETS !DEV
BEGIN
    @server = 'dev-db.local',
    @db     = 'DevWarehouse'
END

CREATE SETS !PROD
BEGIN
    @server = 'prod-db.local',
    @db     = 'ProdWarehouse';
    SET WITH_PROMPT ON;   -- requires confirmation in interactive mode
END

USE SETS !DEV;
CREATE CONNECTION dw AS MSSQL(SERVER=@server, DATABASE=@db, TRUSTED_CONNECTION=TRUE);
```

### 2.4 Choosing a Connector

Choose the connector by the system you are talking to, not by the shape of the data you expect back. SQL databases use SQL connectors, local files use file connectors, APIs use `API`/`REST`, and transfer endpoints use SFTP/FTP/Azure Blob.

| Need | Connector family | Typical use |
| :--- | :--- | :--- |
| SQL Server, Postgres, Oracle | `MSSQL`, `POSTGRES`, `ORACLE`, `ODBC` | Query source systems or load warehouse tables |
| Cloud warehouses | `SNOWFLAKE`, `BIGQUERY`, `ODBC` | Read/write managed analytical stores |
| Local tabular or semi-structured files | `FLATFILE`, `CSV`, `EXCEL`, `JSON`, `XML`, `PARQUET`, `AVRO` | Ingest local extracts and produce deliverables |
| HTTP services | `API`, `REST` | Read or write JSON/XML API payloads |
| File transfer | `SFTP`, `FTP`, `AZURE_BLOB` | Send or receive files after staging/encryption |
| Email delivery | `SMTP` | Send notifications or report delivery emails |
| Filesystem metadata | `DIRECTORY` | Query local file inventories |
| Tests and demos | `MOCKDB` | Develop examples without real infrastructure |

Use `HELP CONNECTION <type>` for the exact options accepted by a connector, and [Data Connectors](../reference/connectors/data-connectors.md) for authentication patterns and mutually exclusive settings.

### 2.5 Running a Script

During development, run scripts from the CLI, TUI, or VS Code. Use the same script file in every host; the host changes how you interact with results, logs, prompts, and reports.

```powershell
# Run a script once
ETL-SQL run nightly_load.etlsql

# Pass INPUT variables
ETL-SQL run monthly_report.etlsql --var @env=PROD --var @month=2026-03

# Capture performance and logs
ETL-SQL run nightly_load.etlsql --perf --log C:\Logs\ETL-SQL\

# Development checkout form
dotnet run --project src/ETL-SQL.App -- run samples\01_Basic.etlsql
```

Use the TUI when you want an interactive editor and result panes:

```powershell
ETL-SQL ui edit MyScript.etlsql
```

Use VS Code when you want inline diagnostics, quick fixes, and report preview. Use the CLI or scheduler for repeatable automation.

---

## 3. Variables & State Management

Variables are the engine's memory. Prefix all variable names with `@`.

```sql
-- Declare with an explicit type (or omit for inferred ANY type)
DECLARE @BatchDate  DATE    = '2026-04-01';
DECLARE @Threshold  DECIMAL = 5000.00;
DECLARE @Label      STRING  = 'Q2-Load';
DECLARE @Note       MARKDOWN = '# Update';  -- Explicitly enables Markdown in reports
DECLARE @ids        LIST    = (1, 2, 3, 4);

-- Set a new value
SET @Threshold = @Threshold * 1.1;

-- Use in a query
SELECT * FROM prod.Sales WHERE SaleDate >= @BatchDate AND Amount > @Threshold;
```

### 3.1 INPUT and OUTPUT Parameters

Variables can be marked `INPUT` (overridable from CLI or parent script) or `OUTPUT` (returns a value to the caller):

```sql
-- In a sub-script
DECLARE @Env     STRING INPUT  = 'DEV';   -- CLI: --var @Env=PROD
DECLARE @RowsOut INT    OUTPUT = 0;

SET @RowsOut = (SELECT COUNT(*) FROM #staging);

-- In the parent script
DECLARE @Loaded INT;
RUN SCRIPT 'ingest.etlsql' WITH (@Env = 'PROD', @Loaded = @Loaded);
PRINT 'Loaded rows: ' + @Loaded;
```

### 3.2 RELDATE Variables

`RELDATE` is a specialized variable type for expressing dates relative to the time a script runs. Instead of computing date arithmetic manually, you declare the intent and the engine resolves it at execution time.

```sql
DECLARE @start RELDATE INPUT = 'M-1';   -- first day of last month
DECLARE @end   RELDATE INPUT = 'D';     -- today

SELECT * FROM prod.Sales WHERE SaleDate BETWEEN @start AND @end;
```

Common expressions:

| Expression | Resolves to |
| :--- | :--- |
| `'D'` | Today at midnight |
| `'D-1'` | Yesterday |
| `'D-7'` | Seven days ago |
| `'W-1'` | First day of last week |
| `'ME-1'` | Last day of last month |
| `'M-1'` | First day of last month |
| `'QE-1'` | Last day of last quarter |
| `'Y-1'` | January 1 of last year |
| `'N-2H'` | Exactly 2 hours before execution |
| `'2026-01-01'` | Fixed date (never changes) |

`RELDATE` variables are most useful when combined with `INPUT`, so callers (CLI, parent scripts, or Portal subscriptions) can override them at run time without editing the script.

```sql
-- Override at CLI
ETL-SQL run report.etlsql --var @start=W-1 --var @end=D

-- Override from a parent script
RUN SCRIPT 'daily_summary.etlsql' WITH (@start = 'D-1', @end = 'D');
```

#### Week-start day

Week-boundary expressions (`W`, `W-1`, `WE-1`, etc.) use **Monday** as the start of the week by default. Override for the current script with:

```sql
SET WEEK_START_DAY = 'Sunday';
```

Valid values: `Monday`, `Tuesday`, `Wednesday`, `Thursday`, `Friday`, `Saturday`, `Sunday`.

The default can also be changed for all scripts by setting `Engine.StartOfWeek` in `appsettings.json`.

Save-time security defaults can also be configured by administrators in `appsettings.json` under `Engine`. Scripts can still override these defaults with the matching `SET` command:

```json
{
  "Engine": {
    "AllowPlaintextSecrets": false,
    "NoSaveSensitive": false,
    "NoSaveConnection": false,
    "ConnectionEncryption": false
  }
}
```

### 3.3 Environment Sets — Switching DEV / QA / PROD

Instead of changing connection strings throughout your script, define **named environment sets** once and activate them with a single command:

```sql
-- Define environments once (usually stored in a shared setup script)
CREATE SETS !DEV
BEGIN
    @server   = 'dev-db.internal',
    @database = 'DevWarehouse'
END

CREATE SETS !PROD
BEGIN
    @server   = 'prod-db.internal',
    @database = 'ProdWarehouse';
    SET WITH_PROMPT ON;    -- requires confirmation before activating
END

-- Activate the environment for this run
USE SETS !DEV;

-- Now use the variables
CREATE CONNECTION dw AS MSSQL(SERVER=@server, DATABASE=@database, TRUSTED_CONNECTION=TRUE);

-- Switch to PROD (prompts for confirmation in interactive mode)
USE SETS !PROD;

-- Remove a set that is no longer needed
DROP SETS IF EXISTS !STAGING;
```

> [!TIP]
> `CREATE SETS` blocks are typically placed in a shared `_environments.etlsql` and loaded at the top of each orchestrator script via `RUN SCRIPT '_environments.etlsql'`.

### 3.4 Persistent Sessions and Checkpoint Resume

When running long or multi-stage pipelines, a failure in a late stage can be costly if you have to start the entire extraction process from scratch. ETL-SQL addresses this through **Persistent Sessions** and **Checkpoint Resumes**.

#### Implicit Session State Checkpoints

When running with `--session`, any top-level script label (a label not nested inside loops, conditionals, or try-catch blocks) acts as a **state checkpoint marker**.

When the execution pointer hits a top-level label, the engine:
1. Updates the internal `@_LAST_CHECKPOINT_LABEL` session variable to the label name.
2. Spills all active `#temp` tables to Apache Arrow table chunks under the session directory.
3. Serializes all variables and active connections to a JSON state file.

```sql
DECLARE @val INT = 10;

-- Hitting this top-level label saves @val = 10 to session storage
step_1: 
SET @val = @val + 5;

-- Hitting this top-level label saves @val = 15 to session storage
step_2:
SET @val = @val + 10;
```

#### How `--session` and `--resume` Interact

These two flags serve distinct roles and must not be confused:

| Command | What happens |
| :--- | :--- |
| `etl-sql run --session "job-id" --file ...` | **Fresh run.** State is saved at each checkpoint but not loaded. Every `--session`-only run starts with a clean environment, regardless of prior runs with the same ID. |
| `etl-sql run --session "job-id" --resume --file ...` | **Resumed run.** The engine loads state from the last checkpoint, skips all statements before that label, and continues from there. |
| `etl-sql run --resume --file ...` | **Error.** `--resume requires --session to be specified.` |
| `etl-sql run --session "job-id" --resume --file ...` (first run, no checkpoint saved) | **Error.** `--resume was specified but no saved session found for 'job-id'. Run without --resume to start fresh.` |

> [!IMPORTANT]
> `--session` alone does **not** restore prior state. Running the same session ID without `--resume` always starts from the top of the script with fresh variables. This prevents stale values from a previous run silently carrying over into a new one.

#### Resuming Execution After a Failure

If a script fails mid-run (e.g., a database timeout or network drop), resume from the last successfully completed checkpoint:

```powershell
# Initial run — fails somewhere after step_2 completes
etl-sql run --session "nightly-load-123" --file .\nightly_load.etlsql

# After fixing the external issue, resume from the last saved checkpoint
etl-sql run --session "nightly-load-123" --resume --file .\nightly_load.etlsql
```

When `--resume` is active, the engine:
1. Loads the saved session state (variables, `#temp` tables, connection metadata).
2. Identifies the last successfully saved checkpoint label (e.g., `step_2`).
3. Skips all statements in the script until it reaches that label.
4. Resumes executing normally from that label using the restored state.

#### Clearing a Session

To discard saved state for a session ID and start clean on the next run:

```powershell
etl-sql session clear "nightly-load-123"
```

---

## 4. The #Temp Table Workspace

Temporary tables (prefixed with `#`) are **in-memory engine-side staging areas**. They are the core of every multi-step pipeline.

```sql
-- Stage data from a remote source
SELECT id, UPPER(name) AS name, email
INTO #stage
FROM pg_source.customers
WHERE updated_at > DATEADD(DAY, -1, GETDATE());

-- Transform it in engine memory
UPDATE #stage SET email = NULL WHERE email NOT LIKE '%@%';

-- Then write to the target
INSERT INTO dest_db.dbo.Customers SELECT * FROM #stage;
```

### Why use `#temp` tables?
1. **Decoupling** — stage data from a slow legacy source before joining it with a modern cloud source
2. **Safety** — validate and clean before executing destructive `MERGE` or `DELETE` operations
3. **Engine-only functions** — apply `REGEX`, `HASHBYTES`, window functions, or `GETDATE()` to data from a source that doesn't natively support them

### 4.1 Creating and Modifying Temp Tables

```sql
-- Define structure explicitly
CREATE TABLE #Summary (
    Category  VARCHAR(50) NOT NULL,
    Total     DECIMAL(18,2),
    LoadedAt  DATETIME DEFAULT GETDATE()
);

-- Or create via SELECT INTO
SELECT * INTO #staging FROM source_db.Orders WHERE status = 'Open';

-- Alter structure
ALTER TABLE #staging ADD BatchTag VARCHAR(20);
ALTER TABLE #staging DROP COLUMN LegacyField;

-- Drop when done (auto-dropped at session end anyway)
DROP TABLE IF EXISTS #staging;
```

### 4.2 Querying Directories
While `FILE_LIST()` is a function that returns a table, you can also mount a directory as a permanent connection. This is useful when you need to join file metadata against other databases:

```sql
CREATE CONNECTION raw_files AS DIRECTORY('C:\Incoming\');

-- Query it like a table
SELECT FileName, Size, LastModified
FROM raw_files
WHERE Extension = '.csv' AND Size > 1024;
```

---

## 5. Core SELECT Patterns

### 5.1 Full Clause Order

```sql
SELECT [DISTINCT] [TOP n [PERCENT] [WITH TIES]]
    <columns>
[INTO <target>]
FROM <source>
[JOIN ... ON ...]
[WHERE ...]
[GROUP BY ...]
[HAVING ...]
[ORDER BY ... [ASC|DESC]]
[OFFSET n ROWS FETCH NEXT n ROWS ONLY]   -- pagination
[LIMIT n]                                 -- ANSI shorthand
[FOR JSON PATH, ROOT('name')]             -- JSON output
```

### 5.2 Row Limiting

```sql
-- T-SQL style
SELECT TOP 10 * FROM #data ORDER BY Amount DESC;

-- ANSI style
SELECT * FROM #data ORDER BY Amount DESC LIMIT 10;

-- Pagination
SELECT * FROM #data ORDER BY id OFFSET 100 ROWS FETCH NEXT 25 ROWS ONLY;
```

### 5.3 Aggregation with ROLLUP

```sql
-- Generate subtotals and a grand total in one query
SELECT Region, Product, SUM(Amount) AS Total
FROM #sales
GROUP BY ROLLUP(Region, Product)
ORDER BY Region, Product;
-- NULL in Region = grand total row; NULL in Product = region subtotal row
```

### 5.4 CTEs (Common Table Expressions)

```sql
WITH RecentOrders AS (
    SELECT CustomerId, COUNT(*) AS OrderCount, SUM(Amount) AS Total
    FROM #orders
    WHERE OrderDate >= DATEADD(MONTH, -3, GETDATE())
    GROUP BY CustomerId
)
SELECT c.Name, r.OrderCount, r.Total
FROM prod.Customers AS c
JOIN RecentOrders AS r ON c.Id = r.CustomerId
WHERE r.Total > 5000;
```

### 5.5 Cross-Source Joins

When data comes from multiple systems, stage each side into engine `#temp` tables first. This avoids asking one remote database to understand another system's dialect or credentials.

```sql
SELECT CustomerId, Email, Region
INTO #customers
FROM crm_db.Customers
WHERE IsActive = 1;

SELECT CustomerId, SUM(Amount) AS Revenue
INTO #revenue
FROM finance_db.Invoices
WHERE InvoiceDate >= DATEADD(MONTH, -1, GETDATE())
GROUP BY CustomerId;

SELECT c.Region, COUNT(*) AS Customers, SUM(r.Revenue) AS Revenue
FROM #customers AS c
LEFT JOIN #revenue AS r ON c.CustomerId = r.CustomerId
GROUP BY c.Region;
```

This is the safest default pattern. Push work down with `EXECUTE ... BEGIN ... END` only when the work is clearly native to one remote system and does not need engine-side tables.

### 5.6 Common Transformations

Use engine functions after staging when you need portable behavior across connectors:

```sql
SELECT
    CustomerId,
    LOWER(TRIM(Email)) AS Email,
    REGEX_REPLACE(Phone, '[^0-9]', '') AS PhoneDigits,
    HASHBYTES('SHA256', LOWER(TRIM(Email))) AS EmailHash,
    COALESCE(Region, 'Unknown') AS Region,
    CAST(OrderDate AS DATE) AS OrderDate
INTO #clean
FROM #raw;
```

Common transformation tools:

| Need | Use |
| :--- | :--- |
| Normalize strings | `TRIM`, `LOWER`, `UPPER`, `REPLACE`, `REGEX_REPLACE` |
| Validate strings | `REGEXP_LIKE`, `LIKE`, `LEN` |
| Handle missing values | `COALESCE`, `ISNULL`, `NULLIF` |
| Convert types safely | `CAST`, `TRY_CAST` |
| Mask or fingerprint sensitive values | `HASHBYTES` |
| Parse semi-structured data | JSON/XML functions in [Standard Library](../reference/functions/README.md) |
| Match messy names or addresses | `NORMALIZE`, `SIMILARITY`, `LEVENSHTEIN`, `SOUNDEX`, `FUZZY JOIN` |

### 5.7 Dialect Awareness

ETL-SQL validates some dialect mismatches before execution. A query against a Postgres connection should use Postgres syntax; a query against SQL Server can use SQL Server syntax.

| Pattern | SQL Server | Postgres | Engine-safe alternative |
| :--- | :--- | :--- | :--- |
| Limit rows | `TOP 10` | `LIMIT 10` | Stage to `#temp`, then use `LIMIT 10` |
| Current timestamp | `GETDATE()` | `NOW()` | Use engine-side `GETDATE()` after staging |
| Null fallback | `ISNULL(x, y)` | `COALESCE(x, y)` | `COALESCE(x, y)` |
| Native block | `EXECUTE mssql BEGIN ... END` | `EXECUTE pg BEGIN ... END` | Write native SQL inside the block |

Rule of thumb: if the query touches one remote SQL system and can benefit from its indexes, push it down. If it touches multiple systems, file data, variables, or engine-only functions, stage to `#temp`.

---


## Continue Learning

You now have the fundamentals: the engine mental model, connections, variables, temp-table
workspaces, and core `SELECT`. From here, each area has a focused home:

**Language and scripting**
- [Control Flow](../reference/control-flow/README.md) - `IF`/`WHILE`/`FOR`/`FOREACH`, `TRY...CATCH`, `WAITFOR`.
- [Statements](../reference/statements/README.md) - error handling, transactions, execution blocks, procedures, expressions.
- [Functions](../reference/functions/README.md) · [Data Types](../reference/data-types.md).

**Data movement and pipelines**
- [ETL Recipes](../cookbooks/etl-recipes.md) - complete patterns: staging, incremental load, quality gates, dead-letter queues, parallel loads.
- [File Operations](../reference/file-operations/README.md) - copy, transfer, compress, encrypt, email.
- [Pipelines and DAGs](pipelines-and-dags.md).

**Operate and schedule**
- [Orchestration](../administration/orchestration/README.md) - `CREATE JOB`, DAGs, sessions, CI/CD.
- [CLI Reference](../reference/cli/README.md) - `lint`, `explain`, profiling, `doctor`, and every command.

**Security and testing**
- [Secrets and Keys](../administration/platform/secrets.md) · [Platform Administration](../administration/platform/README.md).
- [Testing](testing.md) - `GENERATE`, `SEED`, and the MOCKDB connector.

**Reports and the portal**
- [Report SQL](report-sql.md) - author `.rptsql` dashboards.
- [Portal Administration](../administration/portal/README.md).

**Find anything**
- [Task Index](../task-index.md) - goal-oriented "how do I…" locator · [Syntax Index](../syntax-index.md) - keyword map.
## Next Steps

| Topic | Document |
| :--- | :--- |
| Find syntax by keyword | **[Syntax Index](../syntax-index.md)** |
| Statement syntax | **[Statement Reference](../reference/statements/README.md)** |
| Connector options and authentication | **[Data Connectors](../reference/connectors/data-connectors.md)** |
| All built-in functions | **[Standard Library](../reference/functions/README.md)** |
| File ops, email, lineage, Docker, jobs | **[Specialized Operations](../reference/file-operations/specialized-operations.md)** |
| 18 production-ready recipes | **[Cookbook.md](../cookbooks/etl-recipes.md)** |
| 55+ sample scripts inventory | **[Sample_Guide.md](sample-guide.md)** |
| Reporting & dashboards | **[Report_SQL_Guide.md](report-sql.md)** |
| Report examples | **[Report_Cookbook.md](../cookbooks/report-recipes.md)** |
| Portal users | **[ReportPortal_User_Guide.md](portal-user.md)** |
| Portal administrators | **[Portal Admin Guide](../administration/portal/README.md)** |
| Local and release test lanes | **[Testing.md](../../Testing.md)** |
| Documentation map | **[Docs README](../../README.md)** |
| Security policy | **[SECURITY.md](../../src/etl-sql-vscode/.vscode-test/vscode-win32-x64-archive-1.125.1/fcf604774b/resources/app/extensions/ms-vscode.js-debug-companion/SECURITY.md)** |
