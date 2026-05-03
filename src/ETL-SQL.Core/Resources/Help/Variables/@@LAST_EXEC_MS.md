# @@LAST_EXEC_MS
Elapsed time in milliseconds for the most recently completed statement.

Set by: every statement execution.
Scope:  updated after each statement — read it immediately after the statement you want to time.

```sql
SELECT * INTO #data FROM dbo.LargeTable;
PRINT 'Load time: ' + @@LAST_EXEC_MS + ' ms';

-- Log slow queries
IF @@LAST_EXEC_MS > 5000 BEGIN
  INSERT INTO #slow_log (stmt, duration_ms) VALUES ('LargeTable load', @@LAST_EXEC_MS);
END;
```

For aggregate timing across multiple statements, use SET PROFILING = ON and SHOW PROFILE.
