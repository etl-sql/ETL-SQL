# SET MAX_LAST_RESULT_ROWS
Sets the maximum number of rows retained in the interactive display buffer.

## Syntax
```sql
SET MAX_LAST_RESULT_ROWS = <n>;
```

## Parameters
- **n** — Maximum rows in the display buffer. Default: 50,000.

## Example
```sql
-- Increase buffer for interactive exploration
SET MAX_LAST_RESULT_ROWS = 100000;

SELECT * FROM SalesDB.dbo.LargeTable;
```

## Notes
- Affects the REPL and VS Code interactive results display.
- Does not affect `INTO #table` captures, which are unlimited.
- Default: 50,000.

## References
- [SET Commands](README.md)
