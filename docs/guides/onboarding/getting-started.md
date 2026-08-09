# ETL-SQL User Manual: Thinking in Pipelines

Welcome to ETL-SQL. This guide helps you transition from "Single Database SQL" to "Multi-Context Data Flow." It is a **narrative onboarding** — the mental model, your first script, connections, and variables. When a topic turns into lookup (every option, every function, every flag), this guide links you to the exact reference page instead of restating it.

> [!TIP]
> **Looking for exact syntax?** Use the [Syntax Index](../../syntax-index.md) (keyword map) or the [Task Index](../../task-index.md) ("how do I…" locator). For connector options and auth, see [Data Connectors](../../reference/connectors/README.md). For errors and gotchas, see the [FAQ](../patterns/faq.md).

> **Applies to:** every deployment profile. The language is identical from a workstation to SaaS — larger profiles add operational boundaries around your scripts, never new syntax.

## What Makes ETL-SQL Different

ETL-SQL is script-first data orchestration. Pipelines, reports, schedules, validation, and governance metadata live in plain-text `.etlsql` and `.rptsql` files that can be reviewed, diffed, tested, packaged, and run from the CLI, VS Code, notebooks, Portal, Orchestrator, or CI/CD.

The engine puts the **T** back in the middle of ETL. Instead of loading everything first and hoping every downstream transformation fits one warehouse dialect, ETL-SQL stages data in engine-managed `#temp` tables where validation, masking, enrichment, fuzzy matching, lineage tags, and quality gates run before rows reach their destination. Compatible database work can still be pushed down, but cross-source work stays portable and explicit.

Lineage is part of the workflow rather than an after-the-fact reconstruction. Tags and transformation metadata travel with rows and columns through joins, aggregations, reports, OpenLineage exports, and Mermaid diagrams.

## First Hour Walkthrough

The fastest way to understand ETL-SQL is to run one small pipeline that has no external dependencies. `MOCKDB` is an in-memory demo connector, so this script needs no database server, files, credentials, or network access.

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

SELECT * FROM eng.profile;
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
| Diagnose | `eng.profile` | Profiling exposes statement timing while you develop. |

After this works, change only one thing at a time: swap `MOCKDB` for a real connector, add a `WHERE` filter, write the summary to a file, or turn the final query into a report visual.

---

## The Pipeline Mental Model

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

| Context | What runs here | Key examples |
| :--- | :--- | :--- |
| **Engine** | ETL-SQL syntax, `@variables`, `#temp` tables, functions, `FOREACH`, `IF`, `MERGE` | `SELECT ... INTO #stage FROM conn.Table` |
| **Remote** | Native SQL of the target engine — passed verbatim | `EXECUTE mssql_conn BEGIN ... END` |

> [!IMPORTANT]
> **The Golden Rule**: Data always flows *through* the engine. To move data from Postgres to a CSV file, you stage it in a `#temp` table first — that is where validation, masking, regex, and lineage tagging happen. The remote engines only ever receive simple native SQL they can execute directly.

Two consequences worth internalizing early:

