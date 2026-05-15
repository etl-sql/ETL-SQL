# ETL-SQL Specialized Operations & Automation

This document is the technical reference for ETL-SQL's non-query automation features: filesystem management, remote file transfer, email notifications, metadata/lineage tracking, cryptographic key generation, Docker lifecycle integration, and background job scheduling.

---

## 1. Local File Operations

ETL-SQL provides a unified interface for filesystem management. All paths are validated by the `SecurityService` (Zero-Trust sandbox) before any I/O occurs.

### 1.1 File Statements (SQL Style)

```sql
COPY FILE    '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];
MOVE FILE    '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];
RENAME FILE  '<source>' TO '<new_name>'   [WITH(OVERWRITE=ON|OFF)];
DELETE FILE  '<path>';

COMPRESS FILE '<source>' TO '<destination>' [WITH(OVERWRITE=ON|OFF)];
ENCRYPT FILE  '<source>' TO '<destination>' [PASSWORD 'pwd' | KEYFILE 'path' | PGP_KEY 'path'] [WITH(OVERWRITE=ON|OFF)];
DECRYPT FILE  '<source>' TO '<destination>' [PASSWORD 'pwd' | KEYFILE 'path' | PGP_KEY 'path'] [WITH(OVERWRITE=ON|OFF)];
```

### 1.2 File Functions (Underscore Style — Backward Compatible)

All functions accept an optional last argument for `OVERWRITE` (`ON`/`OFF`, default `ON`):

```sql
COPY_FILE('src', 'dest' [, ON|OFF])
MOVE_FILE('src', 'dest' [, ON|OFF])
RENAME_FILE('src', 'new_name' [, ON|OFF])
DELETE_FILE('path')
COMPRESS_FILE('src', 'dest' [, ON|OFF])
ENCRYPT_FILE('src', 'dest', 'pwd' | 'keyfile_path' | 'pgp_key_path' [, ON|OFF])
DECRYPT_FILE('src', 'dest', 'pwd' | 'keyfile_path' | 'pgp_key_path' [, ON|OFF])
```

### 1.3 Directory Statements (SQL Style)
Path arguments can be literal strings or **DIRECTORY connection** aliases (e.g. `COPY DIRECTORY my_alias TO 'C:\Backup'`).

```sql
CREATE DIRECTORY '<path>' [WITH(OVERWRITE=ON|OFF)];

COPY DIRECTORY      '<src>' TO '<dest>'     [WITH(OVERWRITE=ON|OFF)];
MOVE DIRECTORY      '<src>' TO '<dest>'     [WITH(OVERWRITE=ON|OFF)];
RENAME DIRECTORY    '<src>' TO '<new_name>' [WITH(OVERWRITE=ON|OFF)];

DELETE DIRECTORY          '<path>';
DELETE DIRECTORY_CONTENTS '<path>' [WITH(RECURSIVE=ON|OFF)];

COMPRESS DIRECTORY '<src>' TO '<dest.zip>'  [WITH(OVERWRITE=ON|OFF)];
ENCRYPT DIRECTORY  '<src>' TO '<dest>' [PASSWORD 'pwd' | KEYFILE 'path' | PGP_KEY 'path'] [WITH(OVERWRITE=ON|OFF, RECURSIVE=ON|OFF)];
DECRYPT DIRECTORY  '<src>' TO '<dest>' [PASSWORD 'pwd' | KEYFILE 'path' | PGP_KEY 'path'] [WITH(OVERWRITE=ON|OFF, RECURSIVE=ON|OFF)];
```

### 1.4 Directory Functions (Underscore Style)

```sql
CREATE_DIRECTORY('path' [, ON|OFF])
COPY_DIRECTORY('src', 'dest' [, ON|OFF])
MOVE_DIRECTORY('src', 'dest' [, ON|OFF])
RENAME_DIRECTORY('src', 'new_name' [, ON|OFF])
DELETE_DIRECTORY('path')
DELETE_DIRECTORY_CONTENTS('path' [, RECURSIVE=ON|OFF])
COMPRESS_DIRECTORY('src', 'dest' [, ON|OFF])
ENCRYPT_DIRECTORY('src', 'dest', 'pwd' | 'keyfile' | 'pgp_key' [, ON|OFF [, ON|OFF]])
DECRYPT_DIRECTORY('src', 'dest', 'pwd' | 'keyfile' | 'pgp_key' [, ON|OFF [, ON|OFF]])
```

