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
- **Directory Connections**: For local folders, you can use a `DIRECTORY` connection name as the path itself: `CREATE CONNECTION d ON DIRECTORY('C:\tmp'); COPY DIRECTORY d TO 'C:\Backup';`.
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
| `JSON_TABLE(@v, '$.path')` | Expand a JSON array into a table |
| `OPENJSON(@v)` | SQL-Server-style JSON rowset expansion |

```sql
DECLARE @payload JSON = '{"order":{"id":42,"total":99.95}}';
SELECT JSON_VALUE(@payload, '$.order.id')    AS id,
       JSON_VALUE(@payload, '$.order.total') AS total;
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
- **Runtime Masking:** The variable is marked as sensitive. `SHOW VARIABLES` and `PRINT` output will mask the value (displaying `*******` or `ENC:*******`) unless `SET SHOW_PASSWORD ON` is active.
- **Auto-Decryption:** The engine automatically decrypts the `ENC:` value in two scenarios:
  1. When passed to a secure connector parameter (`PASSWORD`, `API_KEY`, `SSH_KEY_PAIR.PASSPHRASE`, etc.).
  2. When evaluated in an expression that is assigned to a non-SENSITIVE target or used in a comparison.
- **Lint Protection (SEC-4):** The linter flags any attempt to concatenate or pass an `ENCRYPTED` variable to insecure sinks (like `SEND EMAIL` bodies or file writes).

```sql
USE PASSWORD = 'my-master-key';
DECLARE @pwd ENCRYPTED = 'ENC:abc123==';

-- Connection automatically handles decryption
CREATE CONNECTION MyDb ON MSSQL(PASSWORD = @pwd); 

-- Linter will warn here, and runtime will mask output
PRINT 'The password is: ' + @pwd; 
```

---

#### `SENSITIVE`

Sets the `IsSensitive` runtime flag on the variable. Three effects activate immediately:

1. **`SHOW VARIABLES` masking** â€” the value is replaced with `*******` in all variable listing output (unless `SET SHOW_PASSWORD ON` is active).
2. **`ENC:` auto-decryption** â€” if the value begins with `ENC:`, the engine automatically decrypts it when the variable is passed to a secure connector parameter (`PASSWORD`, `API_KEY`, `SSH_KEY_PAIR.PASSPHRASE`, etc.). This requires `USE SCRIPT PASSWORD` or a master password to be set.
3. **Lint taint tracking** â€” if you assign a `SENSITIVE` variable into a new variable (`SET @other = @pwd`), the linter marks `@other` as sensitive too, propagating SEC-4 warnings forward.

`SENSITIVE` ensures that the value is protected in output â€” `PRINT @sensitiveVar` will output `*******` unless `SET SHOW_PASSWORD ON` is active. The SEC-4 lint rule also warns you if you attempt to use these variables in insecure sinks.

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

Append `-<n>` to shift back n periods, e.g. `M-3` = first day of three months ago. Append `+<n>` for future offsets. For `N` (Now), use inline units: `N-2H` (2 hours), `N-30M` (30 minutes), `N-7D` (7 days).

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
CREATE CONNECTION db ON MSSQL('ENC:U2FsdGVkX1+...');
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
Enables high-resolution statement-level monitoring. See [Section 2.6](#26-observability--telemetry) for details.

```sql
SET PROFILING ON;
RUN SCRIPT 'heavy_transform.etlsql';
SET PROFILING OFF;
SHOW PROFILE INTO #perf;
```

### 2.3 `SET SHOW_PASSWORD`
Controls whether variables marked as `SENSITIVE` (see Â§4.1) are revealed in plain text during `SHOW VARIABLES` or `PRINT` output. Default: `OFF`.

> [!IMPORTANT]
> For security, the "System Password" set via `USE PASSWORD` is **never** revealed or echoed in any output, even when this setting is `ON`. If you lose this password, any `ENC:` strings or files encrypted with it cannot be recovered.

```sql
SET SHOW_PASSWORD ON;
SET SHOW_PASSWORD OFF;
```

### 2.4 Security Overrides
Only honored when the path is within an approved Safe Zone. All overrides produce an audit entry.

| Command | Description |
| :--- | :--- |
| `SET ALLOW_FILE_TYPE_ACCESS ON/OFF` | Allow file extensions not in the standard whitelist |
| `SET ALLOW_FILE_TYPE_ACCESS = '.ext'` | Add a specific extension (e.g. '.bak') to the authorized session whitelist |
| `SET ALLOW_FILE_OPERATIONS = n` | Overrides the default runaway protection limit (100) for file operations |
| `SET ALLOW_RECURSIVE_LAYERS = n` | Overrides the default recursion limit (5) for directory operations |

### 2.5 Performance & Spilling Thresholds
Override `appsettings.json` defaults for the current session.

| Command | Default | Description |
| :--- | :--- | :--- |
| `SET JOIN_SPILL_THRESHOLD = n` | 100,000 | Rows before a join spills to disk |
| `SET WINDOW_SPILL_THRESHOLD = n` | 100,000 | Rows before window functions spill to disk |
| `SET TEMP_TABLE_SPILL_THRESHOLD = n` | 1,000,000 | Rows before a `#temp` table spills to disk |
| `SET EXTERNAL_HASH_PARTITIONS = n` | 32 | Partitions used when spilling joins/windows |
| `SET EXTERNAL_SORT_CHUNK_SIZE = n` | 50,000 | Rows per chunk during external sort |
| `SET BATCHSIZE = n` | 10,000 | Rows per batch in the engine pipeline |
| `SET MAX_LAST_RESULT_ROWS = n` | 50,000 | Rows in the interactive display buffer |
| `SET LINEAGE = ON\|OFF` | ON | Enables data lineage tracking for the current script session |
| `SET TELEMETRY = ON\|OFF` | ON | Enables execution metrics and telemetry collection |
| `SET MAX_RECURSIVE_DEPTH = n` | 10,000 | Max call depth for recursive CTEs or procedures |
| `SET MAX_IN_MEMORY_BATCHES = n` | 100 | Batches held before automatic `#temp` spill |
| `SET FOREACH_PAGE_SIZE = n` | 10,000 | Items fetched per page when iterating large collections |
| `SET MAX_MESSAGES = n` | 1,000 | Log/print messages captured in the session buffer |
| `SET MAX_FILE_OPERATIONS = n` | 100 | Filesystem operations allowed per script |
| `SET MAX_GENERATE_ROWS = n` | 10,000 | Rows per `GENERATE` statement (prevents resource exhaustion) |
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


