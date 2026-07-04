# ETL-SQL Grammar & Orchestration Syntax

This document is the authoritative reference for the ETL-SQL scripting language. It defines every statement type, clause, and keyword â€” everything needed to write, administer, and automate with ETL-SQL.

> This document reflects the current ETL-SQL engine. All syntax shown here is implemented and parseable.

---

## 1. Variable & State Management

### 1.1 `DECLARE`
Defines one or more variables. The data type is optional; omitting it defaults to `ANY` (inferred from the assigned value).

```sql
DECLARE @name    STRING        = 'Chuck';
DECLARE @note    MARKDOWN      = '# Hello';
DECLARE @id      INT           = 101;
DECLARE @rate    DECIMAL(10,4) = 1.2345;
DECLARE @icon    IMAGE         = 'C:\Data\icon.png';
DECLARE @range   MINMAX(INT)   = (1, 100);
DECLARE @files   LIST(PATH)    = ('C:\Data\1.csv', 'C:\Data\2.csv');
DECLARE @path    PATH          = 'C:\Data\1.csv';

-- Multiple variables in one statement
DECLARE @list LIST = (1, 2, 3), @count INT = 0;

-- No type â€” inferred at runtime
DECLARE @value = 'hello';

-- Table variable â€” acts as an in-memory temp table
DECLARE @inventory TABLE;
```

**Supported Data Types:**

| Category | Types |
| :--- | :--- |
| **Numeric** | `INT`, `INTEGER`, `BIGINT`, `SMALLINT`, `TINYINT`, `BIT`, `BOOLEAN`, `BOOL`, `DECIMAL`, `NUMERIC`, `MONEY`, `SMALLMONEY`, `FLOAT`, `REAL`, `DOUBLE` |
| **Temporal** | `DATE`, `TIME`, `DATETIME`, `DATETIME2`, `SMALLDATETIME`, `DATETIMEOFFSET`, `TIMESTAMP` |
| **Character** | `CHAR`, `VARCHAR`, `VARCHAR2`, `NCHAR`, `NVARCHAR`, `TEXT`, `NTEXT`, `STRING`, `MARKDOWN` |
| **Binary** | `BINARY`, `VARBINARY`, `IMAGE`, `BLOB`, `LOB` |
| **Specialized** | `XML`, `JSON`, `UNIQUEIDENTIFIER`, `UUID`, `GUID`, `GEOMETRY`, `GEOGRAPHY`, `HIERARCHYID`, `VARIANT`, `SQL_VARIANT`, `ANY`, `PATH`, `MINMAX`, `CURSOR`, `ENCRYPTED`, `VECTOR`, `SENSITIVE`, `SECRET`, `RELDATE` |
| **Collections** | `LIST`, `LIST(<type>)`, `TABLE` |

### 1.2 Specialty Types

Specialty types carry semantic meaning beyond a plain string or number. They influence how values are stored, validated, masked, or rendered. The table below gives a quick summary; detailed notes follow.

| Type | Stored as | When behavior activates |
| :--- | :--- | :--- |
| `PATH` | String | At file I/O â€” normalizes separators, resolves relative paths, validates security boundaries |
| `JSON` | String | At assignment â€” validates well-formedness immediately; enables `JSON_VALUE`, `JSON_QUERY`, etc. |
| `XML` | String | At assignment â€” validates well-formedness immediately; enables `XMLVALUE`, `XMLQUERY`, etc. |
| `MARKDOWN` | String | At render time â€” Report Portal renders as rich text; CLI treats as plain string |
| `LIST` | Collection | At iteration â€” can be strictly typed, e.g. `LIST(INT)` |
| `MINMAX` | Struct | At declaration â€” gives a `.MIN` and `.MAX` member; inner type annotation is documentary |
| `ENCRYPTED` | String | At runtime â€” masked in `SHOW VARIABLES`; auto-decrypts `ENC:` values when assigned to non-SENSITIVE targets or passed to secure parameters |
| `SENSITIVE` | Any | At runtime â€” masked in `SHOW VARIABLES`; auto-decrypts `ENC:` values when assigned to non-SENSITIVE targets or passed to secure parameters |
| `SECRET` | Any | At session end â€” nullified in memory automatically; same masking/auto-decryption as `SENSITIVE` |

---

#### `PATH`

Stored as a string at declaration time. When the value is passed to any file I/O operation, `ResolvePath()` activates and:

- Strips surrounding double-quotes that Windows *Copy as path* adds (e.g. `"C:\tmp\file.csv"`)
- **Connector Support**: Accepts a connector name as the root segment: `MyDrive/subdir/file.csv`.
- **Directory Connections**: For local folders, you can use a `DIRECTORY` connection name as the path itself: `CREATE CONNECTION d AS DIRECTORY('C:\tmp'); COPY DIRECTORY d TO 'C:\Backup';`.
- **Normalization**: Normalizes separators, resolves relative paths against the script's working directory.
- **Security**: Validates the resolved path against configured security boundaries (allowed root paths, permitted file extensions).

```sql
DECLARE @out PATH = 'C:\Data\results.csv';       -- absolute
DECLARE @src PATH = 'SftpServer/inbox/feed.csv'; -- connector-relative
```

Use `PATH` instead of `STRING` whenever a variable holds a filesystem path. This makes intent explicit to the LSP, agents, and the security layer.

---

#### `JSON`

Validated at assignment â€” an invalid JSON string raises an `ExecutionException` immediately at the `DECLARE` or `SET` line. Stored as a string internally after passing validation.

Declaring a variable as `JSON` unlocks the full JSON function set on that value:

| Function | Purpose |
| :--- | :--- |
| `JSON_VALUE(@v, '$.path')` | Extract a scalar value at the given JSONPath |
| `JSON_QUERY(@v, '$.path')` | Extract an object or array fragment |
| `JSON_MODIFY(@v, '$.path', val)` | Return a copy with a value updated or inserted |
| `ISJSON(@v)` | Returns `1` if the string is valid JSON, `0` otherwise |
| `JSON_EXISTS(@v, '$.path')` | Returns `1` if the path exists |
| `JSON_TABLE(@v, '$.path' COLUMNS (...))` | Expand and project JSON rows into a table |
| `OPENJSON(@v)` | SQL-Server-style JSON rowset expansion |

```sql
DECLARE @payload JSON = '{"order":{"id":42,"total":99.95}}';
SELECT JSON_VALUE(@payload, '$.order.id')    AS id,
       JSON_VALUE(@payload, '$.order.total') AS total;
```

```sql
SELECT *
FROM JSON_TABLE(@payload, '$.order.items[*]' COLUMNS (
    item_no FOR ORDINALITY,
    sku STRING PATH '$.sku',
    qty INT PATH '$.quantity' DEFAULT 0 ON EMPTY,
    has_discount EXISTS PATH '$.discount'
));
```

---

#### `XML`

Validated at assignment â€” an invalid XML string raises an `ExecutionException` immediately at the `DECLARE` or `SET` line. Stored as a string internally after passing validation.

| Function | Purpose |
| :--- | :--- |
| `XMLVALUE(@v, xpath)` | Extract a scalar value using XPath |
| `XMLQUERY(@v, xpath)` | Extract an XML fragment |
| `XMLTABLE(@v, xpath)` | Expand an XML document into a table |
| `XMLEXISTS(@v, xpath)` | Returns `1` if the XPath matches any node |
| `XMLELEMENT(name, content)` | Construct an XML element |

```sql
DECLARE @doc XML = '<root><item id="1">Alpha</item></root>';
SELECT XMLVALUE(@doc, '//item[@id=1]') AS name;
```

---

#### `MARKDOWN`

Stored as a plain string. No validation is performed at assignment (any string is technically valid markdown). In script execution (CLI, headless), it is treated identically to `STRING`. In the **Report Portal**, a `MARKDOWN` variable bound to a visual component is rendered as HTML-formatted rich text â€” headers, bold, lists, tables, and code blocks are all interpreted.

```sql
DECLARE @summary MARKDOWN = '## Run Complete\n- Records: 1000\n- Errors: 0';
```

Use `MARKDOWN` as a rendering hint for the Report Portal. It has no runtime overhead and signals intent to both the engine and AI agents building dashboards.

---

#### `LIST`

An ordered, index-accessible collection. Elements can be iterated with `FOREACH` or accessed by position. Optionally strongly typed:

```sql
DECLARE @ids   LIST(INT)  = (1, 2, 3);
DECLARE @paths LIST(PATH) = ('C:\a.csv', 'C:\b.csv');
DECLARE @mixed LIST       = ('hello', 42, NULL);   -- inferred
```

The inner type annotation (e.g. `LIST(INT)`) is enforced as a cast â€” assigning a non-castable value raises an error. `LIST` without a type accepts any value.

---

#### `MINMAX`

A structured two-value type with `.MIN` and `.MAX` members. It is the only non-collection specialty type that provides member access via dot notation.

```sql
DECLARE @range MINMAX(INT) = (1, 100);

PRINT @range.MIN;   -- 1
PRINT @range.MAX;   -- 100

SET @range.MIN = 10;
SET @range.MAX = 90;
```

The inner type annotation (`MINMAX(INT)`, `MINMAX(DECIMAL)`, etc.) is documentary â€” the engine stores `.MIN` and `.MAX` as generic `object` values and does not enforce the inner type at runtime. Use it to communicate intent.

Common uses: date window boundaries, numeric filter ranges, batch size limits.

---

#### `ENCRYPTED`

The canonical type for variables that hold an `ENC:...` value â€” a ciphertext string produced by `ENCRYPT()` or stored in the credentials vault. This type ensures the value is protected throughout its lifecycle in the engine.

**Engine Behaviors:**
- **Runtime Masking:** The variable is marked as sensitive. `SHOW VARIABLES` and `PRINT` output will mask the value (displaying `*******` or `ENC:*******`) unless `SET SHOW_SECRETS ON` is active.
- **Auto-Decryption:** The engine automatically decrypts the `ENC:` value in two scenarios:
  1. When passed to a secure connector parameter (`PASSWORD`, `API_KEY`, `SSH_KEY_PAIR.PASSPHRASE`, etc.).
  2. When evaluated in an expression that is assigned to a non-SENSITIVE target or used in a comparison.
- **Lint Protection (SEC-4):** The linter flags any attempt to concatenate or pass an `ENCRYPTED` variable to insecure sinks (like `SEND EMAIL` bodies or file writes).

```sql
USE PASSWORD = 'my-master-key';
DECLARE @pwd ENCRYPTED = 'ENC:abc123==';

-- Connection automatically handles decryption
CREATE CONNECTION MyDb AS MSSQL(PASSWORD = @pwd); 

-- Linter will warn here, and runtime will mask output
PRINT 'The password is: ' + @pwd; 
```

---

#### `SENSITIVE`

Sets the `IsSensitive` runtime flag on the variable. Three effects activate immediately:

1. **`SHOW VARIABLES` masking** â€” the value is replaced with `*******` in all variable listing output (unless `SET SHOW_SECRETS ON` is active).
2. **`ENC:` auto-decryption** â€” if the value begins with `ENC:`, the engine automatically decrypts it when the variable is passed to a secure connector parameter (`PASSWORD`, `API_KEY`, `SSH_KEY_PAIR.PASSPHRASE`, etc.). This requires `USE SCRIPT PASSWORD` or a master password to be set.
3. **Lint taint tracking** â€” if you assign a `SENSITIVE` variable into a new variable (`SET @other = @pwd`), the linter marks `@other` as sensitive too, propagating SEC-4 warnings forward.

`SENSITIVE` ensures that the value is protected in output â€” `PRINT @sensitiveVar` will output `*******` unless `SET SHOW_SECRETS ON` is active. The SEC-4 lint rule also warns you if you attempt to use these variables in insecure sinks.

```sql
DECLARE @dbPass SENSITIVE = 'ENC:abc123==';  -- masked in SHOW VARIABLES, decrypted at connect time
USE PASSWORD = 'my-master-key';
OPEN CONNECTION MyDb WITH (PASSWORD = @dbPass);  -- @dbPass decrypted here automatically
```

---

#### `SECRET`

Implicitly sets `IsSensitive = true`, providing the same masking and auto-decryption effects as `SENSITIVE`. However, it introduces a strict **Zero-Trust memory policy**:

- **Session-End Purge:** To minimize exposure of ultra-sensitive data (like MFA tokens or temporary access keys), variables declared as `SECRET` are explicitly nullified in **all** memory scopes (global and nested) immediately after the evaluator finishes execution, regardless of whether the script succeeded or failed.
- **Transience Hint:** Signals to the engine and auditors that this value is a short-lived token that should never be persisted or logged.

```sql
DECLARE @dbPassword  SENSITIVE = 'ENC:abc123==';      -- persistent for session
DECLARE @bearerToken SECRET    = GetBearerToken(...);  -- purged from RAM on finish
```

> [!NOTE]
> Log scrubbing is always active regardless of variable type. The engine automatically redacts patterns like `password=value`, `token=value`, and any `ENC:...` constant found in log messages or connection string text. Variables are scrubbed by pattern, not by metadata â€” so `SENSITIVE`/`SECRET` masking applies specifically to `SHOW VARIABLES` output and the auto-decrypt pathway.

---

#### `RELDATE`

A relative-date expression â€” a string that the engine resolves to a concrete `DATE` or `DATETIME` value each time the script executes. Storing the expression rather than a fixed date means "yesterday" always means yesterday relative to the run.

```sql
DECLARE @start RELDATE INPUT = 'M-1';   -- first day of last month
DECLARE @end   RELDATE INPUT = 'D';     -- today at midnight
DECLARE @fixed RELDATE       = '2026-01-01';  -- pinned: never changes

SELECT * FROM prod.Sales WHERE SaleDate BETWEEN @start AND @end;
```

**Expression format:** `<anchor><unit><offset>` â€” e.g. `D-7`, `W-1`, `ME-1`, `N-2H`.

| Anchor | Meaning |
| :--- | :--- |
| `D` | Today at midnight |
| `W` | Start of current week |
| `M` | Start of current month |
| `Q` | Start of current quarter |
| `Y` | Start of current year |
| `N` | Now (current timestamp) |
| `WE`, `ME`, `QE`, `YE` | End of current week/month/quarter/year |
| ISO date string | Fixed date â€” resolves to itself |

Append `-<n>` to shift back n periods, e.g. `M-3` = first day of three months ago. Append `+<n>` for forward offsets. For `N` (Now), use inline units: `N-2H` (2 hours), `N-30M` (30 minutes), `N-7D` (7 days).

Week-boundary anchors (`W`, `WE`) use **Monday** as week-start by default; override with `SET WEEK_START_DAY` (Â§2.6) or the `Engine.StartOfWeek` config key.

`RELDATE` is most useful combined with `INPUT` so callers can supply expressions at run time without editing the script. See **Â§1.5 INPUT and OUTPUT Variables**.

---

### 1.3 `SET`
Assigns a new value to an existing variable.

```sql
SET @name  = 'Charles';
SET @count = @count + 1;
SET @label = UPPER(@name) + '_PROCESSED';

-- Member access assignment
SET @range.MIN = 5;
SET @range.MAX = 50;
```

### 1.4 System Variables
Read-only variables that track session state and performance. These are automatically updated by the engine.

| Variable | Description | Example Usage |
| :--- | :--- | :--- |
| `@@ROWCOUNT` | Rows affected by the last DML or returned by the last `SELECT`. | `IF @@ROWCOUNT = 0 PRINT 'No data found';` |
| `@@ERROR` | Integer error code of the preceding statement (0 = success). | `IF @@ERROR <> 0 RETURN;` |
| `@@VERSION` | Full engine version and build metadata string. | `PRINT @@VERSION;` |
| `@@TRANCOUNT` | Current transaction nesting level (0 = no active transaction). | `IF @@TRANCOUNT > 0 COMMIT;` |
| `@@FETCH_STATUS` | Cursor/Foreach fetch status. `0` = Success, `-1` = End of list. | `WHILE @@FETCH_STATUS = 0 BEGIN ... END` |
| `@@TOTAL_SPILLED_BYTES` | Cumulative bytes written to disk for all spill operations this session. | `IF @@TOTAL_SPILLED_BYTES > 1073741824 PRINT 'High disk pressure';` |
| `@@LAST_EXEC_MS` | Milliseconds taken by the last statement. | `IF @@LAST_EXEC_MS > 5000 PRINT 'Warning: slow query';` |
| `@@PEAK_MEMORY_MB` | Peak working-set memory in MB for the current process. | `PRINT 'Peak RAM: ' + @@PEAK_MEMORY_MB + ' MB';` |
| `@@SUBQUERY_CACHE_HITS` | Scalar subquery results retrieved from session cache. | `PRINT 'Cache Hits: ' + @@SUBQUERY_CACHE_HITS;` |
| `@@SUBQUERY_CACHE_MISSES` | Scalar subquery evaluations that required execution. | `PRINT 'Cache Misses: ' + @@SUBQUERY_CACHE_MISSES;` |
| `@@SORT_SPILLS` | External sort runs that spilled to disk this session. | `PRINT 'Sort Spills: ' + @@SORT_SPILLS;` |
| `@@CURRENT_USER` | The effective username running the current session (impersonation-aware). | `WHERE Owner = @@CURRENT_USER` |
| `@@CURRENT_USER_ID` | The effective numeric user identifier for the current session. | `WHERE UserId = @@CURRENT_USER_ID` |
| `@@REAL_USER` | The actual actor running the session (unchanged by impersonation). | `INSERT INTO Audit Logs (Actor) VALUES (@@REAL_USER);` |
| `@@IS_ADMIN` | Boolean indicating if the effective identity has administrator privileges. | `IF @@IS_ADMIN = TRUE PRINT 'Running in admin mode';` |

**Example: Flow control based on row count**
```sql
UPDATE target.Sales SET Status = 'Processed' WHERE Status = 'Pending';
IF @@ROWCOUNT > 0
BEGIN
    PRINT 'Updated ' + @@ROWCOUNT + ' records.';
END
```

### 1.5 `INPUT` and `OUTPUT` Variables
Control how variables are passed to and from sub-scripts.

```sql
DECLARE @BatchId    INT    INPUT  = 0;
DECLARE @ExitStatus STRING OUTPUT = 'Pending';
```

*CLI usage:*
```bash
ETL-SQL run my_script.etlsql --var @BatchId=42 --var @Env=PROD
```

*Script orchestration:*
```sql
-- Parent
DECLARE @SubResult STRING;
RUN SCRIPT 'sub.etlsql' WITH (@Mode = 'FULL', @SubResult = @SubResult);
PRINT 'Sub finished: ' + @SubResult;

-- sub.etlsql
DECLARE @Mode      STRING INPUT;
DECLARE @SubResult STRING OUTPUT = 'OK';
```

### 1.6 `USE PASSWORD`
Sets the master decryption password for the session, used to decrypt `ENC:` connection strings.

```sql
USE PASSWORD = 'myMasterSecret';
CREATE CONNECTION db AS MSSQL('ENC:U2FsdGVkX1+...');
```

### 1.7 `CLEAR SESSION`
Deletes temporary files, recovery manifests, and encrypted session state.

```sql
CLEAR SESSION;                  -- clear current session
CLEAR SESSIONS ALL;             -- clear all sessions for the current user
CLEAR SESSIONS STALE;           -- clear sessions older than 24 hours
CLEAR SESSION 'session-id';     -- clear a specific session
```

### 1.8 Environment Sets (`CREATE SETS` / `USE SETS` / `DROP SETS`)
Named groups of variable assignments for switching between environments.

