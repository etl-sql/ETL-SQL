# ETL-SQL: AI Agent Instruction Manual

Welcome, Agent. You are assisting in the development and operation of **ETL-SQL**, a unique hybrid engine that executes SQL-like syntax against diverse data sources (SQL, NoSQL, FlatFiles) with an emphasis on portability and "Zero-Trust" security.

---

## 1. The Mental Model — Read This First

ETL-SQL is **not** a traditional SQL engine. It is an **orchestration conductor** that coordinates multiple heterogeneous data sources. Before writing any script, internalize this:

```
┌───────────────────────────────────────────────────────┐
│              ETL-SQL Engine ("The Brain")             │
│  - Holds @variables and #temp tables in memory        │
│  - Evaluates ETL-SQL syntax                           │
│  - Coordinates reads/writes across connections        │
└────────────┬──────────────┬──────────────┬────────────┘
             │              │              │
       MSSQL conn     FLATFILE conn   SFTP conn
       (remote SQL)   (local file)  (remote files)
```

**The Golden Rule**: Data always flows *through* the engine. When you move data from Postgres to a CSV, you typically stage it in an engine `#temp` table first — this is where `WHERE` filters, `REGEX`, `HASHBYTES`, and lineage tagging happen. The remote engines only receive simple SQL they understand natively.

### 1.1 Engine Context vs. Remote Context

| Context | What runs here | Examples |
| :--- | :--- | :--- |
| **Engine** | ETL-SQL syntax, variables, functions, `#temp` tables, `FOREACH`, `IF`, `MERGE` | `SELECT ... INTO #stage FROM conn.Table` |
| **Remote** | Native SQL of the target engine, passed verbatim | `EXECUTE mssql_conn BEGIN ... END` |

**Implication**: If you write `SELECT TOP 10` and the connection is Postgres, the engine will catch this at lint time because `TOP` is in Postgres's excluded keyword list. Use `LIMIT 10` instead, or push the query via `EXECUTE ... BEGIN ... END` only.

---

## 2. Dialect & Syntax Reference

ETL-SQL follows a T-SQL-like dialect with extensions and restrictions. For full syntax, see the Reference library:

| Document | Contents |
| :--- | :--- |
| **[Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md)** | Variables, `DECLARE`/`SET`, `IF`/`WHILE`/`FOREACH`/`FOR`, `TRY...CATCH`, `SELECT` clauses, CTEs, JOINs, DML, DDL, `EXECUTE`, `PARALLEL`, `RUN SCRIPT`, job scheduling, transactions |
| **[Data_Connectors.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Data_Connectors.md)** | Every connector token, all `WITH()` options, authentication patterns, aliases, quick-reference table |
| **[Standard_Library.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Standard_Library.md)** | All data types, `CAST`/`TRY_CAST`, string/date/math/regex/window/JSON/XML functions with full signatures and examples |
| **[Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md)** | File/directory operations, `SEND FILE`/`RECEIVE FILE`, `SEND EMAIL`, lineage/tagging, SSH key generation, Docker integration, profiling |
| **[Report_SQL_Guide.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Report_SQL_Guide.md)** | `.rptsql` file structure, all visual types, MAPPINGS roles, STYLE/THEME, CONTAINER/NAVIGATION syntax, filter visuals, multi-report hosting |

Key syntax facts:
- **Variables**: `@VariableName` — always prefix with `@`, case-insensitive
- **Temp tables**: `#TableName` — prefix with `#` for in-memory engine-side tables
- **Encrypted strings**: `'ENC:base64...'` — set session password first with `USE PASSWORD = '...'`
- **Connectors**: Supported types are `MSSQL`, `POSTGRES`, `ORACLE`, `ODBC`, `FLATFILE`/`CSV`, `EXCEL`, `JSON`, `XML`, `PARQUET`, `AVRO`, `API`/`REST`, `SFTP`, `FTP`, `AZURE_BLOB`, `SMTP`, `DIRECTORY` (and `MOCKDB` for test/mock workloads)
- **Suspension**: `WAITFOR DELAY 'hh:mm:ss'` — fixed pause; `WAITFOR TIME 'hh:mm:ss'` — pause until clock time

