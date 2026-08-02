# SET PROFILING
Enables or disables per-statement timing collection. View results through `eng.profile`.

## Syntax
```text
SET PROFILE ON|OFF;
```

## Parameters
- **ON** — Enable statement-level timing collection.
- **OFF** — Disable profiling (default).

## Example
```sql
-- Profile a slow query sequence
SET PROFILE ON;
SELECT region, SUM(amount) INTO #summary FROM prod.Sales GROUP BY region;
SELECT * FROM #summary WHERE amount > 100000;
SELECT * INTO #timing FROM eng.profile;
SET PROFILE OFF;

SELECT statement, duration_ms, rows_affected FROM #timing ORDER BY duration_ms DESC;
```

## Notes
- Only statements executed while profiling is ON are included in the profile.
- Each row shows the statement text, duration in milliseconds, and rows affected.
- Results are queried from `eng.profile` with ordinary `SELECT` and optional `INTO`.
- Default: OFF.

## References
- [SET Commands](README.md)
