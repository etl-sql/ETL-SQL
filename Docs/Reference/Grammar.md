# ETL-SQL Grammar & Orchestration Syntax

This document is the authoritative reference for the orchestration grammar of the ETL-SQL language. It defines how data operations are structured, how advanced queries are constructed, and how branching execution control flow works.

---

## 1. Variable & State Management

Variables in ETL-SQL are prefixed with `@` and are case-insensitive. Types can be inferred natively or explicitly declared.

### 1.1 `DECLARE` & `SET`
```sql
-- Explicit precision types
DECLARE @TotalSales DECIMAL(18,2) = 0.00;
DECLARE @Buffer VARCHAR(MAX) = '';

-- Inferencing
DECLARE @ID = 101, @Status = 'NEW', @IsActive BIT = 1;

-- Assignment
SET @Status = 'PROCESSING';
SET @ID = @ID + 1;
```

### 1.2 `INPUT` & `OUTPUT` Parameters
Used to wire variables to the CLI execution state or nested `RUN SCRIPT` orchestrations.
- **`INPUT`**: Default value is overridden by the CLI parameter (--var).
- **`OUTPUT`**: Maps the variable's final value back to the parent execution scope.

```sql
DECLARE @BatchId INT INPUT = 0;
DECLARE @ExitStatus STRING OUTPUT = 'Success';
```

### 1.3 `CLEAR SESSION`
Aggressively wipes the in-memory execution cache. Explicitly deletes all temporary files, recovery manifests, and encrypted temporary state associated with the current session. Recommended for security-critical scripts or freeing up disk space after massive data ops.

```sql
CLEAR SESSION;
```

### 1.4 Environment Sets (`USE SETS`)
Groups of variables that allow you to seamlessly switch execution contexts (e.g., DEV, QA, PROD).
```sql
CREATE SETS !PROD
BEGIN
    @Server = 'prod-sql.internal',
    @DB = 'Warehouse';
    SET WITH_PROMPT ON; -- Will prompt user before applying
END

USE SETS !PROD;
DROP SETS IF EXISTS !DEV;
```

---

## 2. Advanced Querying (`SELECT`)

ETL-SQL extends the baseline ANSI `SELECT` statement with orchestration-specific features.

### 2.1 The `INTO` Target
Streams the result of a query directly into a destination memory table, physical table, or file export.
- An identifier prefixed with `#` establishes an in-memory staging table.
```sql
SELECT id, name, category 
INTO #temp_staging
FROM prod_db.Users WHERE Active = 1;
```

### 2.2 Pagination (`OFFSET` & `FETCH NEXT`)
Caps or paginates query streams dynamically.
```sql
-- Skip the top 20, fetch exactly the next 10.
SELECT id, name, amount FROM #sales
ORDER BY amount DESC
OFFSET 20 ROWS
FETCH NEXT 10 ROWS ONLY;
```

### 2.3 Hierarchical Aggregation (`ROLLUP`, `CUBE`, `GROUPING SETS`)
Generate sub-totals directly in the query.
```sql
-- Produces per-region/product detail, per-region subtotals, and a grand total.
-- The subtotal/grand total rows will have NULL in their respective columns.
SELECT Region, Product, SUM(Amount) AS Total
FROM #sales
GROUP BY ROLLUP(Region, Product)
ORDER BY Region, Product;
```

### 2.4 Structural Rotation (`PIVOT` & `UNPIVOT`)
Transpose column data across rows and vice versa.
```sql
-- PIVOT: Sales per quarter, pivoted so each quarter becomes its own column
SELECT category, [Q1], [Q2], [Q3], [Q4]
FROM (SELECT category, quarter, amount FROM #sales) AS src
PIVOT (SUM(amount) FOR quarter IN ([Q1], [Q2], [Q3], [Q4])) AS pvt;

-- UNPIVOT: Normalize Q1–Q4 columns back into distinct rows
SELECT category, quarter, amount
FROM #quarterly_sales
UNPIVOT (amount FOR quarter IN ([Q1], [Q2], [Q3], [Q4])) AS unpvt;
```

---

## 3. Data Manipulation Language (DML) & Syncs

Data updates in ETL-SQL can be projected to remote sources or executed against `#temp` memory tables.

### 3.1 Standard Modifiers (`INSERT`, `UPDATE`, `DELETE`)
Support the `OUTPUT` clause to capture the exact row delta (Before/After) into an audit table.

```sql
UPDATE sales_db.archive 
SET status = 'Closed' 
OUTPUT DELETED.status AS OldStatus, INSERTED.status AS NewStatus INTO #Audit
WHERE created_at < '2020-01-01';
```