> [!NOTE]
> `WAITFOR` has three supported forms:
> - `WAITFOR DELAY 'hh:mm:ss'` — fixed pause
> - `WAITFOR TIME 'hh:mm:ss'` — pause until wall-clock time
> - `WAITFOR (condition)` — polls the expression at 200ms intervals until it returns a truthy value
>
> The `WHILE` loop with `WAITFOR DELAY` inside remains the preferred pattern when you need a custom poll interval or inter-check logic.

### 2.1 Report-SQL (`.rptsql`) Key Facts

`.rptsql` files are standard ETL-SQL scripts with additional statement types. Use `Report_SQL_Guide.md` as the full reference. Critical patterns to get right:

- **File structure**: normal ETL-SQL data prep statements first, then `CREATE VISUAL`, `CREATE PAGE`, `CREATE DATASET`, `CREATE CONTAINER`, `CREATE NAVIGATION` at the end
- **Report metadata**: `SET REPORT TITLE = '...'` and `SET REPORT DESCRIPTION = '...'` (optional, appear before visuals)
- **Visual types**: `BAR`, `HBAR`, `LINE`, `SCATTER`, `PIE`, `DONUT`, `COMBO`, `BOXPLOT`, `TREEMAP`, `HEATMAP`, `TABLE`, `CARD`, `TEXT`, `SLICER`, `DATEPICKER`, `SLIDER`, `MULTISELECT`, `SEARCH`
- **SLICER pattern** — SOURCE provides the option list; ACTIONS binds to a parameter:
  ```sql
  CREATE VISUAL RegionFilter AS SLICER (
    SOURCE  = (SELECT DISTINCT region FROM #summary ORDER BY region),
    MAPPINGS (VALUE = region),
    ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, region))
  );
  ```
- **STRUCTURE** is a CSS grid-template-areas string, not a `grid:NxN` shorthand:
  ```sql
  STRUCTURE = 'A A / B C'   -- two rows; A spans both columns; B and C share the second row
  ```
- **MAP slots are quoted strings**: `MAP ('A' = VisualName, 'B' = OtherVisual)`
- **ENCRYPT modes**: `ENCRYPT = MACHINE` (no creds), `ENCRYPT = PASSWORD, PASSWORD = '...'`, or `ENCRYPT = KEYFILE, KEYFILE = '...'`
- **Filter types** (`DATEPICKER`, `SLIDER`, `SEARCH`) do not require a `SOURCE` clause
- **MULTISELECT** requires a `SOURCE` clause for its option list
- **STYLE** cascades: page-level `STYLE (THEME = dark)` applies to all charts; visual-level `STYLE` overrides it

---

## 3. Zero-Trust Security Guardrails

As an AI, you **MUST NOT** generate ETL-SQL scripts that:

