FOR provides a numeric counter loop or a per-row query loop.

Numeric form:
  FOR @idx = <start> TO <end> [STEP <n>] BEGIN
    ...
  END;

Query form:
  FOR @row IN (SELECT ...) BEGIN
    ...access @row.column...
  END;

The query form binds each result row to @row; column values are accessed via @row.column_name.

```sql
-- Numeric: build a date series
FOR @i = 1 TO 7 BEGIN
  INSERT INTO #week (day_num) VALUES (@i);
END;

-- Query: process one row at a time
FOR @row IN (SELECT id, name FROM #batch WHERE status = 'pending') BEGIN
  EXECUTE dbo.ProcessItem @row.id;
  PRINT 'Processed: ' + @row.name;
END;

-- With STEP
FOR @i = 0 TO 100 STEP 10 BEGIN
  INSERT INTO #buckets (bucket) VALUES (@i);
END;
```

BREAK exits the loop early. CONTINUE skips to the next iteration.
For list-typed variables use FOREACH instead.

References:
- [Grammar](../../guides/getting-started.md)