### 2.6 `SET WEEK_START_DAY`
Override the first day of the week for `RELDATE` week-boundary expressions (`W`, `W-1`, `WE`, `WE-1`, etc.) for the current script.

```sql
SET WEEK_START_DAY = 'Sunday';   -- valid for this script only
```

Valid values (case-insensitive): `Monday`, `Tuesday`, `Wednesday`, `Thursday`, `Friday`, `Saturday`, `Sunday`. The engine default is `Monday`; the organisation default can be changed with `Engine.StartOfWeek` in `appsettings.json`.

### 2.7 Observability & Telemetry

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
CREATE CONNECTION prod ON MSSQL(
    HOST = 'sql-server.company.com',
    DATABASE = 'Warehouse',
    TRUSTED_CONNECTION = TRUE,
    APPLICATION_INTENT = READONLY
);

-- PostgreSQL (Common options: HOST, PORT, DATABASE, USER, PASSWORD, SSL_MODE, POOLING)
CREATE CONNECTION pg ON POSTGRES(
    HOST = 'pg-server.company.com',
    DATABASE = 'analytics',
    USER = 'etl',
    PASSWORD = ENC:...,
    SSL_MODE = 'REQUIRE'
);

-- Oracle (Common options: HOST, PORT, SERVICE_NAME, TNS_NAME, USER, PASSWORD)
CREATE CONNECTION ora ON ORACLE(
    HOST = 'ora-server.company.com',
    SERVICE_NAME = 'ORCL',
    USER = 'etl',
    PASSWORD = ENC:...
);

-- ODBC (Common options: DSN, DRIVER, SERVER, DATABASE, UID, PWD)
CREATE CONNECTION legacy ON ODBC(DSN = 'MyLegacyDSN');
```

**File Connectors**

```sql
-- Flat file (Common: PATH, FORMAT, DELIMITER, HEADER, ENCODING, SKIP, COMPRESS, ENCRYPT)
CREATE CONNECTION sales_csv ON FLATFILE(
    PATH = 'C:\Data\sales.csv',
    FORMAT = 'CSV',
    DELIMITER = ',',
    HEADER = ON,
    ENCODING = 'UTF8'
);