- Attempt to read from or write to `.sql`, `.etlsql`, or `.rptsql` script files — the engine blocks this via the **Script Immutability Guardrail**
- Access system directories (`C:\Windows`, `C:\bin`, `/etc`, `/root`, `.git`, `.ssh` **when not accessed via SFTP/KEYFILE**)
- Perform operations on the root of any drive (e.g. `C:\` directly)
- Exceed 100 file operations or 5 levels of recursion without an explicit `### ALLOW_...` override comment in the script
- Log, print, or concatenate connection strings, passwords, API keys, or `ENC:` values into any output string
- Use `DELETE`, `MERGE`, `TRUNCATE`, or destructive file operations without either a `BEGIN TRANSACTION`/`ROLLBACK` guard or a `SET WHAT_IF ON` validation block first

**Recommended safe pattern for any destructive operation:**
```sql
-- Phase 1: Always validate with WHAT_IF first
SET WHAT_IF ON;
DELETE FROM prod_db.logs WHERE log_date < '2024-01-01';
SET WHAT_IF OFF;

-- Phase 2: Run for real only after validating the output
DELETE FROM prod_db.logs WHERE log_date < '2024-01-01';
```

For full security governance rules, see **[Connectors_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Connectors_Standards.md)** (Part I — The Inviolable Rules).

---

## 4. Dialect Awareness

ETL-SQL is **dialect-aware**. A keyword that is valid in the ETL-SQL engine layer may be rejected by the linter if the **target connection** is a specific database type.

| Keyword / Feature | MSSQL | Postgres | Oracle | File connectors |
| :--- | :---: | :---: | :---: | :---: |
| `TOP n` | ✓ | ✗ (use `LIMIT`) | ✗ (use `ROWNUM`) | — |
| `GETDATE()` | ✓ | ✗ (use `NOW()`) | ✗ (use `SYSDATE`) | — |
| `ISNULL(v, d)` | ✓ | ✗ (use `COALESCE`) | ✗ (use `NVL`) | — |
| `DATALENGTH()` | ✓ | ✗ | ✗ | — |
| `IDENTITY` column | ✓ | ✗ | ✗ | — |
| SQL pushdown | ✓ | ✓ | ✓ | ✗ |

**Rules for generating cross-dialect scripts:**
- When the `FROM` clause references a **file connector**, use ETL-SQL engine functions (`ISNULL`, `COALESCE`, `GETDATE()`) — not database-specific ones.
- When writing an `EXECUTE ... BEGIN...END` block, use the **target connection's native dialect verbatim** — the engine passes it unchanged.
- When in doubt, write a query against a `#temp` table (engine context) rather than directly against a remote connection.

---

## 5. Scripting Patterns — How to Think About a Request

When a user asks you to write a script, follow this decision tree:

### 5.1 Is this a simple SELECT for inspection?
```sql
CREATE CONNECTION src ON MSSQL() WITH(SERVER='...', DATABASE='...', TRUSTED_CONNECTION=TRUE);
SELECT TOP 100 * FROM src.dbo.MyTable WHERE Status = 'Active';
```

### 5.2 Is this an Extract → Transform → Load?
Use the **staged ingestion pattern** (never write directly from source to target without staging):
```sql
CREATE CONNECTION src ON POSTGRES() WITH(HOST='...', DATABASE='...', USER='...', PASSWORD='...');
CREATE CONNECTION dest ON MSSQL() WITH(SERVER='...', DATABASE='...', TRUSTED_CONNECTION=TRUE);

BEGIN TRY
    -- 1. Extract into engine memory
    SELECT id, UPPER(name) AS name, email, GETDATE() AS loaded_at
    INTO #staging
    FROM src.customers
    WHERE updated_at > DATEADD(DAY, -1, GETDATE());

    -- 2. Validate / clean
    UPDATE #staging SET email = NULL WHERE email NOT LIKE '%@%';

    -- 3. Load
    MERGE INTO dest.dbo.Customers AS T
    USING #staging AS S ON T.id = S.id
    WHEN MATCHED AND S.name <> T.name THEN
        UPDATE SET T.name = S.name, T.loaded_at = S.loaded_at
    WHEN NOT MATCHED THEN
        INSERT (id, name, email, loaded_at) VALUES (S.id, S.name, S.email, S.loaded_at);

    PRINT 'Load complete.';
END TRY
BEGIN CATCH
    PRINT 'Error: ' + ERROR_MESSAGE();
    THROW;
END CATCH
```

### 5.3 Is this a file operation?
- Use `FLATFILE`, not raw `File.*` calls
- Always use `IF FILE_EXISTS(...)` before destructive operations
- Encrypt before transmitting via SFTP: `ENCRYPT FILE ... → SEND FILE ... AT sftp_conn`

### 5.4 Does this involve scheduling?
Use `CREATE JOB` for recurring tasks; use `RUN SCRIPT` to break large scripts into composable modules.

For 18 production-grade complete recipes, see **[Cookbook.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Cookbook.md)**.

---

## 6. Documentation Library Map

Use this map to find the right document for any task.

### Writing Scripts
| Need | Document |
| :--- | :--- |
| Full language syntax | **[Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md)** |
| Connector options and authentication | **[Data_Connectors.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Data_Connectors.md)** |
| Functions (string, date, math, regex, window) | **[Standard_Library.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Standard_Library.md)** |
| File ops, email, lineage, Docker, jobs | **[Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md)** |
| Complete production recipes | **[Cookbook.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Cookbook.md)** |
| Pipeline mental model for new users | **[User_Manual.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/User_Manual.md)** |
| Sample script inventory (55+ scripts in `/samples/`) | **[Sample_Guide.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Sample_Guide.md)** |
| Reporting (`.rptsql`, `CREATE VISUAL`, dashboards) | **[Report_SQL_Guide.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Report_SQL_Guide.md)** |
| Master language reference (comprehensive) | **[ETL_SQL_Language_Reference.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/ETL_SQL_Language_Reference.md)** |

### Contributing Engine Code
| Need | Document |
| :--- | :--- |
| How connectors work internally | **[Architecture/Connectors.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Connectors.md)** |
| Engine internals (parser, evaluator, AST) | **[Architecture/Engine.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Engine.md)** |
| Presentation layer (IDE, ANSI rendering) | **[Architecture/Presentation.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Presentation.md)** |
| Rules for writing a new connector | **[Standards/Connectors_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Connectors_Standards.md)** |
| Rules for touch the presentation layer | **[Standards/Presentation_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Presentation_Standards.md)** |
| Engine upgrade roadmap | **[Strategy/Engine_Upgrade_Strategy.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Strategy/Engine_Upgrade_Strategy.md)** |

---

## 7. Documentation Stewardship Rules

When you create or modify any documentation, follow these standards:

- **Standard Library**: Every function entry must include its full **signature**, **return type**, and a **copy-pasteable example**. Never add a function without all three.
- **Data Connectors**: Always include **both authentication patterns** (e.g., Password vs. Keyfile for SFTP) and call out any **mutually exclusive options** explicitly.
- **Cookbook**: Prefer **self-contained lifecycle scripts** (Extract → Stage → Validate → Merge → Cleanup → Notify) over isolated snippets. Every recipe must be runnable as-is.
- **Architecture docs**: Any interface contract shown must match the actual C# source. Cross-reference before writing.
- **Grammar docs**: Every syntax form must have a minimal working example. Do not document syntax that the parser does not actually accept.

---

## 8. Engine Coding Principles (When Modifying C# Source)

These rules apply when you are editing the ETL-SQL engine source code — **not** when writing `.etlsql` scripts:

- **Path Resolution**: Never use relative paths in engine code. Always call `IExecutionContext.ResolvePath()` before any file I/O — this is the Zero-Trust security boundary.
- **Logging**: `Logger.Instance` is **obsolete**. Always use the `ILogger` provided via dependency injection or pulled from `IExecutionContext`. Do not use `Console.WriteLine`.
- **AST Nodes**: Prefer `record` types for all AST node classes to enforce immutability. Do not use mutable `class` declarations for nodes.
- **Async**: All I/O calls must use the `Async` overloads with a `CancellationToken`. No `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` in connector or handler code.
- **Exceptions**: Connector-level provider exceptions (`SqlException`, `NpgsqlException`, etc.) must be caught and re-thrown as `ExecutionException` with a sanitized message. Never let raw provider exceptions escape the connector boundary.

For full engine coding standards, see **[Standards/Connectors_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Connectors_Standards.md)** and **[Architecture/Connectors.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Connectors.md)**.

---

## 9. Common Mistakes to Avoid

| Mistake | Correct pattern |
| :--- | :--- |
| Writing `SELECT TOP 10` against a Postgres connection | Use `LIMIT 10` or push via `EXECUTE pg_conn BEGIN SELECT ... LIMIT 10; END` |
| Using relative paths: `FROM 'data\input.csv'` | Always use absolute paths: `FROM 'C:\Data\input.csv'` |
| Printing a connection string in a PRINT or exception message | Never include connection strings, passwords, or tokens in any output |
| Running a `DELETE` or `MERGE` without a safety check | Wrap in `SET WHAT_IF ON` first, then re-run without it |
| Forgetting `IF FILE_EXISTS()` before `COPY FILE` or `ENCRYPT FILE` | Check existence first to avoid silent no-ops or errors |
| Using `Logger.Instance` in C# engine code | Use injected `ILogger` from `IExecutionContext` |
| Declaring `class` for AST nodes in C# | Use `record` types for all AST nodes |
| Writing `WAITFOR (SELECT ...)` | This form does not exist; use `WHILE` + `WAITFOR DELAY` for polling |
| Using `MySQL` as a connector token | MySQL is not a supported connector; use `ODBC` with a MySQL driver instead |
| Writing `FROM FLATFILE` or `FROM FILE` in a `CREATE CONNECTION` | `FLATFILE` is the **connector type**; `FILE` is the **table alias** used in queries — `CREATE CONNECTION src ON FLATFILE('my.csv'); SELECT * FROM src` |

---

*For a complete syntax walkthrough, start at [User_Manual.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/User_Manual.md) and then refer to the [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) reference.*