### 3.2 Target Synchronization (`MERGE` / UPSERT)
The cornerstone of incremental data warehousing. Synchronizes a target table with a source payload in one transaction.

```sql
MERGE INTO target_db.Customers AS T
USING #staging_source AS S
ON T.UUID = S.UUID
WHEN MATCHED AND S.Checksum <> T.Checksum THEN
    UPDATE SET T.Name = S.Name, T.UpdatedDate = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (UUID, Name) VALUES (S.UUID, S.Name)
WHEN NOT MATCHED BY SOURCE THEN
    DELETE;  -- Soft or hard-deletes rows that vanished from source
```

### 3.3 High-Speed Direct Ingestion (`BULK INSERT`)
Optimized for streaming payload binaries (such as raw `CSV`s) directly onto target databases using strict destination schema adherence.

```sql
BULK INSERT target_db.DailyLogs 
FROM 'C:\Incoming\logs.csv'
WITH (
    FORMAT = 'CSV',
    FIRSTROW = 2,       -- Skip header
    BATCHSIZE = 10000,  -- Commit threshold
    MAXERRORS = 5,      -- Threshold before job abort
    STRICT_SCHEMA = ON 
);
```

---

## 4. Execution Blocks & Flow Control

### 4.1 Remote Pushdown (`EXECUTE` Block)
ETL-SQL natively executes operations against remote databases using the target's specific SQL dialect. It can seamlessly retrieve those results back into local memory.

```sql
-- Pushes T-SQL to MSSQL, passing in generic ETL parameters
DECLARE @id INT = 1;
DECLARE @name VARCHAR(50) = 'John';

EXECUTE m_db INTO #emp_results WITH(@id, @name)
BEGIN
    -- This block uses standard T-SQL dialect
    SELECT t.id, t.[name] 
    FROM dbo.Employee AS t 
    WHERE t.id > ?1 AND t.[name] = ?2;
END
```

### 4.2 Simultaneous Orchestration (`PARALLEL`)
Fires entirely independent data operations at the exact same time to limit total execution window duration. Ideal for Dimension loading.

```sql
PARALLEL
BEGIN
    SELECT * INTO #Dim_Date FROM src_db.DateDim;
    SELECT * INTO #Dim_Product FROM src_db.ProductDim;
    SELECT * INTO #Dim_Store FROM src_db.StoreDim;
END
```

### 4.3 Script Nesting (`RUN SCRIPT`)
Execute another ETL-SQL script file inline, effectively mapping parameters via the `WITH` argument wrapper.
```sql
RUN SCRIPT 'sub_process.etlsql' WITH (@batchId = 1234, @env = 'PROD');
```

### 4.4 Conditionals & Error Trapping
Standard structural looping and evaluation.

```sql
BEGIN TRY
    -- Logic block
    IF @i > 10 
    BEGIN
        BREAK;
    END
END TRY
BEGIN CATCH
    PRINT 'Error trapped: ' + ERROR_MESSAGE();
    THROW; -- Escalate
END CATCH
```

### 4.5 Execution Suspensions (`WAITFOR`)
Pauses execution. Useful for polling logic or strict batch window timings.

```sql
-- Fixed duration pause
WAITFOR DELAY '00:00:03.500'; -- 3.5 seconds

-- Fixed clock-time pause
-- Note: If the time has already passed today, it pauses the script until that time *tomorrow*.
WAITFOR TIME '23:30:00'; -- 11:30 PM
```

---

## 5. DDL & Engine Configuration

### 5.1 Dry-Run Logging (`SET WHAT_IF`)
Toggles the engine's "dry-run" mode. When `ON`, all destructive operations (e.g., `DELETE`, `MERGE`, `SEND EMAIL`) are logged to the console in yellow text but completely suppressed structurally. Excellent for validating logical bounds.

```sql
SET WHAT_IF ON;
DELETE FROM production_db.logs WHERE age > 30; -- Will only Log
SET WHAT_IF OFF;
```

### 5.2 `EXPORT` Statements
Projects memory state or results into local serialized files. Useful for generating artifact reports or dumping payloads to disk.
*(Currently under limited structural implementation. Use `COPY FILE` or connection definitions for advanced ingestion mappings)*.

### 5.3 Temporary Tables (`CREATE TABLE`)
Structure mapping for `#temp` datasets.
```sql
CREATE TABLE #OrderItems (
    OrderId INT,
    LineItem INT,
    Amount DECIMAL(18,2) NOT NULL CHECK(Amount > 0),
    PRIMARY KEY (OrderId, LineItem)
);
```
