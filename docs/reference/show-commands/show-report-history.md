# SHOW REPORT HISTORY
Displays portal report refresh and execution history rows for a named report.

## Syntax
```sql
SHOW REPORT HISTORY '<name>' [INTO #table];
```

## Parameters
- **'name'** — The name of the portal report.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with refresh timestamps, duration, status, and triggering user or schedule for each history entry.

## Example
```sql
EXECUTE portal BEGIN
    SHOW REPORT HISTORY 'Monthly Sales Dashboard';

    -- Capture and find failures
    SHOW REPORT HISTORY 'Monthly Sales Dashboard' INTO #hist;
    SELECT RefreshTime, Status, Duration FROM #hist WHERE Status = 'Failed';
END;
```

## Notes
- Must be executed within an `EXECUTE portal BEGIN...END` block.
- See also: `SHOW REPORT`, `SHOW REPORT DEPENDENCIES`.

## References
- [SHOW Commands](README.md)
