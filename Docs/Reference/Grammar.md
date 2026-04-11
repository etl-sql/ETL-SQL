# ETL-SQL Grammar & Structural Syntax

This document is the authoritative reference for the procedural and structural grammar of the ETL-SQL language. It defines how data operations are orchestrated, variables are managed, and control flow is implemented.

---

## 1. Variable & State Management

Variables in ETL-SQL are prefixed with `@` and are case-insensitive.

### 1.1 `DECLARE` & `SET`
Defines and assigns values to variables. ETL-SQL supports strict typing with precision or dynamic type inference (`ANY`).

**Precision & Typing:**
```sql
-- Explicit precision for high-fidelity math
DECLARE @TotalSales DECIMAL(18,2) = 0.00;
DECLARE @Buffer VARCHAR(MAX) = '';

-- Multi-declaration with inference
DECLARE @ID = 101, @Status = 'NEW', @IsActive BIT = 1;

-- Assignment
SET @Status = 'PROCESSING';
SET @ID = @ID + 1;
```

### 1.2 `INPUT` & `OUTPUT` Parameters
Used for script orchestration via the CLI (`--var`) or `RUN SCRIPT`.
- **`INPUT`**: The variable's value is pulled from the caller's environment. If not provided, the default value is used.
- **`OUTPUT`**: The variable's final value is returned to the parent script's scope after execution.

```sql
-- Script Header
DECLARE @BatchId INT INPUT = 0;
DECLARE @ExitStatus STRING OUTPUT = 'Success';

-- Logic...
SET @ExitStatus = 'Failed' IF (SELECT COUNT(*) FROM #Errors) > 0;
```

### 1.3 Environment Sets (`CREATE SETS` / `USE SETS`)
Environment sets allow you to define named groups of variable assignments (e.g., DEV, QA, PROD) to switch contexts without changing script logic.

```sql
-- Define an environment
CREATE SETS !PROD
BEGIN
    @Server = 'prod-sql.internal',
    @DB = 'Warehouse',
    @RetryCount = 5;
    SET WITH_PROMPT ON; -- Requires user confirmation in interactive mode
END

-- Apply the environment
USE SETS !PROD;
DROP SETS IF EXISTS !DEV;
```

### 1.4 Security & Master State
- **`USE PASSWORD = '<pwd>'`**: Sets the session master password for decrypting `ENC:` connection strings. Characters are masked as `*` in interactive mode.
- **`SET SHOW_PASSWORD ON|OFF`**: Controls whether the master password is revealed in plain text in logs or UI panels.

---

## 2. Advanced Querying (`SELECT`)

### 2.1 Aggregation & Rollups
ETL-SQL supports advanced grouping for hierarchical reporting and statistical analysis.

- **`GROUP BY ROLLUP(a, b)`**: Generates subtotals for `(a,b)`, `(a)`, and a grand total `()`.
- **`GROUP BY CUBE(a, b)`**: Generates all possible combinations of subtotals (2ⁿ groupings).
- **`GROUP BY GROUPING SETS((a,b), (a))`**: Explicitly specifies which subtotals to compute.

```sql
SELECT Region, Product, SUM(Sales) as Total
FROM #Sales
GROUP BY ROLLUP(Region, Product);
```

### 2.2 PIVOT & UNPIVOT
Rotates data between row-based and column-based formats.

```sql
-- PIVOT: Rows to Columns
SELECT Category, [Q1], [Q2], [Q3], [Q4]
FROM (SELECT Category, Quarter, Amount FROM #Sales) AS src
PIVOT (SUM(Amount) FOR Quarter IN ([Q1], [Q2], [Q3], [Q4])) AS pvt;

-- UNPIVOT: Columns to Rows
SELECT Category, Quarter, Amount
FROM #QuarterlySales
UNPIVOT (Amount FOR Quarter IN ([Q1], [Q2], [Q3], [Q4])) AS unpvt;
```

### 2.3 APPLY (CROSS / OUTER)
Invokes a table-valued function or subquery for each row of the outer table.
- **`CROSS APPLY`**: Similar to an INNER JOIN; returns only rows with matches.
- **`OUTER APPLY`**: Similar to a LEFT JOIN; returns all rows from the left, with NULLs if the right side produces no rows.

### 2.4 Pagination & Limits
- **`TOP <n> [PERCENT] [WITH TIES]`**: Caps results at the top of the query.
- **`OFFSET <n> ROWS FETCH NEXT <m> ROWS ONLY`**: Standard SQL-style pagination.
- **`LIMIT <n>`**: Shorthand for simple capping at the end of the query.

---

## 3. Data Manipulation (`DML`)

### 3.1 The `OUTPUT` Clause
Available for `INSERT`, `UPDATE`, `DELETE`, and `MERGE`. Captures data changes into a table for auditing or secondary processing.

