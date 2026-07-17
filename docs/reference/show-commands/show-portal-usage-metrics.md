# SHOW PORTAL USAGE METRICS
Displays portal usage and refresh metrics.

## Syntax
```sql
SHOW PORTAL USAGE METRICS [INTO #table];
```

## Parameters
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with usage statistics including report view counts, unique users, refresh counts, and error rates.

## Example
```sql
EXECUTE portal BEGIN
    SHOW PORTAL USAGE METRICS;

    -- Capture and analyze
    SHOW PORTAL USAGE METRICS INTO #usage;
    SELECT ReportName, ViewCount, UniqueUsers, RefreshCount
    FROM #usage
    ORDER BY ViewCount DESC;
END;
```

## Notes
- Must be executed within an `EXECUTE portal BEGIN...END` block.
- Useful for identifying popular reports, underused assets, and refresh performance trends.

## References
- [SHOW Commands](README.md)
