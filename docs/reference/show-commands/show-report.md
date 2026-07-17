# SHOW REPORT
Displays portal report metadata for a named report.

## Syntax
```sql
SHOW REPORT '<name>' [INTO #table];
```

## Parameters
- **'name'** — The name of the portal report.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
Report metadata including name, description, owner, created date, last refresh, and status.

## Example
```sql
EXECUTE portal BEGIN
    SHOW REPORT 'Monthly Sales Dashboard';

    -- Capture and inspect
    SHOW REPORT 'Monthly Sales Dashboard' INTO #rpt;
    SELECT Name, Owner, LastRefresh FROM #rpt;
END;
```

## Notes
- Must be executed within an `EXECUTE portal BEGIN...END` block.
- See also: `SHOW REPORT HISTORY`, `SHOW REPORT DEPENDENCIES`.

## References
- [SHOW Commands](README.md)