### 1.5 Examples

```sql
-- Stage a backup with overwrite
COPY FILE 'C:\Dropzone\data.csv' TO 'D:\Archive\data_backup.csv' WITH(OVERWRITE=ON);

-- Rename then delete the working copy
RENAME FILE 'C:\Incoming\latest.csv' TO 'processing.csv';
DELETE FILE 'C:\Incoming\processing.csv';

-- Compress and encrypt a finished payload using PGP
COMPRESS FILE 'C:\Outbound\payload.xml' TO 'C:\Outbound\payload.zip';
ENCRYPT FILE  'C:\Outbound\payload.zip' TO 'C:\Outbound\payload.pgp' PGP_KEY 'C:\Keys\partner_public.asc';

-- Decrypt using SSH (RSA) private key
DECRYPT FILE 'C:\Incoming\secrets.enc' TO 'C:\Staging\secrets.csv' 
    KEYFILE 'C:\Keys\id_rsa' PASSWORD 'key-passphrase';

-- Create staging directory and wipe stale contents after processing
CREATE DIRECTORY 'C:\AppTemp\PipelineA';
DELETE DIRECTORY_CONTENTS 'C:\AppTemp\PipelineA' WITH(RECURSIVE=ON);
```

### 1.6 Path Resolution & Directory Connections

ETL-SQL supports path aliasing via connections. If a path string starts with a registered connection name, the engine resolves it to the connection's base path.

**Using DIRECTORY connections as path aliases:**
```sql
-- Define a logical name for a physical path
CREATE CONNECTION source_dir ON DIRECTORY('C:\Users\Chuck\Documents\Input');
CREATE CONNECTION backup_dir ON DIRECTORY('D:\Backups\Daily');

-- Use the alias instead of the full path in any file statement or function
COPY DIRECTORY source_dir TO backup_dir;
SELECT * FROM FILE_LIST(source_dir);

-- You can also append sub-paths to the alias
DELETE FILE 'source_dir/stale_lock.txt';
```

This pattern is highly recommended for scripts that move between environments (Dev/Test/Prod) as it isolates the physical path logic to a single `CREATE CONNECTION` block.

---

## 2. Filesystem Query Functions

| Function | Signature | Returns |
| :--- | :--- | :--- |
| `FILE_EXISTS` | `FILE_EXISTS(path)` | `TRUE` if the file exists |
| `DIRECTORY_EXISTS` | `DIRECTORY_EXISTS(path)` | `TRUE` if the directory exists |
| `FILE_LIST` | `FILE_LIST(path [, recursive])` | Table: `Name`, `Path`, `Extension`, `Size`, `LastModified` |

```sql
IF FILE_EXISTS('C:\Incoming\payload.csv')
    COPY FILE 'C:\Incoming\payload.csv' TO 'C:\Archive\payload.csv';

-- List all .csv files recursively
SELECT Name, Size, LastModified FROM FILE_LIST('C:\Incoming', TRUE)
WHERE Extension = '.csv'
ORDER BY LastModified DESC;
```

---

## 2. Remote File Transfer

Coordinates data movement between the local engine layer and remote connectors (`SFTP`, `FTP`, `AZURE_BLOB`).

### 2.1 `SEND FILE` — Upload to Remote

*SQL Style:*
```sql
SEND FILE '<local_path>' TO '<remote_path>' AT <connection> [WITH(OVERWRITE=ON|OFF)];
```

*Function Style:*
```sql
SEND_FILE('<local_path>', <connection>, '<remote_path>' [, OVERWRITE=ON|OFF]);
```

### 2.2 `RECEIVE FILE` — Download from Remote

*SQL Style:*
```sql
RECEIVE FILE FROM '<remote_path>' TO '<local_path>' AT <connection> [WITH(OVERWRITE=ON|OFF)];
```