- Engine-only functions (`REGEXP_LIKE`, `HASHBYTES`, `FORMAT()`) work in **engine context** — on `#temp` tables and engine-side `SELECT`, not inside a remote `EXECUTE` block.
- Dialect keywords are validated against the target connection: `SELECT TOP 10` against a Postgres connection is a lint error (use `LIMIT 10`). See [Dialect awareness](#dialect-awareness) below.

---

## Your First Connection

Every data source is a named **connection**. Create one before querying it.

```sql
-- Create a connection to SQL Server using Windows auth
CREATE CONNECTION prod AS MSSQL(SERVER='sql01', DATABASE='SalesDB', TRUSTED_CONNECTION=TRUE);

-- Query it like any table
SELECT TOP 100 * FROM prod.dbo.Customers WHERE Status = 'Active';
```

Connections accept either a **structured** option list (recommended — readable and diffable) or a **traditional connection string**. Choose the connector by the *system* you are talking to — SQL databases use SQL connectors (`MSSQL`, `POSTGRES`, `ORACLE`, `ODBC`, `SNOWFLAKE`, `BIGQUERY`), local files use file connectors (`FLATFILE`, `CSV`, `EXCEL`, `JSON`, `XML`, `PARQUET`, `AVRO`), HTTP services use `API`/`REST`, and transfer endpoints use `SFTP`/`FTP`/`AZURE_BLOB`/`SMTP`.

> [!TIP]
> Use `HELP CONNECTION <type>` in the TUI (e.g. `HELP CONNECTION MSSQL`) to see every option and default for a connector.

- **All connector options, auth patterns, and mutually exclusive settings** → [Data Connectors](../../reference/connectors/README.md).
- **Encrypting credentials** — never commit plaintext passwords. `USE PASSWORD` plus `ENC:` strings let the engine auto-decrypt at connection time; the IDE/CLI can encrypt a script's plaintext strings on save. → [Secrets and Keys](../../administration/platform/secrets.md).
- **Mounting a directory as a connection** (`CREATE CONNECTION raw AS DIRECTORY('C:\Incoming\')`) so file metadata joins against other sources → [Data Connectors](../../reference/connectors/README.md).

### Running a script

The same script file runs in every host — the host only changes how you interact with results, logs, prompts, and reports.

```powershell
# Run once
ETL-SQL run nightly_load.etlsql

# Pass INPUT variables
ETL-SQL run monthly_report.etlsql --var @env=PROD --var @month=2026-03

# Interactive editor with result panes
ETL-SQL ui edit MyScript.etlsql
```

Use VS Code for inline diagnostics, quick fixes, and report preview; use the CLI or scheduler for repeatable automation. Every flag (`--perf`, `--log`, `--session`, …) is in the [CLI Reference](../../reference/cli/README.md).

---

## Variables & State Management

Variables are the engine's memory. Prefix all variable names with `@`.

```sql
-- Declare with an explicit type (or omit for inferred ANY type)
DECLARE @BatchDate  DATE    = '2026-04-01';
DECLARE @Threshold  DECIMAL = 5000.00;
DECLARE @ids        LIST    = (1, 2, 3, 4);

-- Set a new value, then use it in a query
SET @Threshold = @Threshold * 1.1;
SELECT * FROM prod.Sales WHERE SaleDate >= @BatchDate AND Amount > @Threshold;
```

A few concepts you will reach for quickly — each has a focused reference page:

- **`INPUT` / `OUTPUT` parameters.** `INPUT` variables are overridable from the CLI (`--var @Env=PROD`) or a parent script; `OUTPUT` variables return a value to the caller via `RUN SCRIPT ... WITH (...)`. → [DECLARE](../../reference/variables-parameters/declare.md).
- **`RELDATE` variables.** A date type expressed *relative to run time* (`'M-1'` = first day of last month, `'D-7'` = seven days ago, `'ME-1'` = last day of last month). Combine with `INPUT` so callers override at run time without editing the script; `SET WEEK_START_DAY` controls week boundaries. → [RELDATE](../../reference/functions/datetime/reldate.md).
- **Environment sets.** Define named variable groups once (`CREATE SETS !DEV BEGIN @server = '…' END`) and activate with `USE SETS !DEV` to switch DEV/QA/PROD without editing connection strings. Typically kept in a shared `_environments.etlsql` and loaded via `RUN SCRIPT`. → [CREATE](../../reference/statements/ddl/create.md) · [USE](../../reference/variables-parameters/use.md).
- **Persistent sessions & checkpoint resume.** Running with `--session` spills `#temp` tables and variables at each top-level label; `--resume` reloads the last checkpoint and skips ahead, so a late-stage failure need not restart the whole extract. (`--session` alone always starts fresh — it does not restore.) → [Sessions and variables](../../administration/orchestration/sessions-and-variables.md) · [`session` CLI](../../reference/cli/session.md).

---

## The #Temp Table Workspace

Temporary tables (prefixed with `#`) are **in-memory engine-side staging areas** — the core of every multi-step pipeline.

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

Why stage in `#temp` tables?

1. **Decoupling Connection Hold Time** — Close your connection to a busy source database immediately after the quick extraction step, then perform your downstream load/merge logic independently.
2. **Checkpoint & Resume** — Support recovery by staging data at label boundaries, enabling you to resume a failed load without re-extracting from the source.
3. **Multi-Pass Updates** — Perform multiple sequential operations (such as indexing, secondary updates, or complex multi-statement checks) on the local workspace before inserting.

> [!NOTE]
> **Staged vs. Direct Streaming:** You do **not** need a `#temp` table to apply engine-only functions (like `REGEX`, `HASHBYTES`, or date functions) or enforce data-quality rules (`@expect`). You can stream data directly from source to target in a single statement (e.g. `INSERT INTO dest SELECT HASHBYTES('SHA2_256', email) FROM src`), which runs in a single high-performance pass without local storage overhead. Choose `#temp` when you need connection isolation, recovery checkpoints, or multi-pass staging.

You can define a `#temp` table explicitly (`CREATE TABLE #Summary (...)`), build it with `SELECT ... INTO`, and reshape it with `ALTER TABLE`; it is auto-dropped at session end. Full DDL is in the [Statement Reference](../../reference/statements/README.md).

---

## Core SELECT Patterns

ETL-SQL's query surface is broad — full clause order, `TOP`/`LIMIT`/pagination, `ROLLUP`, CTEs, `PIVOT`, `QUALIFY`, `ASOF JOIN`, and more. Rather than restate it here, the atomic detail lives in the [Query Syntax reference](../../reference/statements/query-syntax/README.md) (see [select modifiers](../../reference/statements/query-syntax/select-modifiers.md) for row limiting/pagination and [WITH](../../reference/statements/query-syntax/with.md) for CTEs). Two patterns matter most for *pipeline* thinking:

### Cross-source joins

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

This is the safest default. Push work down with `EXECUTE ... BEGIN ... END` only when it is clearly native to one remote system and needs no engine-side tables. More complete patterns — incremental load, quality gates, dead-letter queues — are in the [ETL Recipes](../../cookbooks/etl-recipes.md) cookbook.

### Dialect awareness

ETL-SQL validates dialect mismatches before execution: a query against a Postgres connection should use Postgres syntax; a query against SQL Server can use T-SQL. The portable move is to **stage to `#temp`**, where engine functions behave the same regardless of source.

| Pattern | SQL Server | Postgres | Engine-safe alternative |
| :--- | :--- | :--- | :--- |
| Limit rows | `TOP 10` | `LIMIT 10` | Stage to `#temp`, then `LIMIT 10` |
| Current timestamp | `GETDATE()` | `NOW()` | Engine-side `GETDATE()` after staging |
| Null fallback | `ISNULL(x, y)` | `COALESCE(x, y)` | `COALESCE(x, y)` |

Rule of thumb: if the query touches one remote SQL system and benefits from its indexes, push it down; if it touches multiple systems, files, variables, or engine-only functions, stage to `#temp`.

---

## Continue Learning

You now have the fundamentals: the engine mental model, connections, variables, temp-table workspaces, and core `SELECT`. From here, each area has a focused home:

**Language and scripting**
- [Control Flow](../../reference/control-flow/README.md) — `IF`/`WHILE`/`FOR`/`FOREACH`, `TRY...CATCH`, `WAITFOR`.
- [Statements](../../reference/statements/README.md) — error handling, transactions, execution blocks, procedures, expressions.
- [Functions](../../reference/functions/README.md) · [Data Types](../../reference/data-types.md).

**Data movement and pipelines**
- [ETL Recipes](../../cookbooks/etl-recipes.md) — staging, incremental load, quality gates, dead-letter queues, parallel loads.
- [File Operations](../../reference/file-operations/README.md) — copy, transfer, compress, encrypt, email.
- [Pipelines and DAGs](../feature-guides/pipelines-and-dags.md) · [Sample Guide](../patterns/sample-guide.md) — 55+ sample scripts.

**Operate and schedule**
- [Orchestration](../../administration/orchestration/README.md) — `CREATE JOB`, DAGs, sessions, CI/CD.
- [CLI Reference](../../reference/cli/README.md) — `lint`, `explain`, profiling, `doctor`, and every command.

**Security and testing**
- [Secrets and Keys](../../administration/platform/secrets.md) · [Platform Administration](../../administration/platform/README.md).
- [Testing](../feature-guides/testing.md) — `GENERATE`, `SEED`, and the MOCKDB connector · [test lanes](../../../Testing.md).

**Reports and the portal**
- [Report SQL](../feature-guides/report-sql.md) — author `.rptsql` dashboards · [Report Recipes](../../cookbooks/report-recipes.md).
- [Portal User Guide](../tooling/portal-user.md) · [Portal Administration](../../administration/portal/README.md).

**Find anything**
- [Task Index](../../task-index.md) — goal-oriented "how do I…" locator · [Syntax Index](../../syntax-index.md) — keyword map · [Docs map](../../../README.md) · [Security policy](../../../SECURITY.md).
