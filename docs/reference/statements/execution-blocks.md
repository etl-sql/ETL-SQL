# Execution Blocks


### 11.1 EXEC / EXECUTE - Execution & Pushdown

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
Sends a block of admin statements to a `PORTAL` or `ORCHESTRATOR` connection.

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

> **Error behavior:** Stop-on-first-error within each block. The block is not transactional - a failure mid-block leaves prior statements applied.

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

### 11.4 `GO` - Batch Separator
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

## References

- [Statement Reference](README.md)
- [Syntax Index](../../syntax-index.md)

