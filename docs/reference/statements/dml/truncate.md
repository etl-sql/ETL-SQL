# TRUNCATE

TRUNCATE removes all rows from a table quickly by deallocating storage pages rather than issuing row-by-row deletes. It cannot be filtered with WHERE; use DELETE for partial removal.

## Syntax

```sql
TRUNCATE TABLE <target>;
```

```sql
-- Clear a staging table before reloading
TRUNCATE TABLE #staging;
SELECT * INTO #staging FROM dbo.Source;

-- Clear a remote table on a connection
TRUNCATE TABLE SalesDB.dbo.Staging;

-- Reload pattern
TRUNCATE TABLE #results;
INSERT INTO #results SELECT id, SUM(amount) FROM #orders GROUP BY id;
```

TRUNCATE is not logged row-by-row, so it cannot be rolled back on all database platforms. Behaviour is platform-specific for remote connections. For #temp tables it is always safe and instant.

References:
- [Statements](../README.md)
