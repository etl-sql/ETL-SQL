# SET PROFILING
Enables or disables per-statement timing collection. View results with `SHOW PROFILE`.

## Syntax
```text
SET PROFILING = ON|OFF;
```

## Parameters
- **ON** — Enable statement-level timing collection.
- **OFF** — Disable profiling (default).

## Example
```sql
-- Profile a slow query sequence
SET PROFILING = ON;
SELECT region, SUM(amount) FROM prod.Sales GROUP BY region INTO #summary;
SELECT * FROM #summary WHERE amount > 100000;
SHOW PROFILE INTO #timing;
SET PROFILING = OFF;

SELECT statement, duration_ms, rows_affected FROM #timing ORDER BY duration_ms DESC;
```

## Notes
- Only statements executed while profiling is ON are included in the profile.
- Each row shows the statement text, duration in milliseconds, and rows affected.
- Results are captured with `SHOW PROFILE [INTO #table]`.
- Default: OFF.

## References
- [SET Commands](README.md)