*Function Style:*
```sql
RECEIVE_FILE('<remote_path>', <connection>, '<local_path>' [, OVERWRITE=ON|OFF]);
```

### 2.3 `REMOTE_FILE_LIST` — Remote Directory Listing

Returns a queryable table of files from an SFTP, FTP, or Azure Blob connection.

```sql
-- SQL function
SELECT Name, Path, Size, LastModified
INTO #remote_inventory
FROM REMOTE_FILE_LIST(remote_sftp, '/var/ftp/incoming/');
```

**Returned columns:**

| Column | Type | Description |
| :--- | :--- | :--- |
| `Name` | `STRING` | File name (with extension) |
| `Path` | `STRING` | Full remote path to the file |
| `Size` | `BIGINT` | File size in bytes |
| `LastModified` | `DATETIME` | Last-modified timestamp |

### 2.4 Examples

```sql
-- Assuming a configured SFTP connection named remote_sftp:

-- Download today's extract
RECEIVE FILE FROM '/var/ftp/incoming/extract_20260412.csv'
    TO 'C:\LocalDrop\extract.csv'
    AT remote_sftp WITH(OVERWRITE=ON);

-- Process data and upload the result
SEND FILE 'C:\LocalDrop\report.pdf'
    TO '/var/ftp/outgoing/report.pdf'
    AT remote_sftp;

-- Find only files modified in the last 24 hours
SELECT Name, Size, LastModified
INTO #new_files
FROM REMOTE_FILE_LIST(remote_sftp, '/var/ftp/incoming/')
WHERE LastModified >= DATEADD(HOUR, -24, GETDATE())
ORDER BY LastModified DESC;
```

---

## 3. Email Notifications (`SEND EMAIL`)

Sends automated emails using a configured `SMTP` connection. Both styles support any clause order.

*SQL Style:*
```sql
SEND EMAIL
    TO   '<to_address>'
    FROM '<from_address>'
    SUBJECT '<subject>'
    BODY '<body_text>'
    [CC  '<cc_address>' [, '<cc2>', ...]]
    [BCC '<bcc_address>' [, '<bcc2>', ...]]
    [ATTACH '<file_path>' [, '<file2>', ...]]
    [AT <smtp_connection>];
```

*Function Style:*
```sql
SEND_EMAIL(<smtp_conn>, '<to>', '<from>', '<subject>', '<body>' [, '<cc>', '<bcc>', '<attach>']);
```

| Clause | Required | Notes |
| :--- | :---: | :--- |
| `TO` | Yes | One or more recipient addresses |
| `FROM` | Yes | Sender address; omit to use `DEFAULT_FROM` from the SMTP connection |
| `SUBJECT` | Yes | Email subject line |
| `BODY` | Yes | Plain text email body |
| `CC` | No | One or more carbon-copy recipients |
| `BCC` | No | One or more blind-copy recipients |
| `ATTACH` | No | One or more local file paths to attach |
| `AT` | No | If omitted, uses the last configured SMTP connection |

*Example:*
```sql
CREATE CONNECTION mailer ON SMTP('smtp.company.com')
    WITH(PORT=587, USERNAME='alerts@company.com', PASSWORD='apppassword',
         USE_SSL=TRUE, DEFAULT_FROM='alerts@company.com');

-- Clauses can be in any order
SEND EMAIL
    FROM    'etl@company.com'
    TO      'ops@company.com'
    CC      'manager@company.com'
    SUBJECT 'Nightly Load Failed'
    BODY    'The nightly load failed at step 4. See attached log for details.'
    ATTACH  'C:\Logs\nightly_error.log'
    AT      mailer;
```

---

## 4. Metadata, Lineage & Tagging

ETL-SQL natively tracks the ancestry and lineage of every transformation via metadata tags that propagate seamlessly across joins.

### 4.1 Applying Tags (Inline Comment Syntax)

Tags are embedded directly in SQL using `/* @key: value; */` comment blocks.

**Column tags** — placed after a column expression in a `SELECT` list:
```sql
SELECT
    UserId   /* @d: Internal user ID; @PII: true; */,
    UserName /* @d: Full display name; @owner: SecurityTeam; */
INTO #TaggedUsers
FROM m.Users /* @sensitivity: high; */;
```

