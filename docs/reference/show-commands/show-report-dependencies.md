# SHOW REPORT DEPENDENCIES
Displays dependencies discovered for a named portal report.

## Syntax
```sql
SHOW REPORT DEPENDENCIES '<name>' [INTO #table];
```

## Parameters
- **'name'** — The name of the portal report.
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set listing connection names, data sources, scripts, and other reports that the named report depends on.

## Example
```sql
EXECUTE portal BEGIN
    SHOW REPORT DEPENDENCIES 'Monthly Sales Dashboard';

    -- Capture for impact analysis
    SHOW REPORT DEPENDENCIES 'Monthly Sales Dashboard' INTO #deps;
    SELECT DependencyType, DependencyName FROM #deps;
END;
```

## Notes
- Must be executed within an `EXECUTE portal BEGIN...END` block.
- Useful for impact analysis before modifying connections or data sources.
- See also: `SHOW REPORT`, `SHOW REPORT HISTORY`.

## References
- [SHOW Commands](README.md)
