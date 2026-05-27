# ETL-SQL User Manual: Thinking in Pipelines

Welcome to ETL-SQL. This guide is designed to help you transition from thinking in "Single Database SQL" to "Multi-Context Data Flow." Work through each section in order — each one builds on the last.

> [!TIP]
> **Stuck on something specific?** Use the table of contents below to jump directly to the section you need. For a searchable list of errors and gotchas, see the [FAQ](FAQ.md). For connector-specific syntax, see [Data_Connectors.md](Reference/Data_Connectors.md).

## Contents

0. [First Hour Walkthrough](#first-hour-walkthrough)
1. [The Pipeline Mental Model](#1-the-pipeline-mental-model)
2. [Your First Connection](#2-your-first-connection)
3. [Variables & State Management](#3-variables--state-management)
4. [The #Temp Table Workspace](#4-the-temp-table-workspace)
5. [Core SELECT Patterns](#5-core-select-patterns)
6. [Control Flow](#6-control-flow)
7. [Error Handling & Transactions](#7-error-handling--transactions)
8. [Data Movement Patterns](#8-data-movement-patterns)
9. [File Operations](#9-file-operations)
10. [Modular Scripts & Jobs](#10-modular-scripts--jobs)
11. [Metadata, Lineage & Tags](#11-metadata-lineage--tags)
12. [Debugging & Diagnostics](#12-debugging--diagnostics) — LINT, EXPLAIN, profiling, MOCKDB, `etl-sql doctor`
13. [Zero-Trust Security](#13-zero-trust-security)
14. [Mocking & Testing](#14-mocking--testing)
15. [Interactive TUI Editor](#15-interactive-tui-editor) — layout, results, compare, keyboard reference, snippet templates
16. [VS Code Authoring](#16-vs-code-authoring)
17. [Report-SQL Dashboards](#17-report-sql-dashboards)
18. [Report Portal Workflow](#18-report-portal-workflow)
19. [Next Steps](#next-steps)

---

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

Use `HELP CONNECTION <type>` for the exact options accepted by a connector, and [Data_Connectors.md](Reference/Data_Connectors.md) for authentication patterns and mutually exclusive settings.

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

`RELDATE` variables are most useful when combined with `INPUT`, so callers (CLI, parent scripts, or Report Portal subscriptions) can override them at run time without editing the script.

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
CREATE CONNECTION raw_files AS DIRECTORY('C:\Incoming\', RECURSIVE=TRUE);

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
| Parse semi-structured data | JSON/XML functions in [Standard_Library.md](Reference/Standard_Library.md) |
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

## 6. Control Flow

### 6.1 IF / ELSE IF / ELSE

```sql
DECLARE @rowCount INT = (SELECT COUNT(*) FROM #staging);

IF @rowCount = 0
BEGIN
    PRINT 'No data to process. Exiting.';
    RETURN;
END
ELSE IF @rowCount > 1000000
BEGIN
    PRINT 'Large batch detected — enabling chunked processing.';
    -- ... chunked logic
END
ELSE
BEGIN
    INSERT INTO dest_db.Transactions SELECT * FROM #staging;
END
```

### 6.2 WHILE / FOR / FOREACH Loops

```sql
-- WHILE: condition-based
DECLARE @retry INT = 0;
WHILE @retry < 3
BEGIN
    -- try operation
    SET @retry = @retry + 1;
    IF @retry = 3 BREAK;
    WAITFOR DELAY '00:00:05';
END

-- FOR: numeric range
FOR @i = 1 TO 12
BEGIN
    PRINT 'Processing month: ' + @i;
END

-- FOREACH: iterate a LIST
DECLARE @regions LIST = ('North', 'South', 'East', 'West');
FOREACH @r IN @regions
BEGIN
    SELECT * INTO #batch FROM prod.Sales WHERE Region = @r;
    INSERT INTO dw.RegionSales SELECT @r AS Region, * FROM #batch;
END
```

### 6.3 Timing & Polling

```sql
-- Fixed pause
WAITFOR DELAY '00:00:30';        -- 30 seconds
WAITFOR DELAY '00:00:00.500';    -- 500 milliseconds

-- Wait until a specific time today (or tomorrow if already past)
WAITFOR TIME '02:00:00';

-- Polling form — waits until the expression becomes truthy (polls every 200ms)
WAITFOR (SELECT COUNT(*) FROM control_db.JobStatus WHERE Status = 'Ready');
PRINT 'Condition met — proceeding.';

-- WAIT UNTIL is a more readable alias for the same polling form
WAIT UNTIL (SELECT COUNT(*) FROM control_db.JobStatus WHERE Status = 'Ready') > 0;

-- Equivalent WHILE form — use this when you need control over the poll interval
-- or want to perform additional logic between checks:
DECLARE @ready INT = 0;
WHILE @ready = 0
BEGIN
    SET @ready = (SELECT COUNT(*) FROM control_db.JobStatus WHERE Status = 'Ready');
    IF @ready = 0 WAITFOR DELAY '00:01:00';   -- check every minute
END
PRINT 'Job is ready — proceeding.';
```

---

## 7. Error Handling & Transactions

### 7.1 TRY...CATCH with Rollback

```sql
BEGIN TRY
    BEGIN TRANSACTION;

    -- Destructive operations inside a transaction
    MERGE INTO prod.Customers AS T
    USING #staging AS S ON T.id = S.id
    WHEN MATCHED THEN UPDATE SET T.name = S.name
    WHEN NOT MATCHED THEN INSERT (id, name) VALUES (S.id, S.name);

    DELETE FROM prod.StagingQueue WHERE Processed = 1;

    COMMIT;
    PRINT 'Load complete.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    PRINT 'ERROR: ' + ERROR_MESSAGE();
    THROW;   -- re-escalate so the scheduler or parent script sees the failure
END CATCH;
```

### 7.2 SET WHAT_IF — Safe Dry Runs

Always validate a destructive operation before running it for real:

```sql
-- Phase 1: Validate — no data is actually changed
SET WHAT_IF ON;
DELETE FROM prod.logs WHERE log_date < '2024-01-01';
MERGE INTO prod.Customers AS T USING #staging AS S ON T.id = S.id
    WHEN MATCHED THEN UPDATE SET T.status = 'Archived';
SET WHAT_IF OFF;

-- Review the console output, then run for real:
DELETE FROM prod.logs WHERE log_date < '2024-01-01';
```

### 7.3 ASSERT and EXPECT SCHEMA — Proactive Validation

Use these before destructive operations to fail fast with a clear message rather than silently loading bad data.

```sql
-- ASSERT: halt if a logical condition is false
ASSERT (SELECT COUNT(*) FROM #staging) > 0,
    'Staging table is empty — source feed may have failed';

ASSERT @total_amount >= 0,
    'Negative balance detected — aborting before MERGE';

-- EXPECT SCHEMA: halt (or warn) if the source schema has drifted
EXPECT SCHEMA #staging (
    CustomerId INT,
    Name       VARCHAR,
    Email      VARCHAR,
    Amount     DECIMAL(18,2)
);

-- Warn instead of abort (script continues, but logs a yellow warning)
EXPECT SCHEMA #staging (
    CustomerId INT,
    Name       VARCHAR
) ON DRIFT WARN;
```

> [!TIP]
> Place `ASSERT` and `EXPECT SCHEMA` checks immediately after a source extract and before any `MERGE` or `DELETE`. This pattern catches schema changes from upstream teams before they corrupt your target.

---

## 8. Data Movement Patterns

### 8.1 Extract → Transform → Load (ETL)

The standard pattern — always stage before loading:

```sql
-- Extract
SELECT id, name, email INTO #raw FROM source_db.Users WHERE active = 1;

-- Transform
UPDATE #raw SET email = LOWER(TRIM(email));
UPDATE #raw SET email = NULL WHERE email NOT LIKE '%@%.%';

-- Load
MERGE INTO dest_db.Users AS T USING #raw AS S ON T.id = S.id
WHEN MATCHED THEN UPDATE SET T.name = S.name, T.email = S.email
WHEN NOT MATCHED THEN INSERT (id, name, email) VALUES (S.id, S.name, S.email);
```

### 8.2 BULK INSERT — High-Speed File Loading

For large files (1M+ rows), use `BULK INSERT` for O(1) memory usage — it streams without loading the whole file:

```sql
BULK INSERT dest_db.DailyLogs
FROM 'C:\Incoming\logs_20260412.csv'
WITH (
    FORMAT        = 'CSV',
    FIRSTROW      = 2,       -- skip header row
    BATCHSIZE     = 10000,   -- commit every 10k rows
    MAXERRORS     = 5,       -- allow up to 5 bad rows
    FIELDTERMINATOR = ',',
    STRICT_SCHEMA = ON
);
```

### 8.3 EXECUTE — Remote Pushdown

Use `EXECUTE` when you need to run native SQL that isn't representable in ETL-SQL syntax, or to push work to the remote engine:

```sql
-- The block inside is sent verbatim to the remote (T-SQL dialect here)
EXECUTE prod_db INTO #remote_result
BEGIN
    WITH TopCustomers AS (
        SELECT TOP 100 Id, Name, SUM(Amount) AS Total
        FROM dbo.Orders GROUP BY Id, Name ORDER BY Total DESC
    )
    SELECT * FROM TopCustomers WHERE Total > 10000;
END;

-- Now work with the result in engine context
SELECT * FROM #remote_result ORDER BY Total DESC;
```

### 8.4 PARALLEL — Concurrent Execution

Load independent tables simultaneously to reduce wall-clock time:

```sql
PARALLEL
BEGIN
    BEGIN
        SELECT * INTO #DimDate    FROM src.DateDim;
        INSERT INTO dw.DimDate    SELECT * FROM #DimDate;
    END
    BEGIN
        SELECT * INTO #DimProduct FROM src.ProductDim;
        INSERT INTO dw.DimProduct SELECT * FROM #DimProduct;
    END
END;
PRINT 'Dimensions loaded in parallel.';

-- Add a concurrency limit to avoid overwhelming a source system
-- At most 3 branches run simultaneously; the rest queue
PARALLEL(3)
BEGIN
    RUN SCRIPT 'load_north.etlsql';
    RUN SCRIPT 'load_south.etlsql';
    RUN SCRIPT 'load_east.etlsql';
    RUN SCRIPT 'load_west.etlsql';
END;
```

### 8.5 Dynamic SQL & Pushdown (EXEC / EXECUTE)

`EXEC` and `EXECUTE` are functional synonyms. Build and execute SQL statements constructed at runtime or push native SQL blocks to remote connections. The engine supports five forms:


```sql
-- Form 1: Dynamic expression — EXEC(@string_expr)
-- Parses and runs the string in ENGINE context. Full access to #temp tables and @variables.
DECLARE @tbl = '#orders';
DECLARE @sql = 'SELECT COUNT(*) AS Total FROM ' + @tbl + ' WHERE Status = ''Open'';';
EXEC(@sql);
-- Optional: capture result
EXEC(@sql) INTO #result;

-- Form 2: Dynamic expression against a remote connection — EXEC(@expr) AT conn
DECLARE @archiveTable = 'Archive_' + CAST(YEAR(GETDATE()) AS STRING);
DECLARE @sql = 'SELECT TOP 100 * FROM dbo.' + @archiveTable + ' ORDER BY Id DESC;';
EXEC(@sql) AT prod_db INTO #archive;

-- Form 3: Parameterized remote query — EXEC(@expr) AT conn WITH(param1, param2, ...)
-- Parameters bind to @p0, @p1, ... in the SQL string (safe — avoids SQL injection)
EXEC('SELECT * FROM dbo.Orders WHERE Status = @p0 AND Year = @p1') AT prod_db
    WITH('Active', YEAR(GETDATE()))
    INTO #active_orders;

-- Form 4: Remote block pushdown — EXEC conn BEGIN ... END
-- Passes the block verbatim to the remote engine (native dialect). Same as EXECUTE ... BEGIN...END.
EXEC prod_db INTO #result
BEGIN
    WITH ranked AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY CustomerId ORDER BY OrderDate DESC) AS rn FROM dbo.Orders)
    SELECT * FROM ranked WHERE rn = 1;
END;

-- Form 5: Stored procedure call — EXEC proc_name [params]
EXEC LoadRegion 'North', '2026-01-01';
EXEC dbo.sp_archive @Year = 2025, @DryRun = 1;
```

> [!IMPORTANT]
> `EXEC(@sql)` without `AT conn` runs in **engine context** — it can read `#temp` tables and `@variables` from the current session. `EXEC(@sql) AT conn` sends the SQL string to the remote engine and cannot reference engine-side objects. `WHAT_IF` mode is respected: remote EXEC statements log what would run without executing.

> [!TIP]
> **Choosing the right form**: Use Form 1 for dynamic ETL logic that needs engine functions. Use Form 3 (parameterized) any time user input or variable values appear in the query string — it eliminates SQL injection risk. Use Form 4 when you need stored procs, CTEs, or native syntax the ETL-SQL parser doesn't handle directly.

### 8.6 Outputs and Deliverables

Most pipelines end in one of four ways:

| Destination | Pattern |
| :--- | :--- |
| Database table | `INSERT`, `MERGE`, or connector-specific `BULK INSERT` |
| Local file | Create a file connector or use file operations after exporting data |
| Transfer endpoint | Write/encrypt locally, then `SEND FILE ... AT sftp_conn` |
| Report/dashboard | Build a `.rptsql` manifest with `CREATE VISUAL` and `CREATE PAGE` |

Database load example:

```sql
MERGE INTO dw.CustomerSummary AS T
USING #summary AS S ON T.CustomerId = S.CustomerId
WHEN MATCHED THEN
    UPDATE SET T.Revenue = S.Revenue, T.LastSeen = S.LastSeen
WHEN NOT MATCHED THEN
    INSERT (CustomerId, Revenue, LastSeen)
    VALUES (S.CustomerId, S.Revenue, S.LastSeen);
```

File delivery example:

```sql
CREATE CONNECTION out_csv AS FLATFILE('C:\Exports\customer_summary.csv', FORMAT='CSV', HEADER=TRUE);

INSERT INTO out_csv
SELECT CustomerId, Revenue, LastSeen
FROM #summary;

COMPRESS FILE 'C:\Exports\customer_summary.csv'
TO 'C:\Exports\customer_summary.csv.gz'
WITH(OVERWRITE=ON);

SEND FILE 'C:\Exports\customer_summary.csv.gz'
TO '/outbound/customer_summary.csv.gz'
AT sftp_conn;
```

For report deliverables, keep the same data-prep pattern and add Report-SQL declarations at the end of the script. See [Report-SQL Dashboards](#17-report-sql-dashboards).

---

## 9. File Operations

Every file path must be **absolute**. Always check existence before operating.

```sql
DECLARE @src  = 'C:\Incoming\data.csv';
DECLARE @dest = 'C:\Archive\data.csv';

IF NOT FILE_EXISTS(@src)
BEGIN
    PRINT 'Source file not found: ' + @src;
    RETURN;
END

-- Copy, compress, encrypt, then transmit
COPY FILE     @src TO @dest WITH(OVERWRITE=ON);
COMPRESS FILE @dest TO @dest + '.gz' WITH(OVERWRITE=ON);
ENCRYPT FILE  @dest + '.gz' TO @dest + '.enc' PASSWORD('vaultkey', OVERWRITE=ON);

-- Upload to SFTP and clean up
SEND FILE @dest + '.enc' TO '/outbox/' AT sftp_conn;
DELETE FILE @dest + '.gz';
DELETE FILE @dest + '.enc';
```

### File Listing

```sql
-- List local files
SELECT Name, Size, LastModified
FROM FILE_LIST('C:\Incoming', '*.csv')
ORDER BY LastModified DESC;

-- List remote SFTP files
SELECT Name, Size, LastModified
INTO #remote_manifest
FROM REMOTE_FILE_LIST(sftp_conn, '/uploads/')
WHERE LastModified >= DATEADD(HOUR, -24, GETDATE());
```

> [!TIP]
> For the full file operation reference — `COMPRESS FILE`, `ENCRYPT FILE`, `MOVE FILE`, directory operations, and SFTP/FTP patterns — see [Specialized_Operations.md](Reference/Specialized_Operations.md).

---

## 10. Modular Scripts & Jobs

### 10.1 Breaking Scripts into Modules

```sql
-- orchestrator.etlsql
DECLARE @env STRING INPUT = 'DEV';

RUN SCRIPT 'extract.etlsql'   WITH (@env = @env);
RUN SCRIPT 'transform.etlsql' WITH (@env = @env);
RUN SCRIPT 'load.etlsql'      WITH (@env = @env);
```

### 10.2 Scheduling with CREATE JOB

```sql
-- Run every night at 2 AM
CREATE JOB NightlyLoad ON SCHEDULE EVERY 1 DAY AT '02:00' AS
BEGIN
    RUN SCRIPT 'C:\Scripts\nightly_load.etlsql';
END;

-- Monitor
SHOW JOBS;
SHOW JOB HISTORY NightlyLoad;

-- Terminate a hanging job (HistoryId from SHOW JOBS)
KILL JOB 12345;
```

### 10.3 Procedures for Reusable Logic

```sql
CREATE PROCEDURE LoadRegion @Region STRING, @StartDate DATE
AS
BEGIN
    SELECT * INTO #batch
    FROM prod.Sales
    WHERE Region = @Region AND SaleDate >= @StartDate;

    INSERT INTO dw.RegionSales SELECT @Region, * FROM #batch;
    PRINT 'Loaded ' + @Region + ' from ' + CAST(@StartDate AS STRING);
END;

EXEC LoadRegion 'North', '2026-01-01';
EXEC LoadRegion 'South', '2026-01-01';
```

> [!TIP]
> For the full scheduling reference — `SHOW JOBS`, `DROP JOB`, `KILL JOB`, CI/CD integration, and Windows Service deployment — see the [Orchestrator's Guide](Orchestrators_Guide.md).

Published Orchestrator bundles let production jobs run immutable script versions instead of live disk files:

```sql
PUBLISH BUNDLE 'nightly-load' FROM 'C:\ETL\nightly' ENTRY 'main.etlsql';

CREATE JOB NightlyLoad ON SCHEDULE EVERY 1 DAY AT '02:00' AS
    RUN SCRIPT 'orch://nightly-load/main.etlsql';
```

The job stores a pinned version such as `orch://nightly-load@1/main.etlsql`. Dynamic `RUN SCRIPT @path` dependencies cannot be published; keep those scripts in live file mode.

---

## 11. Metadata, Lineage & Tags

ETL-SQL automatically tracks where every piece of data came from. Tag columns and tables using inline `/* ... */` comments:

```sql
SELECT
    UserId   /* @d: Internal surrogate key; @PII: false; */,
    Email    /* @d: User email address; @PII: true; @owner: SecurityTeam; */,
    Region   /* @d: Sales territory code; */
INTO #TaggedUsers
FROM prod.Users /* @sensitivity: medium; */;

-- View the lineage tree
SHOW LINEAGE FOR #TaggedUsers;

-- Export a Mermaid diagram + audit table
SHOW LINEAGE FOR #TaggedUsers TO 'C:\Reports\user_lineage.md';

-- Query lineage programmatically
SELECT Operation, SourceTables, TargetColumn
FROM LINEAGE(#TaggedUsers)
WHERE TargetColumn = 'Email';
```

---

### 11.1 Cross-Run Lineage History

In-session `SHOW LINEAGE` shows what happened within the current script run. For questions that span many runs — "what jobs wrote to this table last week?" or "which outputs ever carried a PII tag?" — use the cross-run catalog:

```sql
-- What wrote to the Orders table across all job runs?
SHOW LINEAGE HISTORY FOR TABLE Orders INTO #h;
SELECT DISTINCT JobName, SourceTables FROM #h ORDER BY JobName;

-- Which jobs touched PII columns this week?
SHOW LINEAGE HISTORY FOR TAG pii = 'true' LIMIT 200 INTO #pii;
SELECT DISTINCT JobName, TargetTable, TargetColumn
FROM #pii
WHERE RunAt >= DATEADD(DAY, -7, GETDATE());
```

Both commands support `LIMIT n` and `INTO #temp`. Results are ordered most-recent-run first. Ad-hoc CLI runs are stored with `JobName = NULL`; scheduled Orchestrator jobs are stored with the job name.

---

### 11.2 Script Metadata Headers

The engine automatically reads a structured comment at the top of any `.etlsql` file and records the metadata in lineage logs:

```sql
/*
   @author:      Chuck
@version:     0.7.0
   @description: Nightly customer sync from Postgres → SQL Server DW
*/

DECLARE @BatchDate DATE = GETDATE();
...
```

Any `@key: value` pair is captured. `@author` defaults to the current system user if omitted. This metadata appears in `SHOW LINEAGE` output and the Orchestrator job history.

---

## 12. Debugging & Diagnostics

### 12.1 LINT — Before You Run

```sql
-- Analyze a script for errors and best-practice violations before executing
LINT 'C:\Scripts\nightly_load.etlsql';
```

### 12.2 EXPLAIN — Query Plans

```sql
EXPLAIN
SELECT o.OrderId, c.Name
FROM prod.Orders AS o
JOIN prod.Customers AS c ON o.CustomerId = c.Id
WHERE o.Status = 'Open';
```

### 12.3 PROFILING — Performance Measurement

```sql
SET PROFILING ON;
RUN SCRIPT 'C:\Scripts\heavy_transform.etlsql';
SET PROFILING OFF;

-- View top 10 slowest operations
SHOW PROFILE INTO #perf;
SELECT * FROM #perf ORDER BY DurationMs DESC LIMIT 10;
```

> [!TIP]
> `@@LAST_EXEC_MS`, `@@TOTAL_SPILLED_BYTES`, and `@@PEAK_MEMORY_MB` are system variables you can query at any time — no profiling mode required. See [Administrators_Guide.md §5.4](Administrators_Guide.md) for the full system variable reference.

### 12.4 MOCKDB — Safe Development

Use the built-in mock database for development without touching production:

```sql
CREATE CONNECTION m AS MOCKDB();

-- Pre-seeded tables: Users, Products, Orders, Employee, departments
SELECT * FROM m.Users;
SELECT u.UserName, o.Total
FROM m.Users AS u
JOIN m.Orders AS o ON u.UserID = o.CustomerID;
```

### 12.5 `etl-sql doctor` — Environment Health Check

Before running scripts in a new environment, or when troubleshooting unexpected failures, run the health check command:

```bash
etl-sql doctor
```

The command checks the most common setup problems and prints a status table:

| Check | What it validates |
| :--- | :--- |
| Operating System / .NET Runtime | Correct platform and runtime version |
| Base Directory Write | Engine can write logs and session files |
| Temp Directory Write | Engine can write spill and staging files |
| Disk Space | At least 500 MB free on the app drive |
| ODBC Driver Manager | `odbc32.dll` present (Windows) or `odbcinst.ini` found (Linux/Mac) |
| appsettings.json | Configuration file is present |
| Authorized Hosts | At least one host is allowed in the security config |
| Registered Connectors | Connector registry loaded at least one connector |
| Orchestrator History DB | SQLite history path is configured |
| App / Script Log Dirs | Log directories are writable |

**Options:**

```bash
# Exit with code 1 if any check is WARN or FAIL (useful in CI setup scripts)
etl-sql doctor --strict

# Run deeper smoke tests and optional configured service probes
etl-sql doctor --profile full

# Output machine-readable JSON (useful for monitoring scripts)
etl-sql doctor --json

# Combine flags
etl-sql doctor --profile full --strict --json
```

The `--profile full` option adds checks that exercise the engine, report stack, local toolchain, and any configured service endpoints:

| Check | What it does |
| :--- | :--- |
| Parser Smoke | Parses `SELECT 1 AS n;` and verifies the AST is non-empty |
| Engine Smoke (MOCKDB) | Runs a live query against the built-in MOCKDB connector |
| ENC: Round-Trip | Encrypts and decrypts a value and verifies the result matches |
| Linter Smoke | Runs the linter on a trivial script and verifies no errors |
| Security Guardrail Smoke | Verifies restricted system paths are rejected |
| Report Build Smoke | Builds a small Report-SQL manifest |
| Report PDF Export | Verifies the built-in PDF exporter returns a PDF payload |
| Graphviz / Browser Runtime | Reports optional runtime availability when configured features require them |
| Asset Drift / Node.js / Portal DB | Checks shared report assets, Node.js availability, and portal database configuration |
| Portal / Orchestrator / SMTP / SFTP / Azure Blob | Probes configured service endpoints; skipped as OK when no endpoint is configured |

> [!TIP]
> Run `etl-sql doctor --profile full` as part of first-time setup, release validation, or when migrating to a new host machine.

---

## 13. Zero-Trust Security

ETL-SQL treats every script as untrusted by default regardless of who wrote it or where it came from. The sandbox rules below are always enforced — they cannot be disabled, only selectively relaxed inside an administrator-approved `Safe Zone`. See [SECURITY.md](../SECURITY.md) for the complete policy.

Key rules enforced by the engine:

| Rule | Details |
| :--- | :--- |
| **No system directory access** | `Windows`, `System32`, `.ssh`, `.git`, `.aws` are permanently blocked |
| **No root drive access** | Operations targeting `C:\` or `/` directly are blocked |
| **No script self-modification** | Writing `.etlsql`, `.sql`, `.py`, `.js`, `.sh` files is blocked |
| **No dangerous file types** | `.dll`, `.exe`, `.bat`, `.cmd`, `.sh`, `.msi` cannot be read or written |
| **Operation cap** | Max 100 filesystem operations per script; max 5 recursive directory levels |
| **Credential masking** | Never `PRINT` passwords, tokens, or `ENC:` values |

Use `SET WHAT_IF ON` before any destructive operation. See [SECURITY.md](../SECURITY.md) for the complete security policy.

---

### 13.1 Approved Safe Zones

ETL-SQL operates on a **Whitelisting** principle. By default, the engine is blocked from reading or writing to any directory on your system. To perform file operations, you must authorize specific paths in your `appsettings.json` file.

#### 13.1.1 Authorizing a Path
In your `appsettings.json`, add your project directories to the `Security.ApprovedSafeZones` list:

```json
{
  "Security": {
    "ApprovedSafeZones": [
      "C:\\Users\\chuck\\scratch\\ETL-SQL\\samples\\",
      "D:\\Data\\Ingestion\\"
    ]
  }
}
```

> [!CAUTION]
> **Trailing Slashes Matter**: Always include a trailing slash (e.g. `C:\Data\`) to ensure the entire directory is whitelisted. Without it, the engine may only authorize the specific file named `Data`.

#### 13.1.2 Troubleshooting "Access Denied"
If you receive an error like `Access to path '...' is denied by security policy`:
1. Check if the path is inside one of the `ApprovedSafeZones`.
2. Ensure you are using **Absolute Paths**. The engine cannot validate relative paths against safe zones reliably.
3. Verify that the file extension is not in the `Security.BlockedExtensions` list.

---

### 13.2 Runaway Protection

To prevent accidental resource exhaustion or malicious behavior, the engine enforces conservative per-script limits.

| Limit | Default | Override |
| :--- | :--- | :--- |
| Filesystem operations | 100 operations | `SET ALLOW_FILE_OPERATIONS = n` |
| Recursive directory depth | 5 levels | `SET ALLOW_RECURSIVE_LAYERS = n` |
| Generated rows | 10,000 rows per `GENERATE` | `SET MAX_GENERATE_ROWS = n` |
| SMTP sends | 100 emails per script | `SET MAX_SMTP_EMAILS_PER_SCRIPT = n` |
| Parallel branches | Host security ceiling | `SET MAX_PARALLEL_DEGREE = n` |
| Regex match time | Host security ceiling | `SET REGEX_MATCH_TIMEOUT = n` |
| String result size | Host security ceiling | `SET MAX_STRING_RESULT_SIZE = n` |

Script overrides can lower limits freely. Raising security-sensitive limits above the configured host ceiling requires administrator-approved safe-zone treatment.

If you are performing a large-scale migration (e.g. moving 1,000 files), explicitly raise the relevant limit in your script:

```sql
-- Raise the limit for the current session
SET ALLOW_FILE_OPERATIONS = 2000;

FOREACH @file IN FILE_LIST('C:\LargeDir\')
BEGIN
    COPY FILE @file.PATH TO 'D:\Archive\';
END
```

For large notification jobs, keep SMTP limits intentional and visible:

```sql
SET MAX_SMTP_EMAILS_PER_SCRIPT = 250;
SEND EMAIL TO 'ops@example.com' SUBJECT 'Batch complete' BODY 'All regions loaded.' AT mailer;
```

---

### 13.3 Securing Credentials

Never store plaintext passwords in your scripts. ETL-SQL provides automated tools to transform vulnerable scripts into secure ones using `ENC:` (Encrypted) strings.

#### 13.3.1 The VS Code Quick Fix
When the ETL-SQL linter detects a plaintext password in a `CREATE CONNECTION` statement, it will highlight it with a security warning. 
1. Hover over the warning.
2. Select **Quick Fix...**
3. Choose **Secure connection credentials...**
4. Enter your **Master Password** when prompted.
5. The extension will automatically encrypt the password and update your script to use the `ENC:` format.

#### 13.3.2 TUI Auto-Encryption
The Terminal IDE (TUI) includes a proactive "One-Way Valve" guardrail. If you attempt to save a script containing plaintext credentials:
- The TUI will interrupt the save.
- It will prompt you for a Master Password.
- It will automatically transform the script before it hits the disk.

#### 13.3.3 Manual Encryption via CLI
If you are working outside the IDE, you can manually generate encrypted strings using the engine utility:
```bash
etl-sql encrypt "myPlainPassword" --pass "myMasterSecret"
```
Copy the output (starting with `ENC:`) and paste it into your `CREATE CONNECTION` statement.

#### 13.3.4 Authorizing the Session
Once your script uses `ENC:` strings, the engine needs the Master Password at runtime to decrypt them. Use the `USE PASSWORD` statement at the top of your script:
```sql
-- Secure: Prompts the user for the password at runtime (never stored)
USE PASSWORD PROMPT;

-- Convenient: Sets the password for the current session (variable masking applies)
USE PASSWORD = 'myMasterSecret';

CREATE CONNECTION db AS MSSQL(USER='me', PASSWORD='ENC:abc123...');
```

---

## 14. Mocking & Testing

Great pipelines require great tests. ETL-SQL provides tools to simulate complex data scenarios without touching production systems.

### 14.1 The `GENERATE` Statement
The `GENERATE` statement is the primary tool for creating synthetic data. It allows you to populate `#temp` tables or `@variable` tables using rule-based functions.

```sql
GENERATE 10 ROWS INTO #orders AS (
    OrderID    = 'SEQUENCE(1001, 1)',
    Status     = 'RANDOM(Open, Shipped, Cancelled)',
    Amount     = 'RANDOM_DECIMAL(10.0, 500.0)',
    OrderDate  = 'SEQUENCE(2026-01-01, 1, DAY)'
);
```

### 14.2 Deterministic Testing with `SEED`
To ensure your tests are reproducible, use the `SEED` option. This forces the random number generator to produce the same sequence of values every time the script runs.

```sql
-- This will always produce the same 10 "random" categories
GENERATE 10 ROWS INTO #test WITH (SEED = 42)
AS (
    Category = 'RANDOM(Electronics, Apparel, Home)'
);
```

### 14.3 Table Variables
For lightweight, isolated data storage, use `DECLARE @var TABLE`. These variables behave exactly like `#temp` tables but are scoped to the variable manager.

```sql
DECLARE @audit TABLE;

-- Populate it
GENERATE 5 ROWS INTO @audit AS (
    Event = 'RANDOM(Login, Logout, Export)',
    Time  = 'SEQUENCE(08:00:00, 00:01:00)'
);

-- Query it
SELECT * FROM @audit;
```

### 14.4 The `MOCKDB` Connector
While `GENERATE` creates custom data, the `MOCKDB` connector provides pre-populated tables (Users, Orders, etc.) for testing standard join and aggregation logic quickly.

```sql
CREATE CONNECTION test_src AS MOCKDB();
SELECT * FROM test_src.Users;
```

---

## 15. Interactive TUI Editor

Launch the terminal IDE with:

```bash
ETL-SQL ui edit MyScript.etlsql
```

### 15.1 Layout

The TUI is divided into three regions:

```
┌───────────────────────────────────────────────────────────────┐
│  Header — filename, focus state                               │
├───────────────────────────────────────────────────────────────┤
│  Editor  (~60% height)                                        │
│  Line numbers │ Syntax-highlighted script                     │
├────────────────────────┬──────────────────────────────────────┤
│  Execution Tree        │  Message Log                         │
│  ├─ ✓ Step 1           │  [INFO] Connected to prod            │
│  ├─ ✓ PARALLEL (4)     │  [ERROR] Division by zero            │
│  └─ ✗ Step 3           │                                      │
├───────────────────────────────────────────────────────────────┤
│  F1:Help  F5:Run  F6:Focus  F4:Panel  │  ○ script.etlsql  PIPELINE  │  Ln 1, Col 1  ⏱ 340ms
└───────────────────────────────────────────────────────────────┘
```

The **lower panel** cycles with `F4`: Pipeline+Messages → Results → Performance.

### 15.2 Results Panel

Press `F4` once to switch to the Results panel after execution.

| Action | Key |
|--------|-----|
| Scroll rows | `↑ ↓ PgUp PgDn` (while focused — press `F6` first) |
| Scroll columns | `Ctrl+Left / Right` |
| Switch result sets | `Left / Right` arrows |
| Filter rows | `Ctrl+F` — type a substring, Enter to apply, Escape to clear |
| Export to CSV | `Ctrl+P` — prompts for a file path, writes RFC 4180 CSV |
| Focus / unfocus | `F6` |

When a filter is active the header shows `Filter: foo  12/847 rows` and the border turns yellow.

### 15.3 Compare Mode

When a script produces multiple result sets, `F7` enters Compare Mode:

- All result sets are stacked vertically in a maximized panel
- The active pane has a **magenta** border and `◀` marker — cycle with `F8`
- Each pane scrolls and filters independently
- `Escape` clears the active pane's filter, or exits Compare Mode if no filter is set
- `Ctrl+M` toggles the panel size if you want to see more of the editor

### 15.4 Performance Panel

Press `F4` twice (or once from Results) to see the Performance panel. It populates when `SET PROFILING ON` is active and shows per-statement timing, row counts, memory usage, and disk-spill totals.

```sql
SET PROFILING ON;
SELECT * FROM prod.Orders JOIN prod.Customers ON ...;
SET PROFILING OFF;
```

### 15.5 Keyboard Reference

Press `F1` inside the editor for the full interactive help overlay (shows live state for focus and active panel). While the overlay is open, press `F2` to toggle to the **Snippet Reference** page, which lists every available `$trigger` and its description. Press `F2` again to return to the keyboard table.

| Key | Action |
|-----|--------|
| `F1` | Help overlay — any key to close |
| `F2` (while F1 open) | Toggle help overlay: keyboard reference ↔ snippet list |
| `F4` | Cycle lower panel |
| `F5` / `Shift+F5` | Run script / run current statement |
| `F6` | Toggle Editor ↔ Results focus |
| `F7` | Enter / exit Compare mode |
| `F8` | Cycle active pane in Compare mode |
| `Ctrl+M` | Maximize / restore lower panel |
| `Ctrl+/` | Toggle `--` comment on selection |
| `Tab` / `Shift+Tab` | Indent / dedent selected block (or navigate snippet placeholders — see §15.6) |
| `Ctrl+Left/Right` | Word jump |
| `Ctrl+Shift+Left/Right` | Word select |
| `Alt+Up/Down` | Add cursor above / below |
| `Ctrl+F` | Find (or filter rows in Results focus) |
| `Ctrl+P` | Export results to CSV |
| `Ctrl+Q` | Exit |

### 15.6 Snippet Templates

Typing a `$trigger` word at the **start of a statement line** and pressing `Tab` or `Enter` expands it into a full scaffold template. Every placeholder in the expanded template is wrapped in `«angle quotes»` so you can cycle through them without hunting.

**Accepting a snippet:**

```
$bar          ← type this at statement start, then Tab
```

Expands to:

```sql
CREATE VISUAL «VisualName» AS BAR (
  SOURCE   = («SELECT * FROM #data»),
  MAPPINGS (X = «category», Y = «value»),
  OPTIONS  (AXIS_SORT = VALUE_DESC, TITLE = '«Chart Title»')
);
```

The first placeholder (`«VisualName»`) is automatically selected. Fill it in and press `Tab` to jump to the next.

**Tab-stop navigation:**

| Key | Action |
|-----|--------|
| `Tab` | Jump to next `«placeholder»` |
| `Shift+Tab` | Jump to previous `«placeholder»` |
| `Escape` | Exit snippet mode (cursor stays, placeholders remain as text) |

Snippet mode exits automatically when you tab past the last placeholder.

**Available snippets by category:**

*Visual charts* — `$bar`, `$line`, `$pie`, `$donut`, `$kpi`, `$tbl`, `$map`, `$hbar`, `$gauge`, `$scatter`, `$heatmap`, `$radar`, `$funnel`, `$waterfall`, `$treemap`, `$boxplot`

*Database connectors* — `$mssql`, `$postgres`, `$oracle`, `$snowflake`, `$bigquery`, `$odbc`

*File connectors* — `$csv`, `$excel`, `$parquet`, `$json`, `$avro`, `$xml`

*Remote / network connectors* — `$sftp`, `$ftp`, `$blob`, `$api`, `$smtp`

*Script objects* — `$proc`, `$func`, `$view`, `$dataset`, `$job`

Type `HELP SNIPPETS` in the REPL to list all triggers and descriptions. Type `HELP SNIPPETS <trigger>` (e.g., `HELP SNIPPETS bar`) to see the full template body for a specific snippet.

**User-defined snippets:**

You can add your own `$trigger` snippets by placing `.md` files in a directory and setting `Snippets:UserSnippetsPath` in `appsettings.json`:

```json
"Snippets": {
  "UserSnippetsPath": "C:\\MyTeam\\etlsql-snippets"
}
```

Each file must follow the same frontmatter format:

```markdown
---
trigger: $myconn
label: My Standard Connection
description: Company-standard database connection template
---
CREATE CONNECTION «ConnName» AS MSSQL(
  SERVER   = '«prod-sql01.example.com»',
  DATABASE = '«database»',
  TRUSTED_CONNECTION = ON
);
```

A user snippet with the same trigger as a built-in overrides the built-in. User snippets appear in autocomplete and `HELP SNIPPETS` alongside the built-ins.

---

## 16. VS Code Authoring

The VS Code extension is the best day-to-day authoring surface when you want linting, syntax highlighting, quick fixes, and report previews without leaving your editor.

Use it for:

- Writing `.etlsql` scripts with live diagnostics.
- Writing `.rptsql` reports with Report-SQL syntax highlighting.
- Encrypting plaintext connection credentials through the security quick fix.
- Browsing pipeline, result, variable, metadata, and report preview panels.
- Expanding `$trigger` snippet templates with native VS Code tab-stop navigation.

**Snippets in VS Code:** The same 38 built-in `$trigger` templates available in the TUI are delivered as VS Code-native completions with `${N:placeholder}` tab stops. Type `$bar` at the start of a statement and accept the completion — VS Code's standard Tab key cycles through all placeholders. User-defined snippets from `Snippets:UserSnippetsPath` are also available in the completion list.

The extension uses the ETL-SQL language server, so diagnostics should match command-line lint behavior. When in doubt, run the same script through the CLI or TUI before scheduling it.

Typical workflow:

```sql
-- 1. Write and lint locally
CREATE CONNECTION src AS MOCKDB();
SELECT * INTO #users FROM src.Users;

-- 2. Add validations before writes
ASSERT (SELECT COUNT(*) FROM #users) > 0, 'Expected at least one user';

-- 3. Run the script or publish it through your chosen host
```

See [Architecture/VSCodeExtension.md](Architecture/VSCodeExtension.md) for implementation details and [Reference/Grammar.md](Reference/Grammar.md) for the syntax accepted by the language server.

---

## 17. Report-SQL Dashboards

Report-SQL files (`.rptsql`) are normal ETL-SQL scripts with reporting statements at the end. Put data preparation first, then define datasets, visuals, pages, buttons, navigation, and optional portal behavior.

Minimal report shape:

```sql
SET REPORT TITLE = 'Sales Overview';
SET REPORT DESCRIPTION = 'Daily revenue and order count by region';

CREATE CONNECTION src AS MOCKDB();

SELECT Region, COUNT(*) AS Orders, SUM(Total) AS Revenue
INTO #sales
FROM src.Orders
GROUP BY Region;

CREATE VISUAL RevenueByRegion AS BAR (
    SOURCE = (SELECT Region, Revenue FROM #sales),
    MAPPINGS (CATEGORY = Region, VALUE = Revenue),
    TITLE = 'Revenue by Region'
);

CREATE PAGE Overview AS DASHBOARD (
    STRUCTURE = 'A',
    MAP ('A' = RevenueByRegion)
);
```

Key rules:

| Rule | Why it matters |
| :--- | :--- |
| Data prep first, report declarations last | The report manifest is built from the final script state |
| `SOURCE` queries feed visuals | Visuals should not repeat heavy preparation logic |
| `CREATE PAGE ... MAP` lays out visuals | Page structure controls the dashboard grid |
| `CREATE BUTTON ButtonName AS (...)` defines button controls | Buttons use the same page-style `AS (...)` form |
| Filters use `ACTIONS` to set parameters | Slicers, date pickers, sliders, search, and inputs drive re-query behavior |
| Page and visual `STYLE` blocks cascade | Use page-level defaults and visual-level overrides |

Filter example:

```sql
DECLARE @region TEXT INPUT = 'All';

CREATE VISUAL RegionFilter AS SLICER (
    SOURCE = (SELECT DISTINCT Region FROM #sales ORDER BY Region),
    MAPPINGS (VALUE = Region),
    ACTIONS (ON_CHANGE = SET_PARAMETER(@region, Region))
);
```

For the complete language surface, use [Report_SQL_Guide.md](Report_SQL_Guide.md). For copy-pasteable dashboard patterns, use [Report_Cookbook.md](Report_Cookbook.md).

---

## 18. Report Portal Workflow

The Report Portal hosts `.rptsql` reports for browser users. It adds catalog folders, snapshots, permissions, favorites, subscriptions, saved views, alerts, share links, embed tokens, and operational history.

The normal lifecycle is:

1. Author and test a `.rptsql` report locally.
2. Publish it to the portal catalog.
3. Assign folder/report permissions.
4. Refresh or schedule snapshots.
5. Let users view, favorite, subscribe, and save filtered views.
6. Review history, dependencies, usage metrics, and effective permissions.

Portal administration is script-first inside a portal execution block:

```sql
EXECUTE portal BEGIN
    PUBLISH REPORT 'C:\Reports\Sales.rptsql'
        TO '/Finance/Sales'
        AS 'Sales Overview';

    FAVORITE REPORT '/Finance/Sales/Sales Overview';

    SHOW REPORT HISTORY '/Finance/Sales/Sales Overview';
    SHOW REPORT DEPENDENCIES '/Finance/Sales/Sales Overview';
END
```

Common portal commands include:

| Task | Command family |
| :--- | :--- |
| Publish and refresh reports | `PUBLISH REPORT`, `REFRESH REPORT` |
| User navigation | `FAVORITE REPORT`, `CREATE SAVED VIEW`, `SHOW CATALOG SEARCH` |
| Delivery | `CREATE SUBSCRIPTION`, `CREATE ALERT` |
| Sharing and embedding | `CREATE SHARE LINK`, `CREATE EMBED TOKEN` |
| Operations | `SHOW REPORT HISTORY`, `SHOW REPORT DEPENDENCIES`, `SHOW PORTAL USAGE METRICS` |
| Security review | `SHOW EFFECTIVE PERMISSIONS`, `VALIDATE REPORT SCRIPT` |

For browser usage, see [ReportPortal_User_Guide.md](ReportPortal_User_Guide.md). For deployment and administration, see [ReportPortal_Administrators_Guide.md](ReportPortal_Administrators_Guide.md).

---

## Next Steps

| Topic | Document |
| :--- | :--- |
| Full language syntax — every keyword | **[Grammar.md](Reference/Grammar.md)** |
| Connector options and authentication | **[Data_Connectors.md](Reference/Data_Connectors.md)** |
| All built-in functions | **[Standard_Library.md](Reference/Standard_Library.md)** |
| File ops, email, lineage, Docker, jobs | **[Specialized_Operations.md](Reference/Specialized_Operations.md)** |
| 18 production-ready recipes | **[Cookbook.md](Cookbook.md)** |
| 55+ sample scripts inventory | **[Sample_Guide.md](Sample_Guide.md)** |
| Reporting & dashboards | **[Report_SQL_Guide.md](Report_SQL_Guide.md)** |
| Report examples | **[Report_Cookbook.md](Report_Cookbook.md)** |
| Portal users | **[ReportPortal_User_Guide.md](ReportPortal_User_Guide.md)** |
| Portal administrators | **[ReportPortal_Administrators_Guide.md](ReportPortal_Administrators_Guide.md)** |
| Local and release test lanes | **[Testing.md](Testing.md)** |
| Documentation map | **[Docs README](README.md)** |
| Security policy | **[SECURITY.md](../SECURITY.md)** |