```sql
UPDATE target_table
SET Status = 'Archived'
OUTPUT DELETED.ID, DELETED.Status AS OldStatus, INSERTED.Status AS NewStatus
INTO #AuditLog
WHERE LastActivity < @CutoffDate;
```

### 3.2 `MERGE` (The "UPSERT" Engine)
Synchronizes a target with a source in a single atomic transaction.

```sql
MERGE INTO target_table AS T
USING #staging_source AS S
ON T.UUID = S.UUID
WHEN MATCHED AND S.Checksum <> T.Checksum THEN
    UPDATE SET T.Name = S.Name, T.UpdatedDate = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (UUID, Name) VALUES (S.UUID, S.Name)
WHEN NOT MATCHED BY SOURCE THEN
    DELETE; -- Synchronizes deletions
```

### 3.3 `BULK INSERT` (High-Speed Ingestion)
Optimized for streaming massive flat files directly into a target database.
- **Options**: `FORMAT='CSV|PARQUET'`, `FIRSTROW=2`, `MAXERRORS=10`, `BATCHSIZE=10000`.

---

## 4. Control Flow & Loops

### 4.1 Conditionals & Blocks
- **`BEGIN...END`**: Wraps multiple statements into a single logical unit.
- **`IF...ELSE IF...ELSE`**: Standard branching logic.

### 4.2 Looping Constructs
- **`WHILE <condition>`**: Repeats while true. Supports `BREAK` / `CONTINUE`.
- **`FOR @idx = <start> TO <end> [STEP <n>]`**: Numeric range iteration.
- **`FOREACH @var IN @list`**: Traverses a `LIST` variable (e.g., file paths or user IDs).

### 4.3 Error Handling (`TRY...CATCH`)
```sql
BEGIN TRY
    BEGIN TRANSACTION;
    -- Destructive ETL logic...
    COMMIT;
END TRY
BEGIN CATCH
    ROLLBACK;
    PRINT 'ERROR: ' + ERROR_MESSAGE() + ' (Code: ' + CAST(ERROR_NUMBER() AS STRING) + ')';
    THROW; -- Escalate to the engine orchestrator
END CATCH
```

---

## 5. DDL: Table & Index Management

### 5.1 Table Modification
- **`ALTER TABLE <name> ADD <col_definition>;`**
- **`ALTER TABLE <name> DROP COLUMN <col_name>;`**
- **`ALTER TABLE <name> RENAME COLUMN <old> TO <new>;`**
- **`TRUNCATE TABLE <name>;`** (High-speed row purging).

### 5.2 Index Management
- **`CREATE [UNIQUE] INDEX <name> ON <table_name> (<cols...>);`**
- **`DROP INDEX <table_name>.<index_name>;`**

---

### 6.2 `CREATE FUNCTION` (Projected Calculations)
Functions must return a scalar value and can be used in `SELECT` or `WHERE`.

```sql
CREATE FUNCTION GetTaxRate(@Region STRING) RETURNS DECIMAL
AS
BEGIN
    IF @Region = 'NY' RETURN 0.088;
    IF @Region = 'TX' RETURN 0.062;
    RETURN 0.00;
END;
```

### 6.3 Remote Execution & Pushdowns (`EXECUTE ON`)
Forces a SQL statement or procedure to execute directly on a target connection, bypassing the ETL-SQL engine for maximum performance.

```sql
-- Pushdown a native stored procedure to the target MSSQL server
EXEC dbo.CalculateDailyTotals @Date = '2026-04-11' ON prod_db;

-- Pushdown a raw SQL string to Postgres
EXECUTE ('VACUUM ANALYZE base_table') ON pg_remote;
```

---

## 7. Configuration & Environment Life-cycle

### 7.1 Infrastructure Management (`USE DOCKER`)
Spin up or manage containerized databases for temporary orchestration.

- **`USE DOCKER('<image>') [AS <alias>]`**: Starts a container.
- **`<alias> STOP | START | CLOSE`**: Management commands.
- **`DOCKER CLOSE`**: Shuts down all active containers managed by the script.

### 7.2 Wait & Coordination
- **`WAITFOR DELAY 'hh:mm:ss[.fff]'`**: Pauses for a fixed duration.
- **`WAITFOR TIME 'hh:mm:ss[.fff]'`**: Pauses until a clock time is reached.

---

## 8. Transactions
ETL-SQL implements an atomic transaction stack.
- **`BEGIN TRANSACTION`** (or `BEGIN TRAN`)
- **`COMMIT`** / **`ROLLBACK`**
- **`@@TRANCOUNT`**: Returns the current transaction nesting level (0 if none).

---
*Refer to [Standard_Library.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Standard_Library.md) for functions and [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) for Email/Filesystem automation.*
