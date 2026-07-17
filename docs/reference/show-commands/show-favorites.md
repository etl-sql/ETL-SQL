# SHOW FAVORITES
Displays portal favorites for the current user or a specified user.

## Syntax
```sql
SHOW FAVORITES [FOR USER '<user>'] [LIMIT <n>] [INTO #table];
```

## Parameters
- **FOR USER 'user'** — Optional. Shows favorites for a specific user. When omitted, shows favorites for the current user.
- **LIMIT n** — Optional. Limits the number of results returned.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with report name, favorited date, and other metadata for each favorited report.

## Example
```sql
EXECUTE portal BEGIN
    -- View current user's favorites
    SHOW FAVORITES;

    -- View favorites for a specific user, limited to 25
    SHOW FAVORITES FOR USER 'jsmith' LIMIT 25;

    -- Capture and query
    SHOW FAVORITES INTO #favs;
    SELECT ReportName, FavoritedDate FROM #favs;
END;
```

## Notes
- Must be executed within an `EXECUTE portal BEGIN...END` block.
- Reports are favorited via `FAVORITE REPORT` in portal execution blocks.

## References
- [SHOW Commands](README.md)
