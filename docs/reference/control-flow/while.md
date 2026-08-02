WHILE repeats a block as long as a condition remains TRUE. The condition is evaluated before each iteration — if FALSE on entry, the block never runs.

Syntax:
  WHILE <condition> BEGIN
    ...
  END;

```sql
-- Retry loop with backoff
DECLARE @attempts INT = 0;
DECLARE @success  BOOL = FALSE;

WHILE @attempts < 3 AND @success = FALSE BEGIN
  BEGIN TRY
    SELECT * INTO #data FROM dbo.Volatile;
    SET @success = TRUE;
  END TRY
  BEGIN CATCH
    SET @attempts = @attempts + 1;
    WAITFOR DELAY '00:00:05';
  END CATCH;
END;

-- Poll until another process marks the work complete
WHILE NOT EXISTS (SELECT 1 FROM #status WHERE state = 'complete') BEGIN
  WAITFOR DELAY '00:00:01';
END;
```

BREAK exits the loop immediately. CONTINUE skips to the next condition check.
Use FOR or FOREACH when iterating a fixed numeric range or result set — WHILE is best for retry loops and polling.

References:
- [Control Flow](README.md)
