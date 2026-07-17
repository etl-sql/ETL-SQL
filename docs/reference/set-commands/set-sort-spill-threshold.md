# SET SORT_SPILL_THRESHOLD
Sets the row count before a sort operation spills intermediate results to disk.

## Syntax
```sql
SET SORT_SPILL_THRESHOLD = <n>;
```

## Parameters
- **n** — Row count threshold. Default: 100,000.

## Example
```sql
-- Raise threshold for a large sort
SET SORT_SPILL_THRESHOLD = 250000;

SELECT * FROM #transactions ORDER BY transaction_date DESC INTO #sorted;
```

## Notes
- Higher thresholds keep more data in memory during sorting, improving performance at the cost of memory.
- See also: `SET JOIN_SPILL_THRESHOLD`, `SET WINDOW_SPILL_THRESHOLD`.
- Default: 100,000.

## References
- [SET Commands](README.md)