-- Parquet (Common: PATH, COMPRESSION)
CREATE CONNECTION facts ON PARQUET(
    PATH = 'C:\Data\facts.parquet',
    COMPRESSION = 'SNAPPY'
);

-- JSON / XML (Common: PATH, ROOT_PATH, ENCODING)
CREATE CONNECTION config ON JSON(PATH = 'C:\Data\config.json', ROOT_PATH = '$.settings');
```

**Transfer Connectors**

```sql
-- SFTP (Common: HOST, PORT, USER, PASSWORD, KEYFILE, PASSPHRASE)
CREATE CONNECTION remote_sftp ON SFTP(
    HOST = 'sftp.company.com',
    USER = 'etl',
    KEYFILE = 'C:\Keys\id_rsa'
);

-- Azure Blob Storage (Common: ACCOUNT_NAME, ACCOUNT_KEY, CONTAINER)
CREATE CONNECTION blob ON AZUREBLOB(
    ACCOUNT_NAME = 'mystorageaccount',
    CONTAINER = 'mycontainer',
    ACCOUNT_KEY = ENC:...
);
```

**API & Notification Connectors**

```sql
-- REST API (Common: URL, METHOD, AUTH_TYPE, TOKEN, BODY, ROOT_PATH, PAG_TYPE, PAG_LIMIT)
CREATE CONNECTION api ON REST(
    URL = 'https://api.company.com/v1',
    AUTH_TYPE = 'BEARER',
    TOKEN = 'tkn_123',
    ROOT_PATH = '$.items'
);

