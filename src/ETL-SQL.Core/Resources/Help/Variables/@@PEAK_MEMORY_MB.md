# @@PEAK_MEMORY_MB
Peak working-set memory in MB used by the engine process since the script started. Useful for monitoring memory pressure during large data operations.

```sql
SELECT * INTO #large FROM dbo.BigTable;
PRINT 'Peak memory: ' + @@PEAK_MEMORY_MB + ' MB';

-- Warn on high memory use
IF @@PEAK_MEMORY_MB > 2048 BEGIN
  PRINT 'Warning: high memory usage — consider adding filters or raising spill thresholds.';
END;
```

This reflects the OS working-set size of the engine process, not just the current statement's allocation. It only increases within a session; it does not reset between statements.

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
