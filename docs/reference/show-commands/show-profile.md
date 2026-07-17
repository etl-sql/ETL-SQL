# SHOW PROFILE
Displays per-statement timing data for the current session. Requires profiling to be enabled.

## Syntax
```sql
SHOW PROFILE [INTO #table];
```

## Parameters
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with statement text, duration in milliseconds, row counts, and execution order for each profiled statement.

## Example
```sql
-- Enable profiling before running statements
SET PROFILING = ON;

SELECT * FROM SalesDB.dbo.Orders INTO #orders;
SELECT * FROM SalesDB.dbo.Customers INTO #custs;

-- View timing results
SHOW PROFILE;

-- Capture and find slowest statements
SHOW PROFILE INTO #perf;
SELECT statement, duration_ms FROM #perf ORDER BY duration_ms DESC;
```

## Notes
- Profiling must be enabled with `SET PROFILING = ON` before statements are executed.
- Only statements executed after profiling is enabled are included.
- Useful for identifying bottlenecks in multi-step scripts.

## References
- [SHOW Commands](README.md)
