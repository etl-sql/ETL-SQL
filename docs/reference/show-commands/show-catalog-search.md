# SHOW CATALOG SEARCH
Searches the portal catalog for reports matching a text query.

## Syntax
```sql
SHOW CATALOG SEARCH '<text>' [INTO #table];
```

## Parameters
- **'text'** — The search text to match against report names, descriptions, and tags.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with report name, description, folder, owner, and relevance score for each matching report.

## Example
```sql
EXECUTE portal BEGIN
    SHOW CATALOG SEARCH 'finance';

    -- Capture and inspect
    SHOW CATALOG SEARCH 'quarterly revenue' INTO #catalog;
    SELECT ReportName, Description, Folder FROM #catalog;
END;
```

## Notes
- Must be executed within an `EXECUTE portal BEGIN...END` block.
- Searches across report names, descriptions, and metadata tags.

## References
- [SHOW Commands](README.md)