```sql
CREATE SETS !DEV
BEGIN
    @server   = 'dev-db.internal',
    @database = 'DevWarehouse'
END

CREATE SETS !PROD
BEGIN
    @server   = 'prod-db.internal',
    @database = 'ProdWarehouse';
    SET WITH_PROMPT ON;   -- prompts for confirmation in interactive mode
END

USE SETS !DEV;
DROP SETS IF EXISTS !STAGING;
```

### 1.9 `REQUIRE VERSION`
Halts the script if the engine version does not meet the minimum.

```sql
REQUIRE >= '0.7.0';   -- VERSION keyword is optional
```

Supported operators: `=`, `>`, `>=`.

### 1.10 Variable Introspection

Use these statements to inspect variables in the current session or scope.

| Statement | Purpose |
| :--- | :--- |
| `SHOW VARIABLES` | Lists all variables (Global + Local) available in the current context. |
| `SHOW LOCAL VARIABLES` | Lists only the variables declared in the current block/script scope. |

All `SHOW` commands support the `INTO #temp` clause.

```sql
SHOW VARIABLES;
SHOW LOCAL VARIABLES INTO #vars;
SELECT * FROM #vars WHERE DataType = 'JSON';
```

---

## 2. Engine Configuration

### 2.1 `SET WHAT_IF`
Dry-run mode. Side-effecting operations are logged but not executed.

```sql
SET WHAT_IF ON;
DELETE FROM prod_db.logs WHERE log_date < '2024-01-01';  -- logged only
SET WHAT_IF OFF;
```

**Suppressed:** `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`, `BULK INSERT`, all file/directory operations, `SEND EMAIL`, Docker actions, DDL.  
**Allowed:** `SELECT`, `DECLARE`, `SET`, `IF`/`WHILE`, `PRINT`, `CREATE CONNECTION`.

