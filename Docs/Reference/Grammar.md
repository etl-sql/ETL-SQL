# ETL-SQL Grammar & Orchestration Syntax

This document is the authoritative reference for the ETL-SQL scripting language. It defines variables, control flow, querying, DML, DDL, and engine configuration — everything needed to write a complete ETL script.

---

## 1. Variable & State Management

Variables in ETL-SQL are prefixed with `@` and are case-insensitive. Types can be declared explicitly or inferred.

### 1.1 `DECLARE`
Defines one or more variables. The data type is optional; omitting it defaults to `ANY` (inferred from the assigned value).

```sql
DECLARE @name STRING = 'Chuck';
DECLARE @id   INT    = 101;
DECLARE @rate DECIMAL(10,4) = 1.2345;

-- Multiple variables in one statement
DECLARE @list LIST = (1, 2, 3), @count INT = 0;

-- No type — inferred at runtime
DECLARE @value = 'hello';
```

### 1.2 `SET`
Assigns a new value to an existing variable.

```sql
SET @name  = 'Charles';
SET @count = @count + 1;
SET @label = UPPER(@name) + '_PROCESSED';
```

### 1.3 System Variables
The engine provides built-in variables for session-level state. All system variables are read-only except for `@@DATASET` (which is user-clearable).

| Variable | Description |
| :--- | :--- |
| `@@VERSION` | Full engine version and build metadata string. |
| `@@TRANCOUNT` | Current transaction nesting level (0 = no active transaction). |
| `@@RESULTSETS` | Collection (LIST) of result sets produced by the last executed statement. Can be iterated via `FOREACH` or counted via `@@RESULTSETS.COUNT`. |
| `@@ROWCOUNT` | The number of rows processed or affected by the **absolute last executed statement**. For SELECT statements, this is the count of rows fetched. |
| `@@ERROR` | The integer error code of the **preceding statement**. If the last statement was successful, `@@ERROR` returns `0`. Capturing this value immediately into a variable (e.g., `SET @err = @@ERROR`) is recommended inside `CATCH` blocks. |
| `@@DATASET` | (Report-Builder Only) A reference to the current data set being processed or manually assigned for visual rendering. |


### 1.4 `INPUT` and `OUTPUT` Variables
Control how variables interact with the CLI or parent scripts via `RUN SCRIPT`.

- **`INPUT`**: Value can be overridden by `--var` on the CLI or by `RUN SCRIPT ... WITH(...)`. If not provided by the caller, the declared default applies.
- **`OUTPUT`**: Variable's final value is mapped back to the parent script scope when the sub-script finishes.

```sql
DECLARE @BatchId   INT    INPUT  = 0;
DECLARE @ExitStatus STRING OUTPUT = 'Pending';
```

*CLI usage:*
```bash
ETL-SQL run my_script.etlsql --var @BatchId=42 --var @Env='PROD'
```

*Script orchestration:*
```sql
-- Parent script
DECLARE @SubResult STRING;
RUN SCRIPT 'sub.etlsql' WITH (@Mode = 'FULL', @SubResult = @SubResult);
PRINT 'Sub finished: ' + @SubResult;

-- sub.etlsql
DECLARE @Mode      STRING INPUT;
DECLARE @SubResult STRING OUTPUT = 'OK';
```

### 1.4 `USE PASSWORD`
Sets the master decryption password for the session. Used to decrypt `ENC:` prefixed connection strings.

```sql
USE PASSWORD = 'myMasterSecret';
CREATE CONNECTION db ON MSSQL('ENC:U2FsdGVkX1+...');
```

### 1.5 `CLEAR SESSION`
Aggressively cleans the in-memory execution cache — deletes all temporary files, recovery manifests, and encrypted session state. Recommended for security-critical pipelines.

```sql
CLEAR SESSION;
```

### 1.6 Environment Sets (`CREATE SETS` / `USE SETS` / `DROP SETS`)
Named groups of variable assignments for seamlessly switching between environments (DEV, QA, PROD).