**Fixed-width width declarations** (special tag for `FORMAT=FIXED` connectors):
```sql
CREATE TABLE #Layout (
    ID   INT          /* @width: 5 */,
    Name VARCHAR(20),           -- Width inferred from VARCHAR(20)
    Code CHAR(3)
);
```

`@d:` is the reserved description tag, displayed in IDE hover and lineage reports.

### 4.2 Script Metadata Headers
Scripts can define global metadata in a comment block at the very top of the file. This metadata is captured by the engine and automatically recorded in every lineage entry produced by the script.

```sql
/* 
   @author: Chuck 
   @version: 1.0.1 
   @description: Quarterly cleanup and archival
*/
```

- **@author**: Defaults to the current OS user if omitted.
- **@engine_version**: Automatically captured from the running engine.
- **Custom Tags**: Any `@key: value` pair can be added and queried via `LINEAGE`.

### 4.3 Querying Tags

```sql
-- Return all tag names on a column
DECLARE @tags LIST = GET_TAGS('#TaggedUsers', 'UserId');

-- Return the value of a specific tag
DECLARE @desc STRING = GET_TAG_VALUE('#TaggedUsers', 'UserId', 'd');

-- Guard on a PII tag
IF 'PII' IN @tags
BEGIN
    PRINT 'Warning: column contains PII — apply masking before export.';
END
```

**Tag introspection statements:**
```sql
SHOW TAGS FOR TABLE #TaggedUsers;
SHOW TAGS FOR TABLE #TaggedUsers COLUMN UserId;
SHOW TAG VALUE FOR TABLE #TaggedUsers COLUMN UserId WITH TAG 'd';
```

### 4.3 `SHOW LINEAGE` — Data Ancestry Reporting

Traces the full lineage graph of a table — every source, join, transformation, and tag inheritance that led to its current state.

*Syntax variants:*

| Variant | Syntax | Effect |
| :--- | :--- | :--- |
| Console view | `SHOW LINEAGE FOR #table;` | Prints a hierarchical tree in the messages panel |
| Column trace | `SHOW LINEAGE FOR #table COLUMN Column;` | Filters the trace to a single column's ancestry |
| Markdown export | `SHOW LINEAGE FOR #table TO 'reports/lineage.md';` | Generates a Mermaid.js diagram + audit table in a Markdown file |
| Temp table capture | `SHOW LINEAGE INTO #lineage;` | Stores lineage rows in an engine temp table |
| Queryable source | `SELECT * FROM LINEAGE(#table);` | Lets you filter/JOIN the lineage log as a standard table |

**Queryable columns from `LINEAGE(...)`:**

| Column | Description |
| :--- | :--- |
| `Timestamp` | When the operation was recorded |
| `Operation` | Statement type (`SELECT`, `INSERT`, `MERGE`, etc.) |
| `TargetTable` | Destination table name |
| `TargetColumn` | Destination column name |
| `SourceTables` | Comma-separated list of source table names |
| `SourceColumns` | Comma-separated list of source column names |
| `Description` | Column `@d` description tag at this point |
| `Metadata` | JSON blob of all custom tags at this point |
| `DerivedFromDescriptions` | Amalgamated descriptions from all upstream sources |
| `SourceFile` | Script file that produced this lineage entry |
| `Line`, `Column` | Position in the script |

*Example:*
```sql
-- Export a full lineage Mermaid report
SHOW LINEAGE FOR #FinalAudit TO 'C:\Reports\Employee_Lineage.md';

-- Validate lineage programmatically in a pipeline
SELECT Operation, SourceTables
INTO #lineage_check
FROM LINEAGE(#FinalAudit);

IF NOT EXISTS (SELECT 1 FROM #lineage_check WHERE SourceTables LIKE '%HR_System%')
BEGIN
    THROW 50010, 'Expected source HR_System not found in lineage.', 1;
END
```

---

## 5. SSH Key Pair Generation

Script-based generation of cryptographic key pairs for SFTP authentication and file encryption.