### 2.2 `SET PROFILING`
Enables high-resolution statement-level monitoring. See [Section 2.11](#211-observability--telemetry) for details.

```sql
SET PROFILING ON;
RUN SCRIPT 'heavy_transform.etlsql';
SET PROFILING OFF;
SHOW PROFILE INTO #perf;
```

### 2.3 `SET SHOW_SECRETS`
Controls display masking only. When `ON`, variables marked as `SENSITIVE` (see §4.1) may be revealed in plain text during `SHOW VARIABLES`, `PRINT`, logs, diagnostics, or exports. Default: `OFF`.

`SET SHOW_PASSWORD` is accepted as an alias and behaves identically. `SHOW_SECRETS` is the preferred form.

> [!IMPORTANT]
> `SET SHOW_SECRETS` does not control whether plaintext secrets are allowed to remain in saved source files. Use `SET ALLOW_PLAINTEXT_SECRETS` for that unsafe local-development escape hatch. The password supplied by `USE PASSWORD` is still treated as a master/session secret and should not be printed or logged.

```sql
SET SHOW_SECRETS = ON;
SHOW VARIABLES;  -- SENSITIVE values may be visible
SET SHOW_SECRETS = OFF;
```

### 2.4 `SET ALLOW_PLAINTEXT_SECRETS`
Controls source persistence for plaintext secrets. Default: `OFF`.

Admin default: `Engine:AllowPlaintextSecrets` in `appsettings.json`.

When `OFF`, editors and save helpers rewrite literal master-password statements to the prompt form:

```sql
USE PASSWORD = 'dev-only';
-- Saved as:
USE PASSWORD PROMPT;
```

When `ON`, the script explicitly opts into preserving plaintext secrets in the saved file. This is intended only for throwaway local development and emits a warning when executed.

```sql
SET ALLOW_PLAINTEXT_SECRETS = ON;
USE PASSWORD = 'dev-only';  -- May remain in saved source

SET ALLOW_PLAINTEXT_SECRETS = OFF;
```

Published Orchestrator bundles still strip literal `USE PASSWORD` statements from the published copy regardless of this setting.

### 2.5 `SET NO_SAVE_SENSITIVE`
Controls save-time scrubbing for sensitive values. Default: `OFF`.

Admin default: `Engine:NoSaveSensitive` in `appsettings.json`.

When `ON`, save helpers remove plaintext sensitive values from saved source. This includes literal `USE PASSWORD` statements, `SENSITIVE`/`ENCRYPTED` declarations with literal values, connection option values containing passwords, and password fragments in connection strings.

```sql
SET NO_SAVE_SENSITIVE = ON;
USE PASSWORD = 'dev-only';
DECLARE @apiToken SENSITIVE = 'local-token';
CREATE CONNECTION api AS REST(
    AUTH_TYPE = 'APIKEY',
    HEADER_NAME = 'X-API-Key',
    TOKEN = 'local-key'
);

-- Saved source uses placeholders/prompt form instead of those values.
```

### 2.6 `SET NO_SAVE_CONNECTION`
Controls save-time scrubbing for connection location and identity details. Default: `OFF`.

Admin default: `Engine:NoSaveConnection` in `appsettings.json`.

When `ON`, save helpers replace `CREATE CONNECTION` targets and quoted connection options with placeholders. This removes hosts, databases, usernames, passwords, API keys, and similar connection details from saved source.

```sql
SET NO_SAVE_CONNECTION = ON;
CREATE CONNECTION prod AS POSTGRES('Host=db01;Username=etl;Password=pw;', HOST = 'db01', DATABASE = 'warehouse', USER = 'etl', PASSWORD = 'pw');
```

### 2.7 `SET CONNECTION_ENCRYPTION`
Controls save-time encryption for connection details. Default: `OFF`.

Admin default: `Engine:ConnectionEncryption` in `appsettings.json`.

When `ON`, save helpers encrypt the `CREATE CONNECTION` target and connection option values using the script/master password. If the same file has `USE PASSWORD = 'literal'`, editors can use that value for save-time encryption and then rewrite the source to `USE PASSWORD PROMPT`.

```sql
SET CONNECTION_ENCRYPTION = ON;
USE PASSWORD = 'dev-only';
CREATE CONNECTION prod AS POSTGRES('Host=db01;Username=etl;Password=pw;', HOST = 'db01', DATABASE = 'warehouse', USER = 'etl', PASSWORD = 'pw');
```

`NO_SAVE_CONNECTION` takes precedence over `CONNECTION_ENCRYPTION` because it removes connection details instead of preserving them encrypted.

### 2.8 Security Overrides
Only honored when the path is within an approved Safe Zone. All overrides produce an audit entry.

| Command | Description |
| :--- | :--- |
| `SET ALLOW_FILE_TYPE_ACCESS ON/OFF` | Allow file extensions not in the standard whitelist |
| `SET ALLOW_FILE_TYPE_ACCESS = '.ext'` | Add a specific extension (e.g. '.bak') to the authorized session whitelist |
| `SET ALLOW_FILE_OPERATIONS = n` | Overrides the default runaway protection limit (100) for file operations |
| `SET ALLOW_RECURSIVE_LAYERS = n` | Overrides the default recursion limit (5) for directory operations |

### 2.9 Performance & Spilling Thresholds
Override `appsettings.json` defaults for the current session.

| Command | Default | Description |
| :--- | :--- | :--- |
| `SET JOIN_SPILL_THRESHOLD = n` | 100,000 | Rows before a join spills to disk |
| `SET WINDOW_SPILL_THRESHOLD = n` | 100,000 | Rows before window functions spill to disk |
| `SET TEMP_TABLE_SPILL_THRESHOLD = n` | 1,000,000 | Rows before a `#temp` table spills to disk |
| `SET EXTERNAL_HASH_PARTITIONS = n` | 32 | Partitions used when spilling joins/windows |
| `SET EXTERNAL_SORT_CHUNK_SIZE = n` | 50,000 | Rows per chunk during external sort |
| `SET BATCHSIZE = n` | 10,000 | Rows per batch in the engine pipeline |
| `SET OPERATOR_MEMORY_GRANT = n` | 256 | Memory grant in MB per query operator before spilling to disk |
| `SET CONNECTION_PREVIEW_LIMIT = n` | 10 | Row limit for schema preview queries during connection setup |
| `SET MAX_LAST_RESULT_ROWS = n` | 50,000 | Rows in the interactive display buffer |
| `SET LINEAGE = ON\|OFF` | ON | Enables data lineage tracking for the current script session |
| `SET LINEAGE_NAMESPACE = 'ns'` | 'etl-sql' | Sets the OpenLineage job namespace for the current session |
| `SET LINEAGE_IMPORT_CATALOG = ON\|OFF` | OFF | Imports database descriptions, comments, and constraints dynamically prior to exporting lineage |
| `SET TELEMETRY = ON\|OFF` | ON | Enables execution metrics and telemetry collection |
| `SET MAX_RECURSIVE_DEPTH = n` | 10,000 | Max call depth for recursive CTEs or procedures |
| `SET MAX_IN_MEMORY_BATCHES = n` | 100 | Batches held before automatic `#temp` spill |
| `SET FOREACH_PAGE_SIZE = n` | 10,000 | Items fetched per page when iterating large collections |
| `SET MAX_MESSAGES = n` | 1,000 | Log/print messages captured in the session buffer |
| `SET MAX_FILE_OPERATIONS = n` | 100 | Filesystem operations allowed per script |
| `SET MAX_GENERATE_ROWS = n` | 10,000 | Rows per `GENERATE` statement (prevents resource exhaustion) |
| `SET MAX_SMTP_EMAILS_PER_SCRIPT = n` | 100 | SMTP emails the current script may send |
| `SET MAX_PARALLEL_DEGREE = n` | 8 | Max concurrent branches inside a `PARALLEL` block |
| `SET MAX_STRING_RESULT_SIZE = n` | 5,242,880 | Max byte length of a string expression result (5 MB) |
| `SET REGEX_MATCH_TIMEOUT = n` | 1,000 | Milliseconds before a regex match is aborted |
| `SET MAX_GROUPING_SETS = n` | 100 | Max `CUBE`/`GROUPING SETS` combinations before abort |
| `SET MAX_SESSION_SIZE = n` | 524,288,000 | Max session state in bytes before eviction (~500 MB) |
| `SET INTERACTIVE_MODE = ON\|OFF` | OFF | Enables "Notebook" behavior: idempotent creation of connections/datasets and cleaned expansion results. |
| `SET PERSIST = ON\|OFF` | ON | Whether to save session state (variables, temp tables) to disk on script completion |
| `SET SPILL_ENCRYPTION = ON/OFF` | ON | AES-256 encryption on spill files |
| `SET SPILL_COMPRESSION = ON/OFF` | ON | Brotli compression on spill files |
| `SET SPILL_FORMAT = 'AUTO'\|'JSON'\|'PARQUET'` | AUTO | Storage format for spilled engine data |


### 2.10 Validation & Error Handling Options
Configure row validation, string truncation, and error skipping behavior for the current session.

| Command | Default | Description |
| :--- | :--- | :--- |
| `SET TRUNCATE_STRING = ON\|OFF` | OFF | When ON, strings exceeding target column/file width are silently truncated to fit. When OFF, truncation triggers a validation error. |
| `SET SKIP_ERROR = ON\|OFF` | OFF | When ON, data conversion/type mismatch errors set the column to NULL and proceed. Primary key/unique constraint violations skip the entire row. When OFF, validation errors abort execution. |

> [!NOTE]
> Database connections utilizing native high-performance bulk protocols (e.g. `SqlBulkCopy` / `COPY`) bypass engine-side validation to prioritize throughput and will fail fast natively if truncation or type boundaries are violated, regardless of session overrides.


### 2.11 `SET WEEK_START_DAY`
Override the first day of the week for `RELDATE` week-boundary expressions (`W`, `W-1`, `WE`, `WE-1`, etc.) for the current script.

```sql
SET WEEK_START_DAY = 'Sunday';   -- valid for this script only
```

Valid values (case-insensitive): `Monday`, `Tuesday`, `Wednesday`, `Thursday`, `Friday`, `Saturday`, `Sunday`. The engine default is `Monday`; the organisation default can be changed with `Engine.StartOfWeek` in `appsettings.json`.

### 2.12 Observability & Telemetry

ETL-SQL provides two layers of performance monitoring: **Telemetry** and **Profiling**.

#### Telemetry (`SET TELEMETRY ON/OFF`)
Telemetry is the "always-on" (by default) lightweight monitoring layer. It tracks cumulative session-wide counters and provides the data for system variables like `@@ROWCOUNT` and `@@TOTAL_SPILLED_BYTES`.

- **Primary Use**: Monitoring overall job progress and resource pressure.
- **Scope**: Session-wide totals and the "last statement" summary.
- **Impact**: Negligible overhead (uses atomic counters).

#### Profiling (`SET PROFILING ON/OFF`)
Profiling is a high-resolution, opt-in monitoring layer that captures detailed timing and resource usage for **every individual statement** executed.

- **Primary Use**: Debugging performance bottlenecks and identifying slow queries.
- **Scope**: Statement-level granularity.
- **Impact**: Low, but creates telemetry objects for every statement; should be disabled in ultra-high-throughput production loops if not needed.
- **Commands**: Enables `SHOW PROFILE` and visual execution tree rendering in the IDE.

| Feature | Telemetry | Profiling |
| :--- | :---: | :---: |
| Default | ON | OFF |
| Granularity | Session / Last Stmt | Per Statement |
| `@@variables` | âœ“ | âœ“ |
| `SHOW PROFILE` | âœ— | âœ“ |
| Execution Tree | âœ— | âœ“ |

**Commands:** `SET TELEMETRY ON` / `SET TELEMETRY OFF` toggle telemetry collection. `SET PROFILING ON` / `SET PROFILING OFF` toggle statement-level profiling.

---

## 3. Connections

### 3.1 `CREATE CONNECTION`
Registers a named data source in the current session.

```sql
-- General form
CREATE CONNECTION <alias> ON <ConnectorType>(<connection-string>);

-- Named-parameter form
CREATE CONNECTION <alias> ON <ConnectorType>(
    KEY = value [, ...]
);

-- Suppress error if already exists
CREATE CONNECTION <alias> IF NOT EXISTS ON <ConnectorType>(...);

-- Update existing connection
CREATE OR ALTER CONNECTION <alias> ON <ConnectorType>(...);
```

**SQL Database Connectors**

```sql
-- SQL Server (Common options: HOST, DATABASE, USER, PASSWORD, TRUSTED_CONNECTION, 
-- USE_SSL, TRUST_SERVER_CERTIFICATE, APPLICATION_INTENT, CONNECT_TIMEOUT, POOL_SIZE)
CREATE CONNECTION prod AS MSSQL(
    HOST = 'sql-server.company.com',
    DATABASE = 'Warehouse',
    TRUSTED_CONNECTION = TRUE,
    APPLICATION_INTENT = READONLY
);

-- PostgreSQL (Common options: HOST, PORT, DATABASE, USER, PASSWORD, SSL_MODE, POOLING)
CREATE CONNECTION pg AS POSTGRES(
    HOST = 'pg-server.company.com',
    DATABASE = 'analytics',
    USER = 'etl',
    PASSWORD = ENC:...,
    SSL_MODE = 'REQUIRE'
);

-- Oracle (Common options: HOST, PORT, SERVICE_NAME, TNS_NAME, USER, PASSWORD)
CREATE CONNECTION ora AS ORACLE(
    HOST = 'ora-server.company.com',
    SERVICE_NAME = 'ORCL',
    USER = 'etl',
    PASSWORD = ENC:...
);

-- ODBC (Common options: DSN, DRIVER, SERVER, DATABASE, UID, PASSWORD)
CREATE CONNECTION legacy AS ODBC(DSN = 'MyLegacyDSN');
```

**File Connectors**

```sql
-- Flat file (Common: PATH, FORMAT, DELIMITER, HEADER, ENCODING, SKIP, COMPRESS, ENCRYPT)
CREATE CONNECTION sales_csv AS FLATFILE(
    PATH = 'C:\Data\sales.csv',
    FORMAT = 'DELIMITED',
    DELIMITER = ',',
    HEADER = ON,
    ENCODING = 'UTF8'
);

-- Parquet (Common: PATH, COMPRESSION)
CREATE CONNECTION facts AS PARQUET(
    PATH = 'C:\Data\facts.parquet',
    COMPRESSION = 'SNAPPY'
);

-- JSON / XML (Common: PATH, ROOT_PATH, ENCODING)
CREATE CONNECTION config AS JSON(PATH = 'C:\Data\config.json', ROOT_PATH = '$.settings');
```

**Transfer Connectors**

```sql
-- SFTP (Common: HOST, PORT, USER, PASSWORD, KEYFILE, PASSPHRASE)
CREATE CONNECTION remote_sftp AS SFTP(
    HOST = 'sftp.company.com',
    USER = 'etl',
    KEYFILE = 'C:\Keys\id_rsa'
);

-- Azure Blob Storage (Common: ACCOUNT_NAME, ACCOUNT_KEY, CONTAINER)
CREATE CONNECTION blob AS AZUREBLOB(
    ACCOUNT_NAME = 'mystorageaccount',
    CONTAINER = 'mycontainer',
    ACCOUNT_KEY = ENC:...
);
```

**API & Notification Connectors**

```sql
-- REST API (Common: URL, METHOD, AUTH_TYPE, TOKEN, BODY, ROOT_PATH, PAGINATION_MODE, PAGE_SIZE)
CREATE CONNECTION api AS REST(
    URL = 'https://api.company.com/v1',
    AUTH_TYPE = 'BEARER',
    TOKEN = 'tkn_123',
    ROOT_PATH = '$.items'
);

-- SMTP (Common: HOST, PORT, USER, PASSWORD, USE_SSL, DEFAULT_FROM)
CREATE CONNECTION mailer AS SMTP(
    HOST = 'smtp.company.com',
    PORT = 587,
    USER = 'alerts@company.com',
    PASSWORD = ENC:...,
    USE_SSL = TRUE
);
```

**Testing Connectors**

```sql
-- In-memory mock database â€” useful for testing without a real database
CREATE CONNECTION testdb AS MOCKDB();

-- Local directory connector â€” treats a folder as a queryable table
CREATE CONNECTION logs_dir AS DIRECTORY('C:\Logs\');
```

**Service Connectors**
```sql
-- Report Portal
CREATE CONNECTION portal AS REPORTPORTAL(
    HOST = 'report-server.company.com',
    PORT = 5000,
    USER = 'admin',
    PASSWORD = ENC:...
);

-- Orchestrator
CREATE CONNECTION orch AS ORCHESTRATOR(
    HOST = 'orch-server.company.com',
    PORT = 5001,
    USER = 'admin',
    PASSWORD = ENC:...
);
```

### 3.2 `ALTER CONNECTION`
Modifies an existing connection. Use this to rotate passwords or update server addresses without dropping the connection.

```sql
ALTER CONNECTION prod AS MSSQL(
    PASSWORD = ENC:...
);

-- Rename or change target only
ALTER CONNECTION stage AS POSTGRES('prod-server-v2');
```

### 3.3 `DROP CONNECTION`

```sql
DROP CONNECTION prod;
DROP CONNECTION prod IF EXISTS;
```

### 3.4 Connection Introspection

Use these statements to inspect the current state of connections in the session.

| Statement | Purpose |
| :--- | :--- |
| `SHOW CONNECTIONS` | Lists all active connections with their type and summary details. |
| `SHOW CONNECTION <name> CONFIG` | Lists all configuration options for a specific connection (passwords redacted). |

All `SHOW` commands support the `INTO #temp` clause to redirect results for further processing.

```sql
-- List all connections
SHOW CONNECTIONS;

-- Inspect configuration for a specific source
SHOW CONNECTION my_db CONFIG;

-- Programmatically inspect and filter config
SHOW CONNECTION my_db CONFIG INTO #cfg;
SELECT * FROM #cfg WHERE Option = 'SERVER';
```

### 3.5 Credential Helpers

| Form | Usage |
| :--- | :--- |
| `ENC:...` | AES-256 encrypted value generated by `ENCRYPT VALUE 'plaintext'` |
| `USE PASSWORD = '...'` | Sets the session decryption key to unlock all `ENC:` values |
| `USE PASSWORD PROMPT` | Prompts the user interactively; password never written to disk |

---

## 4. Control Flow

### 4.1 `IF / ELSE IF / ELSE`

```sql
IF @amount > 1000
BEGIN
    INSERT INTO #high SELECT * FROM #sales WHERE amount > 1000;
END
ELSE IF @amount > 500
BEGIN
    INSERT INTO #mid SELECT * FROM #sales WHERE amount > 500;
END
ELSE
BEGIN
    INSERT INTO #low SELECT * FROM #sales;
END
```

### 4.2 `WHILE`

```sql
DECLARE @i INT = 0;
WHILE @i < 10
BEGIN
    SET @i = @i + 1;
    IF @i = 5 CONTINUE;
    IF @i = 8 BREAK;
    PRINT @i;
END
```

### 4.3 `FOR` â€” Numeric Range

```sql
FOR @idx = 1 TO 10
BEGIN
    INSERT INTO #results (val) VALUES (@idx);
END

FOR @idx = 100 TO 95 STEP -1
BEGIN
    PRINT @idx;
END
```

### 4.4 `FOR @row IN` â€” Query Row Iteration
Executes the body once per row returned by the query. `@row` exposes columns via dot notation.

```sql
FOR @row IN (SELECT id, name, amount FROM sales_db.Orders WHERE status = 'Open')
BEGIN
    PRINT 'Order ' + @row.id + ': ' + @row.name;
    INSERT INTO #summary (OrderId, Name) VALUES (@row.id, @row.name);
END
```

> For large result sets, consider using `SET FOREACH_PAGE_SIZE` to control how many rows are loaded at once.

### 4.5 `FOREACH` â€” Collection Iteration
Iterates through each item in a `LIST` variable, JSON array, or the rows of a `#temp` table.

- **Streaming Support**: When used with a subquery or a connection name, `FOREACH` uses a streaming cursor to minimize memory usage.
- **Auto-Unwrapping**: If the source is a table with a single column named `Value`, the iterator variable will contain the scalar value directly. Otherwise, it contains a `ROW` object.

```sql
-- Iterating a list
DECLARE @months LIST = ('Jan', 'Feb', 'Mar', 'Apr');
FOREACH @month IN @months
BEGIN
    PRINT 'Processing: ' + @month;
END

-- Iterating a #temp table (Dot notation for columns)
SELECT id, name INTO #users FROM src.users;
FOREACH @user IN #users
BEGIN
    PRINT 'User: ' + @user.id + ' - ' + @user.name;
END

-- Commonly used with FILE_LIST
DECLARE @files = FILE_LIST('C:\Data\Drops\', '*.csv');
FOREACH @file IN @files
BEGIN
    PRINT 'Loading: ' + @file.NAME;
    BULK INSERT staging.daily FROM @file.PATH WITH (FORMAT = 'CSV', FIRSTROW = 2);
END
```

### 4.6 `BREAK` / `CONTINUE` / `RETURN`

| Statement | Effect |
| :--- | :--- |
| `BREAK` | Exit the innermost `WHILE` / `FOR` / `FOREACH` loop |
| `CONTINUE` | Skip the rest of the current loop iteration |
| `RETURN` | Exit the current script or procedure immediately |
| `RETURN <expr>` | Exit and return a value to the caller |

### 4.7 `PRINT`

`PRINT` supports two forms: statement and function.

**Statement form** â€” writes one or more expressions to the message log:
```sql
PRINT 'Starting nightly load...';
PRINT 'Processed: ' + @count + ' rows', TRUE;           -- with timestamp
PRINT GETDATE(), TRUE, 'yyyy-MM-dd HH:mm:ss';           -- formatted date

-- Multiple arguments (comma-separated)
PRINT 'User:', @Username, 'Status:', @Status;
```

**Function form** â€” `PRINT(expression)` â€” evaluates the expression and emits the result, useful inside compound expressions or when the statement form's comma-separation would be ambiguous:
```sql
PRINT(@@ROWCOUNT);
PRINT('Rows: ' + @count);

-- Inside a block where a single expression is expected
IF @debug = 1 PRINT(@msg);
```

Both forms accept the same optional `TRUE` timestamp flag and format string as the second and third arguments:
```sql
PRINT(@msg, TRUE, 'HH:mm:ss');
```

> [!IMPORTANT]
> `PRINT` automatically masks any variable or expression result that is of type `SENSITIVE`, `SECRET`, or was declared with `PASSWORD`.

### 4.8 `TRY...CATCH`

```sql
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO target_db.sales SELECT * FROM #staging;
    COMMIT;
END TRY
BEGIN CATCH
    ROLLBACK;
    PRINT 'Error: ' + ERROR_MESSAGE();
    THROW;   -- re-escalate to caller
END CATCH
```

### 4.9 `RAISERROR` / `THROW`

```sql
RAISERROR('Validation failed: missing required column', 16, 1);

THROW 50001, 'Batch ID not found in control table', 1;
```

### 4.10 `WAITFOR` / `WAIT UNTIL`
Pauses script execution. This is an ETL-SQL extension and is not standard SQL.

| Syntax | Behavior |
| :--- | :--- |
| `WAITFOR DELAY 'hh:mm:ss[.fff]'` | Pause for a fixed duration |
| `WAITFOR TIME 'hh:mm:ss'` | Pause until a specific clock time |
| `WAITFOR (condition)` | Poll every 200 ms until condition is truthy |
| `WAIT UNTIL condition` | Preferred ELT-SQL alias for `WAITFOR (condition)` |

```sql
WAITFOR DELAY '00:00:05';
WAITFOR DELAY '00:00:00.500';
WAITFOR TIME '23:30:00';

-- Polling for external state
WAIT UNTIL EXISTS (SELECT 1 FROM incoming_queue WHERE status = 'READY');
WAIT UNTIL (SELECT COUNT(*) FROM #batch_done) = 1;
```

### 4.11 `ASSERT`
Throws an `ExecutionException` if the condition is `FALSE` or `NULL`.

- **Statement Only**: `ASSERT` is a statement, not an expression. It cannot be used inside an `IF` condition or assigned to a variable.
- **Error Handling**: Failed assertions trigger the `CATCH` block of a `TRY...CATCH` statement, allowing for graceful cleanup or custom logging.

```sql
ASSERT (SELECT COUNT(*) FROM #staging) > 0, 'Staging table must not be empty';
ASSERT @total_amount >= 0, 'Negative balances are not allowed';

-- Catching a failed assertion
BEGIN TRY
    ASSERT (SELECT COUNT(*) FROM #errors) = 0, 'Validation errors detected';
END TRY
BEGIN CATCH
    PRINT 'Validation Failed: ' + ERROR_MESSAGE();
    RETURN;
END CATCH
```

### 4.12 `EXPECT SCHEMA`
Validates that a table or connection has the expected column names and type families.

**Inline syntax:**
```sql
EXPECT SCHEMA #staging (
    CustomerId INT,
    Name       VARCHAR,
    Amount     DECIMAL(18,2)
);

-- Warn instead of halting
EXPECT SCHEMA #staging (
    CustomerId INT,
    Name       VARCHAR
) ON DRIFT WARN;
```

**JSON contract specification-backed syntax:**
```sql
-- Validates columns and type families loaded from a reviewed JSON contract spec file
EXPECT SCHEMA #staging FROM 'Specs/customer_spec.json';

-- Warn instead of halting on drift
EXPECT SCHEMA #staging FROM 'Specs/customer_spec.json' ON DRIFT WARN;
```

The JSON specification file must contain a top-level `"schema"` array of objects:
- `column_name` — (string, required) Name of the column.
- `type_family` — (string, required) Expected data type family (e.g., `VARCHAR`, `INT`, `DECIMAL`).
- `nullable` — (boolean, optional) Set to `true` to allow null values, or `false` (default) to enforce NOT NULL.
- `max_length` — (number, optional) Maximum character length for string fields.
- `precision` — (number, optional) Precision for numeric fields.
- `scale` — (number, optional) Scale for numeric fields.

**Named connection checks:**
```sql
-- Also works against named connections
EXPECT SCHEMA myConnection (
    OrderId   INT,
    OrderDate DATE,
    Total     DECIMAL
);
```

**Type families:**

| Family | Matched types |
| :--- | :--- |
| Integer | `INT`, `INTEGER`, `BIGINT`, `SMALLINT`, `TINYINT` |
| Decimal | `DECIMAL`, `NUMERIC`, `MONEY`, `FLOAT`, `REAL`, `DOUBLE` |
| String | `VARCHAR`, `NVARCHAR`, `CHAR`, `TEXT`, `CLOB`, `STRING` |
| Date | `DATE`, `DATETIME`, `DATETIME2`, `TIMESTAMP`, `DATETIMEOFFSET` |
| Boolean | `BIT`, `BOOLEAN`, `BOOL` |
| Binary | `VARBINARY`, `BINARY`, `BLOB`, `IMAGE` |
| Range | `MINMAX` |

### 4.13 Labels and `GOTO`

ETL-SQL supports T-SQL style label markers and GOTO jumps for controlling execution flow and establishing session checkpoints.

#### Syntax
```sql
-- Label definition (must end with a colon)
my_label:

-- GOTO statement (jumps to the label)
GOTO my_label;
```

#### Compile-Time Scoping Constraints
To ensure predictable control flow and execution safety, the compiler enforces the following rules at compile-time:
* **No Jumps INTO Blocks**: You cannot jump from an outer scope into a nested block such as `WHILE`, `FOR`, `FOREACH`, `IF`, `TRY...CATCH`, or `PARALLEL` blocks. Doing so generates a compiler error.
* **Jumps OUT are Allowed**: You can jump from inside a nested block to a label in an outer block (e.g. jumping out of a loop to an error recovery label).
* **Same Batch Constraint**: A `GOTO` cannot cross `GO` batch boundaries.
* **No Cross-Script Jumps**: Jumps must stay within the same script context (you cannot jump into a script called via `RUN SCRIPT`).

#### Session Checkpointing Behavior
In persistent sessions, top-level labels (labels not nested inside loops, conditionals, or try-catch blocks) act as **implicit session state checkpoints**. 
* Hitting a top-level label automatically serializes all variables (as JSON) and active `#temp` tables (spilled via Arrow) to session storage.
* Hitting a nested label (e.g. inside a `WHILE` loop) executes purely as a control flow jump target and does **not** trigger session serialization.

---

## 5. Querying (`SELECT`)

### 5.1 Complete Clause Reference
Clauses must appear in this syntactic order:

```sql
SELECT [DISTINCT] [TOP n [PERCENT] [WITH TIES]]
    <columns | * [EXCLUDE (cols)] [REPLACE (expr AS col)] [RENAME (col AS new)]>
[INTO <target>]
FROM <source> [AS alias]
[JOIN | LEFT JOIN | RIGHT JOIN | FULL JOIN | CROSS JOIN
 | LEFT SEMI JOIN | LEFT ANTI JOIN <table>
    [HASH | LOOP | MERGE]          -- join algorithm hint
    ON <condition>]
[CROSS APPLY | OUTER APPLY (<subquery>) <alias>]
[[CROSS] JOIN LATERAL | LEFT JOIN LATERAL (<subquery>) <alias> [ON <condition>]]   -- ANSI alias for APPLY
[ASOF [LEFT] JOIN <table> ON <equality-keys> AND <inequality>]                     -- nearest-match join
[WHERE <condition>]
[GROUP BY ALL | <columns | positions> | ROLLUP(<cols>) | CUBE(<cols>) | GROUPING SETS(<sets>)]
[HAVING <condition>]
[QUALIFY <condition>]
[PIVOT  (<agg> FOR <col> IN (<vals>)) AS <alias>]
[UNPIVOT (<val_col> FOR <name_col> IN (<cols>)) AS <alias>]
[MATCH_RECOGNIZE (
    [PARTITION BY <expr> [, ...]]
    [ORDER BY <expr> [ASC|DESC] [, ...]]
    [MEASURES <expr> AS <alias> [, ...]]
    [ONE ROW PER MATCH | ALL ROWS PER MATCH]
    PATTERN (<pattern>)
    DEFINE <var> AS <condition> [, ...]
) AS <alias>]
[ORDER BY ALL | <col | position> [ASC|DESC] [, ...]]
[OFFSET n ROWS]
[FETCH NEXT n ROWS ONLY]
[LIMIT n]
[USING SAMPLE n PERCENT | n% | n ROWS [REPEATABLE (seed)]]
[FOR JSON AUTO | PATH | RAW [, ROOT('name')] [, INCLUDE_NULL_VALUES] [, WITHOUT_ARRAY_WRAPPER]]
[FOR XML  AUTO | PATH | RAW [, ROOT('name')] [, ELEMENTS]];
```

### 5.1.1 Modern SELECT conveniences

**Star modifiers** — adjust a `*` projection inline (applied in the order `EXCLUDE` → `REPLACE` → `RENAME`):
```sql
SELECT * EXCLUDE (password, internal_notes) FROM users;
SELECT * REPLACE (UPPER(name) AS name) FROM users;      -- keep all columns, swap one expression
SELECT * RENAME (id AS user_id) FROM users;
```

**`COLUMNS(...)` selector** — select many columns at once in the projection: `COLUMNS(*)`, `COLUMNS(* EXCLUDE (a, b))`, or `COLUMNS('regex')` (case-insensitive name match):
```sql
SELECT COLUMNS('^amount') FROM #sales;     -- every column whose name starts with "amount"
SELECT COLUMNS(* EXCLUDE (secret)) FROM #users;
```

**`ORDER BY ALL`** — order by every output column, left to right (optionally `DESC`):
```sql
SELECT region, product, total FROM #sales ORDER BY ALL;
SELECT region, product, total FROM #sales ORDER BY ALL DESC;
```

**Lateral column aliases** — a SELECT item may reference an alias defined by an *earlier* item in the same list; the earlier expression is inlined. `ORDER BY` may also reference an output alias. A real source column always wins over an alias of the same name:
```sql
SELECT a + b AS total, total * 2 AS doubled FROM #t;
SELECT a, a * -1 AS neg FROM #t ORDER BY neg;
```

**`count()` shorthand** — a zero-argument `COUNT()` is treated as `COUNT(*)`:
```sql
SELECT count() FROM #orders;
```

**Underscore digit separators** — `_` may separate digits in numeric literals:
```sql
SELECT 1_000_000 AS one_million;
```

**`USING SAMPLE`** — return a random subset. `PERCENT`/`%` samples each row with the given probability (Bernoulli); `ROWS` returns a fixed-size random sample (reservoir). `REPEATABLE (seed)` makes it deterministic:
```sql
SELECT * FROM #events USING SAMPLE 10 PERCENT;
SELECT * FROM #events USING SAMPLE 1000 ROWS REPEATABLE (42);
```

**Trailing commas** — an optional trailing comma is allowed in `SELECT`, `GROUP BY`, and `ORDER BY` lists (and function arguments):
```sql
SELECT region, total,
FROM #sales
GROUP BY region,
ORDER BY region,;
```

### 5.2 `INTO` â€” Write Result to Target
```sql
SELECT id, name, category
INTO #temp_staging
FROM sales_db.transactions
WHERE created_at >= '2026-01-01';
```

### 5.3 `TOP` / `LIMIT` / `OFFSET FETCH`
```sql
SELECT TOP 10 * FROM #sales ORDER BY amount DESC;
SELECT TOP 5 PERCENT WITH TIES * FROM #sales ORDER BY amount DESC;
SELECT * FROM #sales ORDER BY amount DESC LIMIT 10;
SELECT * FROM #sales
ORDER BY amount DESC
OFFSET 20 ROWS
FETCH NEXT 10 ROWS ONLY;
SELECT * FROM #sales ORDER BY amount DESC FETCH FIRST 10 ROWS ONLY;
```

### 5.4 `VALUES` Table Constructor
Use `VALUES` as a standalone derived table in a `FROM` or `JOIN` clause. A table alias is required; column aliases are optional and default to `column1`, `column2`, etc.

```sql
SELECT *
FROM (VALUES (1, 'A'), (2, 'B')) AS t(id, name);

SELECT t.name, x.label
FROM (VALUES (1, 'A'), (2, 'B')) AS t(id, name)
JOIN (VALUES (2, 'Two')) AS x(id, label)
    ON t.id = x.id;
```

### 5.5 JOIN Types

| Syntax | Returns |
| :--- | :--- |
| `JOIN` / `INNER JOIN` | Rows with matching keys in both tables |
| `LEFT JOIN` / `LEFT OUTER JOIN` | All left rows; NULLs for unmatched right |
| `RIGHT JOIN` / `RIGHT OUTER JOIN` | All right rows; NULLs for unmatched left |
| `FULL JOIN` / `FULL OUTER JOIN` | All rows from both sides; NULLs for gaps |
| `CROSS JOIN` | Cartesian product |
| `LEFT SEMI JOIN` | Left rows where a match exists on the right |
| `LEFT ANTI JOIN` | Left rows where no match exists on the right |
| `FUZZY JOIN` | Like INNER JOIN but matches on similarity score rather than equality; injects `__score` column |
| `LEFT FUZZY JOIN` | Like LEFT JOIN but matches on similarity score; unmatched left rows appear with NULLs and `__score = NULL` |

#### 5.4.1 Join Algorithms
ETL-SQL supports three join algorithms. While the engine automatically chooses the best one based on table size and statistics, you can provide a hint to override it.

| Algorithm | Hint | Description |
| :--- | :--- | :--- |
| **Nested Loop** | `LOOP` | Standard algorithm; iterates the outer table and probes the inner. Best for small datasets or when the inner side is indexed. |
| **Hash Join** | `HASH` | Builds an in-memory hash table of the inner source. Best for large, unsorted datasets. Higher memory usage. |
| **Merge Join** | `MERGE` | Simultaneously scans two sorted sources. Fastest algorithm for large datasets that are already ordered by the join keys. |

```sql
SELECT * FROM #large AS a
HASH JOIN #lookup AS b ON a.id = b.id;   -- force hash join for performance
```

#### 5.4.2 `FUZZY JOIN` / `LEFT FUZZY JOIN`

Joins two tables on a similarity expression rather than equality. When the `ON` expression contains a `SIMILARITY()` call, the engine builds a trigram blocking index on the right-side table to prune candidates before scoring. If no `SIMILARITY()` call is detected, it falls back to a full nested-loop scan with the threshold expression applied to every pair.

**Syntax**

```sql
SELECT <columns>, __score
FROM   <left_table> [AS alias]
[LEFT] FUZZY JOIN <right_table> [AS alias]
    ON <similarity_expression> > <threshold>
    [KEEP BEST <n>];
```

**Clauses**

| Clause | Required | Description |
| :--- | :--- | :--- |
| `ON <expr> > <threshold>` | Yes | Any expression using `SIMILARITY()`, `LEVENSHTEIN()`, or arithmetic over them. The threshold filters matches. |
| `KEEP BEST <n>` | No | Keep at most N right-side matches per left row, ranked by score descending. Omitting this returns all matches above threshold (fan-out possible). |
| `__score` | Automatic | The similarity score of the winning match is injected into the result. Selecting it explicitly is optional — it is always available. |

**Semantics**

| Situation | `FUZZY JOIN` | `LEFT FUZZY JOIN` |
| :--- | :--- | :--- |
| Match found above threshold | Row(s) included with `__score` | Row(s) included with `__score` |
| No match above threshold | Left row excluded (like INNER JOIN) | Left row included; right columns NULL, `__score` NULL |
| Tie at `KEEP BEST 1` | Deterministic tiebreak by right-side row order | Same |

**Examples**

```sql
-- Basic: best match per left row, must score > 0.80
SELECT a.id, b.canonical_name, __score
FROM   #dirty a
FUZZY JOIN #reference b
    ON SIMILARITY(NORMALIZE(a.name, 'COMPANY'), NORMALIZE(b.name, 'COMPANY')) > 0.80
    KEEP BEST 1;

-- Left variant: keep all unmatched left rows too
SELECT a.id, b.canonical_name, __score
FROM   #unstructured a
LEFT FUZZY JOIN #reference b
    ON SIMILARITY(a.name, b.name) > 0.75
    KEEP BEST 1;

-- Top 3 candidates per row for human review
SELECT a.id, b.id AS candidate_id, b.name, __score
FROM   #dirty a
FUZZY JOIN #reference b
    ON SIMILARITY(a.name, b.name) > 0.60
    KEEP BEST 3
ORDER BY a.id, __score DESC;

-- Composite scoring across two columns
SELECT a.id, b.id, __score
FROM   #dirty a
FUZZY JOIN #reference b
    ON 0.6 * SIMILARITY(a.name, b.name) + 0.4 * SIMILARITY(a.city, b.city) > 0.75
    KEEP BEST 1;
```

**Scale Expectations**

| Left rows | Right rows | Expected behavior |
| :--- | :--- | :--- |
| < 10 k | < 100 k | Fast. Blocking handles it comfortably. |
| 10 k–100 k | < 500 k | Acceptable. Blocking is essential at this scale. |
| > 100 k | > 500 k | May be slow. Consider dedicated record-linkage tooling (e.g. Splink). |

> See `Docs/Reference/Standard_Library.md §16` for `NORMALIZE`, `SIMILARITY`, `LEVENSHTEIN`, `SOUNDEX`, `METAPHONE`, and `NGRAMS/NGRAM_TOKENS` — the building blocks used in `FUZZY JOIN` expressions.

### 5.6 `CROSS APPLY` / `OUTER APPLY` (and `LATERAL`)
```sql
SELECT o.OrderId, t.LineItem
FROM Orders AS o
CROSS APPLY (SELECT * FROM OrderLines WHERE OrderId = o.OrderId) AS t;

SELECT o.OrderId, t.LineItem
FROM Orders AS o
OUTER APPLY (SELECT TOP 1 * FROM OrderLines WHERE OrderId = o.OrderId) AS t;
```

`LATERAL` is the ANSI/DuckDB/PostgreSQL spelling of the same operator — the right-hand subquery is correlated and may reference the left side:

| `LATERAL` form | Equivalent |
| :--- | :--- |
| `CROSS JOIN LATERAL (<subquery>) AS t` | `CROSS APPLY` |
| `, LATERAL (<subquery>) AS t` (comma form) | `CROSS APPLY` |
| `[INNER] JOIN LATERAL (<subquery>) AS t ON <cond>` | `CROSS APPLY` + the `ON` predicate |
| `LEFT [OUTER] JOIN LATERAL (<subquery>) AS t ON <cond>` | `OUTER APPLY` + the `ON` predicate |

```sql
-- Equivalent to the CROSS APPLY above
SELECT o.OrderId, t.LineItem
FROM Orders AS o
CROSS JOIN LATERAL (SELECT * FROM OrderLines WHERE OrderId = o.OrderId) AS t;

-- OUTER APPLY equivalent; the idiomatic LATERAL outer form uses ON true
SELECT o.OrderId, t.LineItem
FROM Orders AS o
LEFT JOIN LATERAL (SELECT TOP 1 * FROM OrderLines WHERE OrderId = o.OrderId) AS t ON true;
```

Unlike `APPLY`, a `LATERAL` join may carry an explicit `ON <condition>`, which is applied as an additional filter over the correlated rows.

### 5.6.1 `ASOF JOIN`
A nearest-match join (DuckDB/ClickHouse/kdb+ style). For each left row it returns the single closest right row that satisfies one inequality, after any equality keys. `ASOF JOIN` drops unmatched left rows; `ASOF LEFT JOIN` keeps them NULL-extended.

```sql
-- Most recent quote at or before each trade
SELECT t.id, q.bid
FROM trades t
ASOF JOIN quotes q
  ON t.symbol = q.symbol   -- zero or more equality keys
  AND t.ts >= q.ts;        -- exactly one inequality
```

- The `ON` clause must contain **exactly one** inequality (`<`, `<=`, `>`, `>=`) plus zero or more equality predicates.
- Direction follows the operator: `>=`/`>` pick the **largest** qualifying right value (most recent at/before); `<=`/`<` pick the **smallest** (nearest at/after).
- The right side is buffered; matching is currently O(left × right). Use equality keys to narrow candidates on large inputs.

`CROSS APPLY` is also used to expand table-valued functions such as `STRING_SPLIT`, `UNNEST`/`FLATTEN` (expand a list/array into rows), `NGRAMS`, and `NGRAM_TOKENS`:

```sql
-- Expand a delimited list column into rows
SELECT s.id, t.Value AS tag
FROM #sources s
CROSS APPLY STRING_SPLIT(s.tags, ',') t;

-- Build a trigram blocking index for fuzzy matching (see §16.5 of Standard_Library.md)
SELECT gram, r.id AS ref_id
INTO   #ref_index
FROM   #reference r
CROSS APPLY (SELECT Value AS gram FROM NGRAM_TOKENS(r.name)) t;
```

### 5.7 Hierarchical Aggregation
```sql
SELECT Region, Product, SUM(Amount) AS Total
FROM #sales
GROUP BY ROLLUP(Region, Product);

SELECT Region, Product, SUM(Amount) AS Total
FROM #sales
GROUP BY CUBE(Region, Product);

SELECT Region, Product, SUM(Amount) AS Total
FROM #sales
GROUP BY GROUPING SETS((Region, Product), (Region), ());
```

#### `GROUP BY ALL`
Groups by **every** SELECT expression that does not contain an aggregate (or window function) — no need to restate the non-aggregated columns:
```sql
-- Equivalent to GROUP BY Region, Product
SELECT Region, Product, SUM(Amount) AS Total
FROM #sales
GROUP BY ALL;
```

#### Positional references (`GROUP BY` / `ORDER BY`)
A bare integer refers to the Nth item in the SELECT list (1-based). A non-trivial expression such as `1 + 1` is **not** treated as a position. Positional references are rejected when the SELECT list contains `*`.
```sql
SELECT Region, SUM(Amount) AS Total
FROM #sales
GROUP BY 1        -- group by Region
ORDER BY 2 DESC;  -- order by Total
```

### 5.8 `PIVOT` / `UNPIVOT`
```sql
SELECT category, [Q1], [Q2], [Q3], [Q4]
FROM (SELECT category, quarter, amount FROM #sales) AS src
PIVOT (SUM(amount) FOR quarter IN ([Q1], [Q2], [Q3], [Q4])) AS pvt;

SELECT category, quarter, amount
FROM #quarterly_sales
UNPIVOT (amount FOR quarter IN ([Q1], [Q2], [Q3], [Q4])) AS unpvt;
```

#### DuckDB-style statement form
A cleaner statement syntax is available alongside the SQL-standard clause above. It supports **dynamic value discovery** (omit `IN` and the distinct values become columns automatically), **multiple `ON` columns**, **multiple aggregates**, and the **`COLUMNS(* EXCLUDE …)`** selector for UNPIVOT.

```sql
-- PIVOT <source> ON <cols> [IN (<values>)] USING <agg>(<col>) [AS <name>] [, ...] [GROUP BY <cols>]
PIVOT #sales ON quarter USING SUM(amount);                       -- dynamic: one column per distinct quarter
PIVOT #sales ON quarter IN ('Q1','Q2') USING SUM(amount) GROUP BY region;
PIVOT #sales ON quarter USING SUM(amount) AS total, COUNT(*) AS cnt;   -- columns: Q1_total, Q1_cnt, ...

-- UNPIVOT <source> ON <cols | COLUMNS(* EXCLUDE (<cols>))> INTO NAME <name_col> VALUE <value_col>
UNPIVOT #sales ON q1, q2, q3 INTO NAME quarter VALUE amount;
UNPIVOT #sales ON COLUMNS(* EXCLUDE (region, name)) INTO NAME quarter VALUE amount;
```

Notes:
- Column naming: with a single unnamed aggregate, output columns are the pivot values (`Q1`, `Q2`); multiple `ON` columns join with `_` (`2000_Q1`); multiple aggregates suffix the aggregate name (`Q1_total`, `Q1_cnt`).
- Omitting `GROUP BY` groups by every column not consumed by `ON` or the aggregates.
- `IN (...)` applies to a single `ON` column; use dynamic discovery for multiple `ON` columns.

### 5.9 `MATCH_RECOGNIZE`

`MATCH_RECOGNIZE` scans an ordered row sequence for named pattern variables. ETL-SQL supports partitioning, ordering, `MEASURES`, `ONE ROW PER MATCH`, `ALL ROWS PER MATCH`, linear `PATTERN` variables, and the `+`, `*`, and `?` quantifiers.

`MATCH_RECOGNIZE` materializes and sorts each input partition in memory. When its input exceeds `Engine:JoinSpillThreshold`, the engine emits an operational warning but continues for compatibility. Pre-filter large sources and keep individual `PARTITION BY` groups within available memory; unlike PIVOT and UNPIVOT, pattern matching does not currently have a spill-backed execution path.

```sql
SELECT start_ts, end_ts
FROM #events
MATCH_RECOGNIZE (
    PARTITION BY account_id
    ORDER BY event_ts
    MEASURES FIRST(A.event_ts) AS start_ts,
             LAST(B.event_ts)  AS end_ts
    ONE ROW PER MATCH
    PATTERN (A B+)
    DEFINE A AS A.amount < 50,
           B AS B.amount >= 80
) AS mr;
```

`ALL ROWS PER MATCH` emits one row for each source row in the match and adds `MATCH_NUMBER` plus `CLASSIFIER` columns.

### 5.10 `FOR JSON` / `FOR XML`
```sql
SELECT id, name, amount FROM #sales
FOR JSON PATH, ROOT('Sales'), INCLUDE_NULL_VALUES;

SELECT id, name FROM #sales
FOR XML PATH, ROOT('Employees'), ELEMENTS;
```

### 5.11 Window Functions
Window functions compute a value across a set of rows related to the current row without collapsing them into a single group. They appear in the `SELECT` column list and require an `OVER` clause.

```sql
SELECT
    id,
    name,
    amount,
    region,

    -- Ranking
    ROW_NUMBER()   OVER (PARTITION BY region ORDER BY amount DESC) AS row_num,
    RANK()         OVER (PARTITION BY region ORDER BY amount DESC) AS rnk,
    DENSE_RANK()   OVER (PARTITION BY region ORDER BY amount DESC) AS dense_rnk,
    NTILE(4)       OVER (ORDER BY amount)                          AS quartile,

    -- Offset
    LAG(amount,  1, 0)  OVER (PARTITION BY region ORDER BY created_at) AS prev_amount,
    LEAD(amount, 1, 0)  OVER (PARTITION BY region ORDER BY created_at) AS next_amount,
    FIRST_VALUE(amount) OVER (PARTITION BY region ORDER BY created_at) AS first_in_region,
    LAST_VALUE(amount)  OVER (PARTITION BY region ORDER BY created_at
                              ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS last_in_region,

    -- Aggregate over window
    SUM(amount)   OVER (PARTITION BY region)                  AS region_total,
    AVG(amount)   OVER (PARTITION BY region)                  AS region_avg,
    COUNT(*)      OVER (PARTITION BY region)                  AS region_count,
    SUM(amount)   OVER (ORDER BY created_at
                        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS running_total,
    SUM(amount)   OVER (ORDER BY created_at
                        GROUPS BETWEEN 1 PRECEDING AND CURRENT ROW
                        EXCLUDE CURRENT ROW) AS peer_group_total

FROM #sales;
```

### 5.10.1 `FILTER` â€” Conditional Aggregation
The `FILTER` clause restricts the rows that an aggregate window function considers. It is evaluated within the window frame but only includes rows that satisfy the condition.

```sql
SELECT date, category, amount,
       SUM(amount) FILTER (WHERE amount > 100) OVER (PARTITION BY category ORDER BY date) as big_sum
FROM sales_data;
```

**Frame syntax:**

| Clause | Meaning |
| :--- | :--- |
| `ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW` | All rows from the partition start to the current row |
| `ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING` | Current row and one row on either side |
| `ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING` | All rows in the partition |
| `RANGE BETWEEN 1 PRECEDING AND 1 FOLLOWING` | Rows whose **ORDER BY value** is within ±1 of the current row's value (value-based, not row count) |
| `GROUPS BETWEEN 1 PRECEDING AND CURRENT ROW` | Current peer group and one prior peer group |
| `EXCLUDE CURRENT ROW` / `EXCLUDE GROUP` / `EXCLUDE TIES` | Removes the current row, current peer group, or peer ties from the frame |

`ROWS`, `RANGE`, and `GROUPS` frame units are all supported. `RANGE` offsets are value-based and require a single **numeric** `ORDER BY` key; date/interval `RANGE` offsets and other unsupported shapes fall back to the full partition.

### 5.11 `QUALIFY` â€” Filter Window Results
The `QUALIFY` clause filters results based on window function values. It is evaluated after window functions are calculated, avoiding the need for a subquery to filter by a ranked or aggregated window value.

```sql
SELECT name, department, salary,
       RANK() OVER (PARTITION BY department ORDER BY salary DESC) as rnk
FROM employee_data
QUALIFY rnk <= 2; -- returns top 2 salaries per department
```

---

## 6. Common Table Expressions (CTE)

```sql
-- Standard CTE
WITH HighSales AS (
    SELECT category, SUM(price) AS Total
    FROM sales_db.transactions
    GROUP BY category
)
SELECT * FROM HighSales WHERE Total > 10000;

-- CTE with explicit column aliases
WITH HighSales (Cat, TotalPrice) AS (
    SELECT category, SUM(price)
    FROM sales_db.transactions
    GROUP BY category
)
SELECT Cat FROM HighSales WHERE TotalPrice > 10000;

-- Multiple CTEs
WITH
    CTE_A AS (SELECT ...),
    CTE_B AS (SELECT ... FROM CTE_A WHERE ...)
SELECT * FROM CTE_B;

-- Recursive CTE
WITH RECURSIVE Counter AS (
    SELECT 1 AS n
    UNION ALL
    SELECT n + 1 FROM Counter WHERE n < 10
)
SELECT n FROM Counter;
```

---

## 7. Set Operations

```sql
SELECT region FROM #east_sales
UNION
SELECT region FROM #west_sales;

SELECT id FROM #batch_a
UNION ALL
SELECT id FROM #batch_b;

SELECT id FROM #full_list
EXCEPT
SELECT id FROM #processed;

SELECT id FROM #active
INTERSECT
SELECT id FROM #eligible;

-- MINUS is an alias for EXCEPT
SELECT id FROM #full_list
MINUS
SELECT id FROM #processed;

-- UNION [ALL] BY NAME aligns inputs by column name (not position); missing columns become NULL
SELECT 1 AS a, 2 AS b
UNION BY NAME
SELECT 20 AS b, 10 AS a;          -- columns a, b -> (1,2), (10,20)

SELECT 1 AS a, 2 AS b
UNION ALL BY NAME
SELECT 3 AS a;                    -- (1,2), (3, NULL)
```

---

## 8. Logical Operators & Filter Predicates

```sql
WHERE amount >= 100 AND status <> 'Cancelled'

WHERE category IN ('Electronics', 'Apparel')
   OR status NOT IN @exclusionList

WHERE email LIKE '%@company.com'
  AND code  LIKE 'US\_%' ESCAPE '\'

WHERE email ILIKE '%@company.com'
  AND notes ~  '^[A-Z]{3}-\d+$'
  AND notes ~* '^abc'

WHERE EXISTS     (SELECT 1 FROM #approved WHERE id = t.id)
WHERE NOT EXISTS (SELECT 1 FROM #blocked  WHERE id = t.id)

WHERE region IS NOT NULL
  AND notes  IS NULL

-- LIKE ANY / LIKE ALL match against a list of patterns (OR / AND); ILIKE variants too
WHERE name LIKE ANY ('A%', 'B%')      -- matches if any pattern matches
  AND code NOT LIKE ALL ('TEST%', 'TMP%')
```

`DESCRIBE <table>` is a DuckDB-style alias for `SHOW COLUMNS FOR <table>`, returning the column metadata of a table:
```sql
DESCRIBE #employees;
```

### 8.1 Member Access (Dot Notation)
Access columns of a row variable, fields of a JSON object, or properties of a system object.

**Resolution order:** Row columns â†’ JSON fields â†’ C# reflection properties (case-insensitive).

| Object | Member | Description |
| :--- | :--- | :--- |
| Row variable (`FOR @row IN`) | `.columnName` | Column value during row iteration â€” see Â§4.4 |
| `FILE_LIST` / `REMOTE_FILE_LIST` rows | `.NAME`, `.PATH`, `.SIZE`, etc. | File metadata columns â€” see Â§16.6 |
| `MINMAX` variable | `.MIN`, `.MAX` | Range bounds â€” see Â§1.2 |
| Docker alias | `.CONNECTION_STRING` | Host-mapped connection string â€” see Â§18 |
| JSON variable | `.fieldName` | Dynamic field extraction â€” see Â§1.2 |

---

## 9. DML (Data Manipulation Language)

### 9.1 `INSERT INTO`
```sql
INSERT INTO sales_db.archive (category, TotalSales)
OUTPUT INSERTED.category, INSERTED.TotalSales INTO #AuditLog
SELECT category, SUM(amount) FROM #daily WHERE processed = 1
GROUP BY category;

SELECT category,
       SUM(amount) FILTER (WHERE amount > 100) AS LargeAmount,
       COUNT(*)    FILTER (WHERE status = 'Open') AS OpenRows
FROM #daily
GROUP BY category;
```

### 9.2 `UPDATE`
```sql
UPDATE sales_db.archive
SET status = 'Closed', closed_at = GETDATE()
OUTPUT DELETED.status AS OldStatus, INSERTED.status AS NewStatus INTO #ChangeLog
WHERE created_at < '2020-01-01';
```

### 9.3 `DELETE`
```sql
DELETE FROM staging.temp_imports
OUTPUT DELETED.id INTO #deleted_ids
WHERE imported_at < DATEADD(DAY, -7, GETDATE());
```

> [!IMPORTANT]
> **Execution Strategy & Directives**:
> - **SQL Pushdown (Database Connections)**: If the target is a database connector supporting SQL pushdown (e.g., PostgreSQL, SQL Server), the engine compiles the statement into native target SQL and runs it directly on the remote server.
> - **Engine-Side Streaming (Flat Files, Local Temp Tables)**: If the target does not support pushdown (e.g., CSV, Excel, in-memory `#temp` tables), the engine reads the entire target source into memory, filters out the deleted rows, and overwrites the target via a batch write (`append = false`).
> - **API Connector Restriction**: Because the API connector maps batch writes (`WriteBatches`) to HTTP `POST`, `PUT`, or `PATCH` requests, using the DML `DELETE` statement on an API connection would cause the engine to rewrite the surviving rows by submitting them again. Therefore, the API connector does not support direct DML `DELETE` statements. To perform deletions on an API target, configure the connection with `METHOD = 'DELETE'` and execute it directly or via a query block.


### 9.4 `MERGE` (Upsert)
```sql
MERGE INTO target_db.Customers AS T
USING (SELECT * FROM #staging) AS S
ON T.UUID = S.UUID
WHEN MATCHED AND S.Checksum <> T.Checksum THEN
    UPDATE SET T.Name = S.Name, T.UpdatedAt = GETDATE()
WHEN NOT MATCHED BY TARGET THEN
    INSERT (UUID, Name) VALUES (S.UUID, S.Name)
WHEN NOT MATCHED BY SOURCE THEN
    DELETE;
```

#### MERGE with OUTPUT clause

Use `OUTPUT` to capture the rows affected by each action into an audit table. The `$action` pseudo-column returns `'INSERT'`, `'UPDATE'`, or `'DELETE'` for each row processed.

```sql
CREATE TABLE #AuditTrail (
    Action     VARCHAR(10),
    CustomerID VARCHAR(50),
    OldSegment VARCHAR(50),
    NewSegment VARCHAR(50)
);

MERGE INTO #Customers AS T
USING #DeltaFeed AS S
ON T.CustomerID = S.CustomerID
WHEN MATCHED AND T.Segment <> S.Segment THEN
    UPDATE SET T.Segment = S.Segment, T.UpdatedAt = GETDATE()
WHEN NOT MATCHED BY TARGET THEN
    INSERT (CustomerID, Name, Segment) VALUES (S.CustomerID, S.Name, S.Segment)
WHEN NOT MATCHED BY SOURCE THEN
    DELETE
OUTPUT
    $action          AS Action,
    INSERTED.CustomerID,
    DELETED.Segment  AS OldSegment,
    INSERTED.Segment AS NewSegment
INTO #AuditTrail;
```

`INSERTED` holds the new row state; `DELETED` holds the previous row state. For `INSERT` actions, `DELETED` columns are `NULL`. For `DELETE` actions, `INSERTED` columns are `NULL`.

> [!NOTE]
> The current engine implementation captures `OUTPUT` rows for `UPDATE` and `DELETE` actions. `INSERT` actions are executed but their rows are not written to the `OUTPUT` target table. Query `#AuditTrail` for `UPDATE`/`DELETE` audit rows and the target table directly for confirmation of inserts.

### 9.5 `BULK INSERT`
```sql
BULK INSERT target_db.DailyLogs (Name, Region, Amount)
FROM 'C:\Incoming\logs.csv'
WITH (
    FORMAT           = 'CSV',
    FIRSTROW         = 2,
    BATCHSIZE        = 10000,
    MAXERRORS        = 5,
    FIELDTERMINATOR  = ',',
    ROWTERMINATOR    = '\n',
    DATE_FORMAT      = 'yyyy-MM-dd',
    STRICT_SCHEMA    = ON
);
```

Supported formats: `CSV`, `PARQUET`, `AVRO`, `EXCEL`.

### 9.6 `GENERATE`
Produces synthetic data for testing.

```sql
GENERATE 100 ROWS INTO #test_data
WITH (SEED = 42)
AS (
    id       = 'SEQUENCE(1, 1)',
    category = 'RANDOM(Electronics, Apparel, Home)',
    price    = 'RANDOM_DECIMAL(10, 500)',
    created  = 'SEQUENCE(2026-01-01, 1, DAY)'
);
```

**Rule functions:** `SEQUENCE(start, step [, unit])`, `RANDOM(val1, val2, ...)`, `RANDOM_INT(min, max)`, `RANDOM_DECIMAL(min, max)`.

### 9.7 `TRUNCATE TABLE`
```sql
TRUNCATE TABLE staging.Daily_Import;
```

---

## 10. DDL (Data Definition Language)

### 10.1 `CREATE TABLE`

Column definitions support inline `/* @tag: value; */` metadata comments placed after the data type (and after the column constraints). These tags are treated identically to `CREATE TAG` — they are seeded into the lineage tracker when the statement executes.

```sql
CREATE TABLE #OrderItems (
    OrderId    INT           IDENTITY PRIMARY KEY  /*@d: Surrogate key*/,
    LineItem   INT           NOT NULL              /*@d: Line position within the order*/,
    Amount     DECIMAL(18,2) NOT NULL CHECK(Amount >= 0) /*@unit: USD; @d: Line item gross amount*/,
    Status     VARCHAR(20)   DEFAULT 'Pending'    /*@d: Fulfillment status; @example: Shipped*/,
    CustomerId INT           REFERENCES Customers(Id) /*@d: FK to customer; @owner: sales_ops*/,
    CONSTRAINT UQ_Line UNIQUE (OrderId, LineItem)
);
```

Tags may also be placed **before** column constraints, immediately after the data type:

```sql
CREATE TABLE #users (
    Email VARCHAR(200) /*@pii: true; @classification: confidential*/ NOT NULL UNIQUE
);
```

```sql
-- CREATE OR REPLACE drops any existing table/view first, then creates it
CREATE OR REPLACE TABLE #staging (id INT, name VARCHAR(100));
CREATE OR REPLACE VIEW active_users AS SELECT * FROM #users WHERE enabled = 1;
```

> [!NOTE]
> `CREATE OR ALTER` is not supported for tables (use `CREATE OR REPLACE`, `DROP TABLE IF EXISTS` + `CREATE TABLE`, or `IF NOT EXISTS`). `CREATE OR ALTER` remains available for views, procedures, and functions.

### 10.2 `ALTER TABLE`
```sql
ALTER TABLE #staging ADD BatchId INT;
ALTER TABLE #staging DROP COLUMN TempFlag;
ALTER TABLE #staging RENAME COLUMN Name TO FullName;
```

-- Drop when done (auto-dropped at session end anyway)
DROP TABLE IF EXISTS #staging;

### 10.3 `DROP TABLE`
```sql
DROP TABLE IF EXISTS #temp_staging;
```

### 10.4 `CREATE INDEX` / `DROP INDEX`
```sql
CREATE UNIQUE INDEX IX_Customers_Email AS Customers(Email ASC);
DROP INDEX Customers.IX_Customers_Email;
DROP INDEX IF EXISTS Customers.IX_Customers_Email;
```

> [!NOTE]
> `CREATE OR ALTER` is not supported for indexes.
---

## 11. Execution Blocks

### 11.1 EXEC / EXECUTE â€” Execution & Pushdown

`EXEC` and `EXECUTE` are functional synonyms in ETL-SQL. They are used for executing dynamic SQL strings, stored procedures, or pushing native SQL blocks directly to a remote connection.

#### Native SQL Pushdown
Pushes a SQL block to a remote connection in its native dialect.

```sql
DECLARE @minId  INT         = 100;
DECLARE @status VARCHAR(20) = 'Active';

EXECUTE m_db INTO #results WITH (@minId, @status)
BEGIN
    SELECT t.id, t.name
    FROM dbo.Employee AS t
    WHERE t.id > ?1 AND t.status = ?2;
END
```

Parameters: `?` = sequential, `?1`/`?2` = indexed.

#### Dynamic SQL
Executes a string as SQL. If `AT` is specified, it executes on the remote connection; otherwise, it executes locally as an ETL-SQL script.

```sql
-- Execute a string as an ETL-SQL script (local)
DECLARE @sql = 'SELECT * FROM #staging';
EXEC (@sql) INTO #results;

-- Push a dynamic string to a remote connection
EXECUTE ('SELECT TOP 10 * FROM Users ORDER BY LastLogin DESC') AT mssql_conn INTO #top_users;
```

#### Stored Procedure Call
```sql
DECLARE @Count INT;
EXECUTE prod_db.dbo.sp_GetCustomerCount @Status = 'Active', @Count = @Count OUTPUT;

-- Shorthand
EXEC ArchiveSales '2025-01-01';
```

#### Service Admin Blocks
Sends a block of admin statements to a `REPORTPORTAL` or `ORCHESTRATOR` connection.

```sql
-- Using EXECUTE
EXECUTE portal BEGIN
    CREATE USER 'john.doe' WITH (EMAIL = 'john@company.com', ROLE = Viewer);
    GRANT READ ON FOLDER '/Finance' TO GROUP 'Finance';
END

-- Using EXEC (Shorthand)
EXEC orch BEGIN
    CREATE JOB 'NightlyArchive' ON SCHEDULE EVERY 1 DAY AT '02:00' AS
        RUN SCRIPT '/scripts/nightly.etlsql';
END
```

> **Error behavior:** Stop-on-first-error within each block. The block is not transactional â€” a failure mid-block leaves prior statements applied.

### 11.2 `PARALLEL`
```sql
PARALLEL
BEGIN
    SELECT * INTO #Dim_Date    FROM src.DateDim;
    SELECT * INTO #Dim_Product FROM src.ProductDim;
    SELECT * INTO #Dim_Region  FROM src.RegionDim;
END
PRINT 'All dimensions loaded.';

-- With concurrency cap
PARALLEL(4)
BEGIN
    RUN SCRIPT 'load_region_north.etlsql';
    RUN SCRIPT 'load_region_south.etlsql';
    RUN SCRIPT 'load_region_east.etlsql';
    RUN SCRIPT 'load_region_west.etlsql';
    RUN SCRIPT 'load_region_central.etlsql';
END
```

### 11.3 `RUN SCRIPT`
```sql
RUN SCRIPT 'sub_process.etlsql' WITH (@batchId = 1234, @env = 'PROD', @result = @out_var OUTPUT);
```

Executes an external `.etlsql` or `.rptsql` file.

**Parameters**:
- **`WITH`**: Optional block to pass variables into the script's scope.
- **`OUTPUT`**: Optional keyword marking a parameter for return-mapping. If a variable passed with `OUTPUT` is modified inside the script, the new value is mapped back to the calling scope's variable.

**Example**:
```sql
DECLARE @count INT = 0;
RUN SCRIPT 'calculate_totals.etlsql' WITH(@category = 'Finance', @total = @count OUTPUT);
PRINT 'Total: ' + CAST(@count AS STRING);
```

### 11.4 `GO` — Batch Separator
The `GO` keyword is a batch separator. It is not an ETL-SQL statement but a signal to the parser to split the script into discrete execution batches. Each batch is compiled and executed completely before the next one begins.

- **Scope**: Variables declared in a previous batch are available in subsequent batches.
- **Errors**: If a batch fails, execution stops immediately; subsequent batches are not executed.
- **Interactive Mode**: In the TUI or VS Code, `GO` defines the "executable unit" for partial runs.

```sql
-- Batch 1: Setup
CREATE TABLE #temp (id INT, name STRING);
GO

-- Batch 2: Processing
INSERT INTO #temp VALUES (1, 'Alice');
SELECT * FROM #temp;
GO
```

---

## 12. Procedures & Functions

### 12.1 `CREATE PROCEDURE`
```sql
CREATE PROCEDURE ArchiveSales @olderThan DATE
AS
BEGIN
    INSERT INTO archive.sales SELECT * FROM prod.sales WHERE created_at < @olderThan;
    DELETE FROM prod.sales WHERE created_at < @olderThan;
END;

EXEC ArchiveSales '2025-01-01';
```

### 12.2 `CREATE FUNCTION`
```sql
CREATE FUNCTION CalculateTax(@amount DECIMAL) RETURNS DECIMAL
AS
BEGIN
    RETURN @amount * 0.15;
END;

SELECT id, CalculateTax(price) AS Tax FROM #sales;
```

### 12.3 `CREATE OR ALTER` / `DROP`
```sql
CREATE OR ALTER PROCEDURE ArchiveSales @olderThan DATE AS BEGIN ... END;
CREATE OR ALTER FUNCTION CalculateTax(@amount DECIMAL) RETURNS DECIMAL AS BEGIN ... END;

DROP FUNCTION  IF EXISTS CalculateTax;
DROP PROCEDURE IF EXISTS ArchiveSales;
```

### 12.4 `CREATE VIEW`

Views are session-scoped query aliases. They store a query definition and evaluate it every time the view is referenced. They do not materialize rows; use `SELECT ... INTO #temp` or `CREATE DATASET` when you need stored results.

```sql
CREATE VIEW ActiveCustomers AS
SELECT id, name, region
FROM #customers
WHERE active = 1;

SELECT * FROM ActiveCustomers WHERE region = 'West';

ALTER VIEW ActiveCustomers AS
SELECT id, name, region, status
FROM #customers
WHERE active = 1;

CREATE OR ALTER VIEW ActiveCustomers AS
SELECT id, name, region
FROM #customers
WHERE active = 1;

SHOW VIEWS INTO #views;
DROP VIEW IF EXISTS ActiveCustomers;
```

Rules:
- Views are read-only and cannot be used as `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`, or `SELECT INTO` targets.
- Views are resolved in the engine, not created in a remote database. Use `EXECUTE <conn> BEGIN CREATE VIEW ... END` for native database views.
- CTEs and local statement sources can shadow view names inside a statement.
- Direct or indirect recursive view references fail at execution time.

---

## 13. Transactions

```sql
BEGIN TRANSACTION;   -- or BEGIN TRAN

-- ... operations ...

COMMIT TRANSACTION;  -- or COMMIT TRAN / COMMIT
ROLLBACK TRANSACTION; -- or ROLLBACK TRAN / ROLLBACK

IF @@TRANCOUNT > 0
    COMMIT;          -- or COMMIT TRAN

-- On failure in CATCH:
ROLLBACK;            -- or ROLLBACK TRAN
```

---

## 14. Expressions & Operators

### 14.1 Arithmetic Operators
`+` (Add), `-` (Subtract), `*` (Multiply), `/` (Divide), `%` (Modulo)

### 14.2 Logical Operators
`AND`, `OR`, `NOT`

### 14.3 Comparison Operators
`=`, `<>`, `!=`, `<`, `<=`, `>`, `>=`, `IN`, `LIKE`, `ILIKE`, `~`, `~*`, `BETWEEN`, `IS [NOT] NULL`, `IS [NOT] DISTINCT FROM`

### 14.4 Null-Coalescing Shorthand `??`
`a ?? b [?? c ...]` is ETL-SQL dialect shorthand that compiles to `COALESCE(a, b, c)` at parse time —
the engine, lineage tracking, and SQL pushdown all see a plain `COALESCE`, so scripts using `??` push
down to every connector unchanged. `CASE`/`COALESCE` remain the portable standard to teach; `??` is a
convenience.

Precedence: binds tighter than comparisons and looser than arithmetic —
`amount ?? 0 > 5` means `(amount ?? 0) > 5`, and `a + b ?? 0` means `(a + b) ?? 0`.

```sql
SELECT amount ?? 0 AS amount FROM #orders;
SELECT nickname ?? legal_name ?? '(unknown)' AS display_name FROM #people;
```

### 14.5 Arrow Conditional `=>`
`cond => value : else` is ETL-SQL dialect shorthand that compiles to
`CASE WHEN cond THEN value ELSE else END` at parse time. Chains flatten into **one** CASE with
multiple WHEN arms — evaluated top to bottom, exactly like CASE (short-circuit; universal SQL on
pushdown):

```sql
-- CASE WHEN score >= 90 THEN 'A' WHEN score >= 80 THEN 'B' ELSE 'F' END
SELECT score >= 90 => 'A' : score >= 80 => 'B' : 'F' AS grade FROM #tests;
```

Rules:
- The final `: else` branch is **required** — a dangling `cond => value` is a syntax error, never an
  implicit NULL.
- Lowest precedence (below `OR`): `a OR b => x : y` means `(a OR b) => x : y`.
- A `NULL`/UNKNOWN condition falls through to the next arm/else (standard CASE behavior).
- `CASE` remains the documented portable standard; `=>` is a convenience.

### 14.6 JSON Access Operators `->` / `->>`
PostgreSQL/MySQL/SQLite-style JSON access, compiled at parse time to the `JSON_GET` /
`JSON_GET_TEXT` functions:
- `json -> key` — object field (string key) or array element (integer index; negative counts from
  the end) **as JSON** — strings keep their quotes, so steps chain.
- `json ->> key` — the same access **as text** — strings unquoted; objects/arrays as raw JSON text.

Left-associative and binding tighter than arithmetic. Null-propagating: a missing key, out-of-range
index, or invalid JSON yields `NULL`, never an error.

```sql
SELECT doc -> 'customer' -> 'address' ->> 'city' AS city FROM #orders;
SELECT doc ->> 'qty' ?? '0' AS qty FROM #orders;   -- combines with ??
SELECT '[10,20,30]' ->> -1;                        -- '30' (negative index from the end)
```

#### `BETWEEN`
Checks if a value is within an inclusive range (equivalent to `val >= start AND val <= end`).

```sql
SELECT * FROM #audit WHERE event_date BETWEEN '2024-01-01' AND '2024-06-30';
SELECT * FROM #data WHERE id NOT BETWEEN @min AND @max;
```

#### `IS [NOT] DISTINCT FROM`
Null-safe comparison that treats `NULL` as an ordinary comparable value rather than producing `UNKNOWN`. Unlike `=`/`<>`, it never yields `NULL`.

- `a IS DISTINCT FROM b` — `TRUE` when the operands differ, **including** when exactly one is `NULL`; `FALSE` when they are equal or **both** `NULL`.
- `a IS NOT DISTINCT FROM b` — the logical negation: a null-safe equality (`NULL IS NOT DISTINCT FROM NULL` is `TRUE`).

```sql
-- Find rows whose value changed, counting NULL <-> value transitions as changes
SELECT id FROM #staging s
JOIN #target t ON s.id = t.id
WHERE s.value IS DISTINCT FROM t.value;

-- Null-safe equality (matches NULL rows, unlike `col = @p`)
SELECT * FROM #data WHERE notes IS NOT DISTINCT FROM @expected;
```

| `a` | `b` | `a IS DISTINCT FROM b` | `a IS NOT DISTINCT FROM b` |
| :-- | :-- | :--: | :--: |
| `1` | `1` | `FALSE` | `TRUE` |
| `1` | `2` | `TRUE` | `FALSE` |
| `1` | `NULL` | `TRUE` | `FALSE` |
| `NULL` | `NULL` | `FALSE` | `TRUE` |

### 14.7 Temporal Expressions

#### `AT TIME ZONE`
Converts a `DATETIME` or `DATETIMEOFFSET` expression to the target timezone. If the input has no offset, it is assumed to be **UTC**.

IANA and Windows timezone IDs are supported. Unknown IDs raise an execution error. See
[Dates, Times, and Time Zones](Dates_and_Times.md) for cross-platform aliases, DST behavior, and
connector storage rules.

```sql
SELECT OrderDate AT TIME ZONE 'Pacific Standard Time' AS local_time FROM #orders;

-- Using a variable for the timezone
DECLARE @tz = 'Eastern Standard Time';
SELECT SYSDATE AT TIME ZONE @tz;
```

**Common Timezone IDs (Windows):**
- `UTC`
- `Eastern Standard Time`
- `Central Standard Time`
- `Mountain Standard Time`
- `Pacific Standard Time`
- `Alaskan Standard Time`
- `Hawaiian Standard Time`
- `GMT Standard Time`
- `W. Europe Standard Time`
- `E. Europe Standard Time`
- `Tokyo Standard Time`
- `AUS Eastern Standard Time`

> [!NOTE]
> Timezone IDs are OS-dependent. On Windows, they follow the *Registry Time Zone* names. On Linux/macOS, the engine automatically attempts to map these to *IANA* names (e.g., `America/New_York`), but using the native OS names is recommended for maximum reliability.

---

## 15. Job Scheduling

### 15.1 `CREATE JOB` — Core Orchestrator
Registers a job with the Core Orchestrator service. Job names must be unquoted identifiers.

```sql
-- EVERY interval syntax
CREATE JOB CleanupJob ON SCHEDULE EVERY 30 MINUTES AS
    RUN SCRIPT 'scripts/cleanup.etlsql';

-- With MAX_RETRIES and RETRY_DELAY (WITH clause must precede the AS block)
CREATE JOB NightlyArchive 
    ON SCHEDULE EVERY 1 DAY AT '02:00'
    WITH (MAX_RETRIES = 3, RETRY_DELAY = 60)
AS
BEGIN
    INSERT INTO archive SELECT * FROM prod.logs WHERE log_date < DATEADD(DAY,-30,GETDATE());
    DELETE FROM prod.logs WHERE log_date < DATEADD(DAY,-30,GETDATE());
END;
```

### 15.1.1 Published Script Bundles

Published bundles store immutable script versions in the Orchestrator lockbox.

```sql
PUBLISH BUNDLE 'finance-load'
FROM 'C:\ETL\finance'
ENTRY 'main.etlsql'
WITH (PASSWORD = 'publish-password', ENCRYPT = MACHINE);

VALIDATE BUNDLE 'finance-load'
FROM 'C:\ETL\finance'
ENTRY 'main.etlsql';

RUN SCRIPT 'orch://finance-load@3/main.etlsql';
```

`PUBLISH BUNDLE` includes every `.etlsql` and `.rptsql` file under a directory source. For a single-file source, it includes the entry file and its literal relative `RUN SCRIPT` dependencies. Dynamic dependencies such as `RUN SCRIPT @path` fail at publish time and should use live file mode instead.

Unversioned `orch://finance-load/main.etlsql` resolves to the latest version for manual execution. Inside `CREATE JOB` or `ALTER JOB`, the engine resolves it once and stores the pinned version.

```sql
CREATE JOB NightlyFinance ON SCHEDULE EVERY 1 DAY AT '02:00' AS
    RUN SCRIPT 'orch://finance-load/main.etlsql';
-- Stored as orch://finance-load@<latest>/main.etlsql
```

Bundle inspection and recovery:

```sql
SHOW PUBLISHED BUNDLES;
SHOW BUNDLE VERSIONS 'finance-load';
SHOW BUNDLE FILES 'finance-load' VERSION 3;
SHOW BUNDLE DEPENDENCIES 'finance-load' VERSION 3;

EXPORT SCRIPT 'orch://finance-load@3/main.etlsql'
TO 'C:\Recovered\finance-load';
```

**Schedule intervals:** `SECOND`, `SECONDS`, `MINUTE`, `MINUTES`, `HOUR`, `HOURS`, `DAY`, `DAYS`. An optional time string (e.g. `'02:00'`) can follow the `AT` keyword for daily schedules.  
**Options (WITH clause):** `MAX_RETRIES` (default 0) and `RETRY_DELAY` or `RETRY_DELAY_SECONDS` (default 30 seconds). Both values must be integers.

> [!NOTE]
> Standard `CREATE JOB` statements do not support cron strings or the `AT <alias>` clause. Cron expressions are only supported in Portal Refresh Jobs (see §15.3).

### 15.2 Remote Orchestrator Job Creation
To create a job on a remote orchestrator, wrap the `CREATE JOB` statement inside an `EXECUTE <alias> BEGIN ... END` block. The alias must be a connection configured with `AS ORCHESTRATOR()`.

```sql
EXECUTE orch_conn BEGIN
    CREATE JOB NightlyArchive ON SCHEDULE EVERY 1 DAY AT '02:00' AS
        RUN SCRIPT '/scripts/nightly_archive.etlsql';
END;
```

### 15.3 Portal Refresh Jobs (Cron Scheduling)
Portal Refresh Jobs are distinct administrative tasks for updating report portal datasets and are defined inside `EXECUTE portal BEGIN ... END` blocks using standard cron strings:

```sql
EXECUTE portal_conn BEGIN
    CREATE REFRESH JOB FOR REPORT 'FinanceSales' SCHEDULE '0 2 * * *' AT orch_conn;
END;
```

### 15.4 Job Management

```sql
-- Drop a job (names must be unquoted identifiers; IF EXISTS is not supported)
DROP JOB CleanupJob;

-- Cancel a running job instance by its execution/history ID expression
KILL JOB 1023;
KILL JOB @historyId;

-- Query jobs and history
SHOW JOBS;
SHOW JOB HISTORY;
SHOW JOB HISTORY NightlyArchive;

-- Saved job-state key/value pairs (SET_JOB_STATE watermarks/markers): all jobs or one
SHOW JOB STATE;
SHOW JOB STATE 'NightlyArchive';

-- Host-utilization time series (capacity planning): all nodes or one node id
SHOW HOST METRICS;
SHOW HOST METRICS 'app-server-01:1234:ab...';

-- Direct output to a temporary table
SHOW JOBS INTO #jobs;
SHOW JOB HISTORY INTO #history;
SHOW JOB STATE INTO #job_state;
SHOW HOST METRICS INTO #host_metrics;
```

## 16. File Operations

All paths are validated against the active Safe Zones before any I/O occurs. See `SET ALLOW_FILE_TYPE_ACCESS` and related overrides in Â§2.4.

### 16.1 File Statements
```sql
COPY FILE    '<source>' TO '<destination>' [WITH (OVERWRITE = ON|OFF)];
MOVE FILE    '<source>' TO '<destination>' [WITH (OVERWRITE = ON|OFF)];
RENAME FILE  '<source>' TO '<new_name>'   [WITH (OVERWRITE = ON|OFF)];
DELETE FILE  '<path>';
DELETE FILE  '<path>' IF EXISTS;

-- Remote file operations (AT <connection>)
MOVE FILE    '<source>' TO '<destination>' AT <connection> [WITH (OVERWRITE = ON|OFF)];
RENAME FILE  '<source>' TO '<new_name>'   AT <connection> [WITH (OVERWRITE = ON|OFF)];
DELETE FILE  '<path>' AT <connection>;

-- Wildcard sources
COPY FILE 'C:\Incoming\*.csv' TO 'C:\Archive\';
```

### 16.2 File Encryption / Compression
```sql
COMPRESS FILE   '<source>' TO '<destination>' [WITH (OVERWRITE = ON|OFF)];
DECOMPRESS FILE '<source>' TO '<destination>' [WITH (OVERWRITE = ON|OFF)];
ENCRYPT FILE  '<source>' TO '<destination>' PASSWORD '<pwd>' [WITH (OVERWRITE = ON|OFF)];
DECRYPT FILE  '<source>' TO '<destination>' PASSWORD '<pwd>' [WITH (OVERWRITE = ON|OFF)];
```

### 16.3 Directory Statements
All path arguments (`<src>`, `<dest>`, `<path>`) can be either a literal string path or a **DIRECTORY connection** alias.

```sql
CREATE DIRECTORY '<path>' [IF NOT EXISTS];
CREATE DIRECTORY '<path>' AT <connection>;

COPY DIRECTORY   '<src>' TO '<dest>'     [WITH (OVERWRITE = ON|OFF)];
MOVE DIRECTORY   '<src>' TO '<dest>'     [WITH (OVERWRITE = ON|OFF)];
RENAME DIRECTORY '<src>' TO '<new_name>' [WITH (OVERWRITE = ON|OFF)];

DELETE DIRECTORY          '<path>' [IF EXISTS];
DELETE DIRECTORY          '<path>' AT <connection>;
DELETE DIRECTORY_CONTENTS '<path>' [WITH (RECURSIVE = ON|OFF)];

COMPRESS DIRECTORY   '<src>' TO '<dest.zip>' [WITH (OVERWRITE = ON|OFF)];
DECOMPRESS DIRECTORY '<src>' TO '<dest>'     [WITH (OVERWRITE = ON|OFF)];
ENCRYPT DIRECTORY  '<src>' TO '<dest>' PASSWORD '<pwd>' [WITH (OVERWRITE = ON|OFF, RECURSIVE = ON|OFF)];
DECRYPT DIRECTORY  '<src>' TO '<dest>' PASSWORD '<pwd>' [WITH (OVERWRITE = ON|OFF, RECURSIVE = ON|OFF)];
```

### 16.4 File Function Aliases
Underscore-style function aliases are available for backward compatibility:

```sql
COPY_FILE('src', 'dest' [, ON|OFF])
MOVE_FILE('src', 'dest' [, ON|OFF])
RENAME_FILE('src', 'new_name' [, ON|OFF])
DELETE_FILE('path')
COMPRESS_FILE('src', 'dest' [, ON|OFF])
DECOMPRESS_FILE('src', 'dest' [, ON|OFF])
ENCRYPT_FILE('src', 'dest', 'pwd' [, ON|OFF])
DECRYPT_FILE('src', 'dest', 'pwd' [, ON|OFF])
CREATE_DIRECTORY('path' [, ON|OFF])
DELETE_DIRECTORY('path')
DELETE_DIRECTORY_CONTENTS('path' [, RECURSIVE = ON|OFF])
```

### 16.5 Remote File Transfer
```sql
-- Upload to remote
SEND FILE '<local_path>' TO '<remote_path>' AT <connection> [WITH (OVERWRITE = ON|OFF)];

-- Download from remote
RECEIVE FILE FROM '<remote_path>' TO '<local_path>' AT <connection> [WITH (OVERWRITE = ON|OFF)];
```

### 16.6 Filesystem Query Functions

| Function | Returns |
| :--- | :--- |
| `FILE_EXISTS(path)` | `TRUE` if the file exists |
| `DIRECTORY_EXISTS(path)` | `TRUE` if the directory exists |
| `FILE_LIST(path [, pattern [, recursive]])` | Table: `Name`, `Path`, `Extension`, `Size`, `LastModified` |
| `REMOTE_FILE_LIST(conn, path)` | Table: `Name`, `FullPath`, `Size`, `LastModified`, `IsDirectory` |
| `REMOTE_FILE_EXISTS(conn, path)` | `TRUE` if the remote file or directory exists |

```sql
IF FILE_EXISTS('C:\Incoming\payload.csv')
    COPY FILE 'C:\Incoming\payload.csv' TO 'C:\Archive\';

-- Query files as a table
SELECT Name, Size, LastModified
INTO #new_files
FROM FILE_LIST('C:\Incoming\', '*.csv', TRUE)
WHERE LastModified >= DATEADD(HOUR, -24, GETDATE())
ORDER BY LastModified DESC;

-- Remote directory listing
SELECT Name, Size, LastModified
INTO #remote_files
FROM REMOTE_FILE_LIST(remote_sftp, '/var/ftp/incoming/')
WHERE LastModified >= DATEADD(HOUR, -24, GETDATE());
```

---

## 17. Email

> **Syntax note:** `SEND EMAIL` is the canonical form. `EMAIL SEND` is a supported alias. `AT <connection>` is the standard keyword for specifying the SMTP connection, consistent with `SEND FILE ... AT conn`, `RECEIVE FILE ... AT conn`, and `CREATE JOB ... AT orch`.

### 17.1 `SEND EMAIL`
```sql
SEND EMAIL
    TO      'recipient@company.com'
    FROM    'sender@company.com'          -- omit to use DEFAULT_FROM on the SMTP connection
    SUBJECT 'Pipeline Status'
    BODY    'The nightly load completed successfully.'
    [CC     'manager@company.com']
    [BCC    'audit@company.com']
    [ATTACH 'C:\Reports\summary.pdf']
    [ATTACH 'C:\Reports\detail.csv']
    AT mailer;
```

| Clause | Required | Notes |
| :--- | :---: | :--- |
| `TO` | Yes | One or more addresses |
| `FROM` | No | Defaults to `DEFAULT_FROM` on the SMTP connector |
| `SUBJECT` | Yes | |
| `BODY` | Yes | Plain text or HTML |
| `CC` | No | |
| `BCC` | No | |
| `ATTACH` | No | Local file path; repeatable |
| `AT` | No | Defaults to the last configured SMTP connection |

### 17.2 Example
```sql
CREATE CONNECTION mailer AS SMTP(
    HOST         = 'smtp.company.com',
    PORT         = 587,
    USER         = 'alerts@company.com',
    PASSWORD     = ENC:...,
    USE_SSL      = true,
    DEFAULT_FROM = 'alerts@company.com'
);

BEGIN TRY
    -- ... pipeline work ...
    SEND EMAIL
        TO      'ops@company.com'
        SUBJECT 'Nightly Load â€” SUCCESS'
        BODY    'All ' + @rowCount + ' rows loaded.'
        AT      mailer;
END TRY
BEGIN CATCH
    SEND EMAIL
        TO      'ops@company.com'
        SUBJECT 'Nightly Load â€” FAILED'
        BODY    'Error at step ' + @step + ': ' + ERROR_MESSAGE()
        ATTACH  'C:\Logs\nightly_error.log'
        AT      mailer;
    THROW;
END CATCH
```

---

## 18. Containerized Test Databases (`USE DOCKER`)

### 18.1 Spawning a Container
```sql
USE DOCKER('<image>') [AS <alias>];

USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest') AS mssql_db;
USE DOCKER('postgres:15-alpine')                         AS pg_db;
USE DOCKER('gvenzl/oracle-free:latest')                  AS ora_db;
```

After startup the connection string is available via the alias:
```sql
DECLARE @conn VARCHAR(500) = mssql_db.CONNECTION_STRING;
CREATE CONNECTION stage_db AS MSSQL(@conn);
```

### 18.2 Supported Images

| Database | Image pattern | Default credentials | Port |
| :--- | :--- | :--- | :--- |
| SQL Server | contains `mssql` | `sa` / `Password123!` | 1433 |
| PostgreSQL | contains `postgres` | `postgres` / `postgres` | 5432 |
| Oracle | contains `oracle` | `system` / `oracle` | 1521 |

### 18.3 Lifecycle Commands

| Command | Effect |
| :--- | :--- |
| `START DOCKER <alias>` | Resume a stopped container |
| `STOP DOCKER <alias>` | Stop the container (state preserved) |
| `PAUSE DOCKER <alias>` | Suspend CPU (faster resume than stop/start) |
| `CLOSE DOCKER <alias>` | Destroy container and all its state |
| `CLOSE_DOCKER` | Destroy **all** containers in the session |

Function-style aliases: `START_DOCKER`, `STOP_DOCKER`, `CLOSE_DOCKER`.

> Containers are **not** automatically closed when a script ends. Always include an explicit `CLOSE_DOCKER` or wrap in `TRY...CATCH`.

### 18.4 Multiple Containers
```sql
USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest') AS src;
USE DOCKER('postgres:15-alpine')                         AS dst;

CREATE CONNECTION source_db AS MSSQL(src.CONNECTION_STRING);
CREATE CONNECTION target_db AS POSTGRES(dst.CONNECTION_STRING);

SELECT * INTO #tmp FROM source_db.dbo.Customers;
INSERT INTO target_db.public.customers SELECT * FROM #tmp;

CLOSE_DOCKER;
```

---

## 19. Introspection & Diagnostics

### 19.1 Metadata
```sql
SHOW CONNECTIONS             [INTO #temp];
SHOW CONNECTION <name> CONFIG [INTO #temp];
SHOW TABLES [ON <conn>]      [INTO #temp];
SHOW COLUMNS FOR <table_ref> [INTO #temp];
SHOW VERSION                 [INTO #temp];
SHOW SAFE ZONES              [INTO #temp];
```

### 19.2 Session
```sql
SHOW VARIABLES                 [INTO #temp];
SHOW LOCAL VARIABLES           [INTO #temp];
SHOW SESSIONS                  [INTO #temp];
SHOW LOCKS                     [INTO #temp];
SHOW PROFILE                   [INTO #temp];
SHOW LINEAGE [FOR <table_ref> [COLUMN <column>]] [INTO #temp];
SHOW LINEAGE FOR REPORT <report_name> [INTO #temp];
SHOW LINEAGE FOR DATASET &<dataset_name> [INTO #temp];
SHOW LINEAGE HISTORY FOR TABLE <table_name> [AT <connection>] [LIMIT <n>] [INTO #temp];
SHOW LINEAGE HISTORY FOR TAG <tag_key> [= '<tag_value>'] [AT <connection>] [LIMIT <n>] [INTO #temp];

-- Import lineage from an OpenLineage document (file path or inline JSON; <table> is expr/@var)
CREATE LINEAGE FOR TABLE <table> FROM <openlineage_source>;

-- Metadata Tags
SHOW TAGS FOR SCRIPT                         [INTO #temp];
SHOW TAGS FOR TABLE <table> [COLUMN <col>]    [INTO #temp];
SHOW TAG VALUE FOR TABLE <table> [COLUMN <col>] WITH TAG <name> [INTO #temp];

-- Seed table-/column-level tags explicitly (<table>/<col> are exprs and may be @variables)
CREATE TAG FOR TABLE <table> [COLUMN <col>] (<tag> = <expr> [, <tag> = <expr> ...]);
```

### 19.3 Jobs
```sql
SHOW JOBS          [INTO #temp];
SHOW ACTIVE JOBS   [INTO #temp];
SHOW JOB HISTORY [<jobName>]  [INTO #temp];
SHOW JOB STATE   [<jobName>]  [INTO #temp];
KILL JOB <HistoryId>;
```

### 19.4 Analysis

#### EXPLAIN
Shows the query execution plan without running the query. Returns a table with the execution operations, estimated costs, and execution mode.

```sql
EXPLAIN SELECT o.OrderId, c.Name
FROM conn.Orders o
INNER JOIN conn.Customers c ON o.CustomerId = c.Id
WHERE o.Status = 'Open'
ORDER BY o.OrderDate DESC;
```

Output columns: `ID`, `Operation`, `Details`, `Cost`, `Mode`, `Est. Rows`.

#### EXPLAIN ANALYZE
Executes the query and returns the plan annotated with actual row counts and elapsed time per step. Use this to diagnose slow queries or verify that plans match expectations.

```sql
EXPLAIN ANALYZE SELECT Region, SUM(Revenue) AS TotalRevenue
FROM #Sales
GROUP BY Region
ORDER BY TotalRevenue DESC;
```

Output columns: `ID`, `Operation`, `Details`, `Cost`, `Mode`, `Est. Rows`, `Actual Rows`, `Actual Time (ms)`, `Spill Bytes`, `Spill Count`.

> [!NOTE]
> `EXPLAIN ANALYZE` executes the full query including any side effects. Use `EXPLAIN` (without `ANALYZE`) during script development to inspect plans without touching data.

#### LINT
```sql
LINT 'scripts/nightly_load.etlsql';
```

### 19.5 Help
```sql
HELP CONNECTION MSSQL;       -- connector-specific options
HELP CONNECTION POSTGRES;
HELP VARIABLES;              -- list all @@ system variables
HELP FUNCTIONS;              -- list all built-in functions
HELP TARGET <name>;          -- documentation for a specific statement or keyword
HELP TYPE <name>;            -- documentation for a data type (e.g. HELP TYPE MINMAX)
```

### 19.6 Script Metadata Header
```sql
/*
   @author:      Chuck
@version:     0.7.0
   @description: Nightly cleanup of staging tables
*/
```

Supported tags: `@author`, `@version`, `@description`, or any custom `@key: value` pair. `@author` defaults to the current system user if omitted.

---

## Appendix A: Report-SQL Grammar (`.rptsql` files)

`.rptsql` files are standard ETL-SQL scripts with the following additional statement types. For the full user guide see `Docs/Report_SQL_Guide.md`.

Report-SQL uses these canonical object buckets:

| Bucket | Syntax role |
| :--- | :--- |
| `SOURCE` | Data-producing query, table, or dataset reference. |
| `MAPPINGS` | Visual data roles. |
| `LAYOUT` | Placement, structure, maps, gaps, responsive behavior, and pinning. |
| `STYLE` | Presentation/theme choices. |
| `OPTIONS` | Renderer-specific settings and non-layout object state. |
| `ACTIONS` | Events emitted by visuals, controls, and buttons. |
| `INTERACTIONS` | Cross-visual selection/filter/highlight behavior. |
| Portal commands | Administrative DDL/operations such as users, folders, grants, publishing, subscriptions, and refresh jobs. |

### A.1 `SET REPORT`
```sql
SET REPORT TITLE       = 'Monthly Sales Dashboard';
SET REPORT DESCRIPTION = 'Revenue by region and product line.';
```

### A.2 `CREATE DATASET`
```sql
CREATE DATASET &<name>
  [REFRESH EVERY '<interval>']
  [TTL = '<duration>']
  [COMPRESS = ON|OFF]
  [ENCRYPT = MACHINE | PASSWORD | KEYFILE]
  [PASSWORD = '<password>']
  [KEYFILE  = '<path>']
AS ( SELECT ... );
```

Interval format: `<n>s`, `<n>m`, `<n>h`, `<n>d`.

#### A.2.1 `EXPORT DATASET`

Produces a portable encrypted copy of a materialized portal dataset. The portal decrypts its managed
at-rest cache and encrypts the export with the supplied transport credential. The credential is used
only for this operation and is never stored in dataset metadata, generated scripts, or scheduled jobs.

```sql
EXPORT DATASET &<name>
  TO '<absolute-output-path>'
  ENCRYPT = PASSWORD
  PASSWORD = '<transport-password>';

EXPORT DATASET &<name>
  TO '<absolute-output-path>'
  ENCRYPT = KEYFILE
  KEYFILE = '<absolute-key-file-path>';
```

`&<name>`, `TO`, one supported `ENCRYPT` mode, and its matching credential are required. The caller must
be able to read the dataset. Output is written to a staging file and atomically committed; a failed export
does not replace an existing destination.

#### A.2.2 `PUBLISH DATASET`

Imports a portable dataset export into a portal. The source is decrypted once with its transport
credential and re-encrypted with the destination portal's managed at-rest key.

```sql
PUBLISH DATASET
  FROM '<absolute-export-path>'
  AS &<globally-unique-name>
  INTO '<portal-folder-path>'
  [ACCESS PUBLIC | PRIVATE]
  ENCRYPT = PASSWORD
  PASSWORD = '<transport-password>';

PUBLISH DATASET
  FROM '<absolute-export-path>'
  AS &<globally-unique-name>
  INTO '<portal-folder-path>'
  [ACCESS PUBLIC | PRIVATE]
  ENCRYPT = KEYFILE
  KEYFILE = '<absolute-key-file-path>';
```

`ACCESS` defaults to `PRIVATE`. The destination folder must exist and the caller must have `Manage` on
it. The name must be globally unique. A failed publish removes its allocated catalog row and partial
files, so the same name can be retried. The published portal copy is not transport-portable; retain the
original export if it may need to move again.

### A.3 `CREATE VISUAL`
```sql
CREATE VISUAL <name> AS <type> (
  [SOURCE     = &dataset | #table | ( SELECT ... ),]
  [TITLE      = '<string>',]
  [SUBTITLE   = '<string>',]
  [TOOLTIP    = '<string>',]
  [VISIBLE    = ON|OFF,]
  [FETCH      = AUTO|ON_LOAD|ON_RUN,]
  [MAPPINGS   ( role = column [, ...] ),]
  [OPTIONS    ( key = value [, ...]
                [, X_AXIS (...)] [, Y_AXIS (...)]
                [, COLORS ( key = '#hex' [, ...] )]
                [, LEGEND ( position = top|bottom|left|right )] ),]
  [INTERACTIONS ( ON_SELECT = FILTER|HIGHLIGHT|NONE
                  [, MATCHING = <column> ] ),]
  [STYLE      ( key = value [, ...] ),]
  [SERIES     ( BAR|LINE column [, ...] ),]
  [FORMATTING ( column op threshold THEN '<color>' [, ...] ),]
  [OVERLAYS   ( <VisualName> [, ...] ),]
  [ACTIONS    ( trigger = action [, ...] )]
);
```

**Visual types:** `BAR`, `HBAR`, `LINE`, `SCATTER`, `BUBBLE`, `PIE`, `DONUT`, `COMBO`, `BOXPLOT`, `TREEMAP`, `HEATMAP`, `GAUGE`, `FUNNEL`, `WATERFALL`, `RADAR`, `CANDLESTICK`, `GANTT`, `SANKEY`, `SUNBURST`, `NETWORK`, `TRELLIS`, `MATRIX`, `MAP`, `TABLE`, `CARD`, `TEXT`, `IMAGE`, `SLICER`, `DATEPICKER`, `RELDATEPICKER`, `SLIDER`, `MULTISELECT`, `SEARCH`, `CHECKBOX`, `TEXTBOX`, `NUMBERBOX`

`VISIBLE` controls UI visibility only. `FETCH` controls source evaluation timing: `AUTO` follows the containing page mode, `ON_LOAD` always loads during initial build, and `ON_RUN` waits for `APPLY_PARAMETERS`.

**Mapping roles by visual type:**

| Type | Required roles | Optional roles |
|------|---------------|----------------|
| `BAR`, `HBAR`, `LINE` | `X`, `Y` | `SERIES` |
| `SCATTER` | `X`, `Y` | `COLOR`, `LABEL` |
| `BUBBLE` | `X`, `Y` | `SIZE`, `LABEL` |
| `PIE`, `DONUT`, `FUNNEL` | `LABEL`, `VALUE` | â€” |
| `COMBO` | `X` | _(declare each series in `SERIES(BAR col, LINE col)`)_ |
| `BOXPLOT` | `X`, `LOW`, `Q1`, `MEDIAN`, `Q3`, `HIGH` | â€” |
| `TREEMAP` | `LABEL`, `VALUE` | `PARENT` |
| `HEATMAP` | `X`, `Y`, `VALUE` | â€” |
| `GAUGE` | `VALUE` | `LABEL` |
| `WATERFALL` | `X`, `Y` | â€” |
| `RADAR` | _(none â€” first column = series name, remaining columns = metric axes)_ | â€” |
| `CANDLESTICK` | `X`, `OPEN`, `HIGH`, `LOW`, `CLOSE` | â€” |
| `GANTT` | `Y` (task label), `START`, `END` | `COLOR` |
| `SANKEY` | `SOURCE` (or `FROM`), `TARGET` (or `TO`), `VALUE` | â€” |
| `SUNBURST` (level mode) | `LEVEL1`, `VALUE` | `LEVEL2`, `LEVEL3` |
| `SUNBURST` (parent-child mode) | `LABEL` (or `NAME`), `PARENT`, `VALUE` | â€” |
| `NETWORK` | `FROM`, `TO` | `VALUE`, `NODE_GROUP` |
| `TRELLIS` | `X`, `Y`, `FACET` | â€” |
| `MATRIX` | `ROW` (or `ROW1`), `COL` (or `COL1`), `VALUE` | `ROW2`, `ROW3`, `COL2`, `COL3` |
| `MAP` (choropleth) | `REGION` | `VALUE` |
| `MAP` (points â€” `MODE=POINTS`) | `LON`, `LAT` | `VALUE`, `LABEL` |
| `TABLE` | _(all source columns rendered automatically)_ | â€” |
| `CARD` | `VALUE` | `LABEL`, `GOAL`, `DELTA` |
| `SLICER`, `MULTISELECT` | `VALUE` | â€” |
| `TEXT`, `IMAGE`, `DATEPICKER`, `RELDATEPICKER`, `SLIDER`, `SEARCH`, `CHECKBOX`, `TEXTBOX`, `NUMBERBOX` | _(no mappings)_ | â€” |

**FORMATTING operators:** `<`, `>`, `<=`, `>=`, `=`, `<>`

**Action forms:**
```
ON_CLICK  = DRILL_DOWN(Target = <VisualName>, Key = <column>)
ON_CLICK  = DRILL_DOWN(Target = <VisualName>, Key = (<col1>, <col2>))
ON_CLICK  = DRILL_IN(HIERARCHY = (<col1>, <col2>, ...))
ON_CHANGE = SET_PARAMETER(@paramName, <columnRef>)
ON_CLICK  = RUN_SCRIPT('<path>', @param = <columnRef> [, ...])
ON_CLICK  = CLEAR_FILTERS
ON_CLICK  = APPLY_PARAMETERS
ON_CLICK  = NAVIGATE_PAGE(<PageName>)
ON_CLICK  = SET_UI_STATE(<Target>, <Key>, <Value>)
```

**Valid triggers by object type:**

| Object type | Valid trigger |
| :--- | :--- |
| Charts and tables | `ON_CLICK` |
| Controls (`SLICER`, `MULTISELECT`, `DATEPICKER`, `RELDATEPICKER`, `SLIDER`, `SEARCH`, `CHECKBOX`, `TEXTBOX`, `NUMBERBOX`) | `ON_CHANGE` |
| Buttons | `ON_CLICK` |
| `TEXT`, `CARD`, `IMAGE` | none |

Invalid trigger/object combinations are syntax errors.

#### A.3.1 Filter Visuals (`SLICER`, `DATEPICKER`, etc.)
Filter visuals differ from charts in that they typically bind to a parameter via `ON_CHANGE`.

```sql
CREATE VISUAL RegionFilter AS SLICER (
  SOURCE  = (SELECT DISTINCT region FROM #summary),
  MAPPINGS (VALUE = region),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@region, region))
);

CREATE VISUAL DateFilter AS DATEPICKER (
  OPTIONS (TYPE = 'RANGE', FORMAT = 'yyyy-MM-dd'),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@date_range, value))
);

CREATE VISUAL StartPicker AS RELDATEPICKER (
  OPTIONS (DEFAULT = 'M-1'),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@start_date, value))
);

CREATE VISUAL IsActive AS CHECKBOX (
  LABEL_POSITION = 'LEFT',
  ACTIONS (ON_CHANGE = SET_PARAMETER(@active_only, value))
);

CREATE VISUAL MinValue AS NUMBERBOX (
  MIN = 0, MAX = 1000, DECIMALS = 2,
  ACTIONS (ON_CHANGE = SET_PARAMETER(@min_val, value))
);

CREATE VISUAL SearchQuery AS TEXTBOX (
  OPTIONS (PLACEHOLDER = 'Enter query...'),
  ACTIONS (ON_CHANGE = SET_PARAMETER(@query, value))
);
```

#### A.3.2 Visual-Specific Properties
Input visuals (`CHECKBOX`, `TEXTBOX`, `NUMBERBOX`) and some filter types support additional top-level properties for layout and validation.

| Property | Applies to | Values | Description |
| :--- | :--- | :--- | :--- |
| `LABEL_POSITION` | All input types | `TOP`, `LEFT`, `HIDDEN` | Position of the visual name label. |
| `MIN` | `NUMBERBOX`, `SLIDER` | Numeric | Minimum allowed value. |
| `MAX` | `NUMBERBOX`, `SLIDER` | Numeric | Maximum allowed value. |
| `DECIMALS` | `NUMBERBOX` | Integer | Number of decimal places allowed. |
```

### A.4 `CREATE PAGE`
```sql
CREATE PAGE <name> AS DASHBOARD|PAGINATED (
  [TITLE = '<string>',]
  [SUBTITLE = '<string>',]
  [TOOLTIP = '<string>' | <ContainerName>,]
  [VISIBLE = ON|OFF,]
  [REFRESH = <seconds>,]
  [
    LAYOUT (
      STRUCTURE = '<css-grid-template-areas>',
      MAP ('<slot>' = <VisualOrContainerName> [, ...] )
      [, GAP = '<css-size>']
      [, <layout_key> = <value> ...]
    )
  |
    STRUCTURE = '<css-grid-template-areas>',
    MAP ('<slot>' = <VisualOrContainerName> [, ...] )
    [, GAP = '<css-size>']
  ]
  [, STYLE ( key = value [, ...] )]
)
;
```

`STRUCTURE` uses CSS grid-template-areas: space-separated slot letters per row, rows separated by `/`. Example: `'A A / B C'`. `DASHBOARD` pages load result visuals immediately. `PAGINATED` pages stage prompt changes and load `AUTO` result visuals when `APPLY_PARAMETERS` runs. `REFRESH` is a page-body option; trailing `WITH (REFRESH = ...)` is not supported.

### A.5 `CREATE CONTAINER`
```sql
CREATE CONTAINER <name> AS BOX|SCROLL|DRAWER|SIDEBAR|TABS|ACCORDION|MODAL|POPOVER|LAYER (
  [TITLE = '<string>',]
  [SUBTITLE = '<string>',]
  [TOOLTIP = '<string>' | <ContainerName>,]
  [VISIBLE = ON|OFF,]
  [ICON = '<name>',]
  [STYLE = <styleName> | STYLE ( key = value [, ...] ),]
  LAYOUT (
    STRUCTURE = '<css-grid-template-areas>',
    MAP ('<slot>' = <VisualOrContainerName> [, ...] )
    [, GAP = '<css-size>']
    [, PINNABLE = ON|OFF]
    [, <layout_key> = <value> ...]
  ),
  [OPTIONS ( key = value [, ...] )]
);
```

`LAYER` stacks mapped children in the same region in map order. Use `STYLE (Z_INDEX = n)` on visuals/containers when an explicit stack order is required.

### A.6 `CREATE NAVIGATION`
```sql
CREATE NAVIGATION <name> AS TAB|BUTTON|LINK (
  [ORIENTATION = HORIZONTAL|VERTICAL,]
  [DEFAULT = <PageName>,]
  [PAGES ( <PageName> [, ...] )]
);
```

### A.7 `CREATE STYLE`
```sql
CREATE STYLE <name> (
  BACKGROUND-COLOR = '#1e1e2e',
  COLOR            = '#cdd6f4',
  FONT-SIZE        = '14px',
  FONT-WEIGHT      = 'bold',
  BORDER           = '1px solid #ccc',
  BORDER-RADIUS    = '8px',
  PADDING          = '12px',
  MARGIN           = '4px',
  HEIGHT           = '300px',
  WIDTH            = '100%',
  THEME            = light | dark
);
```

### A.8 `CREATE BUTTON`
```sql
CREATE BUTTON <name> AS (
  [TITLE   = '<string>',]
  [TOOLTIP = '<string>' | <ContainerName>,]
  [OPTIONS ( key = value [, ...] ),]
  [STYLE = <styleName> | STYLE ( key = value [, ...] ),]
  ACTIONS ( trigger = action [, ...] )
);
```

Common button actions include `BACK`, `REFRESH_REPORT`, `REFRESH_VISUALS(VisualName [, ...])`, `EXPORT_CSV`, `EXPORT_EXCEL`, `EXPORT_PDF`, `NAVIGATE_PAGE(PageName)`, `CLEAR_FILTERS`, `APPLY_PARAMETERS`, and `SET_UI_STATE(Target, Key, Value)`.

### A.9 `EXPORT REPORT`
Exports a Report-SQL script to a static artifact.

```sql
EXPORT REPORT 'reports/sales.rptsql'
FORMAT PDF
TO 'out/sales.pdf';

EXPORT REPORT 'reports/sales.rptsql'
FORMAT PDF
TO 'out/sales.pdf'
WITH (
    PDF_MODE     = STATIC,   -- STATIC | AUTO | HOSTED | BROWSER
    HOST         = 'http://localhost:5200',
    BROWSER_PATH = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
);

EXPORT REPORT 'reports/sales.rptsql'
FORMAT CSV
TO 'out/sales.csv';

EXPORT REPORT 'reports/sales.rptsql'
FORMAT MARKDOWN
TO 'out/sales.md';
```

`WITH (...)` options are valid only with `FORMAT PDF`.

| Option | Values | Meaning |
| :--- | :--- | :--- |
| `PDF_MODE` | `STATIC`, `AUTO`, `HOSTED`, `BROWSER` | Selects the PDF export renderer. Default is `STATIC`. |
| `HOST` | string | ReportPortal or `report serve` URL for hosted browser-backed export. |
| `BROWSER_PATH` | string | Installed Chrome, Edge, or Chromium executable path for optional browser export. |

`STATIC` uses the built-in PDFsharp/MigraDoc exporter and requires no browser. Explicit `HOSTED` and `BROWSER` modes require a `HOST` URL and a discoverable or configured installed Chrome, Edge, or Chromium executable. `AUTO` may fall back to `STATIC`.

### A.10 `ALTER` / `DROP` / `CREATE OR ALTER`
All report object types support these forms:

```sql
ALTER VISUAL    <name> ( <clause> [, ...] );
ALTER PAGE      <name> ( <clause> [, ...] );
ALTER CONTAINER <name> ( <clause> [, ...] );
ALTER BUTTON    <name> ( <clause> [, ...] );
ALTER STYLE     <name> ( <clause> [, ...] );
ALTER NAVIGATION <name> ( <clause> [, ...] );
ALTER DATASET   <name> ( <clause> [, ...] );

DROP VISUAL        [IF EXISTS] <name>;
DROP PAGE          [IF EXISTS] <name>;
DROP CONTAINER     [IF EXISTS] <name>;
DROP BUTTON        [IF EXISTS] <name>;
DROP STYLE         [IF EXISTS] <name>;
DROP NAVIGATION    [IF EXISTS] <name>;
DROP DATASET       [IF EXISTS] <name>;

CREATE OR ALTER VISUAL     <name> AS <type> ( ... );
CREATE OR ALTER PAGE       <name> AS DASHBOARD|PAGINATED ( ... );
CREATE OR ALTER DATASET    &<name> ... AS ( SELECT ... );
CREATE OR ALTER STYLE      <name> ( ... );
CREATE OR ALTER BUTTON     <name> AS ( ... );
CREATE OR ALTER CONTAINER  <name> AS BOX|SCROLL|DRAWER|SIDEBAR|TABS|ACCORDION|MODAL|POPOVER ( ... );
CREATE OR ALTER NAVIGATION <name> AS TAB|BUTTON|LINK ( ... );
```

---

## Appendix B: Report Portal Admin Language

Portal admin statements execute inside an `EXECUTE portal BEGIN...END` block. The `portal` alias must be a connection created with `AS REPORTPORTAL(...)`.

Portal catalog names, user names, group names, recipients, report names, and paths are string literals. Local aliases such as `portal`, `orch`, and `smtp` are identifiers. Secret-bearing fields such as `PASSWORD` remain expression positions so `ENC:` values and variables are accepted.

```sql
CREATE CONNECTION portal AS REPORTPORTAL(
    HOST = 'report-server.company.com',
    PORT = 5000,
    USER = 'admin',
    PASSWORD = ENC:...
);

EXECUTE portal BEGIN

    -- =========================================================
    -- USERS
    -- =========================================================
    CREATE USER 'john.doe'
        WITH (EMAIL = 'john@company.com', PASSWORD = ENC:..., ROLE = Viewer);

    ALTER USER 'john.doe' SET EMAIL       = 'john.doe@newdomain.com';
    ALTER USER 'john.doe' SET ROLE        = Publisher;
    ALTER USER 'john.doe' SET PASSWORD    = ENC:...;
    ALTER USER 'john.doe' SET ENABLE;
    ALTER USER 'john.doe' SET DISABLE;

    DROP USER 'john.doe';
    DROP USER 'john.doe' CASCADE;   -- also removes subscriptions, sessions, group memberships

    SHOW USERS         [INTO #users];
    SHOW USER 'john.doe'  [INTO #detail];

    -- =========================================================
    -- GROUPS
    -- =========================================================
    CREATE GROUP 'Finance' WITH (DESCRIPTION = 'Finance department');
    ALTER GROUP  'Finance' SET DESCRIPTION = 'Finance and Accounting';
    DROP GROUP   'Finance';
    DROP GROUP   'Finance' CASCADE;   -- removes memberships and ACL entries; users remain

    ADD USER    'john.doe' TO GROUP   'Finance';
    REMOVE USER 'john.doe' FROM GROUP 'Finance';

    SHOW GROUPS                    [INTO #groups];
    SHOW GROUP 'Finance'           [INTO #detail];   -- includes member list
    SHOW GROUPS FOR USER 'john.doe' [INTO #groups];

    -- =========================================================
    -- FOLDERS
    -- Folders are catalog containers â€” distinct from filesystem directories.
    -- =========================================================
    CREATE FOLDER '/Finance';
    CREATE FOLDER '/Finance/Monthly';

    ALTER FOLDER '/Finance/Monthly' SET NAME   = 'Monthly Reports';
    ALTER FOLDER '/Finance/Monthly' SET PARENT = '/Shared';

    DROP FOLDER '/Finance/Monthly Reports';
    DROP FOLDER '/Finance' CASCADE;   -- removes all child folders and their reports

    SHOW FOLDERS                    [INTO #folders];   -- full tree visible to caller
    SHOW FOLDERS UNDER '/Finance'   [INTO #folders];   -- subtree only
    SHOW FOLDER  '/Finance'         [INTO #detail];    -- detail + ACL entries + report count

    -- =========================================================
    -- PERMISSIONS
    -- Permissions are granted to groups, not individual users.
    -- Levels: READ < EXECUTE < MANAGE
    -- =========================================================
    GRANT READ    ON FOLDER '/Finance'        TO GROUP 'Finance';
    GRANT EXECUTE ON FOLDER '/Finance'        TO GROUP 'FinanceAnalysts';
    GRANT MANAGE  ON FOLDER '/Finance'        TO GROUP 'FinanceAdmins';
    REVOKE READ   ON FOLDER '/Finance'        FROM GROUP 'Finance';

    SHOW PERMISSIONS ON FOLDER '/Finance'     [INTO #perms];
    SHOW PERMISSIONS FOR GROUP 'Finance'      [INTO #perms];
    SHOW EFFECTIVE PERMISSIONS FOR USER 'john.doe' [INTO #effective];
    SHOW EFFECTIVE PERMISSIONS FOR REPORT 'Monthly Sales' [INTO #effective];
    SHOW EFFECTIVE PERMISSIONS FOR FOLDER '/Finance' [INTO #effective];

    -- =========================================================
    -- REPORT CATALOG
    -- PUBLISH points the portal at an existing .rptsql file.
    -- =========================================================
    PUBLISH REPORT 'Monthly Sales'
        FROM '/reports/finance/monthly_sales.rptsql'
        IN FOLDER '/Finance'
        WITH (DESCRIPTION = 'Monthly revenue by region');

    -- Environment promotion uses normal SETS, then normal portal commands.
    -- Dev/test/prod should not use a parallel deployment grammar.
    CREATE SETS !PROD
    BEGIN
        @PortalEnvironment = 'PROD';
        SET WITH_PROMPT ON;
    END
    USE SETS !PROD;
    IF @PortalEnvironment = 'PROD'
    BEGIN
        PUBLISH REPORT 'Monthly Sales'
            FROM 'C:\Reports\Prod\monthly_sales.rptsql'
            IN FOLDER '/Finance'
            WITH (TAGS = 'finance,monthly,certified');
    END

    ALTER REPORT 'Monthly Sales' SET FOLDER      = '/Finance/Archive';
    ALTER REPORT 'Monthly Sales' SET DESCRIPTION = 'Archived monthly revenue report';
    ALTER REPORT 'Monthly Sales' SET NAME        = 'Monthly Sales (Archive)';

    DROP REPORT 'Monthly Sales';
    DROP REPORT 'Monthly Sales' CASCADE;   -- also removes snapshots and subscriptions

    VALIDATE REPORT SCRIPT '/reports/finance/monthly_sales.rptsql' [INTO #validation];

    FAVORITE REPORT 'Monthly Sales';
    FAVORITE REPORT 'Monthly Sales' FOR USER 'john.doe';
    UNFAVORITE REPORT 'Monthly Sales';
    UNFAVORITE REPORT 'Monthly Sales' FOR USER 'john.doe';

    CREATE SHARE LINK FOR REPORT 'Monthly Sales' [EXPIRES '2026-12-31T23:59:59Z'] [INTO #share];
    SHOW SHARE LINKS FOR REPORT 'Monthly Sales' [INTO #shares];
    REVOKE SHARE LINK 'share-token';

    CREATE EMBED TOKEN FOR REPORT 'Monthly Sales' [NAME 'Intranet'] [EXPIRES '2026-12-31T23:59:59Z'] [INTO #embed];
    SHOW EMBED TOKENS FOR REPORT 'Monthly Sales' [INTO #embed];
    REVOKE EMBED TOKEN 'embed-token';

    CREATE SAVED VIEW 'West Coast' FOR REPORT 'Monthly Sales'
        [DEFAULT]
        [PARAMETERS (@region = 'West', @year = '2026')]
        [INTO #view];
    SHOW SAVED VIEWS FOR REPORT 'Monthly Sales' [INTO #views];
    DROP SAVED VIEW 'West Coast' FOR REPORT 'Monthly Sales';

    CREATE ALERT 'Revenue Floor' FOR REPORT 'Monthly Sales'
        WHEN VISUAL 'Revenue' >= 1000
        [DELIVER TO 'ops@example.com']
        [AT smtp]
        [ENABLE | DISABLE];
    SHOW ALERTS FOR REPORT 'Monthly Sales' [INTO #alerts];
    DROP ALERT 'Revenue Floor' FOR REPORT 'Monthly Sales';

    SHOW REPORTS                         [INTO #reports];
    SHOW REPORTS IN FOLDER '/Finance'    [INTO #reports];
    SHOW REPORT  'Monthly Sales'         [INTO #detail];
    SHOW REPORT HISTORY 'Monthly Sales'  [INTO #history];
    SHOW REPORT DEPENDENCIES 'Monthly Sales' [INTO #deps];
    SHOW FAVORITES                       [INTO #favorites];
    SHOW FAVORITES FOR USER 'john.doe' LIMIT 50 [INTO #favorites];
    SHOW RECENT REPORTS LIMIT 20         [INTO #recent];
    SHOW CATALOG SEARCH 'sales' LIMIT 25 [INTO #catalog];

    -- =========================================================
    -- SNAPSHOTS
    -- =========================================================
    REFRESH REPORT 'Monthly Sales';                    -- queue a report refresh
    DROP SNAPSHOT FOR REPORT 'Monthly Sales';        -- force rebuild on next view
    REBUILD SNAPSHOT FOR REPORT 'Monthly Sales';     -- rebuild now in background

    SHOW SNAPSHOTS                       [INTO #snaps];

    -- =========================================================
    -- DATASET REGISTRY
    -- Portal dataset commands operate on catalog names and folders.
    -- Report-local datasets continue to use &dataset names.
    -- =========================================================
    REFRESH DATASET 'Sales Summary' IN FOLDER '/Finance';

    ALTER DATASET 'Sales Summary' IN FOLDER '/Finance'
        SET ACCESS = PUBLIC, TTL = '2h';

    GRANT VIEWER ON DATASET 'Sales Summary' IN FOLDER '/Finance' TO GROUP 'Finance';
    GRANT REFRESH ON DATASET 'Sales Summary' IN FOLDER '/Finance' TO GROUP 'DataOperations';
    GRANT EDITOR ON DATASET 'Sales Summary' IN FOLDER '/Finance' TO GROUP 'FinanceAnalysts';
    GRANT OWNER  ON DATASET 'Sales Summary' IN FOLDER '/Finance' TO GROUP 'FinanceAdmins';
    REVOKE EDITOR ON DATASET 'Sales Summary' IN FOLDER '/Finance' FROM GROUP 'FinanceAnalysts';

    DROP DATASET 'Sales Summary' IN FOLDER '/Finance';

    -- =========================================================
    -- SMTP CONNECTIONS
    -- Portal-managed mail credentials referenced by subscriptions
    -- and alerts via AT <alias>. The password is sent once over the
    -- authenticated channel and stored encrypted; SHOW never returns it.
    -- =========================================================
    CREATE SMTP CONNECTION 'corporate' WITH (
        HOST         = 'smtp.corp.local',     -- required
        PORT         = 587,                   -- optional, default 587
        USERNAME     = 'mailer',              -- optional
        PASSWORD     = ENC:...,               -- optional; expression (ENC:/variables OK)
        FROM_ADDRESS = 'reports@corp.local',  -- optional
        USE_SSL      = TRUE                   -- optional, default TRUE
    );

    SHOW SMTP CONNECTIONS [INTO #smtp];   -- never includes passwords
    DROP SMTP CONNECTION 'corporate';

    -- =========================================================
    -- SUBSCRIPTIONS
    -- Group membership is evaluated at delivery time, not creation time.
    -- PARAMETERS values are stored as-is; RELDATE expressions are resolved
    -- fresh each time the subscription fires.
    -- =========================================================
    CREATE SUBSCRIPTION 'DailySales'
        FOR REPORT '/Finance/MonthlySales'
        DELIVER TO 'john.doe'
        SCHEDULE '0 8 * * MON'
        FORMAT PDF
        AT smtp
        PARAMETERS (
            @start  = 'D-1',
            @end    = 'D',
            @region = 'All'
        )
        [ENABLE | DISABLE];

    -- Note: DELIVER TO GROUP and FORMAT BOTH are valid syntax and parse correctly,
    -- but the REPORTPORTAL connector currently rejects them at runtime. Use
    -- DELIVER TO '<username>' and FORMAT PDF or FORMAT CSV until portal support ships.
    CREATE SUBSCRIPTION 'MonthlyExec'
        FOR REPORT '/Finance/MonthlySales'
        DELIVER TO GROUP 'Finance'
        ON REFRESH                  -- fires whenever the dataset refreshes
        FORMAT BOTH                 -- PDF and CSV
        AT smtp
        PARAMETERS (
            @period_start = 'M-1',
            @period_end   = 'ME-1'
        );

    -- Optional subscription name; PARAMETERS clause is optional
    CREATE SUBSCRIPTION FOR REPORT '/Ops/StatusReport'
        DELIVER TO 'ops@example.com'
        SCHEDULE '0 9 * * *'
        FORMAT PDF
        AT smtp;

    -- ALTER: change schedule or format only (PARAMETERS unchanged when clause omitted)
    ALTER SUBSCRIPTION 5 SET SCHEDULE = '0 9 * * MON-FRI';
    ALTER SUBSCRIPTION 5 SET FORMAT = CSV;
    ALTER SUBSCRIPTION 5 SET ENABLE;
    ALTER SUBSCRIPTION 5 SET DISABLE;

    -- ALTER: replace full parameter set (empty list clears all parameters)
    ALTER SUBSCRIPTION 5 SET
        PARAMETERS (
            @start  = 'W-1',
            @end    = 'W',
            @region = 'North'
        );

    ALTER SUBSCRIPTION 5 SET PARAMETERS ();   -- clears all parameters

    DROP SUBSCRIPTION 5;            -- by ID

    SHOW SUBSCRIPTIONS                                    [INTO #subs];
    SHOW SUBSCRIPTIONS FOR REPORT '/Finance/MonthlySales'  [INTO #subs];

    -- =========================================================
    -- SESSION MANAGEMENT
    -- =========================================================
    DISCONNECT USER 'dr.allen';          -- force logout; invalidates active session
    REVOKE TOKENS FOR USER 'dr.allen';   -- invalidate all JWT refresh tokens

    SHOW ACTIVE SESSIONS [INTO #sessions];
    SHOW PORTAL USAGE METRICS FOR 30 DAYS [INTO #usage];
    SHOW PORTAL OPERATIONAL METRICS [INTO #ops];

    -- =========================================================
    -- SERVICE CONTROL
    -- RESTART sends 202 Accepted then restarts. SHUTDOWN sends 202 then stops.
    -- START is not available via script (service is not running to receive it).
    -- =========================================================
    RESTART PORTAL;
    SHUTDOWN PORTAL;

    -- =========================================================
    -- PORTAL CONFIGURATION EXPORT
    -- Admin-only. Exports all declarative configuration (groups, users,
    -- memberships, folders, ACLs, SMTP connections, reports, dataset metadata,
    -- subscriptions, alerts) to an idempotent bootstrap script.
    -- Secrets are scrubbed and replaced with `${...}` placeholders.
    -- =========================================================
    EXPORT PORTAL CONFIGURATION TO 'portal_bootstrap.txt';

    -- =========================================================
    -- PORTAL METADATA QUERIES
    -- Use the connection alias as a schema prefix.
    -- All standard SELECT clauses (WHERE, ORDER BY, JOIN, etc.) are supported.
    -- =========================================================
    SELECT * FROM portal.Users;
    SELECT * FROM portal.Groups;
    SELECT * FROM portal.UserGroups;
    SELECT * FROM portal.Folders;
    SELECT * FROM portal.FolderAcl;
    SELECT * FROM portal.Reports;
    SELECT * FROM portal.ReportSnapshots;
    SELECT * FROM portal.Subscriptions;
    SELECT * FROM portal.ActiveSessions;
    SELECT * FROM portal.AuditLog WHERE Action = 'LOGIN_FAILED' AND Timestamp > DATEADD(DAY,-7,GETDATE());

    -- Example: who has access to the Finance folder?
    SELECT u.Username, g.Name AS GroupName, a.Permission
    FROM portal.Users u
    JOIN portal.UserGroups ug ON u.Id = ug.UserId
    JOIN portal.Groups g      ON ug.GroupId = g.Id
    JOIN portal.FolderAcl a   ON a.GroupId = g.Id
    JOIN portal.Folders f     ON a.FolderId = f.Id
    WHERE f.Path = '/Finance'
    ORDER BY a.Permission DESC, u.Username;

END
```

**Permission levels:**

| Level | Can do |
| :--- | :--- |
| `READ` | View the report exists; view cached snapshots and exports |
| `EXECUTE` | Run the report with custom parameters; trigger manual refresh |
| `MANAGE` | Edit metadata, schedules, ACL; delete reports and folders |
| `Admin` role | Implicit MANAGE everywhere + user/group administration |
| `Publisher` role | MANAGE in folders where granted |

---

## Appendix C: Orchestrator Remote Management

Orchestrator admin statements execute inside an `EXECUTE orch BEGIN...END` block. The `orch` alias must be a connection created with `AS ORCHESTRATOR(...)`.

For targeting a remote Orchestrator from a standalone `CREATE JOB` statement (outside a block), use the `AT <alias>` form documented in Â§15.2.

```sql
CREATE CONNECTION orch AS ORCHESTRATOR(
    HOST = 'orch-server.company.com',
    PORT = 5001,
    USER = 'admin',
    PASSWORD = ENC:...
);

EXECUTE orch BEGIN

    -- =========================================================
    -- JOB MANAGEMENT
    -- Both EVERY and cron schedule forms are valid.
    -- =========================================================
    CREATE JOB 'NightlyArchive' ON SCHEDULE EVERY 1 DAY AT '02:00' AS
        RUN SCRIPT '/scripts/nightly_archive.etlsql';

    CREATE JOB 'WeeklyReport' WITH (SCHEDULE = '0 8 * * MON') AS
        RUN SCRIPT '/scripts/weekly_report.etlsql';

    ALTER JOB 'NightlyArchive' SET SCHEDULE = EVERY 1 DAY AT '03:00';
    ALTER JOB 'WeeklyReport'   SET SCHEDULE = '0 9 * * MON';

    ENABLE  JOB 'NightlyArchive';
    DISABLE JOB 'NightlyArchive';
    DROP    JOB 'NightlyArchive' IF EXISTS;

    TRIGGER JOB 'NightlyArchive';       -- manual one-off; does not affect next scheduled run
    KILL    JOB <HistoryId>;            -- cancel a running instance by its history ID

    -- =========================================================
    -- ORCHESTRATOR METADATA QUERIES
    -- =========================================================
    SHOW JOBS         [INTO #jobs];
    SHOW ACTIVE JOBS  [INTO #active];
    SHOW JOB HISTORY  [INTO #history];
    SHOW JOB HISTORY 'NightlyArchive' [INTO #history];

    SELECT * FROM orch.Jobs;
    SELECT * FROM orch.JobHistory WHERE Status = 'FAILED' AND StartedAt > DATEADD(DAY,-7,GETDATE());
    SELECT * FROM orch.ActiveJobs;

    -- Example: jobs that failed more than once in the last week
    SELECT JobName, COUNT(*) AS FailCount
    FROM orch.JobHistory
    WHERE Status = 'FAILED'
      AND StartedAt > DATEADD(DAY, -7, GETDATE())
    GROUP BY JobName
    HAVING COUNT(*) > 1
    ORDER BY FailCount DESC;

END
```

---

## 20. Security & Cryptography

### 20.1 CREATE SSH_KEY_PAIR
Generates a cryptographic SSH key pair (RSA or ECDSA) for use with SFTP connectors or ENCRYPT FILE ... KEYFILE.

```sql
CREATE SSH_KEY_PAIR 'C:\Keys\id_rsa'
    WITH (BITS = 4096, ALGORITHM = 'RSA', PASSPHRASE = 'strong-pwd');
```

### 20.2 CREATE PGP_KEY_PAIR
Generates an OpenPGP key pair (RSA) for use with ENCRYPT FILE ... PGP_KEY.

```sql
CREATE PGP_KEY_PAIR 'C:\Keys\pgp'
    WITH (BITS = 4096, IDENTITY = 'ETL-SQL Service <etl@company.com>', PASSPHRASE = 'strong-pwd');
```

| Option | Default | Description |
| :--- | :--- | :--- |
| BITS | 2048 | Key length (RSA: 2048, 3072, 4096) |
| IDENTITY | user@etl-sql.local | PGP User ID identity string |
| PASSPHRASE | NULL | Password to protect the private key |

### 20.3 Key-Based File Encryption
The ENCRYPT FILE and DECRYPT FILE statements support PGP and SSH keys as alternatives to plaintext passwords.

```sql
-- PGP Encryption
ENCRYPT FILE 'data.csv' TO 'data.pgp' PGP_KEY 'C:\Keys\partner_pub.asc';

-- SSH (RSA) Decryption
DECRYPT FILE 'secrets.enc' TO 'secrets.csv' 
    KEYFILE 'C:\Keys\id_rsa' PASSWORD 'key-passphrase';
```

### 20.4 `GENERATE JWT_SECRET`
Generates a cryptographically strong random string for use as a JWT signing key in the Report Portal. The generated secret is 256 bits (32 bytes) encoded as a Base64 string.

```sql
GENERATE JWT_SECRET;                -- Prints to message log
GENERATE JWT_SECRET INTO @mySecret; -- Store in variable
```

This is the recommended way to generate keys for the `Portal:JwtSecret` configuration key.