```sql
CREATE SETS !DEV
BEGIN
    @server   = 'dev-db.internal',
    @database = 'DevWarehouse',
    @schema   = 'dbo'
END

CREATE SETS !PROD
BEGIN
    @server   = 'prod-db.internal',
    @database = 'ProdWarehouse',
    @schema   = 'dbo';
    SET WITH_PROMPT ON;   -- Prompts for confirmation in interactive mode
END
```

### 1.7 `REQUIRE VERSION`
Ensures the script is running on a minimum version of the engine. If the condition is not met, the engine throws an error immediately before executing any further statements.

```sql
REQUIRE VERSION >= '0.5.0';
-- Optional keyword 'VERSION':
REQUIRE >= '0.5.0';
```

Supported operators: `=`, `>`, `>=`.

---

## 2. Control Flow

-- Apply the DEV set
USE SETS !DEV;

-- Remove a set
DROP SETS IF EXISTS !STAGING;
```

> [!TIP]
> `SET WITH_PROMPT ON` inside a `CREATE SETS` block causes `USE SETS` to ask for confirmation before applying in interactive mode. In batch or scripted mode the set is applied automatically.

### 1.8 Member Access (Dot Notation)
The engine supports accessing members of complex objects using the `.` operator. This is used extensively with loops and metadata functions. All member lookups are **case-insensitive**.

**Resolution Order:**
1. **Row Columns**: If the variable is a `Row` (e.g., from a `SELECT` or `FILE_LIST`), the engine looks for a column matching the name.
2. **JSON Fields**: If the variable contains a JSON object, the engine extracts the field with that name.
3. **C# Properties/Fields**: If the variable is a system object (like a `FileInfo`), the engine uses reflection to access public properties.

#### Known Object Schemas
While many objects are dynamic, the following standard functions return objects with fixed, reliable property sets:

| Object Context | Member Property | Description |
| :--- | :--- | :--- |
| **Local File** (`FILE_LIST`) | `.NAME` | The name of the file (e.g., `data.csv`) |
| | `.PATH` | The absolute path to the file |
| | `.EXTENSION` | The file extension (including the dot) |
| | `.SIZE` | File size in bytes |
| | `.LASTMODIFIED` | Datetime of the last write |
| | `.ISREADONLY` | Boolean indicator |
| | `.CREATIONTIME` | Datetime the file was created |
| **Remote File** (`REMOTE_FILE_LIST`) | `.NAME` | Name of the remote file/directory |
| | `.FULLPATH` | Full remote path |
| | `.SIZE` | Size in bytes |
| | `.LASTMODIFIED` | Last modified time from remote server |
| | `.ISDIRECTORY` | Boolean indicator |
| **Docker Helper** | `.CONNECTION_STRING` | Host-mapped connection string for a container |

*Example using FOREACH with files:*
```sql
DECLARE @Drops = FILE_LIST('C:\Data\Drops');

FOREACH @File IN @Drops
BEGIN
    PRINT 'Processing ' + @File.NAME + ' (' + @File.SIZE + ' bytes)';
    -- Use .PATH for the source of a COPY or SELECT
    COPY FILE @File.PATH TO 'C:\Archive\' + @File.NAME;