*SQL Style (named options):*
```sql
CREATE SSH_KEY_PAIR '<directory_path>'
    [WITH(BITS=2048, ALGORITHM='RSA', PASSPHRASE='pwd', COMMENT='comment')];
```

*Function Style (positional):*
```sql
SSH_KEY_PAIR('<directory_path>' [, bits, 'algorithm', 'passphrase', 'comment']);
```

| Option | Values | Default | Notes |
| :--- | :--- | :--- | :--- |
| `BITS` | 2048, 3072, 4096 (RSA); 256, 384, 521 (ECDSA) | `2048` | Larger = stronger but slower |
| `ALGORITHM` | `RSA`, `ECDSA` | `RSA` | ECDSA with P-256/P-384/P-521 curves |
| `PASSPHRASE` | Any string | *(none)* | Encrypts the private key at rest |
| `COMMENT` | Any string | *(none)* | Embedded in the `.pub` file |

*Examples:*
```sql
-- Quick RSA key (2048-bit, no passphrase)
CREATE SSH_KEY_PAIR 'C:\Keys\id_rsa';

-- Production-grade RSA with passphrase
CREATE SSH_KEY_PAIR 'C:\Keys\prod_rsa'
    WITH(BITS=4096, PASSPHRASE='StrongPassword123!', COMMENT='Production ETL Service');

### 5.2 PGP Key Pair Generation
Generates OpenPGP compatible key pairs (RSA) for secure file sharing.

*SQL Style:*
```sql
CREATE PGP_KEY_PAIR '<directory_path>'
    [WITH(BITS=2048, IDENTITY='user@company.com', PASSPHRASE='pwd')];
```

| Option | Values | Default | Notes |
| :--- | :--- | :--- | :--- |
| `BITS` | 2048, 3072, 4096 | `2048` | Key length |
| `IDENTITY` | Any string | `user@etl-sql.local` | PGP User ID identity |
| `PASSPHRASE` | Any string | *(none)* | Protects the private key |

*Example:*
```sql
CREATE PGP_KEY_PAIR 'C:\Keys\pgp_storage'
    WITH(BITS=4096, IDENTITY='ETL-SQL Backup <backup@company.com>', PASSPHRASE='Secret123');
```

```

---

## 6. Docker Lifecycle Integration

Automatically provision containerized databases for isolated staging or testing, then dispose of them.

### 6.1 SQL-Style Commands

```sql
-- Start a container and assign an alias
USE DOCKER('<image>') AS <alias>;

-- Access the generated connection string
DECLARE @conn VARCHAR(500) = <alias>.CONNECTION_STRING;

-- Access the last started container connection string
DECLARE @last_conn VARCHAR(500) = DOCKER.CONNECTION_STRING;

-- Container lifecycle (Targeted)
START DOCKER <alias>;   -- Resumes a stopped container
STOP DOCKER <alias>;    -- Stops the container (state retained on disk)
PAUSE DOCKER <alias>;   -- Suspends the container CPU
CLOSE DOCKER <alias>;   -- Destroys container and wipes all state

-- Container lifecycle (Last Started)
START DOCKER;
STOP DOCKER;
PAUSE DOCKER;
CLOSE DOCKER;

-- Container lifecycle (All Active)
START ALL DOCKER;
STOP ALL DOCKER;
PAUSE ALL DOCKER;
CLOSE ALL DOCKER;
```

### 6.2 Function-Style Aliases

| Function | Equivalent |
| :--- | :--- |
| `DOCKER('<image>' [, 'alias'])` | `USE DOCKER(...) AS alias` |
| `START_DOCKER(['alias'])` | `START DOCKER [alias]` |
| `STOP_DOCKER(['alias'])` | `STOP DOCKER [alias]` |
| `PAUSE_DOCKER(['alias'])` | `PAUSE DOCKER [alias]` |
| `CLOSE_DOCKER(['alias'])` | `CLOSE DOCKER [alias]` |

### 6.3 Example

