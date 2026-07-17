# SHOW SAVED VIEWS FOR REPORT
Displays saved parameter views for a named portal report.

## Syntax
```sql
SHOW SAVED VIEWS FOR REPORT '<name>' [INTO #table];
```

## Parameters
- **'name'** — The name of the portal report.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with view name, saved parameter values, creator, and created date for each saved view.

## Example
```sql
EXECUTE portal BEGIN
    SHOW SAVED VIEWS FOR REPORT 'Monthly Sales Dashboard';

    -- Capture and inspect
    SHOW SAVED VIEWS FOR REPORT 'Monthly Sales Dashboard' INTO #sv;
    SELECT ViewName, Parameters, CreatedBy FROM #sv;
END;
```

## Notes
- Must be executed within an `EXECUTE portal BEGIN...END` block.
- Saved views are created via `CREATE SAVED VIEW` in portal execution blocks.

## References
- [SHOW Commands](README.md)
