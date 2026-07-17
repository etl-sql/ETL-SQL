# SHOW SHARE LINKS FOR REPORT
Displays active portal share links for a named report.

## Syntax
```sql
SHOW SHARE LINKS FOR REPORT '<name>' [INTO #table];
```

## Parameters
- **'name'** — The name of the portal report.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with link ID, URL, created date, expiration, and permissions for each active share link.

## Example
```sql
EXECUTE portal BEGIN
    SHOW SHARE LINKS FOR REPORT 'Monthly Sales Dashboard';

    -- Capture and audit
    SHOW SHARE LINKS FOR REPORT 'Monthly Sales Dashboard' INTO #links;
    SELECT LinkId, Url, Expiration FROM #links;
END;
```

## Notes
- Must be executed within an `EXECUTE portal BEGIN...END` block.
- Share links are created via `CREATE SHARE LINK` in portal execution blocks.

## References
- [SHOW Commands](README.md)