```sql
-- Spin up two isolated databases
USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest') AS dms;
USE DOCKER('postgres:15-alpine') AS dpost;

DECLARE @ms_conn VARCHAR(500) = dms.CONNECTION_STRING;
DECLARE @pg_conn VARCHAR(500) = dpost.CONNECTION_STRING;

CREATE CONNECTION stage_sql ON MSSQL(@ms_conn);
CREATE CONNECTION stage_pg  ON POSTGRES(@pg_conn);

-- Load and validate data
SELECT * INTO #stage FROM source_db.Orders;
INSERT INTO stage_sql.dbo.Orders SELECT * FROM #stage;

-- Tear down when done
dms CLOSE;
dpost CLOSE;
```

---

## 7. Background Job Scheduling

### 7.1 `CREATE JOB`
Orchestrates script execution on a repeating schedule. Jobs run only when ETL-SQL is operating in headless/service mode.

*Syntax:*
```sql
CREATE JOB <name> ON SCHEDULE EVERY <n> SECONDS|MINUTES|HOURS|DAYS [AT 'HH:MM'] AS
    <statement>;

-- OR with a block:
CREATE JOB <name> ON SCHEDULE EVERY <n> ... AS
BEGIN
    <statements>;
END;
```

| Option | Notes |
| :--- | :--- |
| `EVERY n SECONDS\|MINUTES\|HOURS\|DAYS` | Recurrence interval |
| `AT 'HH:MM'` | Optional. Pin the job to a specific time of day |
| `AS <statement>` | The statement to execute; typically `RUN SCRIPT` or a `BEGIN...END` block |

*Examples:*
```sql
-- Run a cleanup script every 30 minutes
CREATE JOB CleanupJob ON SCHEDULE EVERY 30 MINUTES AS
    RUN SCRIPT 'scripts/cleanup.etlsql';

-- Nightly archive at 2 AM
CREATE JOB NightlyArchive ON SCHEDULE EVERY 1 DAY AT '02:00' AS
BEGIN
    INSERT INTO archive_db.logs
    SELECT * FROM prod_db.logs WHERE log_date < DATEADD(DAY, -30, GETDATE());

    DELETE FROM prod_db.logs WHERE log_date < DATEADD(DAY, -30, GETDATE());
END;
```

### 7.2 Job Management Commands

```sql
SHOW JOBS;                            -- List all registered jobs and their schedules
SHOW JOB HISTORY;                     -- Execution history for all jobs
SHOW JOB HISTORY NightlyArchive;      -- History for a specific job
DROP JOB IF EXISTS CleanupJob;        -- Remove a job
-- To terminate an actively running job, use the Orchestrator REST API:
--   PUT http://localhost:5100/jobs/{job_name}/cancel
```

---

## 8. Diagnostics & Execution Profiling

### 8.1 `SET PROFILING`
Enables deep performance recording: millisecond-level benchmarks, memory deltas, and recursion depths for each statement.

```sql
SET PROFILING ON;

-- Run complex operations
RUN SCRIPT 'massive_transform.etlsql';

SET PROFILING OFF;

-- View captured metrics in the results pane or export to a table
SHOW PROFILE INTO #execution_benchmarks;

SELECT *
FROM #execution_benchmarks
ORDER BY DurationMs DESC
LIMIT 10;
```

### 8.2 `EXPLAIN`
Shows the execution plan for a query — join strategies, index usage, and data flow — before the query runs.

```sql
EXPLAIN
SELECT o.OrderId, c.Name
FROM orders_db.Orders AS o
JOIN customers_db.Customers AS c ON o.CustomerId = c.Id
WHERE o.Status = 'Open';
```

### 8.3 `LINT`
Statically analyzes a script for syntax errors, dialect mismatches, and best-practice violations without executing it.

```sql
LINT 'scripts/nightly_load.etlsql';   -- Analyze a file
LINT;                                  -- Analyze the current interactive buffer
```

### 8.4 `SHOW VERSION`
Displays the current engine version, build metadata, and environment info.

```sql
SHOW VERSION;
SHOW VERSION INTO #version_info;
```

### 8.5 `SHOW VARIABLES`
Lists all variables in the current session as a table. Supports `SHOW LOCAL VARIABLES` to only see variables in the current scope.