-- SMTP (Common: HOST, PORT, USER, PASSWORD, USE_SSL, DEFAULT_FROM)
CREATE CONNECTION mailer ON SMTP(
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
CREATE CONNECTION testdb ON MOCKDB();

-- Local directory connector â€” treats a folder as a queryable table
CREATE CONNECTION logs_dir ON DIRECTORY('C:\Logs\') WITH(RECURSIVE=TRUE);
```

**Service Connectors**
```sql
-- Report Portal
CREATE CONNECTION portal ON REPORTPORTAL(
    HOST = 'report-server.company.com',
    PORT = 5001,
    USER = 'admin',
    PASSWORD = ENC:...
);

-- Orchestrator
CREATE CONNECTION orch ON ORCHESTRATOR(
    HOST = 'orch-server.company.com',
    PORT = 5100,
    USER = 'admin',
    PASSWORD = ENC:...
);
```

### 3.2 `ALTER CONNECTION`
Modifies an existing connection. Use this to rotate passwords or update server addresses without dropping the connection.

```sql
ALTER CONNECTION prod ON MSSQL(
    PASSWORD = ENC:...
);

-- Rename or change target only
ALTER CONNECTION stage ON POSTGRES('prod-server-v2');
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

---

## 5. Querying (`SELECT`)

### 5.1 Complete Clause Reference
Clauses must appear in this syntactic order:

```sql
SELECT [DISTINCT] [TOP n [PERCENT] [WITH TIES]]
    <columns>
[INTO <target>]
FROM <source> [AS alias]
[JOIN | LEFT JOIN | RIGHT JOIN | FULL JOIN | CROSS JOIN
 | LEFT SEMI JOIN | LEFT ANTI JOIN <table>
    [HASH | LOOP | MERGE]          -- join algorithm hint
    ON <condition>]
[CROSS APPLY | OUTER APPLY (<subquery>) <alias>]
[WHERE <condition>]
[GROUP BY <columns> | ROLLUP(<cols>) | CUBE(<cols>) | GROUPING SETS(<sets>)]
[HAVING <condition>]
[QUALIFY <condition>]
[PIVOT  (<agg> FOR <col> IN (<vals>)) AS <alias>]
[UNPIVOT (<val_col> FOR <name_col> IN (<cols>)) AS <alias>]
[ORDER BY <col> [ASC|DESC] [, ...]]
[OFFSET n ROWS]
[FETCH NEXT n ROWS ONLY]
[LIMIT n]
[FOR JSON AUTO | PATH | RAW [, ROOT('name')] [, INCLUDE_NULL_VALUES] [, WITHOUT_ARRAY_WRAPPER]]
[FOR XML  AUTO | PATH | RAW [, ROOT('name')] [, ELEMENTS]];
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
```

### 5.4 JOIN Types

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

### 5.5 `CROSS APPLY` / `OUTER APPLY`
```sql
SELECT o.OrderId, t.LineItem
FROM Orders AS o
CROSS APPLY (SELECT * FROM OrderLines WHERE OrderId = o.OrderId) AS t;

SELECT o.OrderId, t.LineItem
FROM Orders AS o
OUTER APPLY (SELECT TOP 1 * FROM OrderLines WHERE OrderId = o.OrderId) AS t;
```

`CROSS APPLY` is also used to expand table-valued functions such as `STRING_SPLIT`, `NGRAMS`, and `NGRAM_TOKENS`:

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

### 5.6 Hierarchical Aggregation
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

### 5.7 `PIVOT` / `UNPIVOT`
```sql
SELECT category, [Q1], [Q2], [Q3], [Q4]
FROM (SELECT category, quarter, amount FROM #sales) AS src
PIVOT (SUM(amount) FOR quarter IN ([Q1], [Q2], [Q3], [Q4])) AS pvt;

SELECT category, quarter, amount
FROM #quarterly_sales
UNPIVOT (amount FOR quarter IN ([Q1], [Q2], [Q3], [Q4])) AS unpvt;
```

### 5.8 `FOR JSON` / `FOR XML`
```sql
SELECT id, name, amount FROM #sales
FOR JSON PATH, ROOT('Sales'), INCLUDE_NULL_VALUES;

SELECT id, name FROM #sales
FOR XML PATH, ROOT('Employees'), ELEMENTS;
```

### 5.9 Window Functions
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
                        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS running_total

FROM #sales;
```

### 5.9.1 `FILTER` â€” Conditional Aggregation
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

### 5.10 `QUALIFY` â€” Filter Window Results
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
```

---

## 8. Logical Operators & Filter Predicates

```sql
WHERE amount >= 100 AND status <> 'Cancelled'

WHERE category IN ('Electronics', 'Apparel')
   OR status NOT IN @exclusionList

WHERE email LIKE '%@company.com'
  AND code  LIKE 'US\_%' ESCAPE '\'

WHERE EXISTS     (SELECT 1 FROM #approved WHERE id = t.id)
WHERE NOT EXISTS (SELECT 1 FROM #blocked  WHERE id = t.id)

WHERE region IS NOT NULL
  AND notes  IS NULL
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
```sql
CREATE TABLE #OrderItems (
    OrderId    INT           IDENTITY PRIMARY KEY,
    LineItem   INT           NOT NULL,
    Amount     DECIMAL(18,2) NOT NULL CHECK(Amount >= 0),
    Status     VARCHAR(20)   DEFAULT 'Pending',
    CustomerId INT           REFERENCES Customers(Id),
    CONSTRAINT UQ_Line UNIQUE (OrderId, LineItem)
);
```

> [!NOTE]
> `CREATE OR ALTER` is not supported for tables. Use `DROP TABLE IF EXISTS` followed by `CREATE TABLE`, or use the `IF NOT EXISTS` clause.

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
CREATE UNIQUE INDEX IX_Customers_Email ON Customers (Email ASC);
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
`=`, `<>`, `!=`, `<`, `<=`, `>`, `>=`, `IN`, `LIKE`, `BETWEEN`

#### `BETWEEN`
Checks if a value is within an inclusive range (equivalent to `val >= start AND val <= end`).

```sql
SELECT * FROM #audit WHERE event_date BETWEEN '2024-01-01' AND '2024-06-30';
SELECT * FROM #data WHERE id NOT BETWEEN @min AND @max;
```

### 14.4 Temporal Expressions

#### `AT TIME ZONE`
Converts a `DATETIME` or `DATETIMEOFFSET` expression to the target timezone. If the input has no offset, it is assumed to be **UTC**.

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

### 15.1 `CREATE JOB` â€” Local Orchestrator
Registers a job with the local Orchestrator service.

```sql
-- EVERY interval syntax
CREATE JOB CleanupJob ON SCHEDULE EVERY 30 MINUTES AS
    RUN SCRIPT 'scripts/cleanup.etlsql';

CREATE JOB NightlyArchive ON SCHEDULE EVERY 1 DAY AT '02:00' AS
BEGIN
    INSERT INTO archive SELECT * FROM prod.logs WHERE log_date < DATEADD(DAY,-30,GETDATE());
    DELETE FROM prod.logs WHERE log_date < DATEADD(DAY,-30,GETDATE());
END;

-- Cron syntax (equivalent; both forms are valid)
CREATE JOB WeeklyReport WITH (SCHEDULE = '0 8 * * MON') AS
    RUN SCRIPT 'scripts/weekly_report.etlsql';
```

**Schedule intervals (EVERY form):** `SECONDS`, `MINUTES`, `HOURS`, `DAYS`  
**Cron syntax:** standard 5-field cron expression in the `WITH (SCHEDULE = '...')` form.

### 15.2 `CREATE JOB` â€” Remote Orchestrator
Targets a specific remote Orchestrator using `AT <alias>`. The alias must be a connection created with `ON ORCHESTRATOR(...)`.

```sql
CREATE JOB 'NightlyArchive' AT orch ON SCHEDULE EVERY 1 DAY AT '02:00' AS
    RUN SCRIPT '/scripts/nightly_archive.etlsql';

CREATE JOB 'WeeklyReport' AT orch WITH (SCHEDULE = '0 8 * * MON') AS
    RUN SCRIPT '/scripts/weekly_report.etlsql';
```

> [!NOTE]
> `CREATE OR ALTER` is not supported for jobs. Use `DROP JOB` and then `CREATE JOB` or use `ALTER JOB` to modify schedule/properties.

### 15.3 Job Management

```sql
-- Local
ALTER JOB CleanupJob    SET SCHEDULE = EVERY 1 HOUR;
ALTER JOB WeeklyReport  SET SCHEDULE = '0 9 * * MON';   -- cron form
ENABLE  JOB CleanupJob;
DISABLE JOB CleanupJob;
DROP    JOB IF EXISTS CleanupJob;
TRIGGER JOB CleanupJob;         -- manual one-off run (does not affect next scheduled run)
KILL    JOB <HistoryId>;        -- cancel a running instance by its history ID

SHOW JOBS;
SHOW JOB HISTORY;
SHOW JOB HISTORY NightlyArchive;
SHOW ACTIVE JOBS;               -- running instances only

SHOW JOBS    INTO #jobs;
SHOW JOB HISTORY INTO #history;
```

---

## 16. File Operations

All paths are validated against the active Safe Zones before any I/O occurs. See `SET ALLOW_FILE_TYPE_ACCESS` and related overrides in Â§2.4.

### 16.1 File Statements
```sql
COPY FILE    '<source>' TO '<destination>' [WITH (OVERWRITE = ON|OFF)];
MOVE FILE    '<source>' TO '<destination>' [WITH (OVERWRITE = ON|OFF)];
RENAME FILE  '<source>' TO '<new_name>'   [WITH (OVERWRITE = ON|OFF)];
DELETE FILE  '<path>';
DELETE FILE  '<path>' IF EXISTS;

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

COPY DIRECTORY   '<src>' TO '<dest>'     [WITH (OVERWRITE = ON|OFF)];
MOVE DIRECTORY   '<src>' TO '<dest>'     [WITH (OVERWRITE = ON|OFF)];
RENAME DIRECTORY '<src>' TO '<new_name>' [WITH (OVERWRITE = ON|OFF)];

DELETE DIRECTORY          '<path>' [IF EXISTS];
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
CREATE CONNECTION mailer ON SMTP(
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
CREATE CONNECTION stage_db ON MSSQL(@conn);
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

CREATE CONNECTION source_db ON MSSQL(src.CONNECTION_STRING);
CREATE CONNECTION target_db ON POSTGRES(dst.CONNECTION_STRING);

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
SHOW PROFILE                   [INTO #temp];
SHOW LINEAGE [FOR <table_ref> [COLUMN <column>]] [INTO #temp];
SHOW LINEAGE FOR REPORT <report_name> [INTO #temp];
SHOW LINEAGE FOR DATASET &<dataset_name> [INTO #temp];

-- Metadata Tags
SHOW TAGS FOR SCRIPT                         [INTO #temp];
SHOW TAGS FOR TABLE <table> [COLUMN <col>]    [INTO #temp];
SHOW TAG VALUE FOR TABLE <table> [COLUMN <col>] WITH TAG <name> [INTO #temp];
```

### 19.3 Jobs
```sql
SHOW JOBS          [INTO #temp];
SHOW ACTIVE JOBS   [INTO #temp];
SHOW JOB HISTORY [<jobName>]  [INTO #temp];
KILL JOB <HistoryId>;
```

### 19.4 Analysis
```sql
EXPLAIN SELECT * FROM conn.Orders WHERE status = 'Open';
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
   @version:     1.2.3
   @description: Nightly cleanup of staging tables
*/
```

Supported tags: `@author`, `@version`, `@description`, or any custom `@key: value` pair. `@author` defaults to the current system user if omitted.

---

## Appendix A: Report-SQL Grammar (`.rptsql` files)

`.rptsql` files are standard ETL-SQL scripts with the following additional statement types. For the full user guide see `Docs/Report_SQL_Guide.md`.

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

### A.3 `CREATE VISUAL`
```sql
CREATE VISUAL <name> AS <type> (
  [SOURCE     = &dataset | #table | ( SELECT ... ),]
  [TITLE      = '<string>',]
  [SUBTITLE   = '<string>',]
  [TOOLTIP    = '<string>',]
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
| `MATRIX` | `ROW` (or `ROW1`), `COL`, `VALUE` | `ROW2`, `ROW3` |
| `MAP` (choropleth) | `REGION` | `VALUE` |
| `MAP` (points â€” `MODE=POINTS`) | `LON`, `LAT` | `VALUE`, `LABEL` |
| `TABLE` | _(all source columns rendered automatically)_ | â€” |
| `CARD` | `VALUE` | `LABEL` |
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
ON_CLICK  = SET_UI_STATE(<Target>, <Key>, <Value>)
```

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
CREATE PAGE <name> AS (
  STRUCTURE = '<css-grid-template-areas>',
  MAP (
    '<slot>' = <VisualOrContainerName>
    [, '<slot>' = <name> ...]
  )
  [, GAP = '<css-size>']
  [, STYLE ( key = value [, ...] )]
)
;
```

`STRUCTURE` uses CSS grid-template-areas: space-separated slot letters per row, rows separated by `/`. Example: `'A A / B C'`.

### A.5 `CREATE CONTAINER`
```sql
CREATE CONTAINER <name> AS BOX|SCROLL|DRAWER|SIDEBAR|TABS|ACCORDION|MODAL|POPOVER (
  [TITLE = '<string>',]
  [SUBTITLE = '<string>',]
  [TOOLTIP = '<string>' | <ContainerName>,]
  [STYLE = <styleName> | STYLE ( key = value [, ...] ),]
  LAYOUT (
    STRUCTURE = '<css-grid-template-areas>',
    MAP ('<slot>' = <VisualOrContainerName> [, ...] )
    [, GAP = '<css-size>']
    [, <layout_key> = <value> ...]
  ),
  [OPTIONS (
    PINNABLE = ON|OFF,
    VISIBLE = ON|OFF,
    ICON = '<name>'
  )]
);
```

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

### A.9 `ALTER` / `DROP` / `CREATE OR ALTER`
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
CREATE OR ALTER PAGE       <name> AS ( ... );
CREATE OR ALTER DATASET    &<name> ... AS ( SELECT ... );
CREATE OR ALTER STYLE      <name> ( ... );
CREATE OR ALTER BUTTON     <name> AS ( ... );
CREATE OR ALTER CONTAINER  <name> AS BOX|SCROLL|DRAWER|SIDEBAR|TABS|ACCORDION|MODAL|POPOVER ( ... );
CREATE OR ALTER NAVIGATION <name> AS TAB|BUTTON|LINK ( ... );
```

---

## Appendix B: Report Portal Admin Language

Portal admin statements execute inside an `EXECUTE portal BEGIN...END` block. The `portal` alias must be a connection created with `ON REPORTPORTAL(...)`.

```sql
CREATE CONNECTION portal ON REPORTPORTAL(
    HOST = 'report-server.company.com',
    PORT = 5001,
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
    ALTER USER 'john.doe' ENABLE;
    ALTER USER 'john.doe' DISABLE;

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

    -- =========================================================
    -- REPORT CATALOG
    -- PUBLISH points the portal at an existing .rptsql file.
    -- =========================================================
    PUBLISH REPORT 'Monthly Sales'
        FROM '/reports/finance/monthly_sales.rptsql'
        IN FOLDER '/Finance'
        WITH (DESCRIPTION = 'Monthly revenue by region');

    ALTER REPORT 'Monthly Sales' SET FOLDER      = '/Finance/Archive';
    ALTER REPORT 'Monthly Sales' SET DESCRIPTION = 'Archived monthly revenue report';
    ALTER REPORT 'Monthly Sales' SET NAME        = 'Monthly Sales (Archive)';

    DROP REPORT 'Monthly Sales';
    DROP REPORT 'Monthly Sales' CASCADE;   -- also removes snapshots and subscriptions

    SHOW REPORTS                         [INTO #reports];
    SHOW REPORTS IN FOLDER '/Finance'    [INTO #reports];
    SHOW REPORT  'Monthly Sales'         [INTO #detail];

    -- =========================================================
    -- SNAPSHOTS
    -- =========================================================
    DROP SNAPSHOT FOR REPORT 'Monthly Sales';        -- force rebuild on next view
    REBUILD SNAPSHOT FOR REPORT 'Monthly Sales';     -- rebuild now in background

    SHOW SNAPSHOTS                       [INTO #snaps];

    -- =========================================================
    -- SUBSCRIPTIONS
    -- Group membership is evaluated at delivery time, not creation time.
    -- PARAMETERS values are stored as-is; RELDATE expressions are resolved
    -- fresh each time the subscription fires.
    -- =========================================================
    CREATE SUBSCRIPTION DailySales
        FOR REPORT '/Finance/MonthlySales'
        DELIVER TO 'john.doe'
        SCHEDULE '0 8 * * MON'
        FORMAT PDF
        AT smtp
        PARAMETERS (
            @start  = 'D-1',
            @end    = 'D',
            @region = NULL
        );

    CREATE SUBSCRIPTION MonthlyExec
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
        FORMAT LINK
        AT smtp;

    -- ALTER: change schedule or format only (PARAMETERS unchanged when clause omitted)
    ALTER SUBSCRIPTION DailySales SET SCHEDULE '0 9 * * MON-FRI';
    ALTER SUBSCRIPTION DailySales SET FORMAT CSV;
    ALTER SUBSCRIPTION DailySales SET ACTIVE;
    ALTER SUBSCRIPTION DailySales SET INACTIVE;

    -- ALTER: replace full parameter set (empty list clears all parameters)
    ALTER SUBSCRIPTION DailySales
        PARAMETERS (
            @start  = 'W-1',
            @end    = 'W',
            @region = 'North'
        );

    ALTER SUBSCRIPTION DailySales PARAMETERS ();   -- clears all parameters

    DROP SUBSCRIPTION DailySales;
    DROP SUBSCRIPTION 5;            -- by ID

    SHOW SUBSCRIPTIONS                                    [INTO #subs];
    SHOW SUBSCRIPTIONS FOR REPORT '/Finance/MonthlySales'  [INTO #subs];

    -- =========================================================
    -- SESSION MANAGEMENT
    -- =========================================================
    DISCONNECT USER 'dr.allen';          -- force logout; invalidates active session
    REVOKE TOKENS FOR USER 'dr.allen';   -- invalidate all JWT refresh tokens

    SHOW ACTIVE SESSIONS [INTO #sessions];

    -- =========================================================
    -- SERVICE CONTROL
    -- RESTART sends 202 Accepted then restarts. SHUTDOWN sends 202 then stops.
    -- START is not available via script (service is not running to receive it).
    -- =========================================================
    RESTART PORTAL;
    SHUTDOWN PORTAL;

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

Orchestrator admin statements execute inside an `EXECUTE orch BEGIN...END` block. The `orch` alias must be a connection created with `ON ORCHESTRATOR(...)`.

For targeting a remote Orchestrator from a standalone `CREATE JOB` statement (outside a block), use the `AT <alias>` form documented in Â§15.2.

```sql
CREATE CONNECTION orch ON ORCHESTRATOR(
    HOST = 'orch-server.company.com',
    PORT = 5100,
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