END
```

---

## 2. Engine Configuration

### 2.1 `SET WHAT_IF`
Dry-run mode. All side-effecting operations (`INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`, file operations, `SEND EMAIL`, etc.) are **logged in yellow but not executed**.

```sql
SET WHAT_IF ON;
DELETE FROM prod_db.logs WHERE log_date < '2024-01-01';  -- Logged only
SET WHAT_IF OFF;
```

**What is suppressed:** `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`, `BULK INSERT`, any file/directory operation, `SEND EMAIL`, Docker actions, DDL (`CREATE`/`DROP TABLE`, `CREATE`/`DROP INDEX`).

**What is allowed:** `SELECT`, `DECLARE`, `SET`, `IF/WHILE`, `PRINT`, `CREATE CONNECTION`.

### 2.2 `SET PROFILING`
Enables millisecond-level performance metrics for every statement.

```sql
SET PROFILING ON;
RUN SCRIPT 'heavy_transform.etlsql';
SET PROFILING OFF;
SHOW PROFILE INTO #perf_results;
```

### 2.3 `SET SHOW_PASSWORD`
Controls whether the `USE PASSWORD` value is echoed in plain text in the output. Default: `OFF`.

```sql
SET SHOW_PASSWORD ON;
USE PASSWORD = 'visible_for_debugging';
SET SHOW_PASSWORD OFF;
```

---

## 3. Control Flow

### 3.1 `IF / ELSE IF / ELSE`
Conditional branching. Multiple branches supported. Single-statement bodies do not require `BEGIN...END` but it is recommended.

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

### 3.2 `WHILE`
Repeats a block while the condition is true.

```sql
DECLARE @i INT = 0;
WHILE @i < 10
BEGIN
    SET @i = @i + 1;
    IF @i = 5 CONTINUE;   -- Skip to next iteration
    IF @i = 8 BREAK;      -- Exit loop
    PRINT @i;
END
```

### 3.3 `FOR`
Iterates a variable through a numeric range with an optional step.

```sql
-- Count up
FOR @idx = 1 TO 10
BEGIN
    INSERT INTO #results (val) VALUES (@idx);
END

-- Count down with step
FOR @idx = 100 TO 95 STEP -1
BEGIN
    PRINT @idx;
END
```

### 3.4 `FOREACH`
Iterates through each item in a `LIST` variable.

```sql
DECLARE @months LIST = ('Jan', 'Feb', 'Mar', 'Apr');

FOREACH @month IN @months
BEGIN
    PRINT 'Processing: ' + @month;
    INSERT INTO #monthly_log (Month) VALUES (@month);
END
```

### 3.5 `BREAK` / `CONTINUE` / `RETURN`

| Statement | Effect |
| :--- | :--- |
| `BREAK;` | Exit the innermost `WHILE`/`FOR`/`FOREACH` loop immediately |
| `CONTINUE;` | Skip the remainder of the current loop iteration |
| `RETURN;` | Exit the current script or procedure immediately |

### 3.6 `PRINT`
Outputs a message to the console / messages panel.

```sql
PRINT('Starting nightly load...');
PRINT('Processed: ' + @count + ' rows', TRUE);          -- with timestamp
PRINT(GETDATE(), TRUE, 'yyyy-MM-dd HH:mm:ss');          -- formatted date
```

### 3.7 `TRY...CATCH`
Traps runtime exceptions. `THROW` re-raises or raises a new error.

```sql
BEGIN TRY
    BEGIN TRANSACTION;
    INSERT INTO target_db.sales SELECT * FROM #staging;
    COMMIT;
END TRY
BEGIN CATCH
    ROLLBACK;
    PRINT 'Error: ' + ERROR_MESSAGE();
    THROW;   -- Re-escalate to caller
END CATCH
```

### 3.8 `RAISERROR` / `THROW`
Manually raise an error to be trapped by a `CATCH` block.

```sql
RAISERROR('Validation failed: missing required column', 16, 1);

-- THROW form (preferred)
THROW 50001, 'Batch ID not found in control table', 1;
```

### 3.9 `WAITFOR`
Pauses execution for a fixed duration or until a specific clock time.

| Syntax | Behavior |
| :--- | :--- |
| `WAITFOR DELAY 'hh:mm:ss[.fff]';` | Pause for a fixed duration. Supports milliseconds via `.fff`. |
| `WAITFOR TIME 'hh:mm:ss[.fff]';` | Pause until the specified time. If the time has already passed today, waits until **tomorrow**. |
| `WAITFOR (condition);` | Polls the condition (expression or subquery) every 1 second until it evaluates to true or returns rows. |
| `WAIT UNTIL condition;` | Cleaner alternative to `WAITFOR (condition)`. |

```sql
WAITFOR DELAY '00:00:05';          -- 5 seconds
WAITFOR DELAY '00:00:00.500';      -- 500 milliseconds
WAITFOR TIME '23:30:00';           -- Until 11:30 PM

