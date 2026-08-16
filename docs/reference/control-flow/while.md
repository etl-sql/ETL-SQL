# WHILE

Executes a statement block repeatedly as long as a specified boolean condition evaluates to `TRUE`. The test condition is evaluated before each iteration; if `FALSE` upon entry, the loop body is bypassed entirely.

---

## Syntax

```sql
WHILE <boolean_condition>
BEGIN
  -- Loop body statements
END;
```

---

## Execution Rules & Control Statements

- **Pre-Condition Evaluation**: The condition is evaluated at the start of each iteration.
- **`BREAK`**: Exits the loop immediately, jumping to the statement after `END`.
- **`CONTINUE`**: Skips all remaining statements in the current cycle and re-evaluates the loop condition.
- **Loop Choice**: Use `WHILE` for dynamic polling, chunked batching, or retry loops with backoff; use `FOREACH` when iterating over fixed tables, files, or `LIST` collections.

---

## Examples

### 1. Exponential Backoff Retry Loop for Volatile APIs

Retry a flaky external connection up to 4 times with progressive backoff pauses:

```sql
DECLARE @attempt INT = 1;
DECLARE @max_attempts INT = 4;
DECLARE @connected BOOL = FALSE;

WHILE @attempt <= @max_attempts AND @connected = FALSE
BEGIN
  BEGIN TRY
    PRINT 'Attempting connection (Try ' + CAST(@attempt AS VARCHAR) + ' of ' + CAST(@max_attempts AS VARCHAR) + ')...';
    
    CREATE CONNECTION api_src AS API(URL='https://api.vendor.com/v1/health');
    SELECT * INTO #health FROM api_src.data;
    
    SET @connected = TRUE;
    PRINT 'Connection established successfully.';
  END TRY
  BEGIN CATCH
    PRINT 'Connection attempt ' + CAST(@attempt AS VARCHAR) + ' failed: ' + ERROR_MESSAGE();
    SET @attempt = @attempt + 1;
    
    IF @attempt <= @max_attempts
    BEGIN
      -- Exponential backoff pause (5s, 10s, 20s...)
      WAITFOR DELAY '00:00:05';
    END;
  END CATCH;
END;

IF @connected = FALSE
  THROW 50002, 'Failed to connect to API after maximum retry attempts.', 1;
```

### 2. Production Chunked Purge & Archive (Preventing Lock Contention)

Delete millions of expired audit log rows in controlled batches of 10,000 to avoid database transaction log growth and table lock escalation:

```sql
CREATE CONNECTION dw AS MSSQL(SERVER='dw.internal', DATABASE='analytics');

DECLARE @rows_deleted INT = 1;
DECLARE @total_purged INT = 0;
DECLARE @cutoff_date DATE = DATEADD(YEAR, -3, GETDATE());

WHILE @rows_deleted > 0
BEGIN
  -- 1. Identify a chunk of 10,000 old records
  SELECT TOP 10000 log_id 
  INTO #chunk_to_delete
  FROM dw.dbo.AuditLogs 
  WHERE log_date < @cutoff_date;

  SET @rows_deleted = @@ROWCOUNT;

  IF @rows_deleted > 0
  BEGIN
    -- 2. Delete chunk inside a tight transaction
    BEGIN TRANSACTION;
      DELETE FROM dw.dbo.AuditLogs 
      WHERE log_id IN (SELECT log_id FROM #chunk_to_delete);
    COMMIT;

    SET @total_purged = @total_purged + @rows_deleted;
    DROP TABLE #chunk_to_delete;

    PRINT 'Purged batch of ' + CAST(@rows_deleted AS VARCHAR) + ' rows. Total purged: ' + CAST(@total_purged AS VARCHAR);
    
    -- Optional throttle to release I/O locks
    WAITFOR DELAY '00:00:01';
  END;
END;

PRINT 'Purge complete. Total rows removed: ' + CAST(@total_purged AS VARCHAR);
```

---

## References & Related Recipes

- [Control Flow Reference](README.md)
- [FOREACH Loop](foreach.md)
- [WAITFOR / WAIT UNTIL](waitfor.md)
- [TRY...CATCH](try-catch.md)
- [ETL Cookbook: REST API Ingestion](../../cookbooks/etl/rest-api-ingestion.md)
- [ETL Cookbook: Full Refresh](../../cookbooks/etl/full-refresh.md)
- [Syntax Index](../../syntax-index.md)
