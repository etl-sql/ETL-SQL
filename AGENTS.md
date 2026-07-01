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

### 1.0 Product Positioning — Keep This Front and Center

When writing user-facing docs, examples, READMEs, release notes, or onboarding material, make the differentiators explicit:

- **Script-first** — pipelines, reports, jobs, validation, and governance metadata are plain-text `.etlsql` / `.rptsql` artifacts.
- **Source-control friendly** — changes are diffable, reviewable, testable, packageable, and runnable in CLI, VS Code, notebooks, Orchestrator, Report Portal, and CI/CD.
- **The T is back in ETL** — transformation, validation, masking, fuzzy matching, enrichment, lineage tagging, and quality gates can happen before loading or publishing.
- **ELT tradeoffs without ELT lock-in** — compatible work can push down to databases, but cross-source work stays portable instead of being trapped in one warehouse dialect or split across Python, schedulers, and BI designer files.
- **Lineage and tags are native** — lineage metadata follows data through transformations into reports and can be queried, diagrammed, or exported.
- **Zero-trust operations** — path boundaries, script immutability, encrypted secrets, `SECRET:name` references, `WHAT_IF`, audit, and governance policies are part of the execution model.

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
| **[Lineage.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Lineage.md)** | `TAG`, `LINEAGE`, `SET LINEAGE`, lineage capture patterns, metadata tagging on rows and pipelines |
| **[RelativeDate_Parameters.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/RelativeDate_Parameters.md)** | Relative date parameter syntax, `D` (today), `N` (now), offset expressions, use in `WHERE` clauses and report filters |
| **[Report_SQL_Guide.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Report_SQL_Guide.md)** | `.rptsql` file structure, all visual types, MAPPINGS roles, STYLE/THEME, CONTAINER/NAVIGATION syntax, filter visuals, multi-report hosting |
| **[Administrators_Guide.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Administrators_Guide.md)** | Production deployment, HA configuration, Governance Core, OIDC setup, backup/restore, health checks |

Key syntax facts:
- **Variables**: `@VariableName` — always prefix with `@`, case-insensitive
- **Temp tables**: `#TableName` — prefix with `#` for in-memory engine-side tables
- **Encrypted strings**: `'ENC:base64...'` — set session password first with `USE PASSWORD = '...'`
- **Connectors**: Supported types are `MSSQL`, `POSTGRES`, `ORACLE`, `ODBC`, `SNOWFLAKE`, `BIGQUERY`, `FLATFILE`/`CSV`, `EXCEL`, `JSON`, `XML`, `PARQUET`, `AVRO`, `API`/`REST`, `SFTP`, `FTP`, `AZURE_BLOB`, `SMTP`, `DIRECTORY`, `REPORTPORTAL`, `ORCHESTRATOR`, `MYSQL`, `SQLITE`, `MONGODB`, `KAFKA`, `NEO4J`, `S3`, `SHAREPOINT`, `ACTIVE_DIRECTORY` (and `MOCKDB` for test/mock workloads)
- **Suspension**: `WAITFOR DELAY 'hh:mm:ss'` — fixed pause; `WAITFOR TIME 'hh:mm:ss'` — pause until clock time

> [!NOTE]
> `WAITFOR` has four supported forms:
> - `WAITFOR DELAY 'hh:mm:ss'` — fixed pause
> - `WAITFOR TIME 'hh:mm:ss'` — pause until wall-clock time
> - `WAITFOR (condition)` — polls the expression at 200ms intervals until it returns a truthy value; condition may be a scalar expression or a subquery (e.g. `WAITFOR (SELECT COUNT(*) FROM #t) > 0`)
> - `WAIT UNTIL condition` — preferred alias for `WAITFOR (condition)`
>
> The `WHILE` loop with `WAITFOR DELAY` inside remains the preferred pattern when you need a custom poll interval or inter-check logic.

### 2.1 Report-SQL (`.rptsql`) Key Facts

`.rptsql` files are standard ETL-SQL scripts with additional statement types. Use `Report_SQL_Guide.md` as the full reference. Critical patterns to get right:

- **File structure**: normal ETL-SQL data prep statements first, then `CREATE VISUAL`, `CREATE PAGE`, `CREATE DATASET`, `CREATE CONTAINER`, `CREATE NAVIGATION` at the end
- **Report metadata**: `SET REPORT TITLE = '...'` and `SET REPORT DESCRIPTION = '...'` (optional, appear before visuals)
- **Visual types**: `BAR`, `HBAR`, `LINE`, `SCATTER`, `PIE`, `DONUT`, `COMBO`, `BOXPLOT`, `TREEMAP`, `HEATMAP`, `FUNNEL`, `GAUGE`, `WATERFALL`, `BUBBLE`, `RADAR`, `CANDLESTICK`, `MAP`, `SANKEY`, `SUNBURST`, `NETWORK`, `TRELLIS`, `MATRIX`, `GANTT`, `TABLE`, `CARD`, `TEXT`, `IMAGE`, `SLICER`, `DATEPICKER`, `RELDATEPICKER`, `SLIDER`, `MULTISELECT`, `SEARCH`, `TEXTBOX`, `NUMBERBOX`, `CHECKBOX`
- **Interactive bindings** — If a control has a `SOURCE` (like `SLICER`), bind the parameter to the mapped column name. If it lacks a `SOURCE` (like `DATEPICKER` or `SLIDER`), you **must** bind it to the literal keyword `value`:
  ```sql
  -- Slicer (has SOURCE)
  ACTIONS (ON_CHANGE = SET_PARAMETER(@region, region))
  
  -- Datepicker (no SOURCE)
  ACTIONS (ON_CHANGE = SET_PARAMETER(@startDate, value))
  ```
- **STRUCTURE** is a CSS grid-template-areas string:
  ```sql
  STRUCTURE = 'A A / B C'   -- two rows; A spans both columns; B and C share the second row
  ```
- **MAP slots are quoted strings**: `MAP ('A' = VisualName, 'B' = OtherVisual)`
- **Buttons use the page-style form**: `CREATE BUTTON ButtonName AS (...)`; do not use typed button aliases.
- **CREATE DATASET ENCRYPT modes**: `ENCRYPT = MACHINE` (no creds), `ENCRYPT = PASSWORD, PASSWORD = '...'`, or `ENCRYPT = KEYFILE, KEYFILE = '...'`. (Note: This is different from the `WITH(ENCRYPT=ON, PASSWORD='...')` syntax used for file connectors).
- **Filter types** (`DATEPICKER`, `SLIDER`, `SEARCH`) do not require a `SOURCE` clause
- **MULTISELECT** requires a `SOURCE` clause for its option list
- **STYLE** cascades: page-level `STYLE (THEME = dark)` applies to all charts; visual-level `STYLE` overrides it
- **Portal administration is script-first** inside `EXECUTE portal BEGIN...END`: use commands such as `PUBLISH REPORT`, `CREATE SUBSCRIPTION`, `REFRESH REPORT`, `FAVORITE REPORT`, `SHOW REPORT HISTORY`, `SHOW REPORT DEPENDENCIES`, `SHOW CATALOG SEARCH`, `SHOW EFFECTIVE PERMISSIONS`, `SHOW PORTAL USAGE METRICS`, `VALIDATE REPORT SCRIPT`, `CREATE SHARE LINK`, `CREATE EMBED TOKEN`, `CREATE SAVED VIEW`, and `CREATE ALERT`.

---

## 2.2 Enterprise Operations Key Facts

The platform now includes shipped enterprise operations features. When generating scripts, editing server code, or writing docs, do not assume ETL-SQL is single-node or SQLite-only.

- **Practical High Availability is shipped**: Report Portal and Orchestrator support shared PostgreSQL state, shared Portal artifact roots, node heartbeats, lease fencing, leader election, node-capacity heartbeats, health probes, and load-balancer session affinity.
- **Single-node defaults remain SQLite/local storage**. Multi-node HA requires `Portal:Database:Provider=Postgres`, `Orchestrator:Database:Provider=Postgres`, shared Portal storage roots (`Smb`/UNC), a shared Data Protection key ring, and identical JWT/orchestrator/dataset keys across Portal nodes.
- **Interactive report sessions are node-local**. HA deployments must use sticky routing on `ETLSQL_PORTAL_AFFINITY` or the configured `Portal:LoadBalancer:SessionAffinityCookieName`.
- **Load balancers should probe `GET /healthz`** for Portal node readiness. Use `GET /health` for richer monitoring. Orchestrator API routes require `X-Orchestrator-Key`; unauthenticated network-reachable Orchestrator startup is rejected.
- **Lease fencing matters**. Scheduled jobs, refreshes, shared artifact writes, and migration/singleton work use database-backed leases/fencing. Do not replace these with node-local locks or in-memory ownership.
- **Governance Core is shipped** across hosts: typed organization policy, policy enforcement at lint and execution boundaries, `SECRET:name` references via configured secret providers, redaction of raw secret values and `SECRET:` references, and durable remote audit outbox with optional fail-closed mutation behavior.
- **Enterprise Identity is active work**. OIDC support includes federated login/logout/token refresh, issuer/audience/claim validation, OIDC-only account binding, and dynamic group-claim sync. The next phase adds service accounts and approval workflows. Check `TODO.md` and `ROADMAP.md` before assuming identity behavior is complete.

