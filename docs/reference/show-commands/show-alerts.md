# SHOW ALERTS FOR REPORT
Displays portal report alerts configured for a named report.

## Syntax
```sql
SHOW ALERTS FOR REPORT '<name>' [INTO #table];
```

## Parameters
- **'name'** — The name of the portal report.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with alert name, condition, recipients, status, and last triggered time for each alert.

## Example
```sql
EXECUTE portal BEGIN
    SHOW ALERTS FOR REPORT 'Monthly Sales Dashboard';

    -- Capture and review
    SHOW ALERTS FOR REPORT 'Monthly Sales Dashboard' INTO #alerts;
    SELECT AlertName, Condition, Status FROM #alerts;
END;
```

## Notes
- Must be executed within an `EXECUTE portal BEGIN...END` block.
- Alerts are created via `CREATE ALERT` in portal execution blocks.

## References
- [SHOW Commands](README.md)