-- Polling for data
WAITFOR (SELECT 1 FROM incoming_queue WHERE status = 'READY');
WAIT UNTIL EXIST (SELECT 1 FROM #batch_done);

-- Dynamic delay using a variable
DECLARE @pause = '00:00:02';
WAITFOR DELAY @pause;
```

---

## 4. Querying (`SELECT`)

### 4.1 Complete Clause Reference

Clauses must appear in this syntactic order:

```sql
SELECT [DISTINCT] [TOP n [PERCENT] [WITH TIES]]
    <columns>
[INTO <target>]
FROM <source> [AS alias]
[JOIN | LEFT JOIN | RIGHT JOIN | FULL JOIN | CROSS JOIN | LEFT SEMI JOIN | LEFT ANTI JOIN <table>
    [HASH | LOOP | MERGE]     -- optional join algorithm hint
    ON <condition>]
[CROSS APPLY | OUTER APPLY (<subquery>) <alias>]
[WHERE <condition>]
[GROUP BY <columns> | ROLLUP(<cols>) | CUBE(<cols>) | GROUPING SETS(<sets>)]
[HAVING <condition>]
[PIVOT (<agg> FOR <col> IN (<vals>)) AS <alias>]
[UNPIVOT (<val_col> FOR <name_col> IN (<cols>)) AS <alias>]
[ORDER BY <col> [ASC|DESC] [, ...]]
[OFFSET n ROWS]
[FETCH NEXT n ROWS ONLY]
[LIMIT n]
[FOR JSON AUTO | PATH | RAW [, ROOT('name')] [, INCLUDE_NULL_VALUES] [, WITHOUT_ARRAY_WRAPPER]]
[FOR XML AUTO | PATH | RAW [, ROOT('name')] [, ELEMENTS]];
```

### 4.2 `INTO` — Stream to a Target
Writes the result set into a destination. Prefix with `#` for an in-memory staging table.

```sql
SELECT id, name, category
INTO #temp_staging
FROM sales_db.transactions
WHERE created_at >= '2026-01-01';
```

### 4.3 `TOP` / `LIMIT` / `OFFSET FETCH`
Three equivalent ways to cap returned rows:

```sql
-- TOP (T-SQL style) — also supports PERCENT and WITH TIES
SELECT TOP 10 * FROM #sales ORDER BY amount DESC;
SELECT TOP 5 PERCENT WITH TIES * FROM #sales ORDER BY amount DESC;

-- LIMIT (ANSI style) — placed at the end
SELECT * FROM #sales ORDER BY amount DESC LIMIT 10;

-- OFFSET / FETCH — for pagination (requires ORDER BY)
SELECT * FROM #sales
ORDER BY amount DESC
OFFSET 20 ROWS
FETCH NEXT 10 ROWS ONLY;
```

### 4.4 `DISTINCT`
```sql
SELECT DISTINCT category, region FROM #sales;
```

### 4.5 JOIN Types

| Syntax | Returns |
| :--- | :--- |
| `JOIN` / `INNER JOIN` | Rows with matching keys in both tables |
| `LEFT JOIN` / `LEFT OUTER JOIN` | All left rows; NULLs for unmatched right |
| `RIGHT JOIN` / `RIGHT OUTER JOIN` | All right rows; NULLs for unmatched left |
| `FULL JOIN` / `FULL OUTER JOIN` | All rows from both sides; NULLs for gaps |
| `CROSS JOIN` | Cartesian product |
| `LEFT SEMI JOIN` | Left rows where a match exists in the right |
| `LEFT ANTI JOIN` | Left rows where no match exists in the right |

**Join algorithm hints** (optional — force a specific execution strategy):
```sql
-- Force a hash join
SELECT * FROM #large AS a
HASH JOIN #lookup AS b ON a.id = b.id;
```

### 4.6 `CROSS APPLY` / `OUTER APPLY`
Correlated subquery join — the subquery may reference columns from the left side.

```sql
-- CROSS APPLY: like INNER JOIN; excludes rows with no result from the subquery
SELECT o.OrderId, t.LineItem
FROM Orders AS o
CROSS APPLY (SELECT * FROM OrderLines WHERE OrderId = o.OrderId) AS t;

-- OUTER APPLY: like LEFT JOIN; includes rows even when the subquery returns no rows
SELECT o.OrderId, t.LineItem
FROM Orders AS o
OUTER APPLY (SELECT TOP 1 * FROM OrderLines WHERE OrderId = o.OrderId) AS t;
```

### 4.7 Hierarchical Aggregation (`ROLLUP`, `CUBE`, `GROUPING SETS`)

```sql
-- ROLLUP: detail rows + per-region subtotals + grand total
SELECT Region, Product, SUM(Amount) AS Total
FROM #sales
GROUP BY ROLLUP(Region, Product)
ORDER BY Region, Product;

-- CUBE: all combinations (Region×Product, Region only, Product only, grand total)
SELECT Region, Product, SUM(Amount) AS Total
FROM #sales
GROUP BY CUBE(Region, Product);

-- GROUPING SETS: explicitly list which aggregations to compute
SELECT Region, Product, SUM(Amount) AS Total
FROM #sales
GROUP BY GROUPING SETS((Region, Product), (Region), ());
-- () = grand total row
```

### 4.8 `PIVOT` and `UNPIVOT`

```sql
-- PIVOT: rotate Q1–Q4 rows into columns
SELECT category, [Q1], [Q2], [Q3], [Q4]
FROM (SELECT category, quarter, amount FROM #sales) AS src
PIVOT (SUM(amount) FOR quarter IN ([Q1], [Q2], [Q3], [Q4])) AS pvt;

-- UNPIVOT: normalize Q1–Q4 columns back to rows
SELECT category, quarter, amount
FROM #quarterly_sales
UNPIVOT (amount FOR quarter IN ([Q1], [Q2], [Q3], [Q4])) AS unpvt;
```

### 4.9 `FOR JSON` / `FOR XML`

```sql
-- JSON output with root element and null values included
SELECT id, name, amount
FROM #sales
FOR JSON PATH, ROOT('Sales'), INCLUDE_NULL_VALUES;

-- XML output with ROOT and ELEMENTS (column values as child elements)
SELECT id, name
FROM #sales
FOR XML PATH, ROOT('Employees'), ELEMENTS;
```

---

## 5. Common Table Expressions (CTE)

```sql
-- Standard CTE (readable sub-query factoring)
WITH HighSales AS (
    SELECT category, SUM(price) AS Total
    FROM sales_db.transactions
    GROUP BY category
)
SELECT * FROM HighSales WHERE Total > 10000;

-- Recursive CTE (generates a hierarchy or sequence)
WITH RECURSIVE Counter AS (
    SELECT 1 AS n
    UNION ALL
    SELECT n + 1 FROM Counter WHERE n < 10
)
SELECT n FROM Counter;
```

---

## 6. Set Operations

Combine results from multiple queries into a single result set.

```sql
-- UNION: de-duplicated combination
SELECT region FROM #east_sales
UNION
SELECT region FROM #west_sales;

-- UNION ALL: all rows including duplicates (faster)
SELECT id FROM #batch_a
UNION ALL
SELECT id FROM #batch_b;

-- EXCEPT: rows in the first query not present in the second
SELECT id FROM #full_list
EXCEPT
SELECT id FROM #processed;

-- INTERSECT: rows present in both queries
SELECT id FROM #active
INTERSECT
SELECT id FROM #eligible;
```

---

## 7. Logical Operators & Filter Predicates

```sql
-- Standard comparisons
WHERE amount >= 100 AND status <> 'Cancelled'

-- IN / NOT IN (list or @list variable)
WHERE category IN ('Electronics', 'Apparel')
   OR status NOT IN @exclusionList

-- LIKE with ESCAPE
WHERE email LIKE '%@company.com'
  AND code LIKE 'US\_%' ESCAPE '\'

-- EXISTS / NOT EXISTS
WHERE EXISTS (SELECT 1 FROM #approved WHERE id = t.id)

-- IS NULL / IS NOT NULL
WHERE region IS NOT NULL
  AND notes IS NULL
```

---

## 8. Data Manipulation Language (DML)

### 8.1 `INSERT INTO`

```sql
INSERT INTO sales_db.archive (category, TotalSales)
OUTPUT INSERTED.category, INSERTED.TotalSales INTO #AuditLog
SELECT category, SUM(amount) FROM #daily WHERE processed = 1
GROUP BY category;
```

### 8.2 `UPDATE`

```sql
UPDATE sales_db.archive
SET status = 'Closed', closed_at = GETDATE()
OUTPUT DELETED.status AS OldStatus, INSERTED.status AS NewStatus INTO #ChangeLog
WHERE created_at < '2020-01-01';
```

### 8.3 `DELETE`

```sql
DELETE FROM staging.temp_imports
OUTPUT DELETED.id INTO #deleted_ids
WHERE imported_at < DATEADD(DAY, -7, GETDATE());
```

### 8.4 `MERGE` (UPSERT)
Synchronizes a target with a source in one atomic statement — the cornerstone of incremental ETL.

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

### 8.5 `BULK INSERT`
High-speed file-to-table ingestion using strict schema adherence.

```sql
BULK INSERT target_db.DailyLogs (Name, Region, Amount)
FROM 'C:\Incoming\logs.csv'
WITH (
    FORMAT        = 'CSV',     -- CSV, PARQUET, AVRO, EXCEL
    FIRSTROW      = 2,         -- Skip header row
    BATCHSIZE     = 10000,     -- Rows per transaction
    MAXERRORS     = 5,         -- Fail after this many parse errors
    FIELDTERMINATOR = ',',     -- Column separator
    ROWTERMINATOR   = '\n',    -- Row separator
    DATE_FORMAT   = 'yyyy-MM-dd',
    STRICT_SCHEMA = ON         -- Reject rows with wrong column count
);
```

### 8.6 `TRUNCATE TABLE`
Efficiently removes all rows without logging individual deletions.

```sql
TRUNCATE TABLE staging.Daily_Import;
```

---

## 9. DDL (Data Definition Language)

### 9.1 `CREATE TABLE`

```sql
CREATE TABLE #OrderItems (
    OrderId    INT          IDENTITY PRIMARY KEY,
    LineItem   INT          NOT NULL,
    Amount     DECIMAL(18,2) NOT NULL CHECK(Amount >= 0),
    Status     VARCHAR(20)   DEFAULT 'Pending',
    CustomerId INT           REFERENCES Customers(Id),
    CONSTRAINT UQ_Line UNIQUE (OrderId, LineItem)
);
```

### 9.2 `ALTER TABLE`

```sql
ALTER TABLE #staging ADD BatchId INT;
ALTER TABLE #staging DROP COLUMN TempFlag;
ALTER TABLE #staging RENAME COLUMN Name TO FullName;
```

### 9.3 `DROP TABLE`

```sql
DROP TABLE IF EXISTS #temp_staging;
```

### 9.4 `CREATE INDEX` / `DROP INDEX`

```sql
CREATE UNIQUE INDEX IX_Customers_Email ON Customers (Email ASC);
DROP INDEX Customers.IX_Customers_Email;
```

---

## 10. Execution Blocks

### 10.1 `EXECUTE` — Remote Pushdown
Pushes SQL natively to a remote connection. The remote block runs in the **connection's native SQL dialect**.

*Connection block form:*
```sql
DECLARE @minId INT = 100;
DECLARE @status VARCHAR(20) = 'Active';

EXECUTE m_db INTO #results WITH(@minId, @status)
BEGIN
    -- This is T-SQL (MSSQL dialect)
    SELECT t.id, t.name
    FROM dbo.Employee AS t
    WHERE t.id > ?1 AND t.status = ?2;
END
```

*String literal form (dynamic SQL):*
```sql
EXECUTE ('SELECT id, name FROM dbo.Employee WHERE status = ''Active''') AT m_db INTO #results;
```

**Key parameters:**
- `INTO #table` — streams results into a local ETL-SQL staging table
- `WITH (@vars)` — passes variables to the remote; `?` = sequential, `?1`, `?2` = indexed

### 10.2 `PARALLEL`
Fires independent operations concurrently. Execution waits for **all** branches to complete before continuing.

```sql
PARALLEL
BEGIN
    SELECT * INTO #Dim_Date    FROM src.DateDim;
    SELECT * INTO #Dim_Product FROM src.ProductDim;
    SELECT * INTO #Dim_Region  FROM src.RegionDim;
END
-- All three above complete before this line runs
PRINT 'Dimensions loaded.';
```

### 10.3 `RUN SCRIPT`
Executes another `.etlsql` script inline, with optional parameter mapping.

```sql
RUN SCRIPT 'sub_process.etlsql' WITH (@batchId = 1234, @env = 'PROD');
```

---

## 11. Procedures & Functions

### 11.1 `CREATE PROCEDURE`

```sql
CREATE PROCEDURE ArchiveSales @olderThan DATE
AS
BEGIN
    INSERT INTO archive.sales SELECT * FROM prod.sales WHERE created_at < @olderThan;
    DELETE FROM prod.sales WHERE created_at < @olderThan;
END;

EXEC ArchiveSales '2025-01-01';
```

### 11.2 `CREATE FUNCTION`

```sql
CREATE FUNCTION CalculateTax(@amount DECIMAL) RETURNS DECIMAL
AS
BEGIN
    RETURN @amount * 0.15;
END;

SELECT id, CalculateTax(price) AS Tax FROM #sales;
```

### 11.3 Drop

```sql
DROP FUNCTION IF EXISTS CalculateTax;
DROP PROCEDURE IF EXISTS ArchiveSales;
```

---

## 12. Transactions

```sql
BEGIN TRANSACTION;     -- or BEGIN TRAN

-- ... operations ...

IF @@TRANCOUNT > 0
    COMMIT;            -- or COMMIT TRAN

-- On failure in CATCH:
ROLLBACK;              -- or ROLLBACK TRAN
```

---

## 13. Job Scheduling

### 13.1 `CREATE JOB`

```sql
-- Run every 30 minutes
CREATE JOB CleanupJob ON SCHEDULE EVERY 30 MINUTES AS
    RUN SCRIPT 'scripts/cleanup.etlsql';

-- Daily at 2 AM
CREATE JOB NightlyArchive ON SCHEDULE EVERY 1 DAY AT '02:00' AS
BEGIN
    INSERT INTO archive SELECT * FROM prod.logs WHERE log_date < DATEADD(DAY,-30,GETDATE());
    DELETE FROM prod.logs WHERE log_date < DATEADD(DAY, -30, GETDATE());
END;
```

Schedule intervals: `SECONDS`, `MINUTES`, `HOURS`, `DAYS`

### 13.2 Job Management

```sql
SHOW JOBS;                          -- List all registered jobs
SHOW JOB HISTORY;                   -- All execution history
SHOW JOB HISTORY NightlyArchive;    -- History for a specific job
DROP JOB IF EXISTS CleanupJob;
KILL JOB 'job_id_123';              -- Halt a running job
```

---

## 14. Introspection & Diagnostics

```sql
SHOW CONNECTIONS [INTO #temp];                   -- Active connections
SHOW VERSION [INTO #temp];                       -- Engine version and metadata
SHOW TABLES [ON conn] [INTO #temp];              -- Tables in a connection
SHOW COLUMNS FOR conn.TableName [INTO #temp];    -- Columns for a table
SHOW VARIABLES [INTO #temp];                     -- All session variables
SHOW LOCAL VARIABLES [INTO #temp];               -- Variables in the current local scope (e.g. inside procedure)
SHOW PROFILE [INTO #benchmarks];                 -- Last profiling results

EXPLAIN SELECT * FROM conn.Orders WHERE status = 'Open';   -- Execution plan
LINT 'scripts/nightly_load.etlsql';                        -- Static analysis

HELP CONNECTION MSSQL;    -- Connector-specific option help
HELP VARIABLES;           -- List all @@ system variables
```
```

### 14.1 Script Metadata Headers
Scripts can include metadata in a special comment block at the very top of the file. This metadata is automatically captured by the engine and recorded in data lineage logs.

```sql
/* 
   @author: Chuck 
   @version: 1.2.3 
   @description: Nightly cleanup of staging tables 
*/

DECLARE @BatchId INT;
...
```

Supported tags: `@author`, `@version`, `@description`, or any custom `@key: value` pair.
If `@author` is omitted, it defaults to the current system user.

---

## 15. Containerized Test Databases (`USE DOCKER`)
Spins up an isolated containerized database for testing. The container is automatically provisioned and connected.

```sql
-- Start a SQL Server container
USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest') AS dms;
DECLARE @conn VARCHAR(500) = dms.CONNECTION_STRING;
CREATE CONNECTION stage_db ON MSSQL(@conn);

-- Load and test
INSERT INTO stage_db.dbo.TestTable SELECT * FROM #staging;

-- Container management
dms STOP;     -- Pause (state persists)
dms START;    -- Resume
dms CLOSE;    -- Destroy container and all state
DOCKER CLOSE; -- Close ALL active containers
```

*Function-style aliases:*
```sql
START_DOCKER('postgres:15-alpine', 'pg_test');
STOP_DOCKER('pg_test');
CLOSE_DOCKER('pg_test');
```
---

## 16. Dynamic SQL & Remote Execution (EXEC)

The `EXEC` (or `EXECUTE`) statement is used to execute scripts dynamically or to push native SQL commands to remote connections.

### 16.1 Dynamic Expression Execution

Executes a string expression as an ETL-SQL script.

**Syntax:**
```sql
EXEC ( <sql_string_expression> ) [ AT <connection_name> ] [ INTO <#temp_table> ] [ WITH ( <param1>, <param2>, ... ) ];
```

- **Local Execution**: If `AT` is omitted, the string is parsed and executed in the current engine session. It can access local `#temp` tables and `@variables`.
- **Remote Execution**: If `AT` is provided, the string is evaluated as a single command and sent to the remote database.
- **`WITH` (Remote Only)**: Passes parameters to the remote engine. Remote SQL should use `@p0`, `@p1`, etc.

**Example:**
```sql
DECLARE @sql = 'SELECT * FROM #staging';
EXEC(@sql) INTO #results;
```

### 16.2 Remote Block Pushdown (Native SQL)

Sends a block of code to a remote connection to be executed in its native dialect.

**Syntax:**
```sql
EXEC <connection_name> [ INTO <#temp_table> ] [ WITH ( <param1>, ... ) ]
BEGIN
    <native_sql_code>
END;
```

**Example:**
```sql
EXEC mssql_conn INTO #top_users
BEGIN
    SELECT TOP 10 * FROM Users ORDER BY LastLogin DESC
END;
```

### 16.3 Stored Procedure Execution

Executes a stored procedure on a remote connection.

**Syntax:**
```sql
EXEC <connection_name>.<procedure_name> [ [ @param_name = ] <value> [ OUTPUT | INPUT ], ... ];
```

**Example:**
```sql
DECLARE @Count INT;
EXEC prod_db.dbo.sp_GetCustomerCount @Status = 'Active', @Count = @Count OUTPUT;
```

### 16.4 Local Statement Block

Executes a group of ETL-SQL statements.

**Syntax:**
```sql
EXEC ( <statement1>; <statement2>; ... );
```

**Example:**
```sql
EXEC ( PRINT 'Hello'; SET @x = 1; );
```