```sql
SHOW VARIABLES;
SHOW VARIABLES INTO #vars;
SHOW LOCAL VARIABLES;
```

### 8.6 `SHOW PROFILE`
Displays timing and resource usage for the most recently profiled execution.

```sql
SHOW PROFILE;
```

---

## 9. Dynamic SQL & Remote Execution (EXEC / EXECUTE)


ETL-SQL supports dynamic construction and execution of scripts using the `EXEC` statement. This allows for parameterized table names, dynamic DDL, and direct execution of native SQL against remote databases.

### 9.1 Dynamic Expression Execution

Executes a string expression as a script.

```sql
-- Local Dynamic Execution (runs in the current engine session)
-- Can access all current #temp tables and @variables
DECLARE @tableName = '#stage_data';
DECLARE @sql = 'SELECT COUNT(*) FROM ' + @tableName + ';';
EXEC(@sql);

-- Remote Dynamic Execution (sent to the specified connection)
-- The string is evaluated and sent verbatim to the remote database
DECLARE @sql = 'SELECT TOP 10 * FROM dbo.Customers';
EXEC(@sql) AT mssql_conn;
```

### 9.2 Block Pushdown (Native SQL)

Executes a block of native SQL against a remote connection. This is the primary way to use database-specific features (e.g., CTEs, window functions, hints) that are not natively supported in ETL-SQL.

```sql
EXEC prod_db
BEGIN
    -- This block is passed verbatim to the SQL Server
    WITH TopSales AS (
        SELECT SalesPerson, SUM(Amount) as Total
        FROM dbo.Sales
        GROUP BY SalesPerson
    )
    SELECT * FROM TopSales WHERE Total > 10000;
END;
```

### 9.3 Result Capture (`INTO`)

Both dynamic expressions and remote blocks can capture their results into a local `#temp` table.

```sql
-- Capture dynamic SQL result
EXEC(@dynamicSql) AT pg_conn INTO #results;

-- Capture block pushdown result
EXEC prod_db INTO #top_inventory
BEGIN
    SELECT ProductId, StockLevel FROM Warehouse.Inventory WHERE StockLevel < 100;
END;
```

### 9.4 Parameterized Execution (`WITH`)

For remote execution, use the `WITH` clause to pass parameters safely. Parameters can be referenced in the remote SQL using several forms:

1. **Indexed Standard**: `@p0`, `@p1`, etc. (0-indexed)
2. **Indexed ANSI/ODBC**: `?1`, `?2`, etc. (1-indexed)
3. **Sequential**: `?` (positional)

```sql
DECLARE @status = 'Active';
DECLARE @min_amt = 5000;

-- Using @p index
EXEC('SELECT * FROM dbo.Orders WHERE Status = @p0 AND Amount > @p1') 
    AT mssql_conn 
    WITH(@status, @min_amt)
    INTO #filtered_orders;

-- Using sequential ?
EXECUTE pg_conn INTO #results WITH(@status, @min_amt)
BEGIN
    SELECT * FROM orders WHERE status = ? AND amount > ?;
END;
```


### 9.5 Stored Procedure Execution

Executes a stored procedure on a remote connection. Parameters can be passed by position or by name.

```sql
-- Positional parameters
EXEC mssql_conn.dbo.sp_ArchiveData '2024-01-01', 0;

-- Named parameters
EXEC mssql_conn.dbo.sp_ProcessBatch @BatchId = 42, @Mode = 'FULL';

-- With output parameters (if supported by the connector)
DECLARE @RetVal INT;
EXEC mssql_conn.dbo.sp_GetCount @Count = @RetVal OUTPUT;
```

### 9.6 Security & Behavior

- **WHAT_IF Support**: When `SET WHAT_IF ON` is active, `EXEC` against a remote connection will log the SQL that *would* be executed without actually transmitting it.
- **SQL Injection**: Always prefer parameterized execution (`WITH`) or block pushdown over string concatenation when building SQL strings with user-provided input.
- **Transaction Scope**: Remote `EXEC` statements participate in the ambient ETL-SQL transaction if `BEGIN TRANSACTION` has been called and the connector supports it.