For production configuration details, use **[Administrators_Guide.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Administrators_Guide.md)** as the source of truth. For the long-term enterprise model, use **[Enterprise_Platform_Strategy.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Strategy/Enterprise_Platform_Strategy.md)**.

---

## 3. Zero-Trust Security Guardrails

As an AI, you **MUST NOT** generate ETL-SQL scripts that:

- Attempt to read from or write to `.sql`, `.etlsql`, or `.rptsql` script files — the engine blocks this via the **Script Immutability Guardrail**
- Access system directories (`C:\Windows`, `C:\bin`, `/etc`, `/root`, `.git`, `.ssh` **when not accessed via SFTP/KEYFILE**)
- Perform operations on the root of any drive (e.g. `C:\` directly)
- Exceed 100 file operations or 5 levels of recursion without a `SET ALLOW_FILE_OPERATIONS = <n>` or `SET ALLOW_RECURSIVE_LAYERS = <n>` statement in the script
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
CREATE CONNECTION src AS MSSQL(SERVER='...', DATABASE='...', TRUSTED_CONNECTION=TRUE);
SELECT TOP 100 * FROM src.dbo.MyTable WHERE Status = 'Active';
```

### 5.2 Is this an Extract → Transform → Load?
Use the **staged ingestion pattern** (never write directly from source to target without staging):
```sql
CREATE CONNECTION src AS POSTGRES(HOST='...', DATABASE='...', USER='...', PASSWORD='...');
CREATE CONNECTION dest AS MSSQL(SERVER='...', DATABASE='...', TRUSTED_CONNECTION=TRUE);

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

For 26 production-grade complete recipes, see **[Cookbook.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Cookbook.md)**.

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
| Lineage capture, `TAG`, pipeline metadata | **[Lineage.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Lineage.md)** |
| Relative date parameters (`@TODAY`, offsets, report filters) | **[RelativeDate_Parameters.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/RelativeDate_Parameters.md)** |
| Complete production recipes | **[Cookbook.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Cookbook.md)** |
| Pipeline mental model for new users | **[User_Manual.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/User_Manual.md)** |
| Sample script inventory (290+ files in `/samples/`) | **[Sample_Guide.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Sample_Guide.md)** |
| Reporting (`.rptsql`, `CREATE VISUAL`, dashboards) | **[Report_SQL_Guide.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Report_SQL_Guide.md)** |
| Rules for composing ETL-SQL scripts | **[Standards/Script_Composition_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Script_Composition_Standards.md)** |
| Production install, HA, Governance Core, OIDC | **[Administrators_Guide.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Administrators_Guide.md)** |
| Portal user/admin operations | **[ReportPortal_Administrators_Guide.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/ReportPortal_Administrators_Guide.md)** |
| Orchestrator job operations | **[Orchestrators_Guide.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Orchestrators_Guide.md)** |
| Enterprise roadmap and trust model | **[Enterprise_Platform_Strategy.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Strategy/Enterprise_Platform_Strategy.md)** |

### Contributing Engine Code
| Need | Document |
| :--- | :--- |
| How connectors work internally | **[Architecture/Connectors.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Connectors.md)** |
| Engine internals (parser, evaluator, AST) | **[Architecture/Engine.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Engine.md)** |
| Presentation layer (IDE, ANSI rendering) | **[Architecture/Presentation.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Presentation.md)** |
| Orchestrator internals, leases, scheduling, job execution | **[Architecture/Orchestrator.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Orchestrator.md)** |
| Report Portal auth, HA topology, API, health checks | **[Architecture/ReportPortal.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/ReportPortal.md)** |
| C# engine coding guidelines | **[Standards/Engine_Coding_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Engine_Coding_Standards.md)** |
| Rules for writing a new connector | **[Standards/Connectors_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Connectors_Standards.md)** |
| Rules for adding language syntax | **[Standards/Language_Syntax_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Language_Syntax_Standards.md)** |
| Protocol for breaking changes | **[Standards/Breaking_Change_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Breaking_Change_Standards.md)** |
| Rules for third-party dependencies | **[Standards/Third_Party_Dependency_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Third_Party_Dependency_Standards.md)** |
| Source boundaries and project ownership | **[Standards/Source_Boundary_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Source_Boundary_Standards.md)** |
| Rules for report runtime assets | **[Standards/Report_Runtime_Asset_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Report_Runtime_Asset_Standards.md)** |
| Rules for touching the presentation layer | **[Standards/Presentation_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Presentation_Standards.md)** |
| Rules for writing help docs & snippets | **[Standards/Help_and_Snippet_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Help_and_Snippet_Standards.md)** |
| Engine upgrade strategy | **[Strategy/Engine_Upgrade_Strategy.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Strategy/Engine_Upgrade_Strategy.md)** |

---

## 7. Documentation Stewardship Rules

When you create or modify any documentation, follow these standards:

- **Standard Library**: Every function entry must include its full **signature**, **return type**, and a **copy-pasteable example**. Never add a function without all three.
- **Data Connectors**: Always include **both authentication patterns** (e.g., Password vs. Keyfile for SFTP) and call out any **mutually exclusive options** explicitly.
- **Cookbook**: Prefer **self-contained lifecycle scripts** (Extract → Stage → Validate → Merge → Cleanup → Notify) over isolated snippets. Every recipe must be runnable as-is.
- **Architecture docs**: Any interface contract shown must match the actual C# source. Cross-reference before writing.
- **Grammar docs**: Every syntax form must have a minimal working example. Do not document syntax that the parser does not actually accept.

### 7.1 Help Documents Formatting Standards

Help documents located under `src/ETL-SQL.Core/Resources/Help/` are loaded dynamically by the engine and LSP server to serve hover tooltips and in-editor help. Because editor hover viewports are narrow and automatically collapse single-newline paragraphs, all help documents must adhere to these consistency rules:

- **Title Header**: The file must start with a level-1 header `# TOPIC` matching the exact keyword or visual type (e.g., `# PAGE` or `# TABLE`).
- **Description Paragraph**: A short, single-paragraph description immediately following the title header, explaining what the command or feature does.
- **Syntax Block**: The statement syntax MUST be placed inside a code-fenced block ` ```sql ... ``` ` to prevent line collapsing and preserve structure, indentation, and casing.
- **Lists and Options**: All lists of types, mappings, configuration options, or actions MUST be formatted as Markdown bullet points (`- **OptionName** — Description`).
  - Never use raw leading-space indentation (e.g., `  PAGE_SIZE — rows per page`) as it collapses into a single paragraph in editor hover cards.
  - Bold the option/parameter name (e.g., `- **PAGE_SIZE = n** — ...`).
- **Examples**: Provide one or two clean, copy-pasteable example blocks using ` ```sql ... ``` ` to illustrate common use cases.
- **References**: Always end the document with a `References` section pointing to the official manuals or specifications (e.g., `- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)`).

### 7.2 Snippet Formatting Standards

Snippet files located under `src/ETL-SQL.Core/Resources/Help/Snippets/` are used by the LSP server to generate autocomplete templates. They must adhere to this exact structure:

- **Frontmatter**: Every snippet file must start with a YAML frontmatter delimited by `---` containing exactly:
  - `trigger`: The autocomplete trigger keyword, which MUST start with a `$` (e.g., `trigger: $dataset`).
  - `label`: A short visual title of what the snippet creates (e.g., `label: CREATE DATASET &name`).
  - `description`: A brief description shown in the autocomplete suggestion list.
- **Placeholder Syntax**: Use French quotation marks `«` and `»` around placeholder/tabstop names (e.g., `«VisualName»`, `«1h»`). These allow the editor to cycle through fields using `Tab`.
- **Formatting**: Indent block statements using 2 spaces for SQL clauses, options, or mappings. Keep them clean, valid, and consistent with core syntax.

---

## 8. Engine Coding Principles (When Modifying C# Source)

These rules apply when you are editing the ETL-SQL engine source code — **not** when writing `.etlsql` scripts:

- **Path Resolution**: Never use relative paths in engine code. Always call `IExecutionContext.ResolvePath()` before any file I/O — this is the Zero-Trust security boundary.
- **Logging**: `Logger.Instance` is **obsolete**. Always use the `ILogger` provided via dependency injection or pulled from `IExecutionContext`. Do not use `Console.WriteLine`.
- **AST Nodes**: Prefer `record` types for all AST node classes to enforce immutability. Do not use mutable `class` declarations for nodes.
- **Async**: All I/O calls must use the `Async` overloads with a `CancellationToken`. No `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` in connector or handler code.
- **Exceptions**: Connector-level provider exceptions (`SqlException`, `NpgsqlException`, etc.) must be caught and re-thrown as `ExecutionException` with a sanitized message. Never let raw provider exceptions escape the connector boundary.

### 8.1 Connector-Specific Rules

These apply when authoring or modifying any `IConnector` / `IDataSource` implementation:

- **Option naming**: All `WITH()`-style option keys must be **UPPERCASE with underscores** (`TIMEOUT_SECONDS`, `MIN_POOL_SIZE`, `CREDENTIAL_FILE`). Never PascalCase, camelCase, or mixed.
- **PASSWORD for ETL-SQL-owned options; vendor-native names are allowed**: Use `PASSWORD` for ETL-SQL-created connector password options and do not add convenience aliases such as `PWD` for those options. Vendor-native pass-through syntax is exempt when intentionally exposed by a connector; for example, ODBC `PWD` is valid because it is part of the ODBC connection-string vocabulary. The `ConnectionStringBuilder` should map ETL-SQL `PASSWORD` → the driver-native keyword when building structured connection strings.
- **Timeout defaults**: OLTP connectors (MSSQL, Postgres, Oracle, MySQL, SQLite, ODBC) default to **30 seconds**. Data warehouse connectors (Snowflake, BigQuery) default to **1800 seconds** (30 min). Both must read `TIMEOUT_SECONDS` from options to allow override.
- **Constructor signature**: `IDataSource` implementations use `(IExecutionContext context, string connectionString, string? tableName, Dictionary<string, string>? options)` — this order exactly.
- **ResolvePath for file path options**: Any connector option accepting a file path (`CREDENTIAL_FILE`, `KEY_FILE`, `CERT_FILE`, `PRIVATE_KEY_FILE`, `PATH`) must call `context.ResolvePath()` before any I/O, even when the connector is not in the engine tier.
- **No aliases / no legacy fallbacks**: If two option names existed, pick one and remove the other. Product has no live users — consistency now costs nothing.
- **CREATE CONNECTION syntax**: Options go directly in parentheses after the type name. `WITH()` is not used on `CREATE CONNECTION`. `WITH()` is valid in CTEs and `ALTER CONNECTION` only.

```sql
-- Correct
CREATE CONNECTION sales AS MSSQL(SERVER='sql01', DATABASE='SalesDB', USER='etl_worker', PASSWORD='s3cr3t');

-- Wrong (stale syntax — WITH() is not valid on CREATE CONNECTION)
CREATE CONNECTION sales AS MSSQL WITH(SERVER='sql01', DATABASE='SalesDB');
```

For the full 10-inviolable-rules + 25-item checklist, see **[Standards/Connectors_Standards.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Standards/Connectors_Standards.md)** and **[Architecture/Connectors.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Architecture/Connectors.md)**.

---

## 9. Third-Party Dependency Policy

Use only free and open-source software for new third-party libraries, tools, and bundled assets unless the user explicitly approves an exception.

- Prefer OSI-approved licenses such as MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, MPL-2.0, EPL-2.0, LGPL, or GPL-compatible licenses that fit the distribution model.
- Do not add proprietary, source-available-only, noncommercial, trial, paid, freemium-gated, or revenue-threshold licenses without calling out the license and asking first.
- Before adding or upgrading a dependency, check its license metadata and update `THIRD-PARTY-NOTICES.md` and `THIRD-PARTY-INVENTORY.md` when applicable.
- Preserve license and copyright banners in bundled JavaScript, CSS, fonts, images, and generated browser assets.
- Existing non-FOSS or commercially conditioned dependencies are grandfathered only until replaced; do not expand their use without explicit approval.

### 9.1 Before Adding Any NuGet Package

1. **Search first**: Check `Directory.Packages.props` — if a package already handles the need, use it. Do not add a second library for the same domain.
2. **Try BCL/framework first**: If `System.*` or `Microsoft.*` provides the capability, use it. External packages are for capabilities the framework doesn't cover.
3. **One library per domain**: Any second library doing the same job as an existing one requires explicit justification.

**Four-question evaluation checklist:**
1. License: OSI-approved? If not, stop — existing policy.
2. Maintenance: Last commit within 12 months? If abandoned, find an alternative.
3. Transitive deps: Run `dotnet list package --include-transitive` after adding — any conflicts?
4. Necessity: Can the need be met with ~50 lines of BCL code? If yes, write it inline.

**What to update when adding a library:**
- `Directory.Packages.props` — add version under the appropriate property group (`$(MicrosoftExtensionsVersion)`, `$(SerilogVersion)`, etc.)
- `THIRD-PARTY-NOTICES.md` — license attribution
- `THIRD-PARTY-INVENTORY.md` — inventory entry

**Approved library table (one per domain — do not duplicate):**

| Domain | Approved | Do Not Add |
| :--- | :--- | :--- |
| JSON | `System.Text.Json` (BCL) | Newtonsoft.Json |
| Logging | `Serilog` | NLog, log4net, direct console sinks |
| Session store | `Microsoft.Data.Sqlite` | EF Core for engine use |
| PGP crypto | `PgpCore` | Standalone BouncyCastle |

---

## 10. Shared Report Runtime Assets

The report browser runtime has exactly one source of truth:

```
src/ETL-SQL.ReportRuntime/Resources/Shared/
```

Files copied under these host folders are generated sync outputs and must not be edited directly:

- `src/ETL-SQL.ReportPlayer/wwwroot/`
- `src/ETL-SQL.ReportPortal/wwwroot/js/`
- `src/ETL-SQL.ReportPortal/wwwroot/css/`
- `src/etl-sql-vscode/media/`

When changing report runtime JavaScript, CSS, themes, or shared browser dependencies:

1. Edit the canonical file in `src/ETL-SQL.ReportRuntime/Resources/Shared/`.
2. Run `node .\scripts\sync-assets.js`.
3. Run `node .\scripts\sync-assets.js -Check`.

Do not "fix" drift by editing generated host copies. The check step compares host copies to the canonical shared source and will fail if they diverge.

### Prototyping browser-side UI (no Docker)

Before changing a browser-side report/portal component, prototype and visually verify it in the **UI sandbox** at `tools/ui-sandbox/` (`pwsh -File tools\ui-sandbox\serve.ps1`) — do **not** spin up Docker or the full portal just to eyeball a JS/CSS change. It is a no-build "stories" harness that imports the canonical/source files directly (cache-busted on **↻ Reload**), so an edit shows immediately with no sync, no portal build, and no catalog DB. It hosts the `designer.js` exports (`renderDag`, `createScriptEditor`, `createDesigner`) and extracted portal UI modules (e.g. `src/ETL-SQL.ReportPortal/wwwroot/js/lineage-ui.js`); each surface is a story under `tools/ui-sandbox/stories/` driven by fixture data, with an injectable mock fetch (`mockApi.js`) for API-backed components. Add or extend a story when you change a surface. The sandbox is dev-only and does **not** replace the sync step above.

---

## 11. Source Boundary Rules for Agents

Before moving source files, projects, report runtime assets, or host-owned behavior, read **[Source_Boundary_Migration_Plan.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Strategy/Source_Boundary_Migration_Plan.md)**.

- Keep Core focused on shared language contracts, Engine focused on execution, Connectors focused on provider I/O, and host shells focused on hosting.
- Move linting, lineage, explain, dialect checks, help verification, and diagnostics toward `ETL-SQL.Analysis` in small, testable steps.
- Keep report semantics in the reporting layer; ReportPlayer, ReportPortal, and VS Code should host reports, not fork manifest, style, visual, page, dataset, or chart behavior.
- Keep reusable report session hosting in `ETL-SQL.ReportHosting`; ReportPlayer and ReportPortal may consume it, but Portal should not depend on Player for execution/session behavior.
- Preserve the VS Code extension's ecosystem-facing `src/etl-sql-vscode` folder/package naming unless there is a deliberate release plan.
- Do not start source cleanup with a broad restructure. Prefer one ownership boundary at a time, update docs/tests with the move, and leave compatibility shims while hosts migrate.

---

## 12. Developer Workflows & Utility Scripts

To assist in local development, compiling, and executing test suites, the repository includes several core scripts under the `scripts/` folder. Both PowerShell (`.ps1` for Windows) and Bash (`.sh` for Linux/macOS) scripts are provided.

For full usage and script details, refer to **[scripts/README.md](file:///c:/Users/chuck/scratch/ETL-SQL/scripts/README.md)**.

### Key Scripts Reference:
- **Build Debug Environment:** Compiles the .NET solution, Vite React UI components, and the VS Code TypeScript extension.
  - Windows: `.\scripts\build-debug.ps1`
  - Linux/macOS: `./scripts/build-debug.sh`
- **Smoke Tests:** Runs targeted categories of fast smoke tests (Core, Security, Reporting, Portal).
  - Windows: `.\scripts\test-smoke.ps1 -Lane all`
  - Linux/macOS: `./scripts/test-smoke.sh --lane all`
- **General Test Lanes:** Gateway script to run specific test suites (fast, engine, portal, integration, perf, full, benchmarks, slt).
  - Windows: `.\scripts\test-lane.ps1 -Lane fast`
  - Linux/macOS: `./scripts/test-lane.sh --lane fast`
- **SQLite Logic Tests (SLT) Corpus:** Runs the SLT verification engine against corpus files, writing output to teed timestamped logs in `slt_results/`.
  - Windows: `.\scripts\Test-SltCorpus.ps1 -CorpusOnly`
  - Linux/macOS: `./scripts/Test-SltCorpus.sh --corpus-only`
- **TRX Results Summarizer:** In Windows PowerShell, run `.\scripts\Parse-SltResults.ps1` to print a color-coded test run summary directly to the command prompt.

---

## 13. Common Mistakes to Avoid

| Mistake | Correct pattern |
| :--- | :--- |
| Writing `SELECT TOP 10` against a Postgres connection | Use `LIMIT 10` or push via `EXECUTE pg_conn BEGIN SELECT ... LIMIT 10; END` |
| Using relative paths: `FROM 'data\input.csv'` | Always use absolute paths: `FROM 'C:\Data\input.csv'` |
| Printing a connection string in a PRINT or exception message | Never include connection strings, passwords, or tokens in any output |
| Running a `DELETE` or `MERGE` without a safety check | Wrap in `SET WHAT_IF ON` first, then re-run without it |
| Forgetting `IF FILE_EXISTS()` before `COPY FILE` or `ENCRYPT FILE` | Check existence first to avoid silent no-ops or errors |
| Using `Logger.Instance` in C# engine code | Use injected `ILogger` from `IExecutionContext` |
| Declaring `class` for AST nodes in C# | Use `record` types for all AST nodes |
| Using `Firebird` as a connector token | Firebird is not a supported connector; use `ODBC` with a Firebird driver instead |
| Writing `FROM FLATFILE` or `FROM FILE` in a `CREATE CONNECTION` | `FLATFILE` is the **connector type**; `FILE` is the **table alias** used in queries — `CREATE CONNECTION src AS FLATFILE('my.csv'); SELECT * FROM src` |
| Using `PWD=` as an ETL-SQL-owned connection option | Use `PASSWORD=` for ETL-SQL-created options. Vendor-native pass-through syntax such as ODBC `PWD` is valid when the connector intentionally exposes it. |
| Writing `CREATE CONNECTION ... WITH(option=value)` | `WITH()` is not valid on `CREATE CONNECTION`. Options go directly in parentheses: `CREATE CONNECTION c AS MSSQL(SERVER='s', PASSWORD='p')` |

---

## 14. Breaking Change Protocol

A **breaking change** is any modification that could produce different results for identical script input. This includes:
- **Syntax changes** — keyword renamed, clause made required, operator removed
- **Semantic changes** — same syntax, different output (the silent and most dangerous kind)
- **Type system changes** — implicit coercions, NULL propagation edge cases
- **Runtime behavior** — pushdown decisions that change observable ordering or type coercions

### Protocol — follow all three steps when introducing a breaking change:

1. Add a `// COMPAT_BREAK: x.y` comment at the exact line of change in the source
2. Add an entry to `BREAKING_CHANGES.md` at repo root (format: `version | category | description | migration path`)
3. Write a regression test proving old vs. new behavior — mark the test class with `[Trait("CompatBreak", "x.y")]`

For **parser syntax removals**: keep the old form parsing (with a lint warning emitted) for one minor version before removing it entirely.

### High-risk sites — apply extra scrutiny here:
- `ExpressionEvaluator.cs` — operator precedence, NULL propagation, implicit coercions
- `Evaluator.IsSqlPushdown()` — pushdown decisions change observable ordering/types
- `ExternalAggregateEngine`, `ExternalJoinEngine`, `ExternalWindowEngine`, `ExternalSortEngine` — spill behavior affects determinism
- `StatementParser.cs` — any removal of previously-valid syntax

**Do not add behavioral shims** ("run v1 semantics on a v3 engine"). Shims accumulate indefinitely and make the engine unmaintainable. The migration linter is the compatibility layer — build it when a major version ships with breaking changes, not before.

### 14.1 Release, Versioning, and Branching Rules

ETL-SQL strictly follows [Semantic Versioning 2.0.0](https://semver.org/). Agents must respect the following rules:

- **Pre-1.0.0 Releases (`0.y.z`):** The engine runtime is in active development. Breaking changes and syntax deprecations are allowed between minor versions (e.g., `v0.13.0` to `v0.14.0`) but MUST be logged in `BREAKING_CHANGES.md` and follow the deprecation process. Patch releases (e.g., `v0.14.1`) are reserved for bug fixes.
- **Post-1.0.0 Releases (`1.y.z` and beyond):** 
  - **Minor versions (`1.x.0`):** New features, connector additions, or enhancements. Must be strictly backwards-compatible; no breaking changes are permitted.
  - **Patch versions (`1.x.y`):** Backward-compatible bug fixes and security hotfixes only.
  - **Major versions (`x.0.0`):** Reserved for breaking changes, syntax removals, and paradigm shifts.
- **Branch Management for Stable Releases:**
  - Active development for minor/major versions occurs on the `main` branch.
  - Upon tagging a stable release (e.g., `v1.0.0`), create a release branch named `release/v1.0` (or `release/v1.x`).
  - Critical bug fixes and security hotfixes targeting that release must be applied (or cherry-picked) to its corresponding `release/v1.x` branch and released as patch version updates (e.g., `v1.0.1`). These fixes must also be integrated back into the `main` branch.

---

## 15. Syntax Consistency Rules

These rules apply when **adding new language syntax** (keywords, statements, or connection options) to the engine.

### Keyword tokens
All ETL-SQL keywords must be **UPPERCASE, underscore-separated**: `ENGINE_COMPAT`, `WEEK_START_DAY`, `SCRIPT_HASH_POLICY`, `REQUIRE`. Never CamelCase, never mixed-case tokens.

### When to add a keyword vs. a connection option
- **Add a keyword** when: the concept is part of core control flow — an ON/OFF toggle, a mode selection between fundamentally different behaviors
- **Add a connection option** when: it is a configuration value, connector-specific, or optional-by-default
- Never add a keyword for something that varies per-connector or is optional

### Statement naming convention
- AST record: `{Verb}{Noun}Statement` — `SetEngineCompatStatement`, `RequireVersionStatement`, `CreateConnectionStatement`
- Handler: `{Verb}{Noun}StatementHandler` — must match the record name exactly
- AST record properties: **PascalCase** — `TargetTable`, `ConnectionName`, `CompatVersion`, `IfNotExists`
- No abbreviations except universally understood ones (`Sql`, `Id`)

### Option value conventions
| Value type | Form | Example |
| :--- | :--- | :--- |
| Boolean | `ON`/`OFF` or `TRUE`/`FALSE` — pick one, never accept both silently | `POOLING = TRUE` |
| Numeric | Unquoted | `TIMEOUT_SECONDS = 30` |
| String | Single-quoted | `SERVER = 'localhost'` |
| Enum | UPPERCASE, normalized — never expose raw provider casing | `SSL_MODE = 'REQUIRED'` not `'Required'` |

---

*For a complete syntax walkthrough, start at [User_Manual.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/User_Manual.md) and then refer to the [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) reference.*
