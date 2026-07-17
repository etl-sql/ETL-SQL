# SHOW RECENT REPORTS
Displays recently viewed portal reports.

## Syntax
```sql
SHOW RECENT REPORTS [INTO #table];
```

## Parameters
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with report name, last viewed date, and view count for each recently accessed report.

## Example
```sql
EXECUTE portal BEGIN
    SHOW RECENT REPORTS;

    -- Capture and review
    SHOW RECENT REPORTS INTO #recent;
    SELECT ReportName, LastViewed FROM #recent ORDER BY LastViewed DESC;
END;
```

## Notes
- Must be executed within an `EXECUTE portal BEGIN...END` block.
- Tracks the current user's recently viewed reports.

## References
- [SHOW Commands](README.md)
