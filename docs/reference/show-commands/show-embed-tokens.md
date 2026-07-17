# SHOW EMBED TOKENS FOR REPORT
Displays portal embed tokens for a named report.

## Syntax
```sql
SHOW EMBED TOKENS FOR REPORT '<name>' [INTO #table];
```

## Parameters
- **'name'** — The name of the portal report.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with token ID, created date, expiration, and scope for each embed token.

## Example
```sql
EXECUTE portal BEGIN
    SHOW EMBED TOKENS FOR REPORT 'Monthly Sales Dashboard';

    -- Capture and review
    SHOW EMBED TOKENS FOR REPORT 'Monthly Sales Dashboard' INTO #tokens;
    SELECT TokenId, Expiration, Scope FROM #tokens;
END;
```

## Notes
- Must be executed within an `EXECUTE portal BEGIN...END` block.
- Embed tokens are created via `CREATE EMBED TOKEN` in portal execution blocks.

## References
- [SHOW Commands](README.md)
